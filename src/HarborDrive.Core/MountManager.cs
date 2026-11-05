using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace HarborDrive.Core;

public sealed class MountManager(AppStore store, string rclonePath) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, SessionRecord> sessions = [];
    public Dictionary<string, DriveStatus> Statuses { get; } = [];
    public string RclonePath { get; } = Path.GetFullPath(rclonePath);
    public bool IsManaged(string id) => sessions.ContainsKey(id);
    public static bool WinFspInstalled => Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\WinFsp", "InstallDir", null) is string || Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WinFsp", "InstallDir", null) is string;

    public async Task Initialize(IEnumerable<DriveProfile> profiles)
    {
        foreach (var p in profiles)
        {
            GetStatus(p);
            var record = store.ReadSession(p.Id);
            if (record != null)
            {
                if (record.ProfileId != p.Id) throw new InvalidDataException("연결 복구 기록의 ID가 일치하지 않습니다.");
                sessions[p.Id] = record;
            }
            if (record?.Desired == true || p.AutoMount)
            {
                try { await Connect(p); }
                catch (Exception ex) { SetError(p, ex); }
            }
        }
    }

    public async Task Connect(DriveProfile p)
    {
        await gate.WaitAsync();
        try { await ConnectCore(p); }
        finally { gate.Release(); }
    }

    private async Task ConnectCore(DriveProfile p)
    {
        p.Validate();
        var status = GetStatus(p);
        if (!File.Exists(RclonePath)) throw new FileNotFoundException("rclone.exe가 없습니다. scripts/setup.ps1을 실행해 주세요.", RclonePath);
        if (!WinFspInstalled) throw new InvalidOperationException("WinFsp를 설치해 주세요. scripts/setup.ps1 -InstallWinFsp를 실행할 수 있습니다.");
        if (sessions.TryGetValue(p.Id, out var old) && OwnedProcess(old) is { } existing)
        {
            existing.Dispose();
            using var api = new RcloneApi(old.Port, old.Password);
            await VerifyIdentity(api, old);
            var mounts = await api.Call("mount/listmounts");
            if (HasMount(mounts, p.MountPoint)) { await UpdateStatus(p, api); return; }
            // Recover a crash between starting the daemon and issuing mount/mount.
            await CreateMount(p, api);
            await UpdateStatus(p, api);
            return;
        }
        if (DriveInfo.GetDrives().Any(d => d.Name.StartsWith(p.MountPoint, StringComparison.OrdinalIgnoreCase))) throw new IOException($"{p.MountPoint} 드라이브가 이미 사용 중입니다.");
        status.State = MountState.Connecting; status.Message = "드라이브 연결 중…"; status.Snapshot = null;
        Directory.CreateDirectory(store.CachePath(p));
        RotateLog(p);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo(RclonePath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = store.Root };
        foreach (var arg in new[] { "rcd", "--rc-addr", $"127.0.0.1:{port}", "--config", "NUL", "--cache-dir", store.CachePath(p), "--log-file", store.LogPath(p), "--log-file-max-size", "10M", "--log-file-max-backups", "3", "--log-level", "INFO", "--use-json-log", "--contimeout", "10s", "--timeout", "30s", "--low-level-retries", "3", "--retries", "3", "--transfers", "4" }) start.ArgumentList.Add(arg);
        // Remove inherited rclone overrides: only this profile controls the child process.
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("RCLONE_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["RCLONE_RC_USER"] = "harbor";
        start.Environment["RCLONE_RC_PASS"] = password;
        ConfigureRemote(start, p, store.GetCredentials(p.Id));
        using var process = Process.Start(start) ?? throw new IOException("rclone을 시작하지 못했습니다.");
        var record = new SessionRecord(p.Id, process.Id, process.StartTime.ToUniversalTime().Ticks, port, password);
        try { store.SaveSession(record); sessions[p.Id] = record; }
        catch { process.Kill(); throw; } // No mount exists yet, so no dirty data can be lost.
        using var client = new RcloneApi(port, password);
        var ready = false;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (process.HasExited) throw new IOException("rclone이 종료되었습니다. 오류 로그를 확인해 주세요.");
            try { using var timeout = new CancellationTokenSource(500); await VerifyIdentity(client, record, timeout.Token); ready = true; break; }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { await Task.Delay(250); }
        }
        if (!ready) throw new TimeoutException("제어 API 응답을 기다리는 중입니다. 중복 프로세스는 시작하지 않습니다.");
        await CreateMount(p, client);
        await UpdateStatus(p, client);
        status.RestartAttempts = 0;
    }

    public static void ConfigureRemote(ProcessStartInfo start, DriveProfile p, Credentials credentials)
    {
        var key = "RCLONE_CONFIG_" + p.RemoteName.ToUpperInvariant() + "_";
        start.Environment[key + "TYPE"] = "s3";
        start.Environment[key + "PROVIDER"] = p.Endpoint.Length == 0 ? "AWS" : "Other";
        start.Environment[key + "REGION"] = p.Region;
        start.Environment[key + "ENDPOINT"] = p.Endpoint;
        start.Environment[key + "FORCE_PATH_STYLE"] = p.ForcePathStyle ? "true" : "false";
        start.Environment[key + "ENV_AUTH"] = p.UseEnvironmentAuth ? "true" : "false";
        if (p.UseEnvironmentAuth)
        {
            if (p.AwsProfile.Length > 0) start.Environment[key + "PROFILE"] = p.AwsProfile;
        }
        else
        {
            if (credentials.AccessKey.Length == 0 || credentials.SecretKey.Length == 0) throw new ArgumentException("Access Key와 Secret Key를 입력해 주세요.");
            start.Environment[key + "ACCESS_KEY_ID"] = credentials.AccessKey;
            start.Environment[key + "SECRET_ACCESS_KEY"] = credentials.SecretKey;
            start.Environment[key + "SESSION_TOKEN"] = credentials.SessionToken;
        }
    }

    private static async Task VerifyIdentity(RcloneApi api, SessionRecord session, CancellationToken ct = default)
    {
        var pid = await api.Call("core/pid", ct: ct);
        if (pid.GetProperty("pid").GetInt32() != session.Pid) throw new IOException("제어 API의 프로세스 ID가 일치하지 않습니다.");
    }
    private static bool HasMount(JsonElement mounts, string point) => mounts.GetProperty("mountPoints").EnumerateArray().Any(m => m.GetProperty("MountPoint").GetString() == point);
    private static async Task CreateMount(DriveProfile p, RcloneApi api)
    {
        await api.Call("mount/mount", new
        {
            fs = p.Remote, mountPoint = p.MountPoint, mountType = "cmount",
            mountOpt = new { NetworkMode = true, VolumeName = "Harbor-" + p.Id[..8] },
            vfsOpt = new { CacheMode = 3, CacheMaxSize = (long)p.CacheGiB * 1073741824, CacheMinFreeSpace = (long)p.MinFreeGiB * 1073741824, CacheMaxAge = "168h", CachePollInterval = "10s", WriteBack = $"{p.WriteBackSeconds}s", DirCacheTime = "5m", ReadOnly = p.ReadOnly }
        });
    }
    private Process? OwnedProcess(SessionRecord session)
    {
        try
        {
            var process = Process.GetProcessById(session.Pid);
            if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == session.StartTicks && string.Equals(process.MainModule?.FileName, RclonePath, StringComparison.OrdinalIgnoreCase)) return process;
            process.Dispose(); return null;
        }
        catch (ArgumentException) { return null; }
        // Access denied is not equivalent to a dead process. Let the caller report it.
    }
    public async Task Poll()
    {
        if (!await gate.WaitAsync(0)) return;
        try
        {
            foreach (var pair in sessions.ToArray())
            {
                if (!Statuses.TryGetValue(pair.Key, out var status)) continue;
                var p = status.Profile;
                try
                {
                    using var process = OwnedProcess(pair.Value);
                    if (process == null)
                    {
                        status.State = MountState.Retrying; status.Snapshot = null;
                        status.Message = p.AutoRestart ? "엔진 종료 감지 · 자동 재연결 대기" : "엔진 종료 · 다시 연결해 주세요";
                        if (p.AutoRestart && DateTimeOffset.UtcNow >= status.RetryAt)
                        {
                            status.RestartAttempts++;
                            status.RetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(6, status.RestartAttempts))));
                            await ConnectCore(p);
                        }
                    }
                    else
                    {
                        using var api = new RcloneApi(pair.Value.Port, pair.Value.Password);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await VerifyIdentity(api, pair.Value, timeout.Token);
                        var mounts = await api.Call("mount/listmounts", ct: timeout.Token);
                        if (!HasMount(mounts, p.MountPoint))
                        {
                            status.State = MountState.Retrying; status.Snapshot = null;
                            status.Message = "드라이브 연결 없음 · 재연결 대기";
                            if (p.AutoRestart && DateTimeOffset.UtcNow >= status.RetryAt)
                            {
                                status.RestartAttempts++;
                                status.RetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(6, status.RestartAttempts))));
                                await CreateMount(p, api);
                                await UpdateStatus(p, api);
                            }
                            continue;
                        }
                        await UpdateStatus(p, api, timeout.Token);
                    }
                }
                catch (Exception ex) { SetError(p, ex, MountState.Unknown); }
            }
        }
        finally { gate.Release(); }
    }
    private async Task UpdateStatus(DriveProfile p, RcloneApi api, CancellationToken ct = default)
    {
        var snapshot = await api.Snapshot(ct);
        var status = GetStatus(p);
        status.Snapshot = snapshot; status.UpdatedAt = DateTimeOffset.Now;
        status.State = snapshot.OutOfSpace || snapshot.Errors > 0 ? MountState.Error : snapshot.HasPending ? MountState.Uploading : MountState.Connected;
        status.Message = snapshot.OutOfSpace ? "캐시 디스크 공간 부족 · 로그 확인 필요" : snapshot.Errors > 0 ? "전송 오류 · 자동 재시도 중" : snapshot.HasPending ? $"업로드 중 {snapshot.Uploading}개 · 대기 {snapshot.Queued}개" : "연결됨 · 업로드 대기 없음";
    }

    public async Task Disconnect(DriveProfile p, bool preservePending = false)
    {
        await gate.WaitAsync();
        try
        {
            if (!sessions.TryGetValue(p.Id, out var record)) return;
            using var process = OwnedProcess(record);
            if (process != null)
            {
                using var api = new RcloneApi(record.Port, record.Password);
                await VerifyIdentity(api, record);
                if (!preservePending)
                {
                    // Two fresh samples avoid acting on stale UI status; open writers must be closed by the user.
                    for (var i = 0; i < 2; i++)
                    {
                        if ((await api.Snapshot()).HasPending || HasDirtyCache(p)) throw new InvalidOperationException("미전송 파일이 있습니다. 전송 완료 후 다시 해제하거나 '캐시 보존 후 중지'를 사용해 주세요.");
                        if (i == 0) await Task.Delay(750);
                    }
                }
                var mounts = await api.Call("mount/listmounts");
                if (HasMount(mounts, p.MountPoint)) await api.Call("mount/unmount", new { mountPoint = p.MountPoint });
                await api.Call("core/quit");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(timeout.Token);
            }
            store.DeleteSession(p.Id); sessions.Remove(p.Id);
            var status = GetStatus(p); status.State = MountState.Disconnected; status.Snapshot = null;
            status.Message = preservePending ? "연결 해제 · 미전송 캐시 보존됨" : "연결되지 않음";
        }
        finally { gate.Release(); }
    }
    public bool HasDirtyCache(DriveProfile p)
    {
        var meta = Path.Combine(store.CachePath(p), "vfsMeta");
        if (!Directory.Exists(meta)) return false;
        foreach (var file in Directory.EnumerateFiles(meta, "*", SearchOption.AllDirectories))
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var json = JsonDocument.Parse(stream);
                if (json.RootElement.TryGetProperty("Dirty", out var dirty) && dirty.GetBoolean()) return true;
            }
            catch (FileNotFoundException) { }
            catch (JsonException) { return true; } // A partial metadata write cannot establish safety.
        }
        return false;
    }
    public async Task RetryUploads(DriveProfile p)
    {
        await gate.WaitAsync();
        try
        {
            if (!sessions.TryGetValue(p.Id, out var record)) throw new InvalidOperationException("먼저 드라이브에 연결해 주세요.");
            using var api = new RcloneApi(record.Port, record.Password);
            await VerifyIdentity(api, record);
            var snapshot = await api.Snapshot();
            foreach (var item in snapshot.Queue.Where(x => !x.Uploading)) await api.Call("vfs/queue-set-expiry", new { id = item.Id, expiry = -1 });
        }
        finally { gate.Release(); }
    }
    public async Task Refresh(DriveProfile p)
    {
        await gate.WaitAsync();
        try
        {
            if (!sessions.TryGetValue(p.Id, out var record)) throw new InvalidOperationException("먼저 드라이브에 연결해 주세요.");
            using var api = new RcloneApi(record.Port, record.Password);
            await VerifyIdentity(api, record);
            await api.Call("vfs/forget");
        }
        finally { gate.Release(); }
    }
    public async Task<string[]> ListDirectories(DriveProfile p, Credentials credentials, bool buckets)
    {
        p.Validate();
        var start = new ProcessStartInfo(RclonePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("RCLONE_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        ConfigureRemote(start, p, credentials);
        foreach (var arg in new[] { "lsf", buckets ? p.RemoteName + ":" : p.Remote, "--dirs-only", "--config", "NUL", "--contimeout", "10s", "--timeout", "15s", "--low-level-retries", "1", "--retries", "1" }) start.ArgumentList.Add(arg);
        using var proc = Process.Start(start) ?? throw new IOException("rclone을 실행하지 못했습니다.");
        var stdout = proc.StandardOutput.ReadToEndAsync(); var stderr = proc.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch { proc.Kill(); throw new TimeoutException("연결 확인 시간이 초과됐습니다. 엔드포인트와 네트워크를 확인해 주세요."); }
        var error = await stderr;
        if (proc.ExitCode != 0) throw new IOException(Redact(error, credentials));
        return (await stdout).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
    public static string Redact(string text, Credentials credentials)
    {
        foreach (var value in new[] { credentials.AccessKey, credentials.SecretKey, credentials.SessionToken }) if (value.Length > 0) text = text.Replace(value, "[숨김]", StringComparison.Ordinal);
        return text;
    }
    public DriveStatus GetStatus(DriveProfile p)
    {
        if (!Statuses.TryGetValue(p.Id, out var status)) Statuses[p.Id] = status = new(p);
        status.Profile = p; return status;
    }
    public void SetError(DriveProfile p, Exception ex, MountState state = MountState.Error)
    {
        var status = GetStatus(p); status.State = state; status.Snapshot = null;
        status.Message = Redact(ex.Message, store.GetCredentials(p.Id));
    }
    private void RotateLog(DriveProfile p)
    {
        var path = store.LogPath(p);
        if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024) File.Move(path, path + ".previous", true);
    }
    // UI disposal never kills engines or deletes their cache. A new UI can adopt the recorded sessions.
    public void Dispose() => gate.Dispose();
}
