using System.Text.RegularExpressions;

namespace HarborDrive.Core;

public sealed record DriveProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "S3 드라이브";
    public string Bucket { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Region { get; set; } = "ap-northeast-2";
    public string Endpoint { get; set; } = "";
    public string DriveLetter { get; set; } = "S";
    public bool UseEnvironmentAuth { get; set; }
    public string AwsProfile { get; set; } = "";
    public bool ForcePathStyle { get; set; } = true;
    public bool ReadOnly { get; set; }
    public bool AutoMount { get; set; }
    public bool AutoRestart { get; set; } = true;
    public int CacheGiB { get; set; } = 20;
    public int MinFreeGiB { get; set; } = 2;
    public int WriteBackSeconds { get; set; } = 5;
    public string CacheRoot { get; set; } = "";
    public string RemoteName => "hd_" + Id;
    public string Remote => $"{RemoteName}:{Bucket}/{Prefix.Trim('/')}".TrimEnd('/');
    public string MountPoint => DriveLetter.ToUpperInvariant() + ":";
    public string DisplayRemote => $"s3://{Bucket}/{Prefix.Trim('/')}".TrimEnd('/');

    public void Validate()
    {
        if (!Regex.IsMatch(Id, "^[a-f0-9]{32}$")) throw new ArgumentException("잘못된 연결 ID입니다.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 60) throw new ArgumentException("연결 이름은 1~60자로 입력해 주세요.");
        if (!Regex.IsMatch(DriveLetter, "^[D-Zd-z]$")) throw new ArgumentException("드라이브 문자는 D~Z 중에서 선택해 주세요.");
        if (!Regex.IsMatch(Bucket, "^[a-zA-Z0-9][a-zA-Z0-9._-]{1,253}$")) throw new ArgumentException("버킷 이름을 확인해 주세요. s3://와 경로는 제외합니다.");
        if (Prefix.Contains('\\') || Prefix.Split('/').Any(p => p is "." or "..") || Prefix.Any(char.IsControl)) throw new ArgumentException("S3 경로에는 /를 사용하고 상대 경로(..)는 제외해 주세요.");
        if (Endpoint.Length > 0 && (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || u.UserInfo.Length > 0 || u.Query.Length > 0 || u.Fragment.Length > 0)) throw new ArgumentException("엔드포인트는 http(s) 주소로 입력해 주세요.");
        if (CacheGiB is < 1 or > 100000 || MinFreeGiB is < 0 or > 100000 || WriteBackSeconds is < 1 or > 300) throw new ArgumentException("캐시 용량 또는 쓰기 지연 값을 확인해 주세요.");
        if (CacheRoot.Length > 0 && (!Path.IsPathFullyQualified(CacheRoot) || CacheRoot.StartsWith(@"\\") || new DriveInfo(Path.GetPathRoot(CacheRoot)!).DriveType != DriveType.Fixed)) throw new ArgumentException("캐시는 로컬 고정 디스크의 절대 경로를 지정해 주세요.");
    }
}

public sealed record Credentials(string AccessKey = "", string SecretKey = "", string SessionToken = "");
public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool StartAtLogin { get; set; }
    public List<DriveProfile> Profiles { get; set; } = [];
}
public sealed record QueueEntry(long Id, string Name, long Size, int Tries, bool Uploading);
public sealed record MountSnapshot(int Queued, int Uploading, int Errors, long CacheBytes, bool OutOfSpace, int InUse, double Speed, long Bytes, IReadOnlyList<QueueEntry> Queue)
{
    // vfs/stats.inUse is the VFS reference count, not an open-file count.
    public bool HasPending => Queued > 0 || Uploading > 0 || Errors > 0;
}
public enum MountState { Disconnected, Connecting, Connected, Uploading, Retrying, Unknown, Error }
public sealed class DriveStatus(DriveProfile profile)
{
    public DriveProfile Profile { get; set; } = profile;
    public MountState State { get; set; }
    public string Message { get; set; } = "연결되지 않음";
    public MountSnapshot? Snapshot { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset RetryAt { get; set; }
    public int RestartAttempts { get; set; }
}
public sealed record SessionRecord(string ProfileId, int Pid, long StartTicks, int Port, string Password, bool Desired = true);

public static class Units
{
    public static string Bytes(double n) => n >= 1073741824 ? $"{n / 1073741824:0.0} GB" : n >= 1048576 ? $"{n / 1048576:0.0} MB" : n >= 1024 ? $"{n / 1024:0.0} KB" : $"{n:0} B";
}
