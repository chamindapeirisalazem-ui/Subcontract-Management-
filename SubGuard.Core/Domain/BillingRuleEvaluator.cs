using SubGuard.Core.Models;

namespace SubGuard.Core.Domain;

public static class BillingRuleEvaluator
{
    public static BillingRuleResult EvaluateR3(
        decimal scheduledValue,
        decimal previouslyBilled,
        decimal currentClaim)
    {
        var total = previouslyBilled + currentClaim;
        return scheduledValue >= 0 &&
               previouslyBilled >= 0 &&
               currentClaim >= 0 &&
               total <= scheduledValue
            ? new BillingRuleResult("R3", true, "Within the SOV line value.")
            : new BillingRuleResult(
                "R3",
                false,
                $"Previously billed plus this claim ({total:C}) exceeds the scheduled value ({scheduledValue:C}).");
    }

    public static BillingRuleResult EvaluateR4(
        decimal originalContractValue,
        decimal approvedChangeOrderTotal,
        IEnumerable<ClaimBillingHistory> priorClaims,
        int subcontractorId,
        int? currentClaimId,
        decimal currentClaimAmount)
    {
        ArgumentNullException.ThrowIfNull(priorClaims);

        var priorApprovedOrPaid = priorClaims
            .Where(claim =>
                claim.SubcontractorId == subcontractorId &&
                claim.Id != currentClaimId &&
                claim.Status is ClaimStatus.Approved or ClaimStatus.Paid)
            .Sum(claim => claim.Amount);
        var total = priorApprovedOrPaid + currentClaimAmount;
        var cap = originalContractValue + approvedChangeOrderTotal;

        return originalContractValue >= 0 &&
               approvedChangeOrderTotal >= 0 &&
               currentClaimAmount >= 0 &&
               priorApprovedOrPaid >= 0 &&
               total <= cap
            ? new BillingRuleResult("R4", true, "Within the subcontract value.")
            : new BillingRuleResult(
                "R4",
                false,
                $"Approved/paid claims plus this claim ({total:C}) exceed the subcontract value ({cap:C}).");
    }
}
