using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Klaxon.Core.Localization;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MainWindowView = Klaxon.App.Views.MainWindow;

namespace Klaxon.App.Services;

/// <summary>Manages the tray icon and its context menu.</summary>
public sealed class TrayIconService(IServiceProvider services, ILogger<TrayIconService> log) : IDisposable
{
    private readonly IServiceProvider _services = services;
    private readonly ILogger<TrayIconService> _log = log;

    private TaskbarIcon? _icon;
    private bool _disposed;

    /// <summary>Creates and shows the tray icon.</summary>
    public void Show()
    {
        if (_icon is not null)
        {
            return;
        }

        var open = new MenuItem { Header = Loc.Get("Tray_Open") };
        open.Click += (_, _) => ShowMainWindow();

        var exit = new MenuItem { Header = Loc.Get("Tray_Exit") };
        exit.Click += (_, _) =>
        {
            _log.LogInformation("Encerramento solicitado pelo menu da bandeja.");
            Application.Current.Shutdown();
        };

        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute)),
            ToolTipText = Loc.Get("App_Name"),
            ContextMenu = new ContextMenu
            {
                Items = { open, new Separator(), exit },
            },
        };

        _icon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        // enablesEfficiencyMode: false keeps Windows from throttling the timers.
        _icon.ForceCreate(enablesEfficiencyMode: false);

        _log.LogInformation("Ícone da bandeja criado.");
    }

    /// <summary>Shows a native Windows toast (the Whisper-level alert).</summary>
    public void ShowBalloon(string title, string message)
    {
        _icon?.ShowNotification(title, message);
    }

    /// <summary>Sets the tray tooltip text.</summary>
    public void SetStatus(string text)
    {
        if (_icon is not null)
        {
            _icon.ToolTipText = text;
        }
    }

    /// <summary>Shows and activates the main window.</summary>
    private void ShowMainWindow()
    {
        var window = _services.GetRequiredService<MainWindowView>();

        window.Show();

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon?.Dispose();
        _icon = null;
    }
}
