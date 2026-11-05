namespace SubGuard.Core.Models;

public sealed record PayApplication
{
    public int Id { get; init; }
    public required string ApplicationNumber { get; init; }
    public int SubcontractorId { get; init; }
    public string SubcontractorName { get; init; } = string.Empty;
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public decimal Amount { get; init; }
    public string Notes { get; init; } = string.Empty;
    public ClaimStatus Status { get; init; } = ClaimStatus.Draft;
    public DateTimeOffset? SubmittedAt { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public decimal RetainagePercent { get; init; }
    public decimal RetainageAmount => Amount * RetainagePercent / 100m;
    public InvoiceReconciliationStatus InvoiceReconciliationStatus { get; init; } = InvoiceReconciliationStatus.Pending;
    public IReadOnlyList<PayApplicationLine> Lines { get; init; } = [];
}
