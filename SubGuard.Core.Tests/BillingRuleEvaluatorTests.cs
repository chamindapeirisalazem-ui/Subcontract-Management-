using SubGuard.Core.Domain;
using SubGuard.Core.Models;

namespace SubGuard.Core.Tests;

public sealed class BillingRuleEvaluatorTests
{
    [Theory]
    [InlineData(100, 40, 60, true)]
    [InlineData(100, 40, 59.99, true)]
    [InlineData(100, 40, 60.01, false)]
    [InlineData(100, 40, -1, false)]
    public void EvaluatesR3AtAndAroundTheScheduledValue(
        decimal scheduledValue,
        decimal previouslyBilled,
        decimal currentClaim,
        bool expected)
    {
        var result = BillingRuleEvaluator.EvaluateR3(scheduledValue, previouslyBilled, currentClaim);

        Assert.Equal(expected, result.IsValid);
        Assert.Equal("R3", result.RuleCode);
    }

    [Fact]
    public void EvaluatesR4AtAndAroundTheContractCapAndExcludesCurrentAndDraftClaims()
    {
        ClaimBillingHistory[] history =
        [
            new(1, 7, ClaimStatus.Approved, 600m),
            new(2, 7, ClaimStatus.Paid, 200m),
            new(3, 7, ClaimStatus.Draft, 500m),
            new(4, 8, ClaimStatus.Paid, 500m)
        ];

        var exactlyAtCap = BillingRuleEvaluator.EvaluateR4(700m, 100m, history, 7, null, 0m);
        var editingCurrentAtCap = BillingRuleEvaluator.EvaluateR4(700m, 100m, history, 7, 1, 600m);
        var belowCap = BillingRuleEvaluator.EvaluateR4(700m, 100m, history, 7, 1, 599.99m);
        var aboveCap = BillingRuleEvaluator.EvaluateR4(700m, 100m, history, 7, 1, 600.01m);

        Assert.True(exactlyAtCap.IsValid);
        Assert.True(editingCurrentAtCap.IsValid);
        Assert.True(belowCap.IsValid);
        Assert.False(aboveCap.IsValid);
        Assert.Equal("R4", aboveCap.RuleCode);
    }
}
