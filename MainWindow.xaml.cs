using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using DG.Uninstaller.Models;
using DG.Uninstaller.Services;

namespace DG.Uninstaller;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly RegistryScanner _registryScanner = new();
    private readonly StoreAppService _storeService = new();
    private readonly PortableScanner _portableScanner = new();
    private readonly UninstallService _uninstallService = new();
    private readonly LeftoverScanner _leftoverScanner = new();
    private readonly ProcessService _processService = new();
    private readonly RestorePointService _restorePointService = new();
    private readonly CleanerService _cleaner = new();
    private BackupService? _backupService;

    private string _searchText = "";
    private string _selectedFilter = "All apps";
    private string _selectedSort = "Name";
    private InstalledApp? _selectedApp;
    private CleanupCategory? _selectedCategory;
    private BackupSession? _selectedBackup;
    private string _statusText = "Ready.";
    private double _progressValue;
    private bool _busy;
    private string _portableRootsText = "";

    public MainWindow()
    {
        InitializeComponent();
        Settings = SettingsService.Load();
        _portableRootsText = string.Join(Environment.NewLine, Settings.PortableScanRoots);
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = FilterApp;
        ApplySort();
        DataContext = this;

        Apps.CollectionChanged += Apps_CollectionChanged;
        Categories.CollectionChanged += Categories_CollectionChanged;
        OnPropertyChanged(nameof(AppsSummary));
    }

    public ObservableCollection<InstalledApp> Apps { get; } = new();
    public ICollectionView AppsView { get; }
    public ObservableCollection<CleanupCategory> Categories { get; } = new();
    public ObservableCollection<BackupSession> Backups { get; } = new();
    public AppSettings Settings { get; }
    public string[] FilterOptions { get; } = { "All apps", "Desktop", "Store", "Portable", "Microsoft", "System components" };
    public string[] SortOptions { get; } = { "Name", "Size (largest)", "Install date (newest)", "Publisher" };

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            AppsView.Refresh();
        }
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (_selectedFilter == value) return;
            _selectedFilter = value;
            OnPropertyChanged();
            AppsView.Refresh();
        }
    }

    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (_selectedSort == value) return;
            _selectedSort = value;
            OnPropertyChanged();
            ApplySort();
        }
    }

    public InstalledApp? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (_selectedApp == value) return;
            _selectedApp = value;
            OnPropertyChanged();
        }
    }

    public CleanupCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory == value) return;
            _selectedCategory = value;
            OnPropertyChanged();
        }
    }

    public BackupSession? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (_selectedBackup == value) return;
            _selectedBackup = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        set
        {
            if (Math.Abs(_progressValue - value) < 0.01) return;
            _progressValue = value;
            OnPropertyChanged();
        }
    }

    public string PortableRootsText
    {
        get => _portableRootsText;
        set
        {
            if (_portableRootsText == value) return;
            _portableRootsText = value;
            OnPropertyChanged();
        }
    }

    public string AppsSummary => $"{Apps.Count} apps  •  {Apps.Count(a => a.IsSelected)} selected";

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAppsAsync();
        LoadBackups();
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveSettings(false);
        base.OnClosed(e);
    }

    private void Apps_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (InstalledApp app in e.NewItems)
                app.PropertyChanged += App_PropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (InstalledApp app in e.OldItems)
                app.PropertyChanged -= App_PropertyChanged;
        }
        OnPropertyChanged(nameof(AppsSummary));
    }

    private void Categories_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (CleanupCategory category in e.NewItems)
                category.PropertyChanged += Category_PropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (CleanupCategory category in e.OldItems)
                category.PropertyChanged -= Category_PropertyChanged;
        }
        UpdateCleanerSummary();
    }

    private void App_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstalledApp.IsSelected))
            OnPropertyChanged(nameof(AppsSummary));
    }

    private void Category_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupCategory.IsSelected))
            UpdateCleanerSummary();
    }

    private bool FilterApp(object item)
    {
        if (item is not InstalledApp app) return false;

        if (SelectedFilter == "System components")
        {
            if (!app.IsSystemComponent) return false;
        }
        else
        {
            if (!Settings.ShowSystemComponents && app.IsSystemComponent) return false;
        }

        if (!Settings.IncludeMicrosoft && app.IsMicrosoft && SelectedFilter != "Microsoft") return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var query = SearchText.Trim().ToLowerInvariant();
            if (!app.SearchText.Contains(query)) return false;
        }

        return SelectedFilter switch
        {
            "Desktop" => !app.IsStore && !app.IsPortable,
            "Store" => app.IsStore,
            "Portable" => app.IsPortable,
            "Microsoft" => app.IsMicrosoft,
            "System components" => app.IsSystemComponent,
            _ => true
        };
    }

    private void ApplySort()
    {
        if (AppsView == null) return;
        AppsView.SortDescriptions.Clear();
        switch (SelectedSort)
        {
            case "Size (largest)":
                AppsView.SortDescriptions.Add(new SortDescription(nameof(InstalledApp.EstimatedSizeBytes), ListSortDirection.Descending));
                break;
            case "Install date (newest)":
                AppsView.SortDescriptions.Add(new SortDescription(nameof(InstalledApp.InstallDate), ListSortDirection.Descending));
                break;
            case "Publisher":
                AppsView.SortDescriptions.Add(new SortDescription(nameof(InstalledApp.Publisher), ListSortDirection.Ascending));
                AppsView.SortDescriptions.Add(new SortDescription(nameof(InstalledApp.DisplayName), ListSortDirection.Ascending));
                break;
            default:
                AppsView.SortDescriptions.Add(new SortDescription(nameof(InstalledApp.DisplayName), ListSortDirection.Ascending));
                break;
        }
        AppsView.Refresh();
    }

    private async Task RefreshAppsAsync()
    {
        if (_busy) return;
        SetBusy(true, "Scanning installed applications...");
        try
        {
            var progress = new Progress<ProgressInfo>(p =>
            {
                StatusText = p.Status;
                if (p.Percent.HasValue) ProgressValue = Math.Clamp(p.Percent.Value, 0, 100);
            });

            var registry = await _registryScanner.ScanAsync();
            var storeTask = _storeService.ScanAsync(Settings.StoreAllUsers);
            var portableTask = _portableScanner.ScanAsync(
                Settings.PortableScanRoots,
                registry.Select(a => a.InstallLocation),
                progress);
            await Task.WhenAll(storeTask, portableTask);

            var all = registry
                .Concat(storeTask.Result)
                .Concat(portableTask.Result)
                .GroupBy(a => $"{a.DisplayName}|{a.Publisher}|{a.TypeText}|{a.InstallLocation}".ToLowerInvariant())
                .Select(g => g.First())
                .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Apps.Clear();
            foreach (var app in all) Apps.Add(app);
            AppsView.Refresh();
            OnPropertyChanged(nameof(AppsSummary));
            StatusText = $"{Apps.Count} applications found.";
            ProgressValue = 100;
        }
        catch (Exception ex)
        {
            App.Log.Error("Application scan failed: " + ex);
            MessageBox.Show(this, "Could not scan installed applications:\n\n" + ex.Message,
                "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Scan failed.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RefreshApps_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAppsAsync();
    }

    private async void AppsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        await PreviewLeftoversAsync();
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var targets = Apps.Where(a => a.IsSelected).ToList();
        if (targets.Count == 0 && SelectedApp != null)
            targets.Add(SelectedApp);
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Select an application first.", "No application selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (targets.Count > 1)
        {
            var confirmMany = MessageBox.Show(this,
                $"Uninstall {targets.Count} selected applications?\n\nEach app's official uninstaller runs first, then you review its leftovers.",
                "Uninstall applications", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmMany != MessageBoxResult.Yes) return;
        }

        foreach (var app in targets)
        {
            var keepGoing = await UninstallAppAsync(app);
            if (!keepGoing) break;
        }

        await RefreshAppsAsync();
        LoadBackups();
    }

    private async Task<bool> UninstallAppAsync(InstalledApp app)
    {
        var confirm = MessageBox.Show(this,
            $"Uninstall {app.DisplayName}?\n\nThe official uninstaller will run first. You will review leftovers before anything is cleaned.",
            "Confirm uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return false;

        try
        {
            SetBusy(true, $"Preparing {app.DisplayName}...");

            if (Settings.CloseRunningApps)
            {
                var processes = _processService.FindProcesses(app);
                if (processes.Count > 0)
                {
                    var close = MessageBox.Show(this,
                        $"{processes.Count} running process(es) belong to {app.DisplayName}.\n\nClose them before uninstalling?",
                        "Close running processes", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (close == MessageBoxResult.Yes)
                        await _processService.CloseAsync(processes);
                    else
                        foreach (var process in processes) process.Dispose();
                }
            }

            if (Settings.CreateRestorePoint)
            {
                SetBusy(true, "Creating a system restore point...");
                var (ok, message) = await _restorePointService.CreateAsync("Before uninstalling " + app.DisplayName);
                if (!ok)
                {
                    var proceed = MessageBox.Show(this,
                        "Could not create a restore point:\n\n" + message +
                        "\n\nContinue without a restore point?",
                        "Restore point failed", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (proceed != MessageBoxResult.Yes) return false;
                }
            }

            SetBusy(true, $"Running the official uninstaller for {app.DisplayName}...");
            var options = new UninstallOptions
            {
                Silent = Settings.SilentUninstall,
                CreateRestorePoint = Settings.CreateRestorePoint,
                BackupLeftovers = Settings.BackupLeftovers,
                CloseRunningApps = Settings.CloseRunningApps,
                Aggressive = Settings.AggressiveMode,
                RemoveProvisionedStorePackage = Settings.RemoveProvisionedStorePackages,
                AllUsers = Settings.StoreAllUsers
            };
            var progress = new Progress<ProgressInfo>(p =>
            {
                StatusText = p.Status;
                if (p.Percent.HasValue) ProgressValue = Math.Clamp(p.Percent.Value, 0, 100);
            });

            var outcome = await _uninstallService.UninstallAsync(app, options, progress);
            if (outcome.OfficialUninstallerRan && !outcome.Success)
            {
                var proceed = MessageBox.Show(this,
                    outcome.Message + "\n\nContinue to the leftover scan anyway?",
                    "Uninstaller did not finish cleanly", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (proceed != MessageBoxResult.Yes) return false;
            }

            SetBusy(true, $"Scanning leftovers for {app.DisplayName}...");
            var leftovers = await _leftoverScanner.ScanAsync(app, Settings.AggressiveMode, progress);
            SetBusy(false);

            if (leftovers.Count == 0)
            {
                MessageBox.Show(this, "No leftover files, folders, registry keys, or shortcuts were found.",
                    "Leftover scan", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }

            var window = new LeftoverWindow(app, leftovers, Settings) { Owner = this };
            window.ShowDialog();
            return true;
        }
        catch (Exception ex)
        {
            App.Log.Error($"Uninstall workflow failed for {app.DisplayName}: {ex}");
            MessageBox.Show(this, "The uninstall workflow failed:\n\n" + ex.Message,
                "Uninstall failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void PreviewLeftovers_Click(object sender, RoutedEventArgs e)
    {
        await PreviewLeftoversAsync();
    }

    private async Task PreviewLeftoversAsync()
    {
        if (_busy || SelectedApp == null)
        {
            if (SelectedApp == null)
                MessageBox.Show(this, "Select an application first.", "No application selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            SetBusy(true, $"Scanning leftovers for {SelectedApp.DisplayName}...");
            var progress = new Progress<ProgressInfo>(p =>
            {
                StatusText = p.Status;
                if (p.Percent.HasValue) ProgressValue = Math.Clamp(p.Percent.Value, 0, 100);
            });
            var leftovers = await _leftoverScanner.ScanAsync(SelectedApp, Settings.AggressiveMode, progress);
            SetBusy(false);
            if (leftovers.Count == 0)
            {
                MessageBox.Show(this, "No leftover items were found for this application.",
                    "Leftover scan", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var window = new LeftoverWindow(SelectedApp, leftovers, Settings) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            App.Log.Error("Leftover preview failed: " + ex);
            MessageBox.Show(this, "Leftover scan failed:\n\n" + ex.Message,
                "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        var path = SelectedApp?.LocationForOpen ?? "";
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return;
        }
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }
        MessageBox.Show(this, "No existing install location is available for this application.",
            "Location not found", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ScanCleaner_Click(object sender, RoutedEventArgs e)
    {
        await ScanCleanerAsync();
    }

    private async Task ScanCleanerAsync()
    {
        if (_busy) return;
        SetBusy(true, "Scanning cleaner categories...");
        try
        {
            var progress = new Progress<ProgressInfo>(p =>
            {
                StatusText = p.Status;
                if (p.Percent.HasValue) CleanerProgress.Value = Math.Clamp(p.Percent.Value, 0, 100);
            });
            var categories = await _cleaner.ScanAsync(Settings.CleanerAggressiveMode, progress);
            Categories.Clear();
            foreach (var category in categories) Categories.Add(category);
            UpdateCleanerSummary();
            StatusText = $"{Categories.Count} cleaner categories scanned.";
        }
        catch (Exception ex)
        {
            App.Log.Error("Cleaner scan failed: " + ex);
            MessageBox.Show(this, "Cleaner scan failed:\n\n" + ex.Message,
                "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            CleanerProgress.Value = 0;
        }
    }

    private void SelectSafeCleaner_Click(object sender, RoutedEventArgs e)
    {
        foreach (var category in Categories)
            category.IsSelected = !category.IsAggressive;
        UpdateCleanerSummary();
    }

    private void SelectAllCleaner_Click(object sender, RoutedEventArgs e)
    {
        foreach (var category in Categories)
            category.IsSelected = true;
        UpdateCleanerSummary();
    }

    private async void CleanSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var selected = Categories.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one cleaner category.", "Nothing selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var size = selected.Sum(c => c.SizeBytes);
        var confirm = MessageBox.Show(this,
            $"Clean {selected.Count} selected categor{(selected.Count == 1 ? "y" : "ies")} ({Format.Bytes(size)})?\n\n" +
            "Locked files are skipped. The Recycle Bin is only emptied when its category is selected.",
            "Confirm cleaning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true, "Cleaning selected categories...");
            var progress = new Progress<ProgressInfo>(p =>
            {
                StatusText = p.Status;
                if (p.Percent.HasValue) CleanerProgress.Value = Math.Clamp(p.Percent.Value, 0, 100);
            });
            var result = await _cleaner.CleanAsync(selected, progress);
            var message = $"Deleted: {result.DeletedItems}\n" +
                          $"Failed: {result.FailedItems}\n" +
                          $"Freed: {Format.Bytes(result.FreedBytes)}";
            if (result.Errors.Count > 0)
                message += "\n\nFirst errors:\n" + string.Join("\n", result.Errors.Take(8));
            MessageBox.Show(this, message, "Cleaner complete", MessageBoxButton.OK,
                result.FailedItems > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.Log.Error("Cleaner failed: " + ex);
            MessageBox.Show(this, "Cleaner failed:\n\n" + ex.Message,
                "Cleaner failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            CleanerProgress.Value = 0;
        }

        await ScanCleanerAsync();
    }

    private void UpdateCleanerSummary()
    {
        if (CleanerSelectedSizeText == null) return;
        var selected = Categories.Where(c => c.IsSelected).ToList();
        CleanerSelectedSizeText.Text = Format.Bytes(selected.Sum(c => c.SizeBytes));
        CleanerFileCountText.Text = Categories.Sum(c => c.FileCount).ToString("N0");
        CleanerCategoryCountText.Text = $"{selected.Count}/{Categories.Count}";
    }

    private void LoadBackups()
    {
        try
        {
            _backupService = new BackupService(Settings.BackupRoot);
            Backups.Clear();
            foreach (var session in _backupService.ListSessions())
                Backups.Add(session);
            StatusText = $"{Backups.Count} backup session(s) available.";
        }
        catch (Exception ex)
        {
            App.Log.Warn("Could not load backups: " + ex.Message);
        }
    }

    private void RefreshBackups_Click(object sender, RoutedEventArgs e)
    {
        LoadBackups();
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(Settings.BackupRoot);
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(Settings.LogRoot);
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || SelectedBackup == null)
        {
            if (SelectedBackup == null)
                MessageBox.Show(this, "Select a backup session first.", "No backup selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Restore {SelectedBackup.ItemCount} item(s) for {SelectedBackup.AppName}?\n\n" +
            "Existing files and registry values are preserved where possible. Restored file conflicts get a .restored_ suffix.",
            "Restore backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true, "Restoring backup...");
            _backupService ??= new BackupService(Settings.BackupRoot);
            var progress = new Progress<ProgressInfo>(p =>
            {
                StatusText = p.Status;
                if (p.Percent.HasValue) ProgressValue = Math.Clamp(p.Percent.Value, 0, 100);
            });
            var result = await _backupService.RestoreAsync(SelectedBackup, progress);
            var message = $"Restored: {result.RestoredItems}\nFailed: {result.FailedItems}";
            if (result.Errors.Count > 0)
                message += "\n\nFirst errors:\n" + string.Join("\n", result.Errors.Take(8));
            MessageBox.Show(this, message, "Restore complete", MessageBoxButton.OK,
                result.FailedItems > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            LoadBackups();
        }
        catch (Exception ex)
        {
            App.Log.Error("Restore failed: " + ex);
            MessageBox.Show(this, "Restore failed:\n\n" + ex.Message,
                "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBackup == null)
        {
            MessageBox.Show(this, "Select a backup session first.", "No backup selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Delete the backup for {SelectedBackup.AppName} from {SelectedBackup.DateText}?\n\nThis cannot be undone.",
            "Delete backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _backupService ??= new BackupService(Settings.BackupRoot);
            _backupService.DeleteSession(SelectedBackup);
            LoadBackups();
        }
        catch (Exception ex)
        {
            App.Log.Error("Could not delete backup: " + ex);
            MessageBox.Show(this, "Could not delete the backup:\n\n" + ex.Message,
                "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder(Settings.BackupRoot, "Select backup folder");
        if (path == null) return;
        Settings.BackupRoot = path;
        BackupRootBox.Text = path;
    }

    private void BrowseLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder(Settings.LogRoot, "Select log folder");
        if (path == null) return;
        Settings.LogRoot = path;
        LogRootBox.Text = path;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings(true);
    }

    private void SaveSettings(bool showMessage)
    {
        Settings.PortableScanRoots = PortableRootsText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => PathSafety.Normalize(line.Trim().Trim('"')))
            .Where(path => path.Length > 0 && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SettingsService.Save(Settings);
        if (showMessage)
            MessageBox.Show(this, "Settings saved.", "DG Uninstaller & Cleaner",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string? PickFolder(string initial, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = Directory.Exists(initial) ? initial : ""
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Could not open folder {path}: {ex.Message}");
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        MainTabs.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(status)) StatusText = status;
        if (!busy) ProgressValue = 0;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
