using System.Windows;
using SonicScrewdriver.Update;

namespace SonicScrewdriver.UI;

/// <summary>The "Updates" card: manual check, download with checksum, install.</summary>
public partial class SettingsWindow
{
    private readonly UpdateService _updates = new();
    private ReleaseInfo? _release;
    private bool _updateBusy;
    private CancellationTokenSource? _updateCancel;

    private void InitUpdates(bool atStart, ReleaseInfo? known)
    {
        UpdatesAtStartBox.IsChecked = atStart;
        UpdateVersionText.Text = $"Version {UpdateService.CurrentVersion}";
        Closed += (_, _) => _updateCancel?.Cancel();
        if (known is not null) ShowAvailable(known);
        RefreshUpdateButtons();
    }

    private bool InCompetitiveMode => CompetitiveBox.IsChecked == true;

    private void RefreshUpdateButtons()
    {
        CheckUpdateButton.IsEnabled = !_updateBusy && !InCompetitiveMode;
        InstallUpdateButton.IsEnabled = !_updateBusy && !InCompetitiveMode;
        if (InCompetitiveMode && !_updateBusy) UpdateStatus.Text = "Updates are off in competitive mode.";
        else if (UpdateStatus.Text == "Updates are off in competitive mode.") UpdateStatus.Text = "";
    }

    private void ShowAvailable(ReleaseInfo release)
    {
        _release = release;
        UpdateStatus.Text = $"Version {release.Version} is available.";
        InstallUpdateButton.Content = $"Install {release.Version}";
        InstallUpdateButton.Visibility = Visibility.Visible;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        _updateBusy = true;
        RefreshUpdateButtons();
        UpdateStatus.Text = "Checking…";
        _updateCancel = new CancellationTokenSource();
        try
        {
            var release = await _updates.CheckAsync(_updateCancel.Token);
            if (release is null) { _release = null; InstallUpdateButton.Visibility = Visibility.Collapsed; UpdateStatus.Text = "You have the latest version."; }
            else ShowAvailable(release);
        }
        catch (UpdateException ex) { UpdateStatus.Text = ex.Message; }
        catch (OperationCanceledException) { }
        finally { _updateBusy = false; RefreshUpdateButtons(); }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_release is null) return;
        if (CancelButton.Content as string == "Cancel" &&
            MessageBox.Show(this, "Some changes in this window are not saved and will be lost when the app restarts. Install anyway?",
                "Sonic Screwdriver", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _updateBusy = true;
        RefreshUpdateButtons();
        UpdateProgress.Value = 0;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateStatus.Text = $"Downloading {_release.Version}…";
        _updateCancel = new CancellationTokenSource();
        try
        {
            var path = await _updates.DownloadAsync(_release, new Progress<int>(p => UpdateProgress.Value = p), _updateCancel.Token);
            UpdateStatus.Text = "Checksum verified. Updating…";
            UpdateService.Launch(path);
            Application.Current.Shutdown(); // the installer replaces the exe and starts the app again
        }
        catch (UpdateException ex) { UpdateStatus.Text = ex.Message; }
        catch (OperationCanceledException) { }
        catch (System.ComponentModel.Win32Exception ex) { UpdateStatus.Text = "The installer could not be started: " + ex.Message; }
        finally { _updateBusy = false; UpdateProgress.Visibility = Visibility.Collapsed; RefreshUpdateButtons(); }
    }
}
