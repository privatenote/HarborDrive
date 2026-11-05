using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HarborDrive.Core;

public sealed class RcloneApi : IDisposable
{
    private readonly HttpClient http;
    public RcloneApi(int port, string password)
    {
        http = new(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("harbor:" + password)));
    }
    public async Task<JsonElement> Call(string method, object? args = null, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(method, args ?? new { }, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            string detail;
            try { detail = JsonDocument.Parse(text).RootElement.GetProperty("error").GetString() ?? ""; } catch (JsonException) { detail = response.StatusCode.ToString(); }
            throw new HttpRequestException($"{method}: {detail}", null, response.StatusCode);
        }
        return JsonSerializer.Deserialize<JsonElement>(text);
    }
    public async Task<MountSnapshot> Snapshot(CancellationToken ct = default)
    {
        var v = await Call("vfs/stats", ct: ct);
        var q = await Call("vfs/queue", ct: ct);
        var s = await Call("core/stats", ct: ct);
        var d = v.GetProperty("diskCache");
        var entries = q.TryGetProperty("queue", out var queue) && queue.ValueKind == JsonValueKind.Array
            ? queue.EnumerateArray().Select(x => new QueueEntry(x.GetProperty("id").GetInt64(), x.GetProperty("name").GetString()!, x.GetProperty("size").GetInt64(), x.GetProperty("tries").GetInt32(), x.GetProperty("uploading").GetBoolean())).ToArray() : [];
        return new(d.GetProperty("uploadsQueued").GetInt32(), d.GetProperty("uploadsInProgress").GetInt32(), d.GetProperty("erroredFiles").GetInt32(), d.GetProperty("bytesUsed").GetInt64(), d.GetProperty("outOfSpace").GetBoolean(), v.GetProperty("inUse").GetInt32(), s.GetProperty("speed").GetDouble(), s.GetProperty("bytes").GetInt64(), entries);
    }
    public void Dispose() => http.Dispose();
}
