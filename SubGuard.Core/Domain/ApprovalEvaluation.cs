using SubGuard.Core.Models;

namespace SubGuard.Core.Domain;

public enum ApprovalRuleState
{
    Passed,
    Failed,
    NotApplicable
}

public sealed record ApprovalRuleResult(
    string RuleCode,
    ApprovalRuleState State,
    string Target,
    string Comparison,
    string Message,
    IReadOnlyList<string> CandidateDetails)
{
    public string DetailText => CandidateDetails.Count == 0
        ? Message
        : $"{Message} {string.Join(" ", CandidateDetails)}";
}

public sealed record ApprovalEvaluation(
    IReadOnlyList<ApprovalRuleResult> Rules,
    decimal RetainagePercent,
    decimal RetainageAmount)
{
    public bool CanAdvance => Rules.All(rule => rule.State is not ApprovalRuleState.Failed);
}

public sealed record ApprovalLine(
    int LineNumber,
    string Description,
    decimal ScheduledValue,
    decimal PreviouslyBilled,
    decimal CurrentClaim);

public sealed record ApprovalEvaluationInput(
    PayApplication Application,
    Subcontractor Subcontractor,
    IReadOnlyList<ClaimBillingHistory> PriorClaims,
    IReadOnlyList<ApprovalLine> Lines,
    IReadOnlyList<ComplianceRecord> ComplianceRecords,
    IReadOnlyList<LienWaiver> Waivers,
    IReadOnlyList<InvoiceRecord> Invoices);

public static class ApprovalRuleEvaluator
{
    private const decimal InvoiceTolerance = 0.01m;

    public static ApprovalEvaluation EvaluateR1(ApprovalEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rules = new List<ApprovalRuleResult>();
        foreach (var requiredType in new[] { ComplianceType.Insurance, ComplianceType.License })
        {
            var records = input.ComplianceRecords.Where(record => record.Type == requiredType).ToArray();
            if (records.Length == 0)
            {
                rules.Add(Failed(
                    "R1",
                    requiredType.ToString(),
                    $"At least one current {requiredType} record",
                    $"No {requiredType} record exists for {input.Subcontractor.Name}."));
                continue;
            }

            foreach (var record in records)
            {
                var expiry = record.ExpiryDate?.Date;
                var comparison = expiry is null
                    ? $"Expiry missing; required through {input.Application.PeriodEnd:yyyy-MM-dd}"
                    : $"{expiry:yyyy-MM-dd} >= {input.Application.PeriodEnd:yyyy-MM-dd}";
                if (expiry is null || expiry.Value < input.Application.PeriodEnd.Date)
                {
                    rules.Add(Failed(
                        "R1",
                        $"{requiredType} #{record.DocumentNumber}",
                        comparison,
                        $"Document {record.DocumentNumber} expires before the application period end or has no expiry."));
                }
            }
        }

        if (rules.Count == 0)
        {
            rules.Add(Passed("R1", "Insurance and License records", "All records valid through PeriodEnd."));
        }

        return new ApprovalEvaluation(
            rules,
            input.Application.RetainagePercent,
            input.Application.RetainageAmount);
    }

    public static ApprovalRuleResult EvaluateR2(ApprovalEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var candidateDetails = input.Waivers
            .Select(waiver =>
            {
                var failures = new List<string>();
                if (waiver.Type != WaiverType.Conditional)
                {
                    failures.Add("not Conditional");
                }

                if (waiver.Status != WaiverStatus.Signed)
                {
                    failures.Add("not Signed");
                }

                if (waiver.Amount < input.Application.Amount)
                {
                    failures.Add($"amount {waiver.Amount:C} is below gross claim {input.Application.Amount:C}");
                }

                return failures.Count == 0
                    ? $"Waiver #{waiver.Id}: eligible ({waiver.Amount:C})."
                    : $"Waiver #{waiver.Id}: {string.Join(", ", failures)}.";
            })
            .ToArray();

        var eligible = input.Waivers.Any(waiver =>
            waiver.Type == WaiverType.Conditional &&
            waiver.Status == WaiverStatus.Signed &&
            waiver.Amount >= input.Application.Amount);

        return eligible
            ? new ApprovalRuleResult(
                "R2",
                ApprovalRuleState.Passed,
                "Individual lien waiver",
                $"Signed Conditional amount >= gross claim {input.Application.Amount:C}",
                "An eligible waiver is available.",
                candidateDetails)
            : new ApprovalRuleResult(
                "R2",
                ApprovalRuleState.Failed,
                "Individual lien waiver",
                $"Signed Conditional amount >= gross claim {input.Application.Amount:C}",
                input.Waivers.Count == 0 ? "No waiver is recorded." : "No recorded waiver meets the requirement.",
                candidateDetails);
    }

    public static ApprovalRuleResult EvaluateR3(ApprovalEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Lines.Count == 0)
        {
            return Failed("R3", "SOV lines", "At least one claim line", "The application has no SOV claim lines.");
        }

        var failures = input.Lines
            .Select(line =>
            {
                var result = BillingRuleEvaluator.EvaluateR3(
                    line.ScheduledValue,
                    line.PreviouslyBilled,
                    line.CurrentClaim);
                return (Line: line, Result: result);
            })
            .Where(item => !item.Result.IsValid)
            .Select(item => $"Line {item.Line.LineNumber} ({item.Line.Description}): {item.Result.Message}")
            .ToArray();

        return failures.Length == 0
            ? Passed("R3", "SOV lines", "Every line remains within its scheduled value.")
            : Failed("R3", "SOV lines", "Previously billed + current claim <= scheduled value", string.Join(" ", failures));
    }

    public static ApprovalRuleResult EvaluateR4(ApprovalEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var result = BillingRuleEvaluator.EvaluateR4(
            input.Subcontractor.OriginalContractValue,
            input.Subcontractor.ApprovedChangeOrderTotal,
            input.PriorClaims,
            input.Application.SubcontractorId,
            input.Application.Id,
            input.Application.Amount);
        return result.IsValid
            ? Passed("R4", "Subcontract value", result.Message)
            : Failed("R4", "Subcontract value", "Prior Approved/Paid + current claim <= contract + approved change orders", result.Message);
    }

    public static ApprovalRuleResult EvaluateR5(ApprovalEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Invoices.Count == 0)
        {
            return new ApprovalRuleResult(
                "R5",
                ApprovalRuleState.NotApplicable,
                "Invoice line total",
                "No invoice recorded",
                "Invoice reconciliation is not applicable.",
                []);
        }

        var invoiceTotal = input.Invoices.Sum(invoice => invoice.Lines.Sum(line => line.Amount));
        var difference = Math.Abs(invoiceTotal - input.Application.Amount);
        var comparison = $"|{invoiceTotal:C} - {input.Application.Amount:C}| = {difference:C}; tolerance {InvoiceTolerance:C}";
        return difference <= InvoiceTolerance
            ? Passed("R5", "Combined invoice line total", comparison)
            : Failed("R5", "Combined invoice line total", comparison, "Combined invoice line total does not match the gross claim.");
    }

    public static ApprovalEvaluation EvaluateAll(ApprovalEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var r1 = EvaluateR1(input);
        return new ApprovalEvaluation(
            [
                .. r1.Rules,
                EvaluateR2(input),
                EvaluateR3(input),
                EvaluateR4(input),
                EvaluateR5(input)
            ],
            input.Application.RetainagePercent,
            input.Application.RetainageAmount);
    }

    private static ApprovalRuleResult Passed(string code, string target, string comparison) =>
        new(code, ApprovalRuleState.Passed, target, comparison, "Passed.", []);

    private static ApprovalRuleResult Failed(string code, string target, string comparison, string message) =>
        new(code, ApprovalRuleState.Failed, target, comparison, message, []);
}
