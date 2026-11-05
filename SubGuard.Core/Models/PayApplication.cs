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
    public IReadOnlyList<PayApplicationLine> Lines { get; init; } = [];
}
