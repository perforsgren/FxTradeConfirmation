using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;
using FxTradeConfirmation.ViewModels;
using FxTradeConfirmation.Views;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FxTradeConfirmation;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
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
        }

        _vm = e.NewValue as MainViewModel;

        if (_vm != null)
        {
            _vm.Legs.CollectionChanged += OnLegsCollectionChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.ClipboardCaptureDialogRequested += OnClipboardCaptureDialogRequested;
        }
    }

    private void OnClipboardCaptureDialogRequested(
        ClipboardChangedEventArgs e,
        string ovml,
        IReadOnlyList<OvmlLeg> legs,
        bool parsedByAi,
        Action completed)
    {
        try
        {
            var dialog = new ClipboardCaptureDialog(e, ovml, legs, parsedByAi) { Owner = this };
            dialog.ShowDialog();

            var vm = (MainViewModel)DataContext;

            var finalLegs = dialog.ParsedLegs
                .Select((row, i) => row.ToOvmlLeg(legs[i]))
                .ToList();

            switch (dialog.Result)
            {
                case ClipboardCaptureAction.PopulateUi:
                    vm.PopulateLegsFromParsed(finalLegs);
                    vm.StatusMessage = $"✓ Form filled — {finalLegs.Count} leg(s) via {(parsedByAi ? "AI" : "regex")}";
                    break;

                case ClipboardCaptureAction.OpenInBloomberg:
                    _ = vm.SendToBloombergAsync(ovml);
                    break;

                case ClipboardCaptureAction.Both:
                    vm.PopulateLegsFromParsed(finalLegs);
                    _ = vm.SendToBloombergAsync(ovml);
                    vm.StatusMessage = $"✓ Form filled + sent to Bloomberg — {finalLegs.Count} leg(s)";
                    break;

                case ClipboardCaptureAction.Reject:
                    vm.StatusMessage = "Option request rejected.";
                    break;
            }

            // Allow the same text to be captured again if the user copies it once more
            vm.ClipboardWatcher?.ResetLastSignature();
        }
        finally
        {
            completed();
        }
    }

    private void OnLegsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        FitToContent();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowAdminRows) or nameof(MainViewModel.HasAnyHedge))
            FitToContent();
    }

    // --- Window chrome button handlers ---
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeIcon is Path icon)
        {
            icon.Data = WindowState == WindowState.Maximized
                ? Geometry.Parse("M 0 3 H 7 V 10 H 0 Z M 3 3 V 0 H 10 V 7 H 7")
                : Geometry.Parse("M 0 0 H 10 V 10 H 0 Z");
        }

        // Remove rounded corners when maximized
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

    /// <summary>
    /// Re-triggers SizeToContent so the window grows or shrinks to fit the current content.
    /// Forces an invalidate + layout pass first so collapsed elements are fully measured at zero height.
    /// Clamps the result to the current screen's work area so the window never overflows.
    /// </summary>
    private void FitToContent()
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Force WPF to re-measure everything (collapsed rows → 0 height)
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

    private void OnClipboardWatcherToggleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _vm?.ToggleClipboardWatcherCommand.Execute(null);
    }
}