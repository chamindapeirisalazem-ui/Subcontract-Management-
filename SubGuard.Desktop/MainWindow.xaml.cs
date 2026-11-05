using System.IO;
using System.Windows;
using SubGuard.Core.Persistence;
using SubGuard.Desktop.ViewModels;

namespace SubGuard.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SubGuard");
        var databasePath = Path.Combine(dataDirectory, "subguard.db");
        var repository = new SubGuardRepository(databasePath);
        var approvalRepository = new SubGuardRepository(databasePath);
        _viewModel = new MainViewModel(repository, new ApprovalViewModel(approvalRepository));
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.ApprovalWorkflow.InitializeAsync();
        Closed += (_, _) => _viewModel.Dispose();
    }
}