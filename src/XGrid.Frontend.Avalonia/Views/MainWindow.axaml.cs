using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XGrid.Frontend.Avalonia.ViewModels;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Main shell window; maps keyboard input to the emulated Xbox pad.</summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A Window's lifetime is the process's; timers are stopped in the Closed handler, the only point Avalonia offers.")]
public partial class MainWindow : Window
{
    private readonly HashSet<Key> _pressed = [];
    private bool _spacePressed;
    private DispatcherTimer? _padTimer;
    private WindowState _restoreState = WindowState.Normal;

    /// <summary>Creates the window and installs global key handlers and the controller poll.</summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Bubble);
        // Space is the playback shortcut, not a button activation, while a
        // game runs. Tunnel so a focused toolbar button cannot swallow it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
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

    private void OnMenuSaveState(object? sender, RoutedEventArgs e)
    {
        int slot = MenuSlot(sender);
        ExecuteShellCommand(viewModel => viewModel.SaveStateToSlot(slot));
    }

    private void OnMenuLoadState(object? sender, RoutedEventArgs e)
    {
        int slot = MenuSlot(sender);
        ExecuteShellCommand(viewModel => viewModel.LoadStateFromSlot(slot));
    }

    private void OnMenuDeleteState(object? sender, RoutedEventArgs e)
    {
        int slot = MenuSlot(sender);
        ExecuteShellCommand(viewModel => viewModel.DeleteStateInSlot(slot));
    }

    /// <summary>Reads the slot number a save-state menu item carries.</summary>
    private static int MenuSlot(object? sender)
    {
        return sender is MenuItem { Tag: string text }
               && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
            ? slot
            : 0;
    }

    private void OnMenuOpenConfigFolder(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.OpenConfigFolderCommand.Execute(null));
    }

    private void OnMenuOpenScreenshotsFolder(object? sender, RoutedEventArgs e)
    {
        ExecuteShellCommand(static viewModel => viewModel.OpenScreenshotsFolderCommand.Execute(null));
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
        if (DataContext is MainWindowViewModel viewModel)
        {
            // The view model polls the assigned slots and publishes all four
            // pads; an inactive window publishes neutral so it cannot drive a
            // game from the background.
            viewModel.PollGamepads(IsActive);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _padTimer?.Stop();
        _padTimer = null;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space
            || e.KeyModifiers != KeyModifiers.None
            || IsTypingTarget()
            || DataContext is not MainWindowViewModel { IsRunning: true } viewModel)
        {
            return;
        }

        // One press toggles 4x on or off. Keep the physical-key latch so the
        // OS key repeat does not toggle it back and forth while held.
        if (_spacePressed)
        {
            e.Handled = true;
            return;
        }

        _spacePressed = true;
        viewModel.ToggleFastForward();
        e.Handled = true;
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !_spacePressed)
        {
            return;
        }

        _spacePressed = false;
        e.Handled = true;
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

            if (e.Key == Key.E)
            {
                ExecuteShellCommand(static viewModel => viewModel.EjectDiscCommand.Execute(null));
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
            case Key.Escape:
                HandleEscape();
                e.Handled = true;
                return;
            case Key.F5:
                ExecuteShellCommand(static viewModel => viewModel.TogglePowerCommand.Execute(null));
                e.Handled = true;
                return;
            case Key.F6:
                ExecuteShellCommand(static viewModel => viewModel.QuickSaveCommand.Execute(null));
                e.Handled = true;
                return;
            case Key.F7:
                ExecuteShellCommand(static viewModel => viewModel.QuickLoadCommand.Execute(null));
                e.Handled = true;
                return;
            case Key.F8:
                ExecuteShellCommand(static viewModel => viewModel.SaveScreenshotCommand.Execute(null));
                e.Handled = true;
                return;
            case Key.P:
                ExecuteShellCommand(static viewModel => viewModel.TogglePauseCommand.Execute(null));
                e.Handled = true;
                return;
            case Key.F10:
                ExecuteShellCommand(static viewModel => viewModel.StepFrameCommand.Execute(null));
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
            PublishKeyboard();
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
            PublishKeyboard();
            e.Handled = true;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // A key released in another window never reaches this one; clear the
        // latch so the next Space press toggles normally. The chosen 4x state
        // stays until Space is pressed again, paused, or the session ends.
        _spacePressed = false;

        if (_pressed.Count == 0)
        {
            return;
        }

        _pressed.Clear();
        PublishKeyboard();
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

    /// <summary>Switches the shell to fullscreen (CLI launch, menu or F11).</summary>
    public void EnterFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            return;
        }

        _restoreState = WindowState;
        WindowState = WindowState.FullScreen;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsFullscreen = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _restoreState == WindowState.FullScreen ? WindowState.Normal : _restoreState;
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.IsFullscreen = false;
            }
        }
        else
        {
            EnterFullscreen();
        }
    }

    /// <summary>
    /// Esc sequence: pause and leave fullscreen, stop the console and show the
    /// library, then close the shell. A console that cannot pause leaves
    /// fullscreen first so Esc always has a visible effect.
    /// </summary>
    private void HandleEscape()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            ToggleFullscreen();
            return;
        }

        bool fullscreen = WindowState == WindowState.FullScreen;
        switch (FullscreenEscape.Decide(
                    viewModel.IsRunning,
                    viewModel.IsPaused,
                    viewModel.HasPlayback,
                    fullscreen,
                    viewModel.IsLibraryPage))
        {
            case FullscreenEscapeAction.PauseAndLeaveFullscreen:
                viewModel.TogglePauseCommand.Execute(null);
                if (fullscreen)
                {
                    ToggleFullscreen();
                }

                if (viewModel.IsPaused)
                {
                    viewModel.ShowToast("Paused. Press Esc again to return to the library.");
                }
                else
                {
                    viewModel.ShowToast("Pause is not available; use Stop to end the console.");
                }

                break;

            case FullscreenEscapeAction.StopAndShowLibrary:
                if (fullscreen)
                {
                    ToggleFullscreen();
                }

                viewModel.StopSessionAndShowLibrary();
                break;

            case FullscreenEscapeAction.CloseApplication:
                Close();
                break;

            case FullscreenEscapeAction.LeaveFullscreen:
                if (fullscreen)
                {
                    ToggleFullscreen();
                }

                break;

            default:
                break;
        }
    }

    private void PublishKeyboard()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetKeyboardKeys(_pressed);
        }
    }
}
