using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CleanSweep.Models;
using CleanSweep.Services;
using Microsoft.Win32;

namespace CleanSweep;

public partial class MainWindow : Window
{
    private readonly CleanupService _cleanupService = new();
    private readonly SettingsService _settingsService = new();
    private CancellationTokenSource? _cleanupCancellation;
    private bool _closeAfterCleanup;
    private bool _isBulkUpdatingSelection;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        LoadFolders();
        UpdateLocationsCount();
    }

    public ObservableCollection<FolderEntry> Folders { get; } = [];

    private void LoadFolders()
    {
        UserSettings settings = _settingsService.Load();

        foreach (FolderSetting folder in settings.Folders)
        {
            AddFolderEntry(
                folder.Path,
                isDefault: folder.IsDefault,
                displayName: string.IsNullOrWhiteSpace(folder.DisplayName) ? "Custom folder" : folder.DisplayName,
                isEnabled: folder.IsEnabled);
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose folder to clean",
            Multiselect = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ShowHiddenItems = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (string path in dialog.FolderNames)
        {
            AddFolderEntry(path, isDefault: false, displayName: "Custom folder");
        }

        SaveFolderSettings();
        UpdateLocationsCount();
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: FolderEntry entry } || entry.IsDefault)
        {
            return;
        }

        entry.PropertyChanged -= Folder_PropertyChanged;
        Folders.Remove(entry);
        SaveFolderSettings();
        UpdateLocationsCount();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _isBulkUpdatingSelection = true;
        try
        {
            foreach (FolderEntry folder in Folders)
            {
                folder.IsEnabled = true;
            }
        }
        finally
        {
            _isBulkUpdatingSelection = false;
        }

        SaveFolderSettings();
        UpdateLocationsCount();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        _isBulkUpdatingSelection = true;
        try
        {
            foreach (FolderEntry folder in Folders)
            {
                folder.IsEnabled = false;
            }
        }
        finally
        {
            _isBulkUpdatingSelection = false;
        }

        SaveFolderSettings();
        UpdateLocationsCount();
    }

    private async void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupCancellation is not null)
        {
            _cleanupCancellation.Cancel();
            StatusText.Text = "Cancellation requested. Finishing current file operations…";
            CleanButton.Content = "Cancelling…";
            CleanButton.IsEnabled = false;
            return;
        }

        string[] selectedPaths = Folders
            .Where(folder => folder.IsEnabled)
            .Select(folder => folder.Path)
            .ToArray();

        if (selectedPaths.Length == 0)
        {
            MessageBox.Show(this, "Select at least one folder.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IReadOnlyList<string> roots;
        try
        {
            roots = _cleanupService.ValidateAndNormalizeRoots(selectedPaths);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            MessageBox.Show(this, ex.Message, "Folder selection blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string confirmation =
            $"This will permanently delete all files and nested folders inside {roots.Count} selected location(s).\n\n" +
            "The selected root folders will remain. Locked or protected items may be skipped.\n\n" +
            "Deleted items cannot be restored from Recycle Bin. Continue?";

        MessageBoxResult confirm = MessageBox.Show(
            this,
            confirmation,
            "Confirm permanent cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _cleanupCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _cleanupCancellation.Token;
        SetBusyState(isBusy: true);
        CleanupProgressBar.Value = 0;
        CleanupProgressBar.IsIndeterminate = true;
        ProgressText.Text = string.Empty;
        StatusText.Text = "Scanning selected folders…";

        var progress = new Progress<CleanupProgress>(UpdateProgress);

        try
        {
            CleanupPlan plan = await Task.Run(
                () => _cleanupService.Scan(roots, progress, cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            CleanupProgressBar.IsIndeterminate = false;
            CleanupProgressBar.Maximum = Math.Max(1, plan.TotalItems);
            CleanupProgressBar.Value = 0;
            StatusText.Text = $"Found {plan.Files.Count:N0} files and {plan.Directories.Count:N0} folders. Starting cleanup…";

            CleanupResult result = await _cleanupService.DeleteAsync(plan, progress, cancellationToken);
            ShowCleanupResult(result, plan.TotalItems);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled. No deletion was started.";
            ProgressText.Text = string.Empty;
            MessageBox.Show(this,
                "Cleanup was cancelled during scanning. No files or folders were deleted.",
                "Cleanup cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Cleanup stopped because of an unexpected error.";
            MessageBox.Show(this, ex.ToString(), "CleanSweep error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cleanupCancellation.Dispose();
            _cleanupCancellation = null;
            SetBusyState(isBusy: false);
            if (_closeAfterCleanup)
            {
                _closeAfterCleanup = false;
                Close();
            }
        }
    }

    private void UpdateProgress(CleanupProgress update)
    {
        StatusText.Text = update.Message;

        if (update.IsScanning)
        {
            CleanupProgressBar.IsIndeterminate = true;
            ProgressText.Text = string.Empty;
            return;
        }

        CleanupProgressBar.IsIndeterminate = false;
        CleanupProgressBar.Maximum = Math.Max(1, update.Total);
        CleanupProgressBar.Value = Math.Min(update.Completed, CleanupProgressBar.Maximum);
        ProgressText.Text = update.Total == 0
            ? string.Empty
            : $"{update.Completed:N0} / {update.Total:N0}";
    }

    private void ShowCleanupResult(CleanupResult result, int plannedItems)
    {
        string outcome = result.WasCancelled ? "Cleanup cancelled." : "Cleanup completed.";
        StatusText.Text = result.WasCancelled
            ? $"Cancelled. Deleted {result.TotalDeleted:N0} items before cancellation."
            : $"Done. Deleted {result.TotalDeleted:N0} items.";

        if (plannedItems == 0)
        {
            StatusText.Text = result.ScanErrors == 0
                ? "No files or nested folders found. Selected root folders were kept."
                : "No items found. Some locations could not be scanned.";
        }

        var lines = new List<string>
        {
            outcome,
            string.Empty,
            $"Files deleted: {result.DeletedFiles:N0}",
            $"Folders removed: {result.DeletedDirectories:N0}",
            $"Items that could not be deleted: {result.FailedItems:N0}",
            $"Scan errors or missing folders: {result.ScanErrors:N0}",
            $"Junctions / linked folders skipped: {result.SkippedReparsePoints:N0}",
            string.Empty,
            "Selected root folders were kept."
        };

        if (result.SkippedLinks.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"Skipped junctions / symbolic links (details, up to {result.SkippedLinks.Count} shown):");
            foreach (SkippedLinkDetail link in result.SkippedLinks)
            {
                lines.Add(string.Empty);
                lines.Add($"Folder path: {link.Path}");
                lines.Add($"Actual target path: {link.TargetPath}");
                lines.Add($"Reason: {link.Reason}");
            }
            if (result.SkippedReparsePoints > result.SkippedLinks.Count)
            {
                lines.Add($"Additional linked items skipped: {result.SkippedReparsePoints - result.SkippedLinks.Count}");
            }
        }

        if (result.ErrorSamples.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Some details:");
            lines.AddRange(result.ErrorSamples.Take(5).Select(item => "• " + item));
        }

        MessageBox.Show(this, string.Join(Environment.NewLine, lines),
            result.WasCancelled ? "Cleanup cancelled" : "Cleanup report",
            MessageBoxButton.OK,
            result.FailedItems > 0 || result.ScanErrors > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void AddFolderEntry(string path, bool isDefault, string displayName, bool isEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (Folders.Any(folder => string.Equals(folder.Path.TrimEnd('\\'), fullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var entry = new FolderEntry(fullPath, isDefault, displayName, isEnabled);
        entry.PropertyChanged += Folder_PropertyChanged;
        Folders.Add(entry);

        UpdateLocationsCount();
    }

    private void Folder_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderEntry.IsEnabled))
        {
            if (!_isBulkUpdatingSelection)
            {
                SaveFolderSettings();
            }

            UpdateLocationsCount();
        }
    }

    private void SaveFolderSettings()
    {
        try
        {
            _settingsService.Save(new UserSettings
            {
                Version = 1,
                Folders = Folders
                    .GroupBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Select(folder => new FolderSetting
                    {
                        Path = folder.Path,
                        DisplayName = folder.DisplayName,
                        IsDefault = folder.IsDefault,
                        IsEnabled = folder.IsEnabled
                    })
                    .ToList()
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(this,
                $"Could not save folder settings to localsetting.json beside CleanSweep.exe.\n\n{ex.Message}\n\nMove CleanSweep to a folder where your Windows account has write permission.",
                "Settings could not be saved", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateLocationsCount()
    {
        int selected = Folders.Count(folder => folder.IsEnabled);
        LocationsCountText.Text = $"{Folders.Count} locations  ·  {selected} selected";
    }

    private void SetBusyState(bool isBusy)
    {
        FolderListCard.IsEnabled = !isBusy;
        AddFolderButton.IsEnabled = !isBusy;
        SelectAllButton.IsEnabled = !isBusy;
        ClearSelectionButton.IsEnabled = !isBusy;
        CleanButton.IsEnabled = true;
        CleanButton.Content = isBusy ? "Cancel cleanup" : "Clean selected folders";
        CleanButton.Background = isBusy ? System.Windows.Media.Brushes.IndianRed :
            (System.Windows.Media.Brush)FindResource("AccentBrush");
        CleanButton.MinWidth = 190;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_cleanupCancellation is not null)
        {
            e.Cancel = true;
            _closeAfterCleanup = true;
            _cleanupCancellation.Cancel();
            StatusText.Text = "Cancelling cleanup before closing…";
            return;
        }

        base.OnClosing(e);
    }
}
