using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;
using FxTradeConfirmation.ViewModels;
using FxTradeConfirmation.Views;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FxTradeConfirmation;

public partial class MainWindow : Window
{
    private const string Tag = nameof(MainWindow);

    private MainViewModel? _vm;

    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FxTradeConfirmation",
        "MainWindowPos.json");

    private sealed record WindowPosition(double Left, double Top);

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnWindowClosing;
        PreviewKeyDown += OnPreviewKeyDown;

        RestorePosition();
    }

    private bool _isClosing;

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        SavePosition();

        if (_vm is not null && !_isClosing)
        {
            e.Cancel = true;
            _isClosing = true;

            try
            {
                await _vm.SaveDraftOnCloseAsync();
            }
            catch (Exception ex)
            {
                FileLogger.Instance?.Error(Tag,
                    $"SaveDraftOnCloseAsync failed — draft may not have been persisted: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                (_vm as IDisposable)?.Dispose();
                _vm = null;
            }

            // Defer Close() so it runs after the current dispatcher frame has fully
            // unwound — avoids "Window is closing" InvalidOperationException.
            Dispatcher.BeginInvoke(Close);
            return;
        }

        (_vm as IDisposable)?.Dispose();
        _vm = null;
    }

    private void SavePosition()
    {
        try
        {
            if (WindowState != WindowState.Normal)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new WindowPosition(Left, Top)));
        }
        catch { }
    }

    private void RestorePosition()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return;

            var pos = JsonSerializer.Deserialize<WindowPosition>(File.ReadAllText(_settingsPath));
            if (pos is null)
                return;

            const double minVisible = 50;
            bool onScreen =
                pos.Left + minVisible >= SystemParameters.VirtualScreenLeft
             && pos.Left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - minVisible
             && pos.Top + minVisible >= SystemParameters.VirtualScreenTop
             && pos.Top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - minVisible;

            if (!onScreen)
                return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = pos.Left;
            Top = pos.Top;
        }
        catch { }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FitToContent();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.Legs.CollectionChanged -= OnLegsCollectionChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.ClipboardCaptureDialogRequested -= OnClipboardCaptureDialogRequested;
            _vm.BringToFrontRequested -= OnBringToFrontRequested;
            _vm.QuickInputWeekendRollRequested -= OnQuickInputWeekendRollRequested;
        }

        _vm = e.NewValue as MainViewModel;

        if (_vm != null)
        {
            _vm.Legs.CollectionChanged += OnLegsCollectionChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.ClipboardCaptureDialogRequested += OnClipboardCaptureDialogRequested;
            _vm.BringToFrontRequested += OnBringToFrontRequested;
            _vm.QuickInputWeekendRollRequested += OnQuickInputWeekendRollRequested;
        }
    }

    private void OnBringToFrontRequested()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OnClipboardCaptureDialogRequested(
        ClipboardChangedEventArgs e,
        string ovml,
        IReadOnlyList<OvmlLeg> legs,
        bool parsedByAi,
        Action completed)
    {
        var dialog = new ClipboardCaptureDialog(e, ovml, legs, parsedByAi) { Owner = this };

        dialog.Show();

        dialog.Closed += (_, _) =>
        {
            try
            {
                var vm = (MainViewModel)DataContext;

                if (dialog.ParsedLegs.Count != legs.Count)
                {
                    vm.StatusMessage = $"⚠ Leg count mismatch (dialog: {dialog.ParsedLegs.Count}, original: {legs.Count}). Aborting.";
                    completed();
                    return;
                }

                var finalLegs = dialog.ParsedLegs
                    .Zip(legs, (row, original) => row.ToOvmlLeg(original))
                    .ToList();

                var finalOvml = dialog.CurrentOvml;

                switch (dialog.Result)
                {
                    case ClipboardCaptureAction.PopulateUi:
                        vm.PopulateLegsFromParsed(finalLegs);
                        PromptWeekendRoll(vm);
                        vm.StatusMessage = $"✓ Form filled — {finalLegs.Count} leg(s) via {(parsedByAi ? "AI" : "regex")}";
                        _ = vm.RestoreClipboardAsync();
                        break;

                    case ClipboardCaptureAction.OpenInBloomberg:
                        _ = vm.SendToBloombergAsync(finalOvml);
                        break;

                    case ClipboardCaptureAction.Both:
                        vm.PopulateLegsFromParsed(finalLegs);
                        PromptWeekendRoll(vm);
                        _ = vm.SendToBloombergAsync(finalOvml);
                        vm.StatusMessage = $"✓ Form filled + sent to Bloomberg — {finalLegs.Count} leg(s)";
                        break;

                    case ClipboardCaptureAction.Reject:
                        vm.StatusMessage = "Option request rejected.";
                        _ = vm.RestoreClipboardAsync();
                        break;
                }

                vm.ClipboardWatcher?.ResetLastSignature();
            }
            finally
            {
                completed();
            }
        };
    }

    /// <summary>
    /// Iterates all legs after a clipboard populate and shows the weekend roll
    /// dialog for any leg whose expiry falls on a Saturday or Sunday.
    /// </summary>
    private static void PromptWeekendRoll(MainViewModel vm)
    {
        foreach (var leg in vm.Legs)
            TradeGridControl.PromptWeekendRoll(leg);
    }

    private void OnQuickInputWeekendRollRequested()
    {
        if (_vm is not null)
            PromptWeekendRoll(_vm);
    }

    private void OnLegsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        FitToContent();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowAdminRows)
                           or nameof(MainViewModel.HasAnyHedge)
                           or nameof(MainViewModel.ShowPayoffChart)
                           or nameof(MainViewModel.ShowQuickInput))
            FitToContent();

        if (e.PropertyName == nameof(MainViewModel.ShowQuickInput) && _vm?.ShowQuickInput == true)
        {
            Dispatcher.BeginInvoke(() =>
            {
                QuickInputBox.Focus();
                QuickInputBox.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeIcon is System.Windows.Shapes.Path icon)
        {
            icon.Data = WindowState == WindowState.Maximized
                ? Geometry.Parse("M 0 3 H 7 V 10 H 0 Z M 3 3 V 0 H 10 V 7 H 7")
                : Geometry.Parse("M 0 0 H 10 V 10 H 0 Z");
        }

        if (WindowState == WindowState.Maximized)
        {
            RootBorder.CornerRadius = new CornerRadius(0);
            RootBorder.Margin = new Thickness(0);
            RootBorder.Clip = null;
        }
        else
        {
            RootBorder.CornerRadius = new CornerRadius(12);
            RootBorder.Margin = new Thickness(1);
            UpdateClip();
        }
    }

    private void RootBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateClip();
    }

    private void UpdateClip()
    {
        if (WindowState == WindowState.Maximized)
        {
            RootBorder.Clip = null;
            return;
        }

        var w = RootBorder.ActualWidth;
        var h = RootBorder.ActualHeight;
        if (w > 0 && h > 0)
        {
            RootBorder.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, w, h),
                RadiusX = 12,
                RadiusY = 12
            };
        }
    }

    private void FitToContent()
    {
        Dispatcher.BeginInvoke(() =>
        {
            InvalidateMeasure();
            InvalidateArrange();
            UpdateLayout();

            SizeToContent = SizeToContent.WidthAndHeight;

            Dispatcher.BeginInvoke(() =>
            {
                var screen = SystemParameters.WorkArea;

                if (Width > screen.Width)
                    Width = screen.Width;
                if (Height > screen.Height)
                    Height = screen.Height;

                if (Left + Width > screen.Right)
                    Left = Math.Max(screen.Left, screen.Right - Width);
                if (Top + Height > screen.Bottom)
                    Top = Math.Max(screen.Top, screen.Bottom - Height);

                SizeToContent = SizeToContent.Manual;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnActionsDropdownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>
    /// Intercepts Ctrl+V on the main window when passive clipboard monitoring is
    /// disabled. Feeds the clipboard text directly into the same parse/fill flow
    /// that the automatic watcher uses — no logic is duplicated.
    /// </summary>
    private async void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_vm is null) return;
        if (e.Key != System.Windows.Input.Key.V) return;
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
        if (_vm.ClipboardMonitorEnabled) return;   // passive watcher is active — let it handle this

        var text = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text)) return;

        e.Handled = true;

        await _vm.PasteFromClipboardAsync(text);
    }

    /// <summary>
    /// Handles Enter/Escape in the quick-input TextBox.
    /// Enter parses and applies the shorthand; Escape closes the bar.
    /// </summary>
    private void OnQuickInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            if (_vm is not null)
            {
                _vm.ApplyQuickInput(tb.Text);
                tb.Clear();
            }
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            if (_vm is not null)
                _vm.ShowQuickInput = false;
        }
    }
}
