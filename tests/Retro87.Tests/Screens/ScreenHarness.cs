// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Retro87.Localization;
using Retro87.Models;
using Retro87.Services;
using Retro87.ViewModels;
using Retro87.Views;

namespace Retro87.Tests.Screens;

/// <summary>
/// Opens the real window with a fake factory configuration, with no keyboard and without reading or writing anything
/// on the user's disk, and saves PNGs. Meant for tests and for agents that need to "see" the screen.
/// </summary>
internal sealed class ScreenHarness : IDisposable
{
    public const int Width = 1000;
    public const int Height = 700;

    public MainWindow Window { get; }
    public MainWindowViewModel ViewModel { get; }

    public ScreenHarness(string language, int width = Width, int height = Height)
    {
        Loc.Instance.SetLanguage(language);
        MappingIdSpace.Jp.ForgetTranslatedTexts();
        ViewModel = new MainWindowViewModel(new DeviceMonitor(), new JpProtocolFactory(new DeviceMonitor()));
        ViewModel.ApplyToUi(FactoryConfig());
        ViewModel.IsConnected = true; // only opens the tabs; no protocol is created
        Window = new MainWindow { DataContext = ViewModel, Width = width, Height = height };
        Window.Show();
        Settle();
    }

    /// <summary>Every key on its factory target, the ten Super Buttons on the "none" target.</summary>
    public static KeyboardConfig FactoryConfig()
    {
        var config = new KeyboardConfig { ProfileName = "Demo" };
        for (int i = 0; i < config.BoardKeys.Length; i++)
            config.BoardKeys[i] = MappingIdSpace.Jp.DefaultTargets[i];
        return config;
    }

    public void SelectTab(int index)
    {
        ViewModel.SelectedTabIndex = index;
        Settle();
    }

    public MappingRowViewModel Key(string name)
        => ViewModel.Keys.First(k => k.KeyName == name);

    public void Select(MappingRowViewModel key)
    {
        key.SelectCommand.Execute(null);
        Settle();
    }

    /// <summary>Lets layout, bindings, and the popover (which opens in a low-priority Post) settle.</summary>
    public void Settle()
    {
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    public void Save(string path)
    {
        Settle();
        using WriteableBitmapHolder frame = new(Window);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        frame.Bitmap.Save(path);
    }

    public void Dispose() => Window.Close();

    private sealed class WriteableBitmapHolder : IDisposable
    {
        public Avalonia.Media.Imaging.WriteableBitmap Bitmap { get; }

        public WriteableBitmapHolder(Window window)
            => Bitmap = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("No frame rendered (is Skia on?).");

        public void Dispose() => Bitmap.Dispose();
    }
}
