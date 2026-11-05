namespace SubGuard.Core.Models;

public sealed record PayApplicationLine
{
    public int ScheduleOfValuesLineId { get; init; }
    public int LineNumber { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal PreviouslyBilledSnapshot { get; init; }
    public decimal Amount { get; init; }
}
