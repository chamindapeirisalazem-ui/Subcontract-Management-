using SubGuard.Core.Domain;
using SubGuard.Core.Models;

namespace SubGuard.Core.Tests;

public sealed class ApprovalRuleEvaluatorTests
{
    [Fact]
    public void R1RequiresInsuranceAndLicenseAndChecksEveryRecordThroughPeriodEnd()
    {
        var application = NewApplication();
        var validRecords = new[]
        {
            NewCompliance(ComplianceType.Insurance, new DateTime(2026, 1, 31), "INS-1"),
            NewCompliance(ComplianceType.License, new DateTime(2026, 1, 31), "LIC-1")
        };
        var valid = ApprovalRuleEvaluator.EvaluateR1(NewInput(application, validRecords));
        Assert.All(valid.Rules, result => Assert.Equal(ApprovalRuleState.Passed, result.State));

        var invalidRecords = validRecords
            .Append(NewCompliance(ComplianceType.Insurance, null, "INS-OLD"))
            .ToArray();
        var invalid = ApprovalRuleEvaluator.EvaluateR1(NewInput(application, invalidRecords));
        Assert.Contains(invalid.Rules, result =>
            result.State == ApprovalRuleState.Failed &&
            result.Target.Contains("INS-OLD", StringComparison.Ordinal));

        var missingLicense = ApprovalRuleEvaluator.EvaluateR1(NewInput(
            application,
            [NewCompliance(ComplianceType.Insurance, new DateTime(2026, 2, 1), "INS-2")]));
        Assert.Contains(missingLicense.Rules, result =>
            result.State == ApprovalRuleState.Failed &&
            result.Target == "License");
    }

    [Fact]
    public void R2RequiresSignedConditionalWaiverCoveringGrossClaim()
    {
        var input = NewInput(NewApplication(),
            waivers:
            [
                new LienWaiver { Id = 1, Type = WaiverType.Conditional, Status = WaiverStatus.Pending, Amount = 500m },
                new LienWaiver { Id = 2, Type = WaiverType.Unconditional, Status = WaiverStatus.Signed, Amount = 500m },
                new LienWaiver { Id = 3, Type = WaiverType.Conditional, Status = WaiverStatus.Signed, Amount = 499.99m }
            ]);

        var failed = ApprovalRuleEvaluator.EvaluateR2(input);
        Assert.Equal(ApprovalRuleState.Failed, failed.State);
        Assert.Equal(3, failed.CandidateDetails.Count);

        var passed = ApprovalRuleEvaluator.EvaluateR2(input with
        {
            Waivers =
            [
                .. input.Waivers,
                new LienWaiver { Id = 4, Type = WaiverType.Conditional, Status = WaiverStatus.Signed, Amount = 500m }
            ]
        });
        Assert.Equal(ApprovalRuleState.Passed, passed.State);
    }

    [Fact]
    public void R5UsesCombinedInvoiceLineTotalsWithCentToleranceAndNAToNoInvoice()
    {
        var input = NewInput(NewApplication(), invoices:
        [
            NewInvoice(1, 200m),
            NewInvoice(2, 299.99m)
        ]);

        Assert.Equal(ApprovalRuleState.Passed, ApprovalRuleEvaluator.EvaluateR5(input).State);
        Assert.Equal(
            ApprovalRuleState.Failed,
            ApprovalRuleEvaluator.EvaluateR5(input with { Invoices = [NewInvoice(3, 499.98m)] }).State);
        Assert.Equal(
            ApprovalRuleState.NotApplicable,
            ApprovalRuleEvaluator.EvaluateR5(input with { Invoices = [] }).State);
    }

    [Fact]
    public void EvaluationReportsR3R4AndInformationalRetainage()
    {
        var input = NewInput(
            NewApplication(),
            lines: [new ApprovalLine(1, "Concrete", 1000m, 400m, 100m)],
            claims: [new ClaimBillingHistory(9, 7, ClaimStatus.Approved, 400m)]);

        var evaluation = ApprovalRuleEvaluator.EvaluateAll(input);
        Assert.Equal(ApprovalRuleState.Passed, Assert.Single(evaluation.Rules, rule => rule.RuleCode == "R3").State);
        Assert.Equal(ApprovalRuleState.Passed, Assert.Single(evaluation.Rules, rule => rule.RuleCode == "R4").State);
        Assert.Equal(50m, evaluation.RetainageAmount);
    }

    private static ApprovalEvaluationInput NewInput(
        PayApplication application,
        IReadOnlyList<ComplianceRecord>? compliance = null,
        IReadOnlyList<LienWaiver>? waivers = null,
        IReadOnlyList<InvoiceRecord>? invoices = null,
        IReadOnlyList<ApprovalLine>? lines = null,
        IReadOnlyList<ClaimBillingHistory>? claims = null) =>
        new(
            application,
            new Subcontractor
            {
                Id = 7,
                Name = "Test Subcontractor",
                OriginalContractValue = 2000m,
                ApprovedChangeOrderTotal = 0m
            },
            claims ?? [],
            lines ?? [new ApprovalLine(1, "Work", 1000m, 0m, 500m)],
            compliance ?? [],
            waivers ?? [],
            invoices ?? []);

    private static PayApplication NewApplication() =>
        new()
        {
            Id = 5,
            ApplicationNumber = "PAY-5",
            SubcontractorId = 7,
            PeriodStart = new DateTime(2026, 1, 1),
            PeriodEnd = new DateTime(2026, 1, 31),
            Amount = 500m,
            RetainagePercent = 10m
        };

    private static ComplianceRecord NewCompliance(ComplianceType type, DateTime? expiry, string number) =>
        new()
        {
            Id = number.GetHashCode(),
            SubcontractorId = 7,
            Type = type,
            DocumentNumber = number,
            ExpiryDate = expiry
        };

    private static InvoiceRecord NewInvoice(int id, decimal lineAmount) =>
        new()
        {
            Id = id,
            PayApplicationId = 5,
            InvoiceNumber = $"INV-{id}",
            Lines = [new InvoiceLine { Description = "Work", Amount = lineAmount }]
        };
}
