using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, e);
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeIcon != null)
        {
            MaximizeIcon.Data = WindowState == WindowState.Maximized
                ? System.Windows.Media.Geometry.Parse("M0,3 H7 V10 H0 Z M3,0 H10 V7 H7 M3,3 V0")
                : System.Windows.Media.Geometry.Parse("M0,0 H10 V10 H0 Z");
        }
    }

    /// <summary>
    /// Re-triggers SizeToContent so the window grows or shrinks to fit the current content.
    /// Uses BeginInvoke so layout has time to measure the new content first.
    /// Clamps the result to the current screen's work area so the window never overflows.
    /// </summary>
    private void FitToContent()
    {
        Dispatcher.BeginInvoke(() =>
        {
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