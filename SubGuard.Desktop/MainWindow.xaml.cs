using System.IO;
using System.Windows;
using System.Windows.Input;
using SubGuard.Core.Persistence;
using SubGuard.Desktop.ViewModels;

namespace SubGuard.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var dataDirectory = Environment.GetEnvironmentVariable("SUBGUARD_DATA_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SubGuard");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "subguard.db");
        var repository = new SubGuardRepository(databasePath);
        var approvalRepository = new SubGuardRepository(databasePath);
        _viewModel = new MainViewModel(repository, new ApprovalViewModel(approvalRepository));
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.ApprovalWorkflow.InitializeAsync();
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D7)
            {
                _viewModel.SelectedTabIndex = e.Key - Key.D1;
                e.Handled = true;
            }
        };
        Closed += (_, _) => _viewModel.Dispose();
    }
}