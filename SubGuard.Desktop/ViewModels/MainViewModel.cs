using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SubGuard.Core.Domain;
using SubGuard.Core.Models;
using SubGuard.Core.Persistence;

namespace SubGuard.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SubGuardRepository _repository;
    private string _statusMessage = "Ready. Records are stored on this device.";
    private string _subcontractorName = string.Empty;
    private string _contactName = string.Empty;
    private string _email = string.Empty;
    private string _phone = string.Empty;
    private string _originalContractValueText = "0";
    private string _approvedChangeOrderTotalText = "0";
    private Subcontractor? _selectedSubcontractor;
    private string _applicationNumber = string.Empty;
    private Subcontractor? _selectedApplicationSubcontractor;
    private DateTime? _periodStart;
    private DateTime? _periodEnd;
    private string _notes = string.Empty;
    private string _validationSummary = "Select a subcontract and enter claim lines.";
    private PayApplication? _selectedPayApplication;
    private Subcontractor? _selectedSovSubcontractor;
    private ScheduleLineViewModel? _selectedScheduleLine;

    public MainViewModel(SubGuardRepository repository)
    {
        _repository = repository;
        Subcontractors = [];
        PayApplications = [];
        AuditEntries = [];
        ScheduleLines = [];
        ClaimLines = [];

        NewSubcontractorCommand = new RelayCommand(NewSubcontractor);
        SaveSubcontractorCommand = new RelayCommand(SaveSubcontractor, () => !string.IsNullOrWhiteSpace(SubcontractorName));
        DeleteSubcontractorCommand = new RelayCommand(DeleteSubcontractor, () => SelectedSubcontractor is not null);
        NewPayApplicationCommand = new RelayCommand(NewPayApplication);
        SavePayApplicationCommand = new RelayCommand(SavePayApplication, CanSavePayApplication);
        DeletePayApplicationCommand = new RelayCommand(
            DeletePayApplication,
            () => SelectedPayApplication?.Status == ClaimStatus.Draft);
        AddScheduleLineCommand = new RelayCommand(AddScheduleLine, () => SelectedSovSubcontractor is not null);
        RemoveScheduleLineCommand = new RelayCommand(RemoveScheduleLine, () => SelectedScheduleLine is not null);
        SaveScheduleCommand = new RelayCommand(SaveSchedule, () => SelectedSovSubcontractor is not null);
        RefreshClaimSnapshotCommand = new RelayCommand(() => ReloadClaimLines(refreshSnapshot: true));

        RefreshData();
    }

    public ObservableCollection<Subcontractor> Subcontractors { get; }
    public ObservableCollection<PayApplication> PayApplications { get; }
    public ObservableCollection<AuditEntry> AuditEntries { get; }
    public ObservableCollection<ScheduleLineViewModel> ScheduleLines { get; }
    public ObservableCollection<ClaimEntryLineViewModel> ClaimLines { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SubcontractorName
    {
        get => _subcontractorName;
        set
        {
            if (SetProperty(ref _subcontractorName, value))
            {
                SaveSubcontractorCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ContactName
    {
        get => _contactName;
        set => SetProperty(ref _contactName, value);
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string Phone
    {
        get => _phone;
        set => SetProperty(ref _phone, value);
    }

    public string OriginalContractValueText
    {
        get => _originalContractValueText;
        set => SetProperty(ref _originalContractValueText, value);
    }

    public string ApprovedChangeOrderTotalText
    {
        get => _approvedChangeOrderTotalText;
        set => SetProperty(ref _approvedChangeOrderTotalText, value);
    }

    public Subcontractor? SelectedSubcontractor
    {
        get => _selectedSubcontractor;
        set
        {
            if (!SetProperty(ref _selectedSubcontractor, value))
            {
                return;
            }

            SubcontractorName = value?.Name ?? string.Empty;
            ContactName = value?.ContactName ?? string.Empty;
            Email = value?.Email ?? string.Empty;
            Phone = value?.Phone ?? string.Empty;
            OriginalContractValueText = value?.OriginalContractValue.ToString(CultureInfo.CurrentCulture) ?? "0";
            ApprovedChangeOrderTotalText = value?.ApprovedChangeOrderTotal.ToString(CultureInfo.CurrentCulture) ?? "0";
            DeleteSubcontractorCommand.NotifyCanExecuteChanged();
        }
    }

    public string ApplicationNumber
    {
        get => _applicationNumber;
        set
        {
            if (SetProperty(ref _applicationNumber, value))
            {
                UpdateClaimValidation();
            }
        }
    }

    public Subcontractor? SelectedApplicationSubcontractor
    {
        get => _selectedApplicationSubcontractor;
        set
        {
            if (SetProperty(ref _selectedApplicationSubcontractor, value))
            {
                ReloadClaimLines();
                UpdateClaimValidation();
            }
        }
    }

    public DateTime? PeriodStart
    {
        get => _periodStart;
        set
        {
            if (SetProperty(ref _periodStart, value))
            {
                UpdateClaimValidation();
            }
        }
    }

    public DateTime? PeriodEnd
    {
        get => _periodEnd;
        set
        {
            if (SetProperty(ref _periodEnd, value))
            {
                UpdateClaimValidation();
            }
        }
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    public decimal ClaimTotal => ClaimLines.Sum(line => line.Amount);

    public PayApplication? SelectedPayApplication
    {
        get => _selectedPayApplication;
        set
        {
            if (!SetProperty(ref _selectedPayApplication, value))
            {
                return;
            }

            ApplicationNumber = value?.ApplicationNumber ?? string.Empty;
            SelectedApplicationSubcontractor = value is null
                ? null
                : Subcontractors.FirstOrDefault(item => item.Id == value.SubcontractorId);
            PeriodStart = value?.PeriodStart;
            PeriodEnd = value?.PeriodEnd;
            Notes = value?.Notes ?? string.Empty;
            DeletePayApplicationCommand.NotifyCanExecuteChanged();
            ReloadClaimLines();
            UpdateClaimValidation();
        }
    }

    public Subcontractor? SelectedSovSubcontractor
    {
        get => _selectedSovSubcontractor;
        set
        {
            if (SetProperty(ref _selectedSovSubcontractor, value))
            {
                LoadScheduleLines();
                AddScheduleLineCommand.NotifyCanExecuteChanged();
                SaveScheduleCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ScheduleLineViewModel? SelectedScheduleLine
    {
        get => _selectedScheduleLine;
        set
        {
            if (SetProperty(ref _selectedScheduleLine, value))
            {
                RemoveScheduleLineCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RelayCommand NewSubcontractorCommand { get; }
    public RelayCommand SaveSubcontractorCommand { get; }
    public RelayCommand DeleteSubcontractorCommand { get; }
    public RelayCommand NewPayApplicationCommand { get; }
    public RelayCommand SavePayApplicationCommand { get; }
    public RelayCommand DeletePayApplicationCommand { get; }
    public RelayCommand AddScheduleLineCommand { get; }
    public RelayCommand RemoveScheduleLineCommand { get; }
    public RelayCommand SaveScheduleCommand { get; }
    public RelayCommand RefreshClaimSnapshotCommand { get; }

    public void Dispose() => _repository.Dispose();

    private void NewSubcontractor()
    {
        SelectedSubcontractor = null;
        SubcontractorName = string.Empty;
        ContactName = string.Empty;
        Email = string.Empty;
        Phone = string.Empty;
        OriginalContractValueText = "0";
        ApprovedChangeOrderTotalText = "0";
        StatusMessage = "Enter the subcontractor details and save.";
    }

    private void SaveSubcontractor()
    {
        if (!decimal.TryParse(OriginalContractValueText, NumberStyles.Number, CultureInfo.CurrentCulture, out var originalValue) ||
            !decimal.TryParse(ApprovedChangeOrderTotalText, NumberStyles.Number, CultureInfo.CurrentCulture, out var changeOrderTotal) ||
            originalValue < 0 ||
            changeOrderTotal < 0)
        {
            StatusMessage = "Contract value and approved change orders must be non-negative amounts.";
            return;
        }

        try
        {
            _repository.SaveSubcontractor(new Subcontractor
            {
                Id = SelectedSubcontractor?.Id ?? 0,
                Name = SubcontractorName,
                ContactName = ContactName,
                Email = Email,
                Phone = Phone,
                OriginalContractValue = originalValue,
                ApprovedChangeOrderTotal = changeOrderTotal
            });
            RefreshData();
            NewSubcontractor();
            StatusMessage = "Subcontractor saved.";
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not save subcontractor: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void DeleteSubcontractor()
    {
        if (SelectedSubcontractor is null)
        {
            return;
        }

        try
        {
            var deleted = _repository.DeleteSubcontractor(SelectedSubcontractor.Id);
            RefreshData();
            StatusMessage = deleted
                ? "Subcontractor deleted."
                : "This subcontractor was already removed.";
        }
        catch (SqliteException exception)
        {
            StatusMessage = exception.SqliteErrorCode == 19
                ? "This subcontractor is linked to pay applications or SOV lines and cannot be deleted."
                : $"Could not delete subcontractor: {exception.Message}";
        }
    }

    private void NewPayApplication()
    {
        SelectedPayApplication = null;
        ApplicationNumber = string.Empty;
        SelectedApplicationSubcontractor = null;
        PeriodStart = null;
        PeriodEnd = null;
        Notes = string.Empty;
        ReloadClaimLines();
        UpdateClaimValidation();
        StatusMessage = "Enter the claim details and SOV line amounts.";
    }

    private void SavePayApplication()
    {
        if (PeriodStart is null || PeriodEnd is null || SelectedApplicationSubcontractor is null)
        {
            UpdateClaimValidation();
            return;
        }

        try
        {
            _repository.SavePayApplication(new PayApplication
            {
                Id = SelectedPayApplication?.Id ?? 0,
                ApplicationNumber = ApplicationNumber,
                SubcontractorId = SelectedApplicationSubcontractor.Id,
                PeriodStart = PeriodStart.Value,
                PeriodEnd = PeriodEnd.Value,
                Amount = ClaimTotal,
                Notes = Notes,
                Status = SelectedPayApplication?.Status ?? ClaimStatus.Draft,
                Lines = ClaimLines.Select(line => new PayApplicationLine
                {
                    ScheduleOfValuesLineId = line.ScheduleOfValuesLineId,
                    LineNumber = line.LineNumber,
                    Description = line.Description,
                    PreviouslyBilledSnapshot = line.PreviouslyBilled,
                    Amount = line.Amount
                }).ToArray()
            });
            RefreshData();
            NewPayApplication();
            StatusMessage = "Claim saved.";
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not save claim: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
            UpdateClaimValidation();
        }
    }

    private void DeletePayApplication()
    {
        if (SelectedPayApplication is null)
        {
            return;
        }

        try
        {
            var deleted = _repository.DeletePayApplication(SelectedPayApplication.Id);
            RefreshData();
            NewPayApplication();
            StatusMessage = deleted
                ? "Claim deleted."
                : "This claim was already removed.";
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not delete claim: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void AddScheduleLine()
    {
        if (SelectedSovSubcontractor is null)
        {
            return;
        }

        ScheduleLines.Add(new ScheduleLineViewModel(
            0,
            ScheduleLines.Count == 0 ? 1 : ScheduleLines.Max(line => line.LineNumber) + 1,
            string.Empty,
            0m,
            0m));
    }

    private void SaveSchedule()
    {
        if (SelectedSovSubcontractor is null)
        {
            return;
        }

        try
        {
            var savedLines = _repository.SaveScheduleOfValues(
                SelectedSovSubcontractor.Id,
                ScheduleLines.Select(line => new ScheduleOfValuesLine
                {
                    Id = line.Id,
                    SubcontractorId = SelectedSovSubcontractor.Id,
                    LineNumber = line.LineNumber,
                    Description = line.Description,
                    ScheduledValue = line.ScheduledValue,
                    PreviouslyBilledSnapshot = line.PreviouslyBilledSnapshot
                }).ToArray());
            ReplaceItems(ScheduleLines, savedLines.Select(ToViewModel));
            RefreshData();
            StatusMessage = "Schedule of values saved.";
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not save schedule of values: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void RemoveScheduleLine()
    {
        if (SelectedScheduleLine is not null)
        {
            ScheduleLines.Remove(SelectedScheduleLine);
            SelectedScheduleLine = null;
        }
    }

    private bool CanSavePayApplication() => GetClaimValidationErrors().Count == 0;

    private void UpdateClaimValidation()
    {
        OnPropertyChanged(nameof(ClaimTotal));
        var errors = GetClaimValidationErrors();
        ValidationSummary = errors.Count == 0
            ? "R3 and R4 checks pass. Claim is ready to save."
            : string.Join(Environment.NewLine, errors);
        SavePayApplicationCommand.NotifyCanExecuteChanged();
    }

    private List<string> GetClaimValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ApplicationNumber))
        {
            errors.Add("Enter an application number.");
        }

        if (SelectedPayApplication?.Status is ClaimStatus.Approved or ClaimStatus.Paid)
        {
            errors.Add("Approved or paid claim history is read-only.");
        }

        if (SelectedApplicationSubcontractor is null)
        {
            errors.Add("Select a subcontractor.");
            return errors;
        }

        if (PeriodStart is null || PeriodEnd is null)
        {
            errors.Add("Select both claim period dates.");
        }
        else if (PeriodStart.Value.Date > PeriodEnd.Value.Date)
        {
            errors.Add("The period start must be on or before the period end.");
        }
        else if (PayApplications.Any(application =>
                     application.SubcontractorId == SelectedApplicationSubcontractor.Id &&
                     application.Id != (SelectedPayApplication?.Id ?? 0) &&
                     PeriodStart.Value.Date <= application.PeriodEnd.Date &&
                     PeriodEnd.Value.Date >= application.PeriodStart.Date))
        {
            errors.Add("Claim periods for the same subcontract cannot overlap.");
        }

        if (ClaimLines.Count == 0)
        {
            errors.Add("Add SOV lines before entering a claim.");
        }

        foreach (var line in ClaimLines)
        {
            var result = BillingRuleEvaluator.EvaluateR3(line.ScheduledValue, line.PreviouslyBilled, line.Amount);
            if (!result.IsValid)
            {
                errors.Add($"Line {line.LineNumber}: {result.Message}");
            }
        }

        var total = ClaimTotal;
        if (total <= 0)
        {
            errors.Add("The claim total must be positive.");
        }

        var r4 = BillingRuleEvaluator.EvaluateR4(
            SelectedApplicationSubcontractor.OriginalContractValue,
            SelectedApplicationSubcontractor.ApprovedChangeOrderTotal,
            PayApplications.Select(application => new ClaimBillingHistory(
                application.Id,
                application.SubcontractorId,
                application.Status,
                application.Amount)),
            SelectedApplicationSubcontractor.Id,
            SelectedPayApplication?.Id,
            total);
        if (!r4.IsValid)
        {
            errors.Add(r4.Message);
        }

        return errors;
    }

    private void ReloadClaimLines(bool refreshSnapshot = false)
    {
        var draftAmounts = refreshSnapshot
            ? ClaimLines.ToDictionary(line => line.ScheduleOfValuesLineId, line => line.Amount)
            : null;
        ClaimLines.Clear();
        if (SelectedApplicationSubcontractor is null)
        {
            UpdateClaimValidation();
            return;
        }

        var schedule = _repository.GetScheduleOfValues(SelectedApplicationSubcontractor.Id);
        foreach (var line in schedule)
        {
            var savedLine = SelectedPayApplication?.Lines
                .FirstOrDefault(existing => existing.ScheduleOfValuesLineId == line.Id);
            var priorClaims = PayApplications
                .Where(application =>
                    application.SubcontractorId == SelectedApplicationSubcontractor.Id &&
                    application.Id != (SelectedPayApplication?.Id ?? 0))
                .SelectMany(application => application.Lines)
                .Where(existing => existing.ScheduleOfValuesLineId == line.Id)
                .Sum(existing => existing.Amount);
            var previouslyBilled = savedLine is not null && !refreshSnapshot
                ? savedLine.PreviouslyBilledSnapshot
                : line.PreviouslyBilledSnapshot + priorClaims;
            var amount = savedLine?.Amount ?? 0m;
            if (draftAmounts is not null && draftAmounts.TryGetValue(line.Id, out var draftAmount))
            {
                amount = draftAmount;
            }
            ClaimLines.Add(new ClaimEntryLineViewModel(
                line.Id,
                line.LineNumber,
                line.Description,
                line.ScheduledValue,
                previouslyBilled,
                amount,
                UpdateClaimValidation));
        }

        UpdateClaimValidation();
    }

    private void LoadScheduleLines()
    {
        ScheduleLines.Clear();
        if (SelectedSovSubcontractor is not null)
        {
            foreach (var line in _repository.GetScheduleOfValues(SelectedSovSubcontractor.Id))
            {
                ScheduleLines.Add(ToViewModel(line));
            }
        }
    }

    private void RefreshData()
    {
        SelectedSubcontractor = null;
        SelectedPayApplication = null;
        SelectedApplicationSubcontractor = null;
        SelectedSovSubcontractor = null;
        ReplaceItems(Subcontractors, _repository.GetSubcontractors());
        ReplaceItems(PayApplications, _repository.GetPayApplications());
        ReplaceItems(AuditEntries, _repository.GetAuditEntries());
        SelectedSovSubcontractor = Subcontractors.FirstOrDefault();
        UpdateClaimValidation();
    }

    private static ScheduleLineViewModel ToViewModel(ScheduleOfValuesLine line) =>
        new(line.Id, line.LineNumber, line.Description, line.ScheduledValue, line.PreviouslyBilledSnapshot);

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
