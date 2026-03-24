using System.ComponentModel;
using System.Windows;
using System.Windows.Shapes;
using FxTradeConfirmation.ViewModels;

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
        }

        _vm = e.NewValue as MainViewModel;

        if (_vm != null)
        {
            _vm.Legs.CollectionChanged += OnLegsCollectionChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
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
                ? System.Windows.Media.Geometry.Parse("M 0 3 H 7 V 10 H 0 Z M 3 3 V 0 H 10 V 7 H 7")
                : System.Windows.Media.Geometry.Parse("M 0 0 H 10 V 10 H 0 Z");
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
}