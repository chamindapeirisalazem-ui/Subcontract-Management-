namespace SubGuard.Core.Models;

public enum WaiverType
{
    Conditional,
    Unconditional
}

public enum WaiverStatus
{
    Pending,
    Signed
}

public sealed record LienWaiver
{
    public int Id { get; init; }
    public int PayApplicationId { get; init; }
    public WaiverType Type { get; init; }
    public WaiverStatus Status { get; init; }
    public decimal Amount { get; init; }
    public DateTimeOffset? SignedAt { get; init; }
    public string FilePath { get; init; } = string.Empty;
}
