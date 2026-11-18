using System.ComponentModel;
using System.Windows;
using Klaxon.App.ViewModels;

namespace Klaxon.App.Views;

/// <summary>The main window: alarms and daily-habits tabs.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Refresh the relative texts only while the window is visible.
        IsVisibleChanged += (_, e) => _viewModel.SetActive((bool)e.NewValue);
    }

    /// <summary>Closing hides to the tray instead of quitting.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
