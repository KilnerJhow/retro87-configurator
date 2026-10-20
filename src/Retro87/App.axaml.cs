// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;
using Avalonia.Markup.Xaml;
using Retro87.Localization;
using Retro87.Services;
using Retro87.ViewModels;

namespace Retro87;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Set by the screen tests (Avalonia.Headless): no main window, no keyboard, no process exit.</summary>
    internal static bool WithoutMainWindow { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (WithoutMainWindow)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        Loc.Instance.SetLanguage(new LanguagePreference().Load());
        var monitor = new DeviceMonitor();
        var protocol = new JpProtocolFactory(monitor);
        var main = new MainWindowViewModel(monitor, protocol);
        var window = new Views.MainWindow
        {
            DataContext = main,
        };
        main.Attach(window);
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
