using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using SubGuard.Core.Domain;
using SubGuard.Core.Models;
using SubGuard.Core.Persistence;
using SubGuard.Core.Services;

namespace SubGuard.Desktop.ViewModels;

public sealed class ApprovalViewModel : ObservableObject, IDisposable
{
    private readonly SubGuardRepository _repository;
    private readonly ApprovalWorkflowService _workflow;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private PayApplication? _selectedApplication;
    private ComplianceRecord? _selectedComplianceRecord;
    private LienWaiver? _selectedWaiver;
    private InvoiceRecord? _selectedInvoice;
    private InvoiceLineEditor? _selectedInvoiceLine;
    private ComplianceType _complianceType = ComplianceType.Insurance;
    private string _documentNumber = string.Empty;
    private DateTime? _expiryDate;
    private string _complianceFilePath = string.Empty;
    private WaiverType _waiverType = WaiverType.Conditional;
    private WaiverStatus _waiverStatus = WaiverStatus.Pending;
    private string _waiverAmountText = "0";
    private DateTime? _waiverSignedAt;
    private string _waiverFilePath = string.Empty;
    private string _invoiceNumber = string.Empty;
    private string _invoiceProvenance = "Manual";
    private string _statusMessage = "Select a pay application to review its approval gates.";
    private bool _hasCurrentEvaluation;
    private int _selectedEvidenceTabIndex;

    public ApprovalViewModel(SubGuardRepository repository)
    {
        _repository = repository;
        _workflow = new ApprovalWorkflowService(repository);

        Applications = [];
        ComplianceTypes = Enum.GetValues<ComplianceType>();
        WaiverTypes = Enum.GetValues<WaiverType>();
        WaiverStatuses = Enum.GetValues<WaiverStatus>();
        ComplianceRecords = [];
        Waivers = [];
        Invoices = [];
        InvoiceLines = [];
        Rules = [];

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        GoToRuleCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<string>(GoToRule);
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, CanSubmit);
        CompleteComplianceReviewCommand = new AsyncRelayCommand(CompleteComplianceReviewAsync, CanCompleteComplianceReview);
        CompleteWaiverReviewCommand = new AsyncRelayCommand(CompleteWaiverReviewAsync, CanCompleteWaiverReview);
        ApproveCommand = new AsyncRelayCommand(ApproveAsync, CanApprove);
        NewComplianceCommand = new RelayCommand(NewCompliance, CanEditLinkedEvidence);
        SaveComplianceCommand = new AsyncRelayCommand(SaveComplianceAsync, CanSaveCompliance);
        DeleteComplianceCommand = new AsyncRelayCommand(DeleteComplianceAsync, () => SelectedComplianceRecord is not null && CanEditLinkedEvidence());
        NewWaiverCommand = new RelayCommand(NewWaiver, CanEditLinkedEvidence);
        SaveWaiverCommand = new AsyncRelayCommand(SaveWaiverAsync, CanSaveWaiver);
        DeleteWaiverCommand = new AsyncRelayCommand(DeleteWaiverAsync, () => SelectedWaiver is not null && CanEditLinkedEvidence());
        NewInvoiceCommand = new RelayCommand(NewInvoice, CanEditLinkedEvidence);
        AddInvoiceLineCommand = new RelayCommand(AddInvoiceLine, CanEditLinkedEvidence);
        RemoveInvoiceLineCommand = new RelayCommand(
            RemoveInvoiceLine,
            () => SelectedInvoiceLine is not null && CanEditLinkedEvidence());
        SaveInvoiceCommand = new AsyncRelayCommand(SaveInvoiceAsync, CanSaveInvoice);
        DeleteInvoiceCommand = new AsyncRelayCommand(DeleteInvoiceAsync, () => SelectedInvoice is not null && CanEditLinkedEvidence());
    }

    public ObservableCollection<PayApplication> Applications { get; }
    public IReadOnlyList<ComplianceType> ComplianceTypes { get; }
    public IReadOnlyList<WaiverType> WaiverTypes { get; }
    public IReadOnlyList<WaiverStatus> WaiverStatuses { get; }
    public ObservableCollection<ComplianceRecord> ComplianceRecords { get; }
    public ObservableCollection<LienWaiver> Waivers { get; }
    public ObservableCollection<InvoiceRecord> Invoices { get; }
    public ObservableCollection<InvoiceLineEditor> InvoiceLines { get; }
    public ObservableCollection<ApprovalRuleResult> Rules { get; }
    public event Action<int>? NavigateToMainTabRequested;

    public PayApplication? SelectedApplication
    {
        get => _selectedApplication;
        set
        {
            if (SetProperty(ref _selectedApplication, value))
            {
                OnPropertyChanged(nameof(RetainageAmount));
                _hasCurrentEvaluation = false;
                ReplaceItems(Rules, []);
                UpdateCommandStates();
                _ = LoadSelectedApplicationAsync();
            }
        }
    }

    public ComplianceRecord? SelectedComplianceRecord
    {
        get => _selectedComplianceRecord;
        set
        {
            if (SetProperty(ref _selectedComplianceRecord, value))
            {
                ComplianceType = value?.Type ?? ComplianceType.Insurance;
                DocumentNumber = value?.DocumentNumber ?? string.Empty;
                ExpiryDate = value?.ExpiryDate;
                ComplianceFilePath = value?.FilePath ?? string.Empty;
                DeleteComplianceCommand.NotifyCanExecuteChanged();
                SaveComplianceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public LienWaiver? SelectedWaiver
    {
        get => _selectedWaiver;
        set
        {
            if (SetProperty(ref _selectedWaiver, value))
            {
                WaiverType = value?.Type ?? WaiverType.Conditional;
                WaiverStatus = value?.Status ?? WaiverStatus.Pending;
                WaiverAmountText = value?.Amount.ToString(CultureInfo.CurrentCulture) ?? "0";
                WaiverSignedAt = value?.SignedAt?.LocalDateTime;
                WaiverFilePath = value?.FilePath ?? string.Empty;
                DeleteWaiverCommand.NotifyCanExecuteChanged();
                SaveWaiverCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public InvoiceRecord? SelectedInvoice
    {
        get => _selectedInvoice;
        set
        {
            if (SetProperty(ref _selectedInvoice, value))
            {
                InvoiceNumber = value?.InvoiceNumber ?? string.Empty;
                InvoiceProvenance = value?.Provenance ?? "Manual";
                ReplaceInvoiceLines(value?.Lines ?? []);
                DeleteInvoiceCommand.NotifyCanExecuteChanged();
                SaveInvoiceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public InvoiceLineEditor? SelectedInvoiceLine
    {
        get => _selectedInvoiceLine;
        set
        {
            if (SetProperty(ref _selectedInvoiceLine, value))
            {
                RemoveInvoiceLineCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ComplianceType ComplianceType
    {
        get => _complianceType;
        set => SetProperty(ref _complianceType, value);
    }

    public string DocumentNumber
    {
        get => _documentNumber;
        set
        {
            if (SetProperty(ref _documentNumber, value))
            {
                SaveComplianceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DateTime? ExpiryDate
    {
        get => _expiryDate;
        set => SetProperty(ref _expiryDate, value);
    }

    public string ComplianceFilePath
    {
        get => _complianceFilePath;
        set => SetProperty(ref _complianceFilePath, value);
    }

    public WaiverType WaiverType
    {
        get => _waiverType;
        set => SetProperty(ref _waiverType, value);
    }

    public WaiverStatus WaiverStatus
    {
        get => _waiverStatus;
        set => SetProperty(ref _waiverStatus, value);
    }

    public string WaiverAmountText
    {
        get => _waiverAmountText;
        set
        {
            if (SetProperty(ref _waiverAmountText, value))
            {
                SaveWaiverCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DateTime? WaiverSignedAt
    {
        get => _waiverSignedAt;
        set => SetProperty(ref _waiverSignedAt, value);
    }

    public string WaiverFilePath
    {
        get => _waiverFilePath;
        set => SetProperty(ref _waiverFilePath, value);
    }

    public string InvoiceNumber
    {
        get => _invoiceNumber;
        set
        {
            if (SetProperty(ref _invoiceNumber, value))
            {
                SaveInvoiceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string InvoiceProvenance
    {
        get => _invoiceProvenance;
        set => SetProperty(ref _invoiceProvenance, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public decimal InvoiceLineTotal => InvoiceLines.Sum(line => line.Amount);
    public decimal RetainagePercent => SelectedApplication?.RetainagePercent ?? 0m;
    public decimal RetainageAmount => SelectedApplication?.RetainageAmount ?? 0m;
    public int SelectedEvidenceTabIndex
    {
        get => _selectedEvidenceTabIndex;
        set => SetProperty(ref _selectedEvidenceTabIndex, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public CommunityToolkit.Mvvm.Input.RelayCommand<string> GoToRuleCommand { get; }
    public AsyncRelayCommand SubmitCommand { get; }
    public AsyncRelayCommand CompleteComplianceReviewCommand { get; }
    public AsyncRelayCommand CompleteWaiverReviewCommand { get; }
    public AsyncRelayCommand ApproveCommand { get; }
    public RelayCommand NewComplianceCommand { get; }
    public AsyncRelayCommand SaveComplianceCommand { get; }
    public AsyncRelayCommand DeleteComplianceCommand { get; }
    public RelayCommand NewWaiverCommand { get; }
    public AsyncRelayCommand SaveWaiverCommand { get; }
    public AsyncRelayCommand DeleteWaiverCommand { get; }
    public RelayCommand NewInvoiceCommand { get; }
    public RelayCommand AddInvoiceLineCommand { get; }
    public RelayCommand RemoveInvoiceLineCommand { get; }
    public AsyncRelayCommand SaveInvoiceCommand { get; }
    public AsyncRelayCommand DeleteInvoiceCommand { get; }

    public Task InitializeAsync() => RefreshAsync();

    public void InvalidatePreview()
    {
        _hasCurrentEvaluation = false;
        ReplaceItems(Rules, []);
        StatusMessage = "Approval data changed. Refresh to revalidate before advancing.";
        UpdateCommandStates();
    }

    public void Dispose()
    {
        _repository.Dispose();
        _operationGate.Dispose();
    }

    private async Task RefreshAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            var selectedId = SelectedApplication?.Id;
            var applications = await Task.Run(_repository.GetPayApplications);
            ReplaceItems(Applications, applications);
            SelectedApplication = Applications.FirstOrDefault(application => application.Id == selectedId)
                ?? Applications.FirstOrDefault();
            await LoadSelectedApplicationCoreAsync();
            StatusMessage = "Approval workspace refreshed.";
        }
        catch (SqliteException exception)
        {
            _hasCurrentEvaluation = false;
            UpdateCommandStates();
            StatusMessage = $"Could not load approval workspace: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            _hasCurrentEvaluation = false;
            UpdateCommandStates();
            StatusMessage = exception.Message;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task LoadSelectedApplicationAsync()
    {
        try
        {
            await _operationGate.WaitAsync();
            try
            {
                await LoadSelectedApplicationCoreAsync();
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (SqliteException exception)
        {
            _hasCurrentEvaluation = false;
            UpdateCommandStates();
            StatusMessage = $"Could not load approval evidence: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            _hasCurrentEvaluation = false;
            UpdateCommandStates();
            StatusMessage = exception.Message;
        }
    }

    private async Task LoadSelectedApplicationCoreAsync()
    {
        if (SelectedApplication is null)
        {
            _hasCurrentEvaluation = false;
            SelectedComplianceRecord = null;
            SelectedWaiver = null;
            SelectedInvoice = null;
            ReplaceItems(ComplianceRecords, []);
            ReplaceItems(Waivers, []);
            ReplaceItems(Invoices, []);
            ReplaceItems(Rules, []);
            return;
        }

        var application = SelectedApplication;
        _hasCurrentEvaluation = false;
        ReplaceItems(Rules, []);
        UpdateCommandStates();
        SelectedComplianceRecord = null;
        SelectedWaiver = null;
        SelectedInvoice = null;
        var result = await Task.Run(() =>
        {
            var compliance = _repository.GetComplianceRecords(application.SubcontractorId);
            var waivers = _repository.GetLienWaivers(application.Id);
            var invoices = _repository.GetInvoices(application.Id);
            var evaluation = _repository.EvaluateApproval(application.Id);
            return (Compliance: compliance, Waivers: waivers, Invoices: invoices, Evaluation: evaluation);
        });

        ReplaceItems(ComplianceRecords, result.Compliance);
        ReplaceItems(Waivers, result.Waivers);
        ReplaceItems(Invoices, result.Invoices);
        ReplaceItems(Rules, result.Evaluation.Rules);
        _hasCurrentEvaluation = true;
        OnPropertyChanged(nameof(InvoiceLineTotal));
        OnPropertyChanged(nameof(RetainageAmount));
        OnPropertyChanged(nameof(RetainagePercent));
        UpdateCommandStates();
    }

    private async Task SubmitAsync() => await TransitionAsync(ApprovalTransition.Submit);
    private async Task CompleteComplianceReviewAsync() => await TransitionAsync(ApprovalTransition.CompleteComplianceReview);
    private async Task CompleteWaiverReviewAsync() => await TransitionAsync(ApprovalTransition.CompleteWaiverReview);
    private async Task ApproveAsync() => await TransitionAsync(ApprovalTransition.Approve);

    private async Task TransitionAsync(ApprovalTransition transition)
    {
        if (SelectedApplication is null)
        {
            return;
        }

        await _operationGate.WaitAsync();
        try
        {
            var applicationId = SelectedApplication.Id;
            var result = await _workflow.TransitionAsync(applicationId, transition);
            StatusMessage = result.Message;
            var applications = await Task.Run(_repository.GetPayApplications);
            ReplaceItems(Applications, applications);
            SelectedApplication = Applications.FirstOrDefault(application => application.Id == applicationId);
            await LoadSelectedApplicationCoreAsync();
            if (!result.Succeeded)
            {
                ReplaceItems(Rules, result.Evaluation.Rules);
            }
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not update approval state: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void NewCompliance()
    {
        SelectedComplianceRecord = null;
        DocumentNumber = string.Empty;
        ExpiryDate = null;
        ComplianceFilePath = string.Empty;
        ComplianceType = ComplianceType.Insurance;
    }

    private async Task SaveComplianceAsync()
    {
        if (SelectedApplication is null)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                var saved = await Task.Run(() => _repository.SaveComplianceRecord(new ComplianceRecord
                {
                    Id = SelectedComplianceRecord?.Id ?? 0,
                    SubcontractorId = SelectedApplication.SubcontractorId,
                    Type = ComplianceType,
                    DocumentNumber = DocumentNumber,
                    ExpiryDate = ExpiryDate,
                    FilePath = ComplianceFilePath
                }));
                StatusMessage = $"Saved compliance document {saved.DocumentNumber}.";
                NewCompliance();
                await LoadSelectedApplicationCoreAsync();
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not save compliance evidence: {exception.Message}";
        }
    }

    private async Task DeleteComplianceAsync()
    {
        if (SelectedComplianceRecord is null)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                await Task.Run(() => _repository.DeleteComplianceRecord(SelectedComplianceRecord.Id));
                NewCompliance();
                await LoadSelectedApplicationCoreAsync();
                StatusMessage = "Compliance evidence deleted.";
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not delete compliance evidence: {exception.Message}";
        }
    }

    private void NewWaiver()
    {
        SelectedWaiver = null;
        WaiverType = WaiverType.Conditional;
        WaiverStatus = WaiverStatus.Pending;
        WaiverAmountText = "0";
        WaiverSignedAt = null;
        WaiverFilePath = string.Empty;
    }

    private async Task SaveWaiverAsync()
    {
        if (SelectedApplication is null ||
            !decimal.TryParse(WaiverAmountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) ||
            amount < 0)
        {
            StatusMessage = "Enter a non-negative waiver amount.";
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                await Task.Run(() => _repository.SaveLienWaiver(new LienWaiver
                {
                    Id = SelectedWaiver?.Id ?? 0,
                    PayApplicationId = SelectedApplication.Id,
                    Type = WaiverType,
                    Status = WaiverStatus,
                    Amount = amount,
                    SignedAt = WaiverSignedAt is null ? null : new DateTimeOffset(WaiverSignedAt.Value),
                    FilePath = WaiverFilePath
                }));
                NewWaiver();
                await LoadSelectedApplicationCoreAsync();
                StatusMessage = "Lien waiver saved.";
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not save lien waiver: {exception.Message}";
        }
    }

    private async Task DeleteWaiverAsync()
    {
        if (SelectedWaiver is null)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                await Task.Run(() => _repository.DeleteLienWaiver(SelectedWaiver.Id));
                NewWaiver();
                await LoadSelectedApplicationCoreAsync();
                StatusMessage = "Lien waiver deleted.";
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not delete lien waiver: {exception.Message}";
        }
    }

    private void NewInvoice()
    {
        SelectedInvoice = null;
        InvoiceNumber = string.Empty;
        InvoiceProvenance = "Manual";
        ReplaceInvoiceLines([]);
        AddInvoiceLine();
    }

    private void AddInvoiceLine()
    {
        var line = new InvoiceLineEditor(string.Empty, 0m, OnInvoiceLineChanged);
        InvoiceLines.Add(line);
        SelectedInvoiceLine = line;
        OnPropertyChanged(nameof(InvoiceLineTotal));
        SaveInvoiceCommand.NotifyCanExecuteChanged();
    }

    private void RemoveInvoiceLine()
    {
        if (SelectedInvoiceLine is not null)
        {
            InvoiceLines.Remove(SelectedInvoiceLine);
            SelectedInvoiceLine = null;
            OnPropertyChanged(nameof(InvoiceLineTotal));
            SaveInvoiceCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task SaveInvoiceAsync()
    {
        if (SelectedApplication is null)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                var saved = await Task.Run(() => _repository.SaveInvoice(new InvoiceRecord
                {
                    Id = SelectedInvoice?.Id ?? 0,
                    PayApplicationId = SelectedApplication.Id,
                    InvoiceNumber = InvoiceNumber,
                    Provenance = InvoiceProvenance,
                    Lines = InvoiceLines.Select(line => new InvoiceLine
                    {
                        Description = line.Description,
                        Amount = line.Amount
                    }).ToArray()
                }));
                StatusMessage = $"Invoice {saved.InvoiceNumber} saved with a derived total of {saved.Amount:C}.";
                NewInvoice();
                await LoadSelectedApplicationCoreAsync();
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not save invoice: {exception.Message}";
        }
    }

    private async Task DeleteInvoiceAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                await Task.Run(() => _repository.DeleteInvoice(SelectedInvoice.Id));
                NewInvoice();
                await LoadSelectedApplicationCoreAsync();
                StatusMessage = "Invoice deleted.";
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not delete invoice: {exception.Message}";
        }
    }

    private void ReplaceInvoiceLines(IEnumerable<InvoiceLine> lines)
    {
        InvoiceLines.Clear();
        foreach (var line in lines)
        {
            InvoiceLines.Add(new InvoiceLineEditor(line.Description, line.Amount, OnInvoiceLineChanged));
        }

        SelectedInvoiceLine = null;
        OnPropertyChanged(nameof(InvoiceLineTotal));
        AddInvoiceLineCommand.NotifyCanExecuteChanged();
    }

    private void OnInvoiceLineChanged()
    {
        OnPropertyChanged(nameof(InvoiceLineTotal));
        SaveInvoiceCommand.NotifyCanExecuteChanged();
    }

    private void GoToRule(string? ruleCode)
    {
        switch (ruleCode)
        {
            case "R1":
                SelectedEvidenceTabIndex = 0;
                break;
            case "R2":
                SelectedEvidenceTabIndex = 1;
                break;
            case "R5":
                SelectedEvidenceTabIndex = 2;
                break;
            case "R3":
                NavigateToMainTabRequested?.Invoke(2);
                break;
            case "R4":
                NavigateToMainTabRequested?.Invoke(1);
                break;
        }
    }

    private bool CanSubmit() => SelectedApplication?.Status == ClaimStatus.Draft && _hasCurrentEvaluation;
    private bool CanCompleteComplianceReview() =>
        SelectedApplication?.Status == ClaimStatus.PendingCompliance && RulesPass("R1");
    private bool CanCompleteWaiverReview() =>
        SelectedApplication?.Status == ClaimStatus.PendingWaiver && RulesPass("R1", "R2");
    private bool CanApprove() =>
        SelectedApplication?.Status == ClaimStatus.PendingApproval && RulesPass("R1", "R2", "R3", "R4", "R5");
    private bool CanSaveCompliance() =>
        CanEditLinkedEvidence() && !string.IsNullOrWhiteSpace(DocumentNumber);
    private bool CanSaveWaiver() =>
        CanEditLinkedEvidence() &&
        decimal.TryParse(WaiverAmountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) &&
        amount >= 0;
    private bool CanSaveInvoice() =>
        CanEditLinkedEvidence() &&
        !string.IsNullOrWhiteSpace(InvoiceNumber) &&
        InvoiceLines.Count > 0 &&
        InvoiceLines.All(line => !string.IsNullOrWhiteSpace(line.Description) && line.Amount >= 0);

    private bool CanEditLinkedEvidence() =>
        SelectedApplication is not null &&
        SelectedApplication.Status is not ClaimStatus.Approved and not ClaimStatus.Paid;

    private bool RulesPass(params string[] ruleCodes)
    {
        if (!_hasCurrentEvaluation || ruleCodes.Length == 0)
        {
            return false;
        }

        var rules = Rules.Where(rule => ruleCodes.Contains(rule.RuleCode, StringComparer.Ordinal)).ToArray();
        return rules.Length > 0 && rules.All(rule => rule.State is not ApprovalRuleState.Failed);
    }

    private void UpdateCommandStates()
    {
        SubmitCommand.NotifyCanExecuteChanged();
        CompleteComplianceReviewCommand.NotifyCanExecuteChanged();
        CompleteWaiverReviewCommand.NotifyCanExecuteChanged();
        ApproveCommand.NotifyCanExecuteChanged();
        NewComplianceCommand.NotifyCanExecuteChanged();
        SaveComplianceCommand.NotifyCanExecuteChanged();
        DeleteComplianceCommand.NotifyCanExecuteChanged();
        NewWaiverCommand.NotifyCanExecuteChanged();
        SaveWaiverCommand.NotifyCanExecuteChanged();
        DeleteWaiverCommand.NotifyCanExecuteChanged();
        NewInvoiceCommand.NotifyCanExecuteChanged();
        AddInvoiceLineCommand.NotifyCanExecuteChanged();
        SaveInvoiceCommand.NotifyCanExecuteChanged();
        DeleteInvoiceCommand.NotifyCanExecuteChanged();
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}

public sealed class InvoiceLineEditor : ObservableObject
{
    private readonly Action _changed;
    private string _description;
    private decimal _amount;

    public InvoiceLineEditor(string description, decimal amount, Action changed)
    {
        _description = description;
        _amount = amount;
        _changed = changed;
    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value))
            {
                _changed();
            }
        }
    }

    public decimal Amount
    {
        get => _amount;
        set
        {
            if (SetProperty(ref _amount, value))
            {
                _changed();
            }
        }
    }
}
