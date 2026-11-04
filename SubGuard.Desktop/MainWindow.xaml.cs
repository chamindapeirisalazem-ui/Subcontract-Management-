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
        var repository = new SubGuardRepository(Path.Combine(dataDirectory, "subguard.db"));
        _viewModel = new MainViewModel(repository);
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }
}