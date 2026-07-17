using System.IO;
using System.Text;
using System.Text.Json;
using CodexQuotaWidget.Models;

namespace CodexQuotaWidget.Services;

public sealed class RolloutQuotaReader
{
    public const int MaximumTailBytes = 1_048_576;

    public QuotaSnapshot? ReadLatest(string rolloutPath)
    {
        try
        {
            using var stream = new FileStream(
                rolloutPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var start = Math.Max(0, stream.Length - MaximumTailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);

            if (start > 0)
            {
                _ = reader.ReadLine();
            }

            var text = reader.ReadToEnd();
            if (text.Length == 0)
            {
                return null;
            }

            // A writer may be between writes. Only newline-terminated records are complete.
            if (text[^1] is not '\n' and not '\r')
            {
                var lastNewline = text.LastIndexOf('\n');
                if (lastNewline < 0)
                {
                    return null;
                }

                text = text[..(lastNewline + 1)];
            }

            QuotaSnapshot? latest = null;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = ParseLine(line.TrimEnd('\r'));
                if (candidate is not null &&
                    (latest is null || candidate.CapturedAt > latest.CapturedAt))
                {
                    latest = candidate;
                }
            }

            return latest;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static QuotaSnapshot? ParseLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!HasStringValue(root, "type", "event_msg") ||
                !root.TryGetProperty("payload", out var payload) ||
                !HasStringValue(payload, "type", "token_count") ||
                !payload.TryGetProperty("rate_limits", out var rateLimits) ||
                rateLimits.ValueKind != JsonValueKind.Object ||
                !TryGetCapturedAt(root, out var capturedAt))
            {
                return null;
            }

            var primary = ParseWindow(rateLimits, "primary");
            var secondary = ParseWindow(rateLimits, "secondary");
            if (primary is null && secondary is null)
            {
                return null;
            }

            return new QuotaSnapshot(
                capturedAt,
                GetOptionalString(rateLimits, "plan_type"),
                GetOptionalString(rateLimits, "limit_id"),
                primary,
                secondary,
                ParseCredits(rateLimits));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static QuotaWindow? ParseWindow(JsonElement rateLimits, string propertyName)
    {
        if (!rateLimits.TryGetProperty(propertyName, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("used_percent", out var usedPercentElement) ||
            !usedPercentElement.TryGetDouble(out var usedPercent) ||
            !double.IsFinite(usedPercent) ||
            !window.TryGetProperty("window_minutes", out var minutesElement) ||
            !minutesElement.TryGetInt32(out var windowMinutes) ||
            windowMinutes <= 0 ||
            !window.TryGetProperty("resets_at", out var resetsElement) ||
            !resetsElement.TryGetInt64(out var resetsAt))
        {
            return null;
        }

        return new QuotaWindow(
            usedPercent,
            windowMinutes,
            DateTimeOffset.FromUnixTimeSeconds(resetsAt));
    }

    private static CreditSnapshot? ParseCredits(JsonElement rateLimits)
    {
        if (!rateLimits.TryGetProperty("credits", out var credits) ||
            credits.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var hasCredits = credits.TryGetProperty("has_credits", out var hasCreditsElement) &&
            hasCreditsElement.ValueKind is JsonValueKind.True;
        var unlimited = credits.TryGetProperty("unlimited", out var unlimitedElement) &&
            unlimitedElement.ValueKind is JsonValueKind.True;

        return new CreditSnapshot(
            hasCredits,
            unlimited,
            GetOptionalString(credits, "balance"));
    }

    private static bool TryGetCapturedAt(JsonElement root, out DateTimeOffset capturedAt)
    {
        capturedAt = default;
        return root.TryGetProperty("timestamp", out var timestamp) &&
            timestamp.ValueKind == JsonValueKind.String &&
            timestamp.TryGetDateTimeOffset(out capturedAt);
    }

    private static bool HasStringValue(JsonElement element, string propertyName, string expected) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
