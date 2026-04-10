using System.Windows;
using System.Windows.Input;
using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;

namespace FxTradeConfirmation.Views;

public partial class OpenRecentDialog : Window
{
    private readonly IRecentTradeService _recentTradeService;
    private IReadOnlyList<RecentTradeEntry> _allEntries = [];
    private bool _isLoaded;

    /// <summary>The selected trade entry, or null if cancelled.</summary>
    public RecentTradeEntry? SelectedEntry { get; private set; }

    /// <summary>The loaded trade data for the selected entry.</summary>
    public SavedTradeData? LoadedTrade { get; private set; }

    public OpenRecentDialog(IRecentTradeService recentTradeService)
    {
        InitializeComponent();
        _recentTradeService = recentTradeService;
        Loaded += async (_, _) =>
        {
            _isLoaded = true;
            await LoadEntriesAsync();
        };
    }

    private async Task LoadEntriesAsync()
    {
        try
        {
            _allEntries = await _recentTradeService.LoadIndexAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to load recent trades: {ex.Message}", isError: true);
        }
    }

    private void ApplyFilter()
    {
        // Guard: controls may not be ready during InitializeComponent
        if (!_isLoaded || SearchBox is null || MyTradesOnly is null || TradeList is null)
            return;

        var filtered = _allEntries.AsEnumerable();

        // Filter by current user
        if (MyTradesOnly.IsChecked == true)
        {
            var currentUser = Environment.UserName.ToUpperInvariant();
            filtered = filtered.Where(e =>
                e.Username.Equals(currentUser, StringComparison.OrdinalIgnoreCase));
        }

        // Text search
        var search = SearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(e =>
                e.Counterparty.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.CurrencyPair.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.Username.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.LegSummary.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var result = filtered.ToList();
        TradeList.ItemsSource = result;

        if (result.Count == 0 && _allEntries.Count > 0)
            ShowStatus("No trades match the current filter.", isError: false);
        else if (_allEntries.Count == 0)
            ShowStatus("No recent trades found.", isError: false);
        else
            HideStatus();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        await OpenSelectedAsync();
    }

    private async void TradeList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        await OpenSelectedAsync();
    }

    private async Task OpenSelectedAsync()
    {
        if (TradeList.SelectedItem is not RecentTradeEntry entry)
        {
            ShowStatus("Please select a trade to open.", isError: true);
            return;
        }

        try
        {
            var data = await _recentTradeService.LoadTradeAsync(entry);
            if (data?.Legs == null || data.Legs.Count == 0)
            {
                ShowStatus("Trade file is empty or corrupted.", isError: true);
                return;
            }

            SelectedEntry = entry;
            LoadedTrade = data;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to load trade: {ex.Message}", isError: true);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TradeList.SelectedItem is not RecentTradeEntry entry)
        {
            ShowStatus("Please select a trade to delete.", isError: true);
            return;
        }

        try
        {
            await _recentTradeService.DeleteTradeAsync(entry);

            // Reload the full index so _allEntries stays in sync, then re-apply filter
            _allEntries = await _recentTradeService.LoadIndexAsync();
            ApplyFilter();

            ShowStatus($"Deleted: {entry.Counterparty} {entry.CurrencyPair} ({entry.SavedDate:yyyy-MM-dd HH:mm})", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to delete trade: {ex.Message}", isError: true);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && TradeList.SelectedItem != null)
        {
            _ = OpenSelectedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && TradeList.SelectedItem != null)
        {
            _ = DeleteSelectedAsync();
            e.Handled = true;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (TradeList.SelectedItem is not RecentTradeEntry entry) return;

        try
        {
            await _recentTradeService.DeleteTradeAsync(entry);
            _allEntries = await _recentTradeService.LoadIndexAsync();
            ApplyFilter();
            ShowStatus($"Deleted: {entry.Counterparty} {entry.CurrencyPair} ({entry.SavedDate:yyyy-MM-dd HH:mm})", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to delete trade: {ex.Message}", isError: true);
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusLabel.Text = message;
        StatusLabel.Foreground = isError
            ? (System.Windows.Media.Brush)FindResource("NegativeRedBrush")
            : (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        StatusLabel.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        StatusLabel.Visibility = Visibility.Collapsed;
    }
}
