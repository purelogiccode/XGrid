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
    private readonly HashSet<Key> _pressed = new();
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

        // ~60 Hz: XInputGetState is a cheap driver query and the emulation thread
        // only consumes the result once per batch.
        _padTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, OnPadTimer);
        _padTimer.Start();
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

        if (!_gamepad.TryRead(out HostPad pad))
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
            case Key.F6:
                ExecuteShellCommand(static viewModel => viewModel.TogglePauseCommand.Execute(null));
                e.Handled = true;
                return;
            case Key.F7:
                ExecuteShellCommand(viewModel =>
                {
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        viewModel.StepInstructionsCommand.Execute(null);
                    }
                    else
                    {
                        viewModel.StepFrameCommand.Execute(null);
                    }
                });
                e.Handled = true;
                return;
            case Key.F8:
                ExecuteShellCommand(static viewModel => _ = viewModel.SaveStateCommand.ExecuteAsync(null));
                e.Handled = true;
                return;
            case Key.F9:
                ExecuteShellCommand(static viewModel => _ = viewModel.LoadStateCommand.ExecuteAsync(null));
                e.Handled = true;
                return;
            default:
                break;
        }

        if (DataContext is not MainWindowViewModel { IsRunning: true })
        {
            return;
        }

        if (TryMap(e.Key, out _, out _))
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

        if (TryMap(e.Key, out _, out _))
        {
            _pressed.Remove(e.Key);
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
        return FocusManager?.GetFocusedElement() is TextBox or ComboBox or Slider;
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

        ushort buttons = _hostPad.Buttons;
        var analog = new byte[HostPad.AnalogCount];
        for (int i = 0; i < analog.Length; i++)
        {
            analog[i] = _hostPad.Analog[i];
        }

        var sticks = new short[HostPad.StickCount];
        Array.Copy(_hostPad.Sticks, sticks, sticks.Length);

        foreach (Key key in _pressed)
        {
            if (!TryMap(key, out ushort button, out int analogIndex))
            {
                continue;
            }

            buttons |= button;
            if (analogIndex >= 0)
            {
                // Max, not assign: a controller trigger must not be cancelled by
                // a keyboard press of the same logical button, or vice versa.
                analog[analogIndex] = Math.Max(analog[analogIndex], (byte)0xFF);
            }
        }

        viewModel.SetPad(buttons, analog, sticks);
    }

    private static bool TryMap(Key key, out ushort button, out int analogIndex)
    {
        button = 0;
        analogIndex = -1;
        switch (key)
        {
            case Key.Up:
                button = 0x0001;
                return true;
            case Key.Down:
                button = 0x0002;
                return true;
            case Key.Left:
                button = 0x0004;
                return true;
            case Key.Right:
                button = 0x0008;
                return true;
            case Key.Enter:
                button = 0x0010;
                return true;
            case Key.Back:
                button = 0x0020;
                return true;
            case Key.A:
                analogIndex = 0;
                return true;
            case Key.S:
                analogIndex = 1;
                return true;
            case Key.D:
                analogIndex = 2;
                return true;
            case Key.F:
                analogIndex = 3;
                return true;
            case Key.Q:
                analogIndex = 5;
                return true;
            case Key.E:
                analogIndex = 4;
                return true;
            case Key.Z:
                analogIndex = 6;
                return true;
            case Key.C:
                analogIndex = 7;
                return true;
            case Key.Space:
                analogIndex = 0;
                return true;
            default:
                return false;
        }
    }
}
