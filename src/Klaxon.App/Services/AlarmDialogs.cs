using System.Windows;
using Klaxon.App.ViewModels;
using Klaxon.App.Views;
using Klaxon.Core.Abstractions;
using Klaxon.Core.Localization;
using Klaxon.Core.Model;

namespace Klaxon.App.Services;

/// <summary>Opens the app's modal dialogs, keeping view models UI-free.</summary>
public sealed class AlarmDialogs
{
    private readonly ISystemClock _clock;

    public AlarmDialogs(ISystemClock clock) => _clock = clock;

    /// <summary>Shows the editor; returns the edited alarm, or null if cancelled.</summary>
    public Alarm? Edit(Alarm? existente)
    {
        var viewModel = new AlarmEditorViewModel(_clock, existente);

        var janela = new AlarmEditorWindow(viewModel)
        {
            Owner = Application.Current.MainWindow,
        };

        return janela.ShowDialog() == true ? viewModel.Result : null;
    }

    /// <summary>Asks the user to confirm deleting an alarm.</summary>
    public bool ConfirmDelete(string titulo)
    {
        var resposta = MessageBox.Show(
            Loc.Format("Confirm_Delete", titulo),
            Loc.Get("App_Name"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        return resposta == MessageBoxResult.Yes;
    }
}
