using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.ViewModels;
using XGrid.Input;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Main shell window; maps keyboard and XInput controller input to the emulated Xbox pad.</summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A Window's lifetime is the process's; the reader is released in the Closed handler, the only point Avalonia offers.")]
public partial class MainWindow : Window
{
    private readonly HashSet<Key> _pressed = [];
    private readonly XInputGamepadReader _gamepad = new();
    private HostPad _hostPad = HostPad.Neutral;
    private DispatcherTimer? _padTimer;
    private bool _gamepadLogged;
    private WindowState _restoreState = WindowState.Normal;

    /// <summary>Creates the window and installs global key handlers and the controller poll.</summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Bubble);
        Deactivated += OnDeactivated;
        Closed += OnClosed;
        DataContextChanged += OnDataContextChanged;

        // ~60 Hz: XInputGetState is a cheap driver query and the emulation thread
        // only consumes the result once per batch.
        _padTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, OnPadTimer);
        _padTimer.Start();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.LogEntries.CollectionChanged += OnLogEntriesChanged;
        }
    }

    private void OnLogEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // The dock always shows the newest line, like a console tail.
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
            && e.NewItems is { Count: > 0 }
            && LogDockList.ItemCount > 0)
        {
            Dispatcher.UIThread.Post(
                () => LogDockList.ScrollIntoView(LogDockList.ItemCount - 1),
                DispatcherPriority.Background);
        }
    }

    // ---- Menu and toolbar handlers -----------------------------------------

    private void OnMenuOpenDisc(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => _ = viewModel.OpenDiscCommand.ExecuteAsync(null));
    }

    private void OnMenuChooseDisc(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => _ = viewModel.OpenDiscCommand.ExecuteAsync(null));
    }

    private void OnMenuExit(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnMenuPower(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.TogglePowerCommand.Execute(null));
    }

    private void OnMenuStop(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.StopSessionCommand.Execute(null));
    }

    private void OnMenuScreenshot(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.SaveScreenshotCommand.Execute(null));
    }

    private void OnMenuToggleLogDock(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.ToggleLogDockCommand.Execute(null));
    }

    private void OnMenuHome(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.NavigateTo(PageKind.Home));
    }

    private void OnMenuLibrary(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.NavigateTo(PageKind.Library));
    }

    private void OnMenuSettings(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.NavigateTo(PageKind.Settings));
    }

    private void OnMenuLogs(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.NavigateTo(PageKind.Logs));
    }

    private void OnMenuAbout(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.NavigateTo(PageKind.About));
    }

    private void OnMenuFullscreen(object? sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void OnPadTimer(object? sender, EventArgs e)
    {
        // Match the keyboard rule: a background window must not drive the game.
        if (!IsActive || DataContext is not MainWindowViewModel { IsRunning: true })
        {
            if (!_hostPad.IsNeutral)
            {
                _hostPad = HostPad.Neutral;
                PublishPad();
            }

            return;
        }

        GamepadMappingOptions options = DataContext is MainWindowViewModel current
            ? current.GamepadOptions
            : GamepadMappingOptions.Default;
        if (!_gamepad.TryRead(out HostPad pad, options))
        {
            if (!_hostPad.IsNeutral)
            {
                _hostPad = HostPad.Neutral;
                PublishPad();
            }

            return;
        }

        _hostPad = pad;
        if (!_gamepadLogged)
        {
            _gamepadLogged = true;
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.AddLog(
                    LogLevel.Info,
                    $"XInput controller {_gamepad.UserIndex} connected (analog + sticks).");
            }
        }

        PublishPad();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _padTimer?.Stop();
        _padTimer = null;
        _gamepad.Dispose();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.O)
            {
                ExecuteShellCommand(static viewModel => _ = viewModel.OpenDiscCommand.ExecuteAsync(null));
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S)
            {
                ExecuteShellCommand(static viewModel =>
                {
                    if (viewModel.SelectedNavigationItem?.Kind == PageKind.Settings)
                    {
                        viewModel.Settings.SaveCommand.Execute(null);
                    }
                });
                e.Handled = true;
                return;
            }
        }

        if (IsTypingTarget())
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                return;
            case Key.Escape when WindowState == WindowState.FullScreen:
                ToggleFullscreen();
                e.Handled = true;
                return;
            case Key.F5:
                ExecuteShellCommand(static viewModel => viewModel.TogglePowerCommand.Execute(null));
                e.Handled = true;
                return;
            case Key.F8:
                ExecuteShellCommand(static viewModel => viewModel.SaveScreenshotCommand.Execute(null));
                e.Handled = true;
                return;
        }

        if (DataContext is not MainWindowViewModel { IsRunning: true } viewModel)
        {
            return;
        }

        if (viewModel.TryMapKey(e.Key, out _))
        {
            _pressed.Add(e.Key);
            PublishPad();
            e.Handled = true;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (IsTypingTarget())
        {
            return;
        }

        if (_pressed.Remove(e.Key))
        {
            PublishPad();
            e.Handled = true;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_pressed.Count == 0)
        {
            return;
        }

        _pressed.Clear();
        PublishPad();
    }

    private bool IsTypingTarget()
    {
        return FocusManager.GetFocusedElement() is TextBox or ComboBox or Slider;
    }

    private void ExecuteShellCommand(Action<MainWindowViewModel> command)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            command(viewModel);
        }
    }

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _restoreState == WindowState.FullScreen ? WindowState.Normal : _restoreState;
        }
        else
        {
            _restoreState = WindowState;
            WindowState = WindowState.FullScreen;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsFullscreen = WindowState == WindowState.FullScreen;
        }
    }

    private void PublishPad()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        HostPad pad = viewModel.MergeKeyboard(_hostPad, _pressed);
        ushort buttons = pad.Buttons;
        var analog = (byte[])pad.Analog.Clone();
        var sticks = (short[])pad.Sticks.Clone();
        viewModel.SetPad(buttons, analog, sticks);
    }
}
