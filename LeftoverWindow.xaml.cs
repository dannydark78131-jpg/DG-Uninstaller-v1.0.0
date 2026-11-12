using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using DG.Uninstaller.Models;
using DG.Uninstaller.Services;

namespace DG.Uninstaller;

public partial class LeftoverWindow : Window, INotifyPropertyChanged
{
    private readonly InstalledApp _app;
    private readonly BackupService _backupService;
    private bool _backupEnabled;
    private bool _busy;

    public LeftoverWindow(InstalledApp app, IEnumerable<LeftoverItem> items, AppSettings settings)
    {
        InitializeComponent();
        DataContext = this;
        _app = app;
        _backupService = new BackupService(settings.BackupRoot);
        Items = new ObservableCollection<LeftoverItem>(items);
        foreach (var item in Items)
            item.PropertyChanged += Item_PropertyChanged;
        _backupEnabled = settings.BackupLeftovers;
        SubtitleText.Text = $"Deep scan results for {app.DisplayName}. Review every item before cleaning.";
        BackupHintText.Text = string.IsNullOrWhiteSpace(settings.BackupRoot)
            ? "Backups are stored in the default backup folder."
            : "Backups are stored in " + settings.BackupRoot;
        UpdateSummary();
    }

    public ObservableCollection<LeftoverItem> Items { get; }
    public CleanupResult? CleanResult { get; private set; }

    public bool BackupEnabled
    {
        get => _backupEnabled;
        set
        {
            if (_backupEnabled == value) return;
            _backupEnabled = value;
            OnPropertyChanged();
        }
    }

    public string SummaryText
    {
        get
        {
            var selected = Items.Count(i => i.IsSelected);
            var size = Items.Where(i => i.IsSelected).Sum(i => i.SizeBytes);
            return $"{selected} of {Items.Count} items selected  •  {Format.Bytes(size)}  •  " +
                   $"{Items.Count(i => i.Risk >= RiskLevel.High)} high-risk items require review";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeftoverItem.IsSelected))
            UpdateSummary();
    }

    private void UpdateSummary()
    {
        OnPropertyChanged(nameof(SummaryText));
    }

    private void SelectRecommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
            item.IsSelected = item.IsRecommended;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
            item.IsSelected = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
            item.IsSelected = false;
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one item to clean.", "Nothing selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var backupText = BackupEnabled
            ? "Selected items will be moved to the backup folder first."
            : "Selected items will be permanently removed. No backup will be created.";
        var confirm = MessageBox.Show(this,
            $"Clean {selected.Count} selected item(s) for {_app.DisplayName}?\n\n{backupText}",
            "Confirm cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _busy = true;
        CleanButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        StatusTextBlock.Text = "Starting cleanup...";
        var progress = new Progress<ProgressInfo>(p =>
        {
            StatusTextBlock.Text = p.Status;
            if (p.Percent.HasValue) Progress.Value = Math.Clamp(p.Percent.Value, 0, 100);
        });

        try
        {
            CleanResult = await _backupService.CleanAsync(selected, _app.DisplayName, BackupEnabled, progress);
            var message = $"Deleted: {CleanResult.DeletedItems}\n" +
                          $"Failed: {CleanResult.FailedItems}\n" +
                          $"Freed: {Format.Bytes(CleanResult.FreedBytes)}";
            if (!string.IsNullOrWhiteSpace(CleanResult.BackupPath))
                message += "\n\nBackup: " + CleanResult.BackupPath;
            if (CleanResult.Errors.Count > 0)
                message += "\n\nFirst errors:\n" + string.Join("\n", CleanResult.Errors.Take(8));
            MessageBox.Show(this, message, "Cleanup complete",
                MessageBoxButton.OK,
                CleanResult.FailedItems > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            if (CleanResult.DeletedItems > 0)
                DialogResult = true;
        }
        catch (Exception ex)
        {
            App.Log.Error("Leftover cleanup failed: " + ex);
            MessageBox.Show(this, "Cleanup failed:\n\n" + ex.Message, "Cleanup failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            CleanButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            StatusTextBlock.Text = "Ready.";
            Progress.Value = 0;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
