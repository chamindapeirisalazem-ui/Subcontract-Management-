namespace SubGuard.Core.Models;

public enum ComplianceType
{
    Insurance,
    License,
    W9,
    SafetyCert,
    Other
}

public sealed record ComplianceRecord
{
    public int Id { get; init; }
    public int SubcontractorId { get; init; }
    public ComplianceType Type { get; init; }
    public string DocumentNumber { get; init; } = string.Empty;
    public DateTime? ExpiryDate { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string CurrentDayStatus => ExpiryDate is null
        ? "Expiry missing"
        : ExpiryDate.Value.Date < DateTime.Today
            ? "Expired"
            : ExpiryDate.Value.Date == DateTime.Today
                ? "Expires today"
                : "Current";
}
