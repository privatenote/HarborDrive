using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarborDrive.Core;

var repo = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? Environment.CurrentDirectory);
var testRoot = Path.Combine(repo, ".devdata", "test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var count = 0;
Console.WriteLine("Test process architecture: " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
async Task RejectAsync(Func<Task> action, string name) { try { await action(); } catch (InvalidOperationException) { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
async Task Until(Func<Task<bool>> check, string label, int seconds = 50)
{
    var until = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < until) { if (await check()) return; await Task.Delay(400); }
    throw new TimeoutException(label);
}
var store = new AppStore(Path.Combine(testRoot, "app"));
var p = new DriveProfile { Bucket = "harbor-test", Prefix = "테스트/nested", Name = "Integration", DriveLetter = "R", MinFreeGiB = 0, WriteBackSeconds = 2 };
p.Validate();
Reject(() => (p with { Prefix = "../x" }).Validate(), "reject relative S3 paths");
Reject(() => (p with { DriveLetter = "C" }).Validate(), "protect system drive");
Reject(() => (p with { Id = "../x" }).Validate(), "reject profile ID traversal");
Reject(() => (p with { Endpoint = "https://user:secret@example.com" }).Validate(), "reject credentials in endpoint URL");
Reject(() => (p with { CacheGiB = 0 }).Validate(), "validate cache bounds");
var credentials = new Credentials("harbor-test-key", "harbor-test-secret-123456", "session-only-test");
store.SaveCredentials(p.Id, credentials);
Check(store.GetCredentials(p.Id) == credentials, "DPAPI round trip");
Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(store.Root, p.Id + ".credential"))).Contains(credentials.SecretKey), "no plaintext secret on disk");
var settings = new AppSettings { Profiles = [p] };
store.Save(settings); Check(store.Load().Profiles[0].Prefix == p.Prefix, "atomic Unicode settings round trip");
var start = new ProcessStartInfo(); MountManager.ConfigureRemote(start, p, credentials);
Check(!start.Arguments.Contains(credentials.SecretKey) && start.ArgumentList.Count == 0, "credentials never enter command line");
Check(start.Environment["RCLONE_CONFIG_" + p.RemoteName.ToUpperInvariant() + "_SECRET_ACCESS_KEY"] == credentials.SecretKey, "per-process remote environment");
Check(MountManager.Redact("abc " + credentials.SecretKey, credentials) == "abc [숨김]", "redact secrets in errors");
Check(!new MountSnapshot(0, 0, 0, 0, false, 1, 0, 0, []).HasPending, "VFS reference count is not pending uploads");
Check(new MountSnapshot(1, 0, 0, 0, false, 1, 0, 0, []).HasPending, "queued uploads block safe disconnect");
var record = new SessionRecord(p.Id, 123, 456, 5572, "rc-secret");
store.SaveSession(record); Check(store.ReadSession(p.Id) == record, "encrypted recovery journal round trip"); store.DeleteSession(p.Id);
File.WriteAllText(Path.Combine(store.Root, "settings.json"), "{bad");
try { store.Load(); throw new Exception("Corrupt settings accepted"); } catch (JsonException) { Check(true, "corrupt settings fail without overwriting"); }
store.Save(settings);

if (args.Contains("--integration"))
{
    if (!MountManager.WinFspInstalled) throw new Exception("Install WinFsp before integration tests.");
    var engine = Environment.GetEnvironmentVariable("HARBORDRIVE_TEST_RCLONE") ?? Path.Combine(repo, "tools", "rclone.exe");
    var remoteRoot = Path.Combine(testRoot, "s3-server");
    Directory.CreateDirectory(Path.Combine(remoteRoot, p.Bucket));
    var portListener = new TcpListener(IPAddress.Loopback, 0); portListener.Start(); var port = ((IPEndPoint)portListener.LocalEndpoint).Port; portListener.Stop();
    p = p with { Endpoint = $"http://127.0.0.1:{port}", Region = "us-east-1", DriveLetter = Enumerable.Range('D', 23).Select(x => ((char)x).ToString()).Reverse().First(l => !DriveInfo.GetDrives().Any(d => d.Name.StartsWith(l + ":", StringComparison.OrdinalIgnoreCase))) };
    credentials = credentials with { SessionToken = "" };
    store.SaveCredentials(p.Id, credentials); store.Save(new AppSettings { Profiles = [p] });
    Process StartServer()
    {
        var si = new ProcessStartInfo(engine) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "serve", "s3", remoteRoot, "--addr", $"127.0.0.1:{port}", "--config", "NUL", "--dir-cache-time", "1s", "--log-file", Path.Combine(testRoot, "server.log") }) si.ArgumentList.Add(arg);
        si.Environment["RCLONE_AUTH_KEY"] = $"\"{credentials.AccessKey},{credentials.SecretKey}\"";
        return Process.Start(si)!;
    }
    Process? server = StartServer();
    MountManager? manager = new(store, engine);
    async Task Drain()
    {
        await Until(async () => { await manager!.Poll(); return manager.GetStatus(p).Snapshot is { HasPending: false } && !manager.HasDirtyCache(p); }, "upload queue did not drain");
    }
    string RemoteFile(string name) => Path.Combine(remoteRoot, p.Bucket, p.Prefix.Replace('/', Path.DirectorySeparatorChar), name);
    var drive = p.MountPoint + "\\";
    try
    {
        await Task.Delay(800);
        var buckets = await manager.ListDirectories(p, credentials, true);
        Check(buckets.Any(b => b.TrimEnd('/') == p.Bucket), "signed S3 bucket discovery");
        await manager.Initialize([p]); await manager.Connect(p);
        Check(Directory.Exists(drive), "real WinFsp network drive mount");
        var session = store.ReadSession(p.Id)!;
        using (var unauth = new HttpClient(new HttpClientHandler { UseProxy = false }))
        {
            var response = await unauth.PostAsync($"http://127.0.0.1:{session.Port}/mount/listmounts", new StringContent("{}", Encoding.UTF8, "application/json"));
            Check(response.StatusCode == HttpStatusCode.Unauthorized, "RC rejects unauthenticated control requests");
        }
        var subdir = Path.Combine(drive, "한글 폴더"); Directory.CreateDirectory(subdir);
        var file = Path.Combine(subdir, "테스트 파일.txt");
        await File.WriteAllTextAsync(file, "처음 저장");
        Check(await File.ReadAllTextAsync(file) == "처음 저장", "Unicode create/read through mounted drive");
        await File.WriteAllTextAsync(file, "덮어쓰기");
        await Drain();
        Check(await File.ReadAllTextAsync(RemoteFile(Path.Combine("한글 폴더", "테스트 파일.txt"))) == "덮어쓰기", "overwrite and verify S3 server content");
        var renamed = Path.Combine(subdir, "이름 변경.txt"); File.Move(file, renamed);
        await Drain();
        await Until(() => Task.FromResult(File.Exists(RemoteFile(Path.Combine("한글 폴더", "이름 변경.txt")))), "rename not reflected remotely");
        Check(!File.Exists(RemoteFile(Path.Combine("한글 폴더", "테스트 파일.txt"))), "S3 rename removes old key");
        var bytes = RandomNumberGenerator.GetBytes(8 * 1024 * 1024);
        await File.WriteAllBytesAsync(Path.Combine(drive, "binary.bin"), bytes); await Drain();
        Check(SHA256.HashData(File.ReadAllBytes(RemoteFile("binary.bin"))).SequenceEqual(SHA256.HashData(bytes)), "8 MB binary integrity after upload");
        File.Delete(renamed);
        await Until(() => Task.FromResult(!File.Exists(RemoteFile(Path.Combine("한글 폴더", "이름 변경.txt")))), "delete not reflected remotely");
        Check(true, "file deletion reaches S3");
        manager.Dispose(); manager = new(store, engine); await manager.Initialize([p]);
        Check(store.ReadSession(p.Id)!.Pid == session.Pid, "UI restart adopts live engine without duplicate mount");
        await manager.Disconnect(p); Check(!Directory.Exists(drive), "safe disconnect removes drive");
        p = p with { ReadOnly = true }; await manager.Connect(p);
        try { await File.WriteAllTextAsync(Path.Combine(drive, "must-not-write.txt"), "x"); throw new Exception("Read-only write succeeded"); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { Check(true, "read-only mount rejects writes"); }
        await manager.Disconnect(p); p = p with { ReadOnly = false, WriteBackSeconds = 30 }; await manager.Connect(p);
        // Offline writes require the directory/file metadata to be cached first.
        await File.WriteAllTextAsync(Path.Combine(drive, "offline.txt"), "warm-cache");
        await manager.RetryUploads(p); await Drain();
        await manager.Refresh(p); _ = Directory.GetFiles(drive); _ = await File.ReadAllTextAsync(Path.Combine(drive, "offline.txt"));
        server.Kill(); await server.WaitForExitAsync(); server.Dispose(); server = null;
        await File.WriteAllTextAsync(Path.Combine(drive, "offline.txt"), "offline-durable-data");
        await Until(async () => { await manager.Poll(); return manager.GetStatus(p).Snapshot is { HasPending: true }; }, "offline write not queued");
        await RejectAsync(() => manager.Disconnect(p), "safe disconnect refuses pending offline upload");
        Check(manager.HasDirtyCache(p), "dirty offline cache is persisted on disk");
        await manager.Disconnect(p, true);
        Check(Directory.Exists(store.CachePath(p)) && manager.HasDirtyCache(p), "explicit stop retains pending cache");
        server = StartServer(); await Task.Delay(500); await manager.Connect(p); await manager.RetryUploads(p); await Drain();
        Check(await File.ReadAllTextAsync(RemoteFile("offline.txt")) == "offline-durable-data", "offline upload resumes from preserved cache");
        await File.WriteAllTextAsync(Path.Combine(drive, "crash.txt"), "recover-after-engine-crash");
        var crashSession = store.ReadSession(p.Id)!;
        using (var child = Process.GetProcessById(crashSession.Pid)) { child.Kill(); await child.WaitForExitAsync(); }
        await Task.Delay(500);
        manager.Dispose(); manager = new(store, engine); await manager.Initialize([p]); await manager.RetryUploads(p); await Drain();
        Check(store.ReadSession(p.Id)!.Pid != crashSession.Pid && await File.ReadAllTextAsync(RemoteFile("crash.txt")) == "recover-after-engine-crash", "engine crash recovers durable upload with same cache");
        await File.WriteAllTextAsync(Path.Combine(drive, "automatic.txt"), "automatic-restart-data");
        var automaticSession = store.ReadSession(p.Id)!;
        using (var child = Process.GetProcessById(automaticSession.Pid)) { child.Kill(); await child.WaitForExitAsync(); }
        await Task.Delay(500); await manager.Poll(); await manager.RetryUploads(p); await Drain();
        Check(store.ReadSession(p.Id)!.Pid != automaticSession.Pid && await File.ReadAllTextAsync(RemoteFile("automatic.txt")) == "automatic-restart-data", "live supervisor automatically restarts engine and recovers writes");
        await manager.Disconnect(p);
        Check(store.ReadSession(p.Id) == null, "clean disconnect clears recovery journal");
        if (args.Contains("--ui-preview"))
        {
            store.Save(new AppSettings { Profiles = [p with { AutoMount = true }] });
            var preview = new ProcessStartInfo(Environment.GetEnvironmentVariable("HARBORDRIVE_TEST_APP") ?? Path.Combine(repo, "artifacts", "HarborDrive-win-arm64", "HarborDrive.exe")) { UseShellExecute = false };
            preview.Environment["HARBORDRIVE_HOME"] = store.Root;
            using var app = Process.Start(preview)!;
            File.WriteAllText(Path.Combine(repo, ".devdata", "ui-preview.json"), JsonSerializer.Serialize(new { app.Id, Store = store.Root, TestRoot = testRoot }));
            Console.WriteLine("UI preview is running against the temporary local S3 server. Exit the app normally to finish.");
            using var previewTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            await app.WaitForExitAsync(previewTimeout.Token);
        }
    }
    finally
    {
        if (manager != null) { try { await manager.Disconnect(p, true); } catch (Exception e) { Console.WriteLine("Cleanup: " + e.Message); } manager.Dispose(); }
        if (server != null) { if (!server.HasExited) { server.Kill(); await server.WaitForExitAsync(); } server.Dispose(); }
    }
}
Console.WriteLine($"All {count} checks passed. Evidence: {testRoot}");
