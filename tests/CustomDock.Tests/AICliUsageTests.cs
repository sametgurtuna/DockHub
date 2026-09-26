using CustomDock.Services;

namespace CustomDock.Tests;

public class AICliUsageTests
{
    [Fact]
    public void Codex_rate_limits_are_read_from_a_token_count_event()
    {
        long reset = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        long weekReset = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds();
        string line = "{\"timestamp\":\"2026-09-26T10:00:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null,"
                      + "\"rate_limits\":{\"primary\":{\"used_percent\":42.5,\"window_minutes\":300,\"resets_at\":" + reset + "},"
                      + "\"secondary\":{\"used_percent\":12,\"window_minutes\":10080,\"resets_at\":" + weekReset + "}}}}";

        Assert.True(CodexUsageService.TryParse(line, out var data));
        Assert.Equal(42.5, data.SessionPercent);
        Assert.Equal(12, data.WeekPercent);
        Assert.NotNull(data.SessionResets);
        Assert.Equal("Weekly", data.SecondaryLabel);
        Assert.Equal("5-hour", data.PrimaryLabel);
    }

    [Fact]
    public void Codex_window_that_already_reset_counts_as_unused()
    {
        long past = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        string line = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":90,\"window_minutes\":300,\"resets_at\":" + past + "}}}}";
        Assert.True(CodexUsageService.TryParse(line, out var data));
        Assert.Equal(0, data.SessionPercent);
        Assert.Null(data.SessionResets);
    }

    [Fact]
    public void Codex_lines_without_limits_are_skipped()
    {
        Assert.False(CodexUsageService.TryParse("{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":null}}", out _));
        Assert.False(CodexUsageService.TryParse("not json", out _));
    }

    [Fact]
    public void Gemini_counts_todays_answers_once_each()
    {
        string today = DateTime.UtcNow.ToString("o");
        string yesterday = DateTime.UtcNow.AddDays(-2).ToString("o");
        using var dir = new TempDir();
        string file = dir.File("session-1.jsonl", string.Join("\n",
            "{\"sessionId\":\"s\",\"projectHash\":\"p\"}",
            "{\"id\":\"a\",\"timestamp\":\"" + today + "\",\"type\":\"user\",\"content\":\"hi\"}",
            "{\"id\":\"b\",\"timestamp\":\"" + today + "\",\"type\":\"gemini\",\"content\":\"hello\"}",
            "{\"id\":\"b\",\"timestamp\":\"" + today + "\",\"type\":\"gemini\",\"content\":\"hello\",\"tokens\":{\"input\":10,\"output\":5,\"cached\":0,\"total\":15}}",
            "{\"id\":\"c\",\"timestamp\":\"" + yesterday + "\",\"type\":\"gemini\",\"content\":\"old\"}"));

        var answers = new Dictionary<string, long>();
        GeminiUsageService.CountFile(file, answers);
        Assert.Single(answers);
        Assert.Equal(15, answers["b"]);
    }
}
