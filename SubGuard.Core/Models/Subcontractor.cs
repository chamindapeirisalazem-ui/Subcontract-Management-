namespace SubGuard.Core.Models;

public sealed record Subcontractor
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string ContactName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public decimal OriginalContractValue { get; init; }
    public decimal ApprovedChangeOrderTotal { get; init; }
    public decimal RetainagePercent { get; init; }
    public int? ProjectId { get; init; }
}
