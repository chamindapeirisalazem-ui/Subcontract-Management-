using SubGuard.Core.Models;

namespace SubGuard.Core.Domain;

public enum ApprovalTransition
{
    Submit,
    CompleteComplianceReview,
    CompleteWaiverReview,
    Approve
}

public sealed record ApprovalTransitionResult(
    bool Succeeded,
    ClaimStatus Status,
    ApprovalEvaluation Evaluation,
    string Message);
