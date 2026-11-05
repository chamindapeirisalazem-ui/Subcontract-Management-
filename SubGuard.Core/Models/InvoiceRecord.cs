namespace SubGuard.Core.Models;

public enum InvoiceReconciliationStatus
{
    Pending,
    NotApplicable,
    Matched,
    Mismatch
}

public sealed record InvoiceLine
{
    public int Id { get; init; }
    public int InvoiceId { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

public sealed record InvoiceRecord
{
    public int Id { get; init; }
    public int PayApplicationId { get; init; }
    public required string InvoiceNumber { get; init; }
    public string Provenance { get; init; } = "Manual";
    public IReadOnlyList<InvoiceLine> Lines { get; init; } = [];
    public decimal Amount => Lines.Sum(line => line.Amount);
    public InvoiceReconciliationStatus ReconciliationStatus { get; init; } = InvoiceReconciliationStatus.Pending;
}
