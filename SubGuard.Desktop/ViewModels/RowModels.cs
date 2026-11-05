using SubGuard.Core.Models;

namespace SubGuard.Desktop.ViewModels;

public sealed record ValidationRow(string Rule, string State, string Message);

public sealed record ProjectChoice(int Id, string Label);

public sealed record SubcontractRow(Subcontractor Subcontractor, string ProjectLabel, string ComplianceStatus)
{
    public string Name => Subcontractor.Name;
    public decimal ContractValue => Subcontractor.OriginalContractValue + Subcontractor.ApprovedChangeOrderTotal;
}
