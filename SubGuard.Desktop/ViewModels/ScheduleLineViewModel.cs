namespace SubGuard.Desktop.ViewModels;

public sealed class ScheduleLineViewModel : ObservableObject
{
    private int _lineNumber;
    private string _description;
    private decimal _scheduledValue;
    private decimal _previouslyBilledSnapshot;

    public ScheduleLineViewModel(int id, int lineNumber, string description, decimal scheduledValue, decimal previouslyBilledSnapshot)
    {
        Id = id;
        _lineNumber = lineNumber;
        _description = description;
        _scheduledValue = scheduledValue;
        _previouslyBilledSnapshot = previouslyBilledSnapshot;
    }

    public int Id { get; }
    public int LineNumber { get => _lineNumber; set => SetProperty(ref _lineNumber, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public decimal ScheduledValue { get => _scheduledValue; set => SetProperty(ref _scheduledValue, value); }
    public decimal PreviouslyBilledSnapshot
    {
        get => _previouslyBilledSnapshot;
        set => SetProperty(ref _previouslyBilledSnapshot, value);
    }
}
