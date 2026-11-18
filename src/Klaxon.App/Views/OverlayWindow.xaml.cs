using System.Windows;
using Klaxon.App.ViewModels;

namespace Klaxon.App.Views;

public partial class OverlayWindow : Window
{
    public OverlayWindow(AlertViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
