using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Data.Sqlite;
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
    private Subcontractor? _selectedSubcontractor;
    private string _applicationNumber = string.Empty;
    private Subcontractor? _selectedApplicationSubcontractor;
    private DateTime? _periodStart;
    private DateTime? _periodEnd;
    private string _amountText = string.Empty;
    private string _notes = string.Empty;
    private PayApplication? _selectedPayApplication;

    public MainViewModel(SubGuardRepository repository)
    {
        _repository = repository;
        Subcontractors = [];
        PayApplications = [];
        AuditEntries = [];

        NewSubcontractorCommand = new RelayCommand(NewSubcontractor);
        SaveSubcontractorCommand = new RelayCommand(SaveSubcontractor, () => !string.IsNullOrWhiteSpace(SubcontractorName));
        DeleteSubcontractorCommand = new RelayCommand(DeleteSubcontractor, () => SelectedSubcontractor is not null);
        NewPayApplicationCommand = new RelayCommand(NewPayApplication);
        SavePayApplicationCommand = new RelayCommand(SavePayApplication, CanSavePayApplication);
        DeletePayApplicationCommand = new RelayCommand(DeletePayApplication, () => SelectedPayApplication is not null);

        RefreshData();
    }

    public ObservableCollection<Subcontractor> Subcontractors { get; }
    public ObservableCollection<PayApplication> PayApplications { get; }
    public ObservableCollection<AuditEntry> AuditEntries { get; }

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
                SavePayApplicationCommand.NotifyCanExecuteChanged();
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
                SavePayApplicationCommand.NotifyCanExecuteChanged();
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
                SavePayApplicationCommand.NotifyCanExecuteChanged();
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
                SavePayApplicationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string AmountText
    {
        get => _amountText;
        set
        {
            if (SetProperty(ref _amountText, value))
            {
                SavePayApplicationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

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
            AmountText = value?.Amount.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            Notes = value?.Notes ?? string.Empty;
            DeletePayApplicationCommand.NotifyCanExecuteChanged();
        }
    }

    public RelayCommand NewSubcontractorCommand { get; }
    public RelayCommand SaveSubcontractorCommand { get; }
    public RelayCommand DeleteSubcontractorCommand { get; }
    public RelayCommand NewPayApplicationCommand { get; }
    public RelayCommand SavePayApplicationCommand { get; }
    public RelayCommand DeletePayApplicationCommand { get; }

    public void Dispose() => _repository.Dispose();

    private void NewSubcontractor()
    {
        SelectedSubcontractor = null;
        SubcontractorName = string.Empty;
        ContactName = string.Empty;
        Email = string.Empty;
        Phone = string.Empty;
        StatusMessage = "Enter the subcontractor details and save.";
    }

    private void SaveSubcontractor()
    {
        try
        {
            _repository.SaveSubcontractor(new Subcontractor
            {
                Id = SelectedSubcontractor?.Id ?? 0,
                Name = SubcontractorName,
                ContactName = ContactName,
                Email = Email,
                Phone = Phone
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
                ? "This subcontractor is linked to pay applications and cannot be deleted."
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
        AmountText = string.Empty;
        Notes = string.Empty;
        StatusMessage = "Enter the pay-application details and save.";
    }

    private void SavePayApplication()
    {
        if (!decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount))
        {
            StatusMessage = "Enter a valid amount.";
            return;
        }

        if (PeriodStart is null || PeriodEnd is null)
        {
            StatusMessage = "Select both period dates.";
            return;
        }

        try
        {
            _repository.SavePayApplication(new PayApplication
            {
                Id = SelectedPayApplication?.Id ?? 0,
                ApplicationNumber = ApplicationNumber,
                SubcontractorId = SelectedApplicationSubcontractor!.Id,
                PeriodStart = PeriodStart.Value,
                PeriodEnd = PeriodEnd.Value,
                Amount = amount,
                Notes = Notes
            });
            RefreshData();
            NewPayApplication();
            StatusMessage = "Pay application saved.";
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not save pay application: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
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
            StatusMessage = deleted
                ? "Pay application deleted."
                : "This pay application was already removed.";
        }
        catch (SqliteException exception)
        {
            StatusMessage = $"Could not delete pay application: {exception.Message}";
        }
    }

    private bool CanSavePayApplication() =>
        !string.IsNullOrWhiteSpace(ApplicationNumber) &&
        SelectedApplicationSubcontractor is not null &&
        PeriodStart is not null &&
        PeriodEnd is not null &&
        !string.IsNullOrWhiteSpace(AmountText);

    private void RefreshData()
    {
        SelectedSubcontractor = null;
        SelectedPayApplication = null;
        ReplaceItems(Subcontractors, _repository.GetSubcontractors());
        ReplaceItems(PayApplications, _repository.GetPayApplications());
        ReplaceItems(AuditEntries, _repository.GetAuditEntries());
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
