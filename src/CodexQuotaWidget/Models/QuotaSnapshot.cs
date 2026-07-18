namespace CodexQuotaWidget.Models;

public sealed record QuotaWindow(
    double UsedPercent,
    int WindowMinutes,
    DateTimeOffset ResetsAt)
{
    public double RemainingPercent =>
        Math.Clamp(100d - UsedPercent, 0d, 100d);
}

public sealed record CreditSnapshot(
    bool HasCredits,
    bool Unlimited,
    string? Balance);

public sealed record QuotaSnapshot(
    DateTimeOffset CapturedAt,
    string? PlanType,
    string? LimitId,
    QuotaWindow? Primary,
    QuotaWindow? Secondary,
    CreditSnapshot? Credits);
