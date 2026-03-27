using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;
using FxTradeConfirmation.ViewModels;
using FxTradeConfirmation.Views;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace FxTradeConfirmation;

public partial class MainWindow : Window
{
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
        Closing += (_, _) => SavePosition();
        Closing += (_, _) => (_vm as IDisposable)?.Dispose();

        RestorePosition();
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
        }

        _vm = e.NewValue as MainViewModel;

        if (_vm != null)
        {
            _vm.Legs.CollectionChanged += OnLegsCollectionChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.ClipboardCaptureDialogRequested += OnClipboardCaptureDialogRequested;
            _vm.BringToFrontRequested += OnBringToFrontRequested;
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

                var finalLegs = dialog.ParsedLegs
                    .Select((row, i) => row.ToOvmlLeg(legs[i]))
                    .ToList();

                var finalOvml = dialog.CurrentOvml;

                switch (dialog.Result)
                {
                    case ClipboardCaptureAction.PopulateUi:
                        vm.PopulateLegsFromParsed(finalLegs);
                        vm.StatusMessage = $"✓ Form filled — {finalLegs.Count} leg(s) via {(parsedByAi ? "AI" : "regex")}";
                        break;

                    case ClipboardCaptureAction.OpenInBloomberg:
                        _ = vm.SendToBloombergAsync(finalOvml);
                        break;

                    case ClipboardCaptureAction.Both:
                        vm.PopulateLegsFromParsed(finalLegs);
                        _ = vm.SendToBloombergAsync(finalOvml);
                        vm.StatusMessage = $"✓ Form filled + sent to Bloomberg — {finalLegs.Count} leg(s)";
                        break;

                    case ClipboardCaptureAction.Reject:
                        vm.StatusMessage = "Option request rejected.";
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

    private void OnLegsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        FitToContent();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowAdminRows) or nameof(MainViewModel.HasAnyHedge))
            FitToContent();
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

    private void OnClipboardWatcherToggleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _vm?.ToggleClipboardWatcherCommand.Execute(null);
    }
}