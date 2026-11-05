using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
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
    private string _retainagePercentText = "0";
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
    private int _selectedTabIndex;
    private string _projectSearchText = string.Empty;
    private string _projectStatusFilter = "All";
    private string _projectsError = string.Empty;
    private Project? _selectedProject;
    private string _projectNumber = string.Empty;
    private string _projectName = string.Empty;
    private string _projectOwner = string.Empty;
    private ProjectStatus _projectStatus = ProjectStatus.Active;
    private int _subcontractProjectId;
    private SubcontractRow? _selectedSubcontractRow;
    private string _subcontractComplianceStatus = string.Empty;

    public MainViewModel(SubGuardRepository repository, ApprovalViewModel approvalWorkflow)
    {
        _repository = repository;
        ApprovalWorkflow = approvalWorkflow;
        ApprovalWorkflow.NavigateToMainTabRequested += NavigateToMainTab;
        ApprovalWorkflow.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ApprovalViewModel.SelectedApplication))
            {
                OnPropertyChanged(nameof(ContextLine));
            }
        };
        Subcontractors = [];
        PayApplications = [];
        AuditEntries = [];
        ScheduleLines = [];
        ClaimLines = [];
        Projects = [];
        ProjectChoices = [];
        SubcontractRows = [];
        ProjectSubcontracts = [];
        SubcontractPayApplications = [];
        SubcontractCompliance = [];
        ValidationRows = [];
        ProjectsView = CollectionViewSource.GetDefaultView(Projects);
        ProjectsView.Filter = FilterProject;
        ProjectsView.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ProjectsEmptyMessage));

        NewProjectCommand = new RelayCommand(NewProject);
        SaveProjectCommand = new RelayCommand(
            SaveProject,
            () => !string.IsNullOrWhiteSpace(ProjectNumber) && !string.IsNullOrWhiteSpace(ProjectName));
        DeleteProjectCommand = new RelayCommand(DeleteProject, () => SelectedProject is not null);
        ClearProjectFiltersCommand = new RelayCommand(() =>
        {
            ProjectSearchText = string.Empty;
            ProjectStatusFilter = "All";
        });

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
    public ApprovalViewModel ApprovalWorkflow { get; }
    public ObservableCollection<Project> Projects { get; }
    public ICollectionView ProjectsView { get; }
    public ObservableCollection<ProjectChoice> ProjectChoices { get; }
    public ObservableCollection<SubcontractRow> SubcontractRows { get; }
    public ObservableCollection<SubcontractRow> ProjectSubcontracts { get; }
    public ObservableCollection<PayApplication> SubcontractPayApplications { get; }
    public ObservableCollection<ComplianceRecord> SubcontractCompliance { get; }
    public ObservableCollection<ValidationRow> ValidationRows { get; }
    public IReadOnlyList<string> ProjectStatusFilters { get; } = ["All", "Active", "Closed"];
    public IReadOnlyList<ProjectStatus> ProjectStatuses { get; } = [ProjectStatus.Active, ProjectStatus.Closed];
    public RelayCommand NewProjectCommand { get; }
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand DeleteProjectCommand { get; }
    public RelayCommand ClearProjectFiltersCommand { get; }

    private static readonly string[] PageNames = ["Projects", "Subcontracts", "Pay applications", "Compliance", "Audit history"];

    public string PageName => PageNames[Math.Clamp(SelectedTabIndex, 0, PageNames.Length - 1)];

    public string ContextLine => SelectedTabIndex switch
    {
        0 when SelectedProject is not null => $"Projects  ›  {SelectedProject.ProjectNumber} {SelectedProject.Name}",
        1 when SelectedSubcontractor is not null => $"Subcontracts  ›  {SelectedSubcontractor.Name}",
        2 when SelectedPayApplication is not null => $"Pay applications  ›  {SelectedPayApplication.ApplicationNumber} ({SelectedPayApplication.SubcontractorName})",
        3 when ApprovalWorkflow.SelectedApplication is not null => $"Compliance  ›  {ApprovalWorkflow.SelectedApplication.ApplicationNumber}",
        _ => PageName
    };

    public string ProjectSearchText
    {
        get => _projectSearchText;
        set
        {
            if (SetProperty(ref _projectSearchText, value))
            {
                ProjectsView.Refresh();
                OnPropertyChanged(nameof(ProjectsEmptyMessage));
            }
        }
    }

    public string ProjectStatusFilter
    {
        get => _projectStatusFilter;
        set
        {
            if (SetProperty(ref _projectStatusFilter, value))
            {
                ProjectsView.Refresh();
                OnPropertyChanged(nameof(ProjectsEmptyMessage));
            }
        }
    }

    public string ProjectsError
    {
        get => _projectsError;
        private set => SetProperty(ref _projectsError, value);
    }

    public string ProjectsEmptyMessage
    {
        get
        {
            if (Projects.Count == 0)
            {
                return "No projects yet. Use New project to add the first one.";
            }

            return ProjectsView.IsEmpty ? "No projects match the current search or status filter." : string.Empty;
        }
    }

    public bool ProjectsFilteredEmpty => Projects.Count > 0 && ProjectsView.IsEmpty;

    public Project? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            ProjectNumber = value?.ProjectNumber ?? string.Empty;
            ProjectName = value?.Name ?? string.Empty;
            ProjectOwner = value?.OwnerName ?? string.Empty;
            ProjectStatus = value?.Status ?? ProjectStatus.Active;
            ReplaceItems(
                ProjectSubcontracts,
                value is null ? [] : SubcontractRows.Where(row => row.Subcontractor.ProjectId == value.Id));
            DeleteProjectCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ContextLine));
        }
    }

    public string ProjectNumber
    {
        get => _projectNumber;
        set
        {
            if (SetProperty(ref _projectNumber, value))
            {
                SaveProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (SetProperty(ref _projectName, value))
            {
                SaveProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ProjectOwner
    {
        get => _projectOwner;
        set => SetProperty(ref _projectOwner, value);
    }

    public ProjectStatus ProjectStatus
    {
        get => _projectStatus;
        set => SetProperty(ref _projectStatus, value);
    }

    public int SubcontractProjectId
    {
        get => _subcontractProjectId;
        set => SetProperty(ref _subcontractProjectId, value);
    }

    public SubcontractRow? SelectedSubcontractRow
    {
        get => _selectedSubcontractRow;
        set
        {
            if (SetProperty(ref _selectedSubcontractRow, value) &&
                SelectedSubcontractor?.Id != value?.Subcontractor.Id)
            {
                SelectedSubcontractor = value?.Subcontractor;
            }
        }
    }

    public string SubcontractComplianceStatus
    {
        get => _subcontractComplianceStatus;
        private set => SetProperty(ref _subcontractComplianceStatus, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                OnPropertyChanged(nameof(PageName));
                OnPropertyChanged(nameof(ContextLine));
            }
        }
    }

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

    public string RetainagePercentText
    {
        get => _retainagePercentText;
        set => SetProperty(ref _retainagePercentText, value);
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
            RetainagePercentText = value?.RetainagePercent.ToString(CultureInfo.CurrentCulture) ?? "0";
            SubcontractProjectId = value?.ProjectId ?? 0;
            SelectedSovSubcontractor = value is null
                ? null
                : Subcontractors.FirstOrDefault(item => item.Id == value.Id);
            SelectedSubcontractRow = value is null
                ? null
                : SubcontractRows.FirstOrDefault(row => row.Subcontractor.Id == value.Id);
            LoadSubcontractDetail(value);
            DeleteSubcontractorCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ContextLine));
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
            OnPropertyChanged(nameof(ContextLine));
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

    public void Dispose()
    {
        _repository.Dispose();
        ApprovalWorkflow.Dispose();
    }

    private void NewSubcontractor()
    {
        SelectedSubcontractor = null;
        SubcontractorName = string.Empty;
        ContactName = string.Empty;
        Email = string.Empty;
        Phone = string.Empty;
        OriginalContractValueText = "0";
        ApprovedChangeOrderTotalText = "0";
        RetainagePercentText = "0";
        SubcontractProjectId = 0;
        StatusMessage = "Enter the subcontractor details and save.";
    }

    private void SaveSubcontractor()
    {
        if (!decimal.TryParse(OriginalContractValueText, NumberStyles.Number, CultureInfo.CurrentCulture, out var originalValue) ||
            !decimal.TryParse(ApprovedChangeOrderTotalText, NumberStyles.Number, CultureInfo.CurrentCulture, out var changeOrderTotal) ||
            !decimal.TryParse(RetainagePercentText, NumberStyles.Number, CultureInfo.CurrentCulture, out var retainagePercent) ||
            originalValue < 0 ||
            changeOrderTotal < 0 ||
            retainagePercent is < 0 or > 100)
        {
            StatusMessage = "Contract values and approved change orders must be non-negative; retainage must be from 0 to 100 percent.";
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
                ApprovedChangeOrderTotal = changeOrderTotal,
                RetainagePercent = retainagePercent,
                ProjectId = SubcontractProjectId == 0 ? null : SubcontractProjectId
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
            var scheduleSubcontractorId = SelectedSovSubcontractor.Id;
            RefreshData();
            SelectedSubcontractor = Subcontractors.FirstOrDefault(item => item.Id == scheduleSubcontractorId);
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
        ReplaceItems(
            ValidationRows,
            errors.Count == 0
                ? [new ValidationRow("R3", "Pass", "Each line stays within its scheduled value."),
                   new ValidationRow("R4", "Pass", "Cumulative billing stays within the contract value.")]
                : errors.Select(error => new ValidationRow("Check", "Fail", error)));
        SavePayApplicationCommand.NotifyCanExecuteChanged();
    }

    private List<string> GetClaimValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ApplicationNumber))
        {
            errors.Add("Enter an application number.");
        }

        if (SelectedPayApplication is not null && SelectedPayApplication.Status != ClaimStatus.Draft)
        {
            errors.Add("Submitted claim history is read-only in this editor.");
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
        ApprovalWorkflow.InvalidatePreview();
        var selectedProjectId = SelectedProject?.Id;
        SelectedSubcontractor = null;
        SelectedProject = null;
        SelectedPayApplication = null;
        SelectedApplicationSubcontractor = null;
        SelectedSovSubcontractor = null;
        ReplaceItems(Subcontractors, _repository.GetSubcontractors());
        ReplaceItems(PayApplications, _repository.GetPayApplications());
        ReplaceItems(AuditEntries, _repository.GetAuditEntries());
        LoadProjects();
        SelectedProject = Projects.FirstOrDefault(project => project.Id == selectedProjectId);
        UpdateClaimValidation();
    }

    private void LoadProjects()
    {
        try
        {
            var projects = _repository.GetProjects();
            ReplaceItems(Projects, projects);
            ProjectsError = string.Empty;
            ReplaceItems(
                ProjectChoices,
                new[] { new ProjectChoice(0, "(No project)") }
                    .Concat(projects.Select(project => new ProjectChoice(project.Id, $"{project.ProjectNumber} — {project.Name}"))));
            var labels = projects.ToDictionary(project => project.Id, project => project.ProjectNumber);
            ReplaceItems(
                SubcontractRows,
                Subcontractors.Select(subcontractor => new SubcontractRow(
                    subcontractor,
                    subcontractor.ProjectId is { } projectId && labels.TryGetValue(projectId, out var label) ? label : "—",
                    GetComplianceStatus(_repository.GetComplianceRecords(subcontractor.Id)))));
        }
        catch (SqliteException exception)
        {
            ProjectsError = $"Projects could not be loaded: {exception.Message}";
        }

        OnPropertyChanged(nameof(ProjectsEmptyMessage));
        OnPropertyChanged(nameof(ProjectsFilteredEmpty));
    }

    private static string GetComplianceStatus(IReadOnlyList<ComplianceRecord> records)
    {
        if (records.Count == 0)
        {
            return "No records";
        }

        foreach (var status in new[] { "Expired", "Expiry missing", "Expires today" })
        {
            if (records.Any(record => record.CurrentDayStatus == status))
            {
                return status;
            }
        }

        return "Current";
    }

    private void LoadSubcontractDetail(Subcontractor? subcontractor)
    {
        if (subcontractor is null)
        {
            SubcontractPayApplications.Clear();
            SubcontractCompliance.Clear();
            SubcontractComplianceStatus = string.Empty;
            return;
        }

        ReplaceItems(
            SubcontractPayApplications,
            PayApplications.Where(application => application.SubcontractorId == subcontractor.Id));
        var records = _repository.GetComplianceRecords(subcontractor.Id);
        ReplaceItems(SubcontractCompliance, records);
        SubcontractComplianceStatus = GetComplianceStatus(records);
    }

    private bool FilterProject(object item)
    {
        if (item is not Project project)
        {
            return false;
        }

        if (ProjectStatusFilter != "All" && project.Status.ToString() != ProjectStatusFilter)
        {
            return false;
        }

        var term = ProjectSearchText.Trim();
        return term.Length == 0 ||
               project.ProjectNumber.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
               project.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
               project.OwnerName.Contains(term, StringComparison.CurrentCultureIgnoreCase);
    }

    private void NewProject()
    {
        SelectedProject = null;
        ProjectNumber = string.Empty;
        ProjectName = string.Empty;
        ProjectOwner = string.Empty;
        ProjectStatus = ProjectStatus.Active;
        ProjectsError = string.Empty;
        StatusMessage = "Enter the project details and save.";
    }

    private void SaveProject()
    {
        try
        {
            var saved = _repository.SaveProject(new Project
            {
                Id = SelectedProject?.Id ?? 0,
                ProjectNumber = ProjectNumber,
                Name = ProjectName,
                OwnerName = ProjectOwner,
                Status = ProjectStatus
            });
            RefreshData();
            SelectedProject = Projects.FirstOrDefault(project => project.Id == saved.Id);
            ProjectsError = string.Empty;
            StatusMessage = "Project saved.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or SqliteException)
        {
            ProjectsError = exception.Message;
            StatusMessage = "Project not saved.";
        }
    }

    private void DeleteProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        try
        {
            var deleted = _repository.DeleteProject(SelectedProject.Id);
            RefreshData();
            NewProject();
            StatusMessage = deleted ? "Project deleted." : "This project was already removed.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or SqliteException)
        {
            ProjectsError = exception.Message;
            StatusMessage = "Project not deleted.";
        }
    }

    private void NavigateToMainTab(int tabIndex)
    {
        var application = ApprovalWorkflow.SelectedApplication;
        RefreshData();
        if (tabIndex == 2 && application is not null)
        {
            SelectedPayApplication = PayApplications.FirstOrDefault(item => item.Id == application.Id);
        }
        else if (tabIndex == 1 && application is not null)
        {
            SelectedSubcontractor = Subcontractors.FirstOrDefault(item => item.Id == application.SubcontractorId);
        }

        SelectedTabIndex = tabIndex;
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
