namespace SubGuard.Core.Models;

public sealed record AuditEntry
{
    public long Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public required string EntityType { get; init; }
    public int EntityId { get; init; }
    public required string Action { get; init; }
    public required string Summary { get; init; }
}
