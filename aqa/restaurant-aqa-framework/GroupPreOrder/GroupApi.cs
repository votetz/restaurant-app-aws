using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Restaurant_Aqa_Framework.Configuration;

namespace Restaurant_Aqa_Framework.GroupPreOrder;

internal sealed record ApiReply(int Status, JsonElement Body, string Raw)
{
    public JsonElement Field(string name)
    {
        foreach (var property in Body.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        throw new InvalidOperationException($"Response has no '{name}': {Raw}");
    }

    public string Text(string name) => Field(name).GetString() ?? throw new InvalidOperationException($"'{name}' is null: {Raw}");
}

internal sealed class GroupApi : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public GroupApi()
    {
        _client.BaseAddress = new Uri(Settings.Current.ApiBaseUrl.TrimEnd('/') + "/");
    }

    public async Task<ApiReply> SendAsync(HttpMethod method, string path, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        JsonElement json;
        try { json = JsonDocument.Parse(raw).RootElement.Clone(); }
        catch (JsonException) { json = JsonDocument.Parse("null").RootElement.Clone(); }
        return new ApiReply((int)response.StatusCode, json, raw);
    }

    public Task<ApiReply> GetAsync(string path, string? token = null) => SendAsync(HttpMethod.Get, path, token: token);
    public Task<ApiReply> PostAsync(string path, object body, string? token = null) => SendAsync(HttpMethod.Post, path, body, token);
    public Task<ApiReply> DeleteAsync(string path, object? body = null, string? token = null) => SendAsync(HttpMethod.Delete, path, body, token);
    public void Dispose() => _client.Dispose();
}
