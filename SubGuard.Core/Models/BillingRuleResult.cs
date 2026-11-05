namespace SubGuard.Core.Models;

public sealed record BillingRuleResult(string RuleCode, bool IsValid, string Message);
