namespace SubGuard.Core.Models;

public sealed record ClaimBillingHistory(
    int Id,
    int SubcontractorId,
    ClaimStatus Status,
    decimal Amount);
