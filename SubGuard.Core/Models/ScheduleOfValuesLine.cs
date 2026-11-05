namespace SubGuard.Core.Models;

public sealed record ScheduleOfValuesLine
{
    public int Id { get; init; }
    public int SubcontractorId { get; init; }
    public int LineNumber { get; init; }
    public required string Description { get; init; }
    public decimal ScheduledValue { get; init; }
    public decimal PreviouslyBilledSnapshot { get; init; }
}
