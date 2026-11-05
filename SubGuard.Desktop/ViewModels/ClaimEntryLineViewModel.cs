namespace SubGuard.Desktop.ViewModels;

public sealed class ClaimEntryLineViewModel : ObservableObject
{
    private readonly Action _onAmountChanged;
    private decimal _amount;

    public ClaimEntryLineViewModel(
        int scheduleOfValuesLineId,
        int lineNumber,
        string description,
        decimal scheduledValue,
        decimal previouslyBilled,
        decimal amount,
        Action onAmountChanged)
    {
        ScheduleOfValuesLineId = scheduleOfValuesLineId;
        LineNumber = lineNumber;
        Description = description;
        ScheduledValue = scheduledValue;
        PreviouslyBilled = previouslyBilled;
        _amount = amount;
        _onAmountChanged = onAmountChanged;
    }

    public int ScheduleOfValuesLineId { get; }
    public int LineNumber { get; }
    public string Description { get; }
    public decimal ScheduledValue { get; }
    public decimal PreviouslyBilled { get; }

    public decimal Amount
    {
        get => _amount;
        set
        {
            if (SetProperty(ref _amount, value))
            {
                _onAmountChanged();
            }
        }
    }

    public decimal Remaining => ScheduledValue - PreviouslyBilled;
}
