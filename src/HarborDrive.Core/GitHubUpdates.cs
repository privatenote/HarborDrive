using System.Net;
using System.Text.Json;

namespace HarborDrive.Core;

public enum UpdateState { NotPublished, UpToDate, Available, PackageMissing }
public sealed record UpdateCheck(UpdateState State, Version? Version = null);

public sealed class GitHubUpdates : IDisposable
{
    public const string Repository = "privatenote/HarborDrive";
    public static Uri ReleasePage { get; } = new($"https://github.com/{Repository}/releases/latest");
    private readonly HttpClient http;
    public GitHubUpdates(HttpMessageHandler? handler = null)
    {
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(15);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HarborDrive-UpdateCheck");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    public async Task<UpdateCheck> Check(Version current, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return new(UpdateState.NotPublished);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var release = json.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return new(UpdateState.NotPublished);
        var tag = release.GetProperty("tag_name").GetString();
        if (!TryVersion(tag, out var latest)) throw new InvalidDataException("배포 버전 형식을 확인할 수 없습니다.");
        if (latest <= Normalize(current)) return new(UpdateState.UpToDate, latest);
        var installer = $"HarborDrive-{latest.Major}.{latest.Minor}.{latest.Build}-x64-Setup.exe";
        var hasPackage = release.GetProperty("assets").EnumerateArray().Any(a => a.GetProperty("name").GetString() == installer);
        return new(hasPackage ? UpdateState.Available : UpdateState.PackageMissing, latest);
    }
    public static bool TryVersion(string? tag, out Version result)
    {
        result = new Version(0, 0, 0, 0);
        if (tag == null) return false;
        if (tag.StartsWith('v')) tag = tag[1..];
        if (!Version.TryParse(tag, out var parsed) || parsed.Build < 0 || parsed.Revision > 0) return false;
        result = Normalize(parsed); return true;
    }
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
    public void Dispose() => http.Dispose();
}
