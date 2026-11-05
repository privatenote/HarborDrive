using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HarborDrive.Core;

public static class UserProtection
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
    public static byte[] Protect(byte[] input) => Transform(input, true);
    public static byte[] Unprotect(byte[] input) => Transform(input, false);
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = encrypt ? CryptProtectData(ref input, "HarborDrive", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 계정 암호화 저장소에 접근하지 못했습니다.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            unsafe { CryptographicOperations.ZeroMemory(new Span<byte>((void*)input.Data, input.Size)); }
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                unsafe { CryptographicOperations.ZeroMemory(new Span<byte>((void*)output.Data, output.Size)); }
                LocalFree(output.Data);
            }
        }
    }
}

public sealed class AppStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Root { get; }
    public string Logs => Path.Combine(Root, "logs");
    public string Sessions => Path.Combine(Root, "sessions");
    public AppStore(string? root = null)
    {
        Root = root ?? Environment.GetEnvironmentVariable("HARBORDRIVE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HarborDrive");
        Directory.CreateDirectory(Root); Directory.CreateDirectory(Logs); Directory.CreateDirectory(Sessions);
    }
    public AppSettings Load()
    {
        var path = Path.Combine(Root, "settings.json");
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("설정 파일이 비어 있습니다.");
        if (settings.Version != 1) throw new InvalidDataException("지원하지 않는 설정 파일 버전입니다.");
        foreach (var p in settings.Profiles) p.Validate();
        if (settings.Profiles.Select(x => x.Id).Distinct().Count() != settings.Profiles.Count || settings.Profiles.Select(x => x.DriveLetter.ToUpperInvariant()).Distinct().Count() != settings.Profiles.Count) throw new InvalidDataException("설정에 중복 연결이 있습니다.");
        return settings;
    }
    public void Save(AppSettings settings) => AtomicWrite(Path.Combine(Root, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(settings, Json));
    public string CachePath(DriveProfile p) => Path.Combine(p.CacheRoot.Length == 0 ? Path.Combine(Root, "cache") : p.CacheRoot, p.Id);
    public string LogPath(DriveProfile p) => Path.Combine(Logs, p.Id + ".log");
    public Credentials GetCredentials(string id) => ReadEncrypted<Credentials>(Path.Combine(Root, id + ".credential")) ?? new();
    public void SaveCredentials(string id, Credentials credentials) => WriteEncrypted(Path.Combine(Root, id + ".credential"), credentials);
    public SessionRecord? ReadSession(string id) => ReadEncrypted<SessionRecord>(Path.Combine(Sessions, id + ".session"));
    public void SaveSession(SessionRecord record) => WriteEncrypted(Path.Combine(Sessions, record.ProfileId + ".session"), record);
    public void DeleteSession(string id) => File.Delete(Path.Combine(Sessions, id + ".session"));
    public void DeleteCredentials(string id) => File.Delete(Path.Combine(Root, id + ".credential"));
    private static T? ReadEncrypted<T>(string path)
    {
        if (!File.Exists(path)) return default;
        var bytes = UserProtection.Unprotect(File.ReadAllBytes(path));
        try { return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("암호화 파일이 비어 있습니다."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void WriteEncrypted<T>(string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        try { AtomicWrite(path, UserProtection.Protect(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static void AtomicWrite(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { file.Write(bytes); file.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public string ReadLog(DriveProfile p)
    {
        var path = LogPath(p);
        if (!File.Exists(path)) return "아직 로그가 없습니다.";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 48000) stream.Seek(-48000, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
