using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XGrid.Frontend.Avalonia.ViewModels;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Main shell window; maps keyboard input to the emulated Xbox pad.</summary>
public partial class MainWindow : Window
{
    private readonly HashSet<Key> _pressed = new();
    private WindowState _restoreState = WindowState.Normal;

    /// <summary>Creates the window and installs global key handlers.</summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Bubble);
        Deactivated += OnDeactivated;
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

        ushort buttons = 0;
        var analog = new byte[8];
        foreach (Key key in _pressed)
        {
            if (!TryMap(key, out ushort button, out int analogIndex))
            {
                continue;
            }

            buttons |= button;
            if (analogIndex >= 0)
            {
                analog[analogIndex] = 0xFF;
            }
        }

        viewModel.SetPad(buttons, analog, new short[4]);
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
