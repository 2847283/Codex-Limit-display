using CodexQuotaWidget.Services;

namespace CodexQuotaWidget.Tests;

public sealed class RolloutQuotaReaderTests
{
    [Fact]
    public void ReadLatest_ReturnsRemainingQuotaFromValidEvent()
    {
        const string line = """
            {"timestamp":"2026-07-16T12:58:43.758Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","plan_type":"plus","primary":{"used_percent":39.0,"window_minutes":10080,"resets_at":1784797605},"secondary":null,"credits":{"has_credits":false,"unlimited":false,"balance":"0"}}}}
            """;
        var path = WriteRollout(line + Environment.NewLine);

        try
        {
            var snapshot = new RolloutQuotaReader().ReadLatest(path);

            Assert.NotNull(snapshot);
            Assert.Equal(61d, snapshot.Primary!.RemainingPercent);
            Assert.Equal(10080, snapshot.Primary.WindowMinutes);
            Assert.Null(snapshot.Secondary);
            Assert.Equal("plus", snapshot.PlanType);
            Assert.Equal(
                DateTimeOffset.FromUnixTimeSeconds(1784797605),
                snapshot.Primary.ResetsAt);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadLatest_ReturnsEveryQuotaWindowThatIsPresent()
    {
        const string line = """
            {"timestamp":"2026-07-16T13:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":10,"window_minutes":300,"resets_at":1784797605},"secondary":{"used_percent":25,"window_minutes":10080,"resets_at":1785000000}}}}
            """;
        var path = WriteRollout(line + Environment.NewLine);

        try
        {
            var snapshot = new RolloutQuotaReader().ReadLatest(path);

            Assert.Equal(90d, snapshot!.Primary!.RemainingPercent);
            Assert.Equal(75d, snapshot.Secondary!.RemainingPercent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadLatest_IgnoresPartiallyWrittenTrailingLine()
    {
        const string complete = """
            {"timestamp":"2026-07-16T13:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":40,"window_minutes":300,"resets_at":1784797605}}}}
            """;
        const string partial = "{\"timestamp\":\"2026-07-16T13:01:00Z\",\"type\":\"event_msg\"";
        var path = WriteRollout(complete + Environment.NewLine + partial);

        try
        {
            var snapshot = new RolloutQuotaReader().ReadLatest(path);

            Assert.Equal(60d, snapshot!.Primary!.RemainingPercent);
            Assert.Equal(DateTimeOffset.Parse("2026-07-16T13:00:00Z"), snapshot.CapturedAt);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadLatest_DoesNotInspectMessageTextForRateLimits()
    {
        const string line = """
            {"timestamp":"2026-07-16T13:00:00Z","type":"response_item","payload":{"role":"user","content":"rate_limits used_percent 99"}}
            """;
        var path = WriteRollout(line + Environment.NewLine);

        try
        {
            Assert.Null(new RolloutQuotaReader().ReadLatest(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadLatest_SelectsNewestCapturedTimestampInsteadOfLastLine()
    {
        const string newer = """
            {"timestamp":"2026-07-16T13:02:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":20,"window_minutes":300,"resets_at":1784797605}}}}
            """;
        const string older = """
            {"timestamp":"2026-07-16T13:01:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":80,"window_minutes":300,"resets_at":1784797605}}}}
            """;
        var path = WriteRollout(newer + Environment.NewLine + older + Environment.NewLine);

        try
        {
            var snapshot = new RolloutQuotaReader().ReadLatest(path);

            Assert.Equal(DateTimeOffset.Parse("2026-07-16T13:02:00Z"), snapshot!.CapturedAt);
            Assert.Equal(80d, snapshot.Primary!.RemainingPercent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-20d, 100d)]
    [InlineData(125d, 0d)]
    public void ReadLatest_ClampsRemainingPercentage(double usedPercent, double expectedRemaining)
    {
        const string template = """
            {"timestamp":"2026-07-16T13:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":USED_PERCENT,"window_minutes":300,"resets_at":1784797605}}}}
            """;
        var line = template.Replace(
            "USED_PERCENT",
            usedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var path = WriteRollout(line + Environment.NewLine);

        try
        {
            var snapshot = new RolloutQuotaReader().ReadLatest(path);

            Assert.Equal(expectedRemaining, snapshot!.Primary!.RemainingPercent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteRollout(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"codex-quota-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, contents);
        return path;
    }
}
