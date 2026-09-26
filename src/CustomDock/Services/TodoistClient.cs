using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomDock.Core;

namespace CustomDock.Services;

public sealed record TodoTask(string Id, string Text, DateTime? Due, bool Done = false);

public sealed class TodoistException : Exception
{
    public TodoistException(string message, bool unauthorized = false) : base(message) => Unauthorized = unauthorized;

    public bool Unauthorized { get; }
}

/// <summary>
/// Todoist REST API v1 with a personal API token (Todoist › Settings › Integrations › Developer). Reads today's and
/// overdue tasks, completes tasks and adds new ones due today.
/// </summary>
public static class TodoistClient
{
    private const string BaseUrl = "https://api.todoist.com/api/v1/";
    private static readonly HttpClient Http = CreateClient();

    public static async Task<List<TodoTask>> GetTodayAsync(string token, CancellationToken cancellation = default)
    {
        var tasks = new List<TodoTask>();
        string? cursor = null;
        // Pages of up to 200; a day's list rarely needs a second one, but stop after a few to stay polite.
        for (int page = 0; page < 5; page++)
        {
            string url = "tasks/filter?query=" + Uri.EscapeDataString("today | overdue") + "&limit=200"
                + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            using var request = Request(HttpMethod.Get, url, token);
            using var response = await Http.SendAsync(request, cancellation);
            await EnsureSuccess(response);
            var body = await response.Content.ReadFromJsonAsync<Page>(Json, cancellation);
            if (body?.Results is null) break;
            tasks.AddRange(body.Results.Where(t => !t.Checked).Select(t => new TodoTask(t.Id, t.Content, ParseDue(t.Due))));
            cursor = body.NextCursor;
            if (string.IsNullOrEmpty(cursor)) break;
        }
        return tasks.OrderBy(t => t.Due ?? DateTime.MaxValue).ToList();
    }

    public static async Task CompleteAsync(string token, string id)
    {
        using var request = Request(HttpMethod.Post, $"tasks/{Uri.EscapeDataString(id)}/close", token);
        using var response = await Http.SendAsync(request);
        await EnsureSuccess(response);
    }

    public static async Task<TodoTask> AddAsync(string token, string text)
    {
        using var request = Request(HttpMethod.Post, "tasks", token);
        request.Content = JsonContent.Create(new { content = text, due_string = "today" });
        using var response = await Http.SendAsync(request);
        await EnsureSuccess(response);
        var task = await response.Content.ReadFromJsonAsync<ApiTask>(Json);
        return task is null ? new TodoTask("", text, DateTime.Today) : new TodoTask(task.Id, task.Content, ParseDue(task.Due));
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return request;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TodoistException(L.T("Todoist didn't accept the API token."), unauthorized: true);
        string detail = "";
        try { detail = (await response.Content.ReadAsStringAsync()).Trim(); } catch { /* no body */ }
        if (detail.Length > 160) detail = detail[..160];
        throw new TodoistException(L.T("Todoist error {0}", (int)response.StatusCode) + (detail.Length > 0 ? ": " + detail : ""));
    }

    /// <summary>"2026-09-26" (all day) or "2026-09-26T15:00:00" (with time).</summary>
    private static DateTime? ParseDue(ApiDue? due)
    {
        string? value = due?.Datetime ?? due?.Date;
        if (string.IsNullOrEmpty(value)) return null;
        return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeLocal, out var parsed)
            ? (value.Length > 10 ? parsed.ToLocalTime() : parsed.Date)
            : null;
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DockHub (+https://github.com/sametgurtuna/DockHub)");
        return client;
    }

    private sealed class Page
    {
        public List<ApiTask>? Results { get; set; }

        [JsonPropertyName("next_cursor")]
        public string? NextCursor { get; set; }
    }

    private sealed class ApiTask
    {
        public string Id { get; set; } = "";

        public string Content { get; set; } = "";

        public bool Checked { get; set; }

        public ApiDue? Due { get; set; }
    }

    private sealed class ApiDue
    {
        public string? Date { get; set; }

        public string? Datetime { get; set; }
    }
}
