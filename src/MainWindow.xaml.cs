using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Resources;
using L5ArgentinaLauncher.Services;

namespace L5ArgentinaLauncher
{
    public partial class MainWindow : Window
    {
        private readonly UserConfig _config;
        private readonly ConfigService _configService = new ConfigService();
        private readonly LauncherEngine _engine;
        private bool _initializing = true;
        private bool _busy;

        private string _settingsPath;
        private TaskCompletionSource<bool> _dialogTcs;

        public MainWindow(UserConfig config)
        {
            InitializeComponent();
            _config = config;
            _engine = new LauncherEngine(config);
        }

        // ---------------- Title bar custom ----------------

        private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // ---------------- Carga inicial ----------------

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LauncherVersionText.Text = Strings.Format("Fmt_LauncherVersion", AppConstants.LauncherVersion);
            InstallPathText.Text = Strings.Format("Fmt_Install", _config.SunAndMoonPath ?? Strings.Get("Common_NotConfigured"));

            var selected = string.IsNullOrWhiteSpace(_config.SelectedBaseId) ? LauncherEngine.CommunityId : _config.SelectedBaseId;
            if (string.Equals(selected, LauncherEngine.OriginalId, StringComparison.OrdinalIgnoreCase))
                OriginalCard.IsChecked = true;
            else
                CommunityCard.IsChecked = true;

            UpdateOriginalAvailability();
            _initializing = false;

            await RefreshAsync();
        }

        // ---------------- Refresh / manifest ----------------

        private async Task RefreshAsync()
        {
            if (_busy) return;
            SetBusy(true, Strings.Get("Status_Checking"), indeterminate: true);
            try
            {
                await _engine.LoadManifestAsync();
                PopulateFromManifest();
                SetStatus(Strings.Get("Status_Ready"));
            }
            catch (Exception ex)
            {
                PopulateOffline();
                SetStatus(Strings.Format("Fmt_Offline", Short(ex)));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void PopulateFromManifest()
        {
            var community = _engine.CommunityEntry;
            if (community != null)
            {
                CommunityVersionText.Text = Strings.Format("Fmt_Version", community.Version ?? Strings.Get("Value_Dash"));
                CommunityStatusText.Text = _engine.CommunityNeedsDownload()
                    ? Strings.Get("Status_UpdateAvailable")
                    : Strings.Get("Status_Downloaded");
            }
            else
            {
                CommunityVersionText.Text = Strings.Format("Fmt_Version", Strings.Get("Value_Dash"));
                CommunityStatusText.Text = Strings.Get("Status_NotInManifest");
            }

            if (_engine.LauncherUpdateAvailable())
            {
                var latest = _engine.Manifest.Launcher.LatestVersion;
                var notes = _engine.Manifest.Launcher.Notes;
                UpdateBannerText.Text = string.IsNullOrWhiteSpace(notes)
                    ? Strings.Format("Fmt_LauncherUpdateBanner", latest, AppConstants.LauncherVersion)
                    : Strings.Format("Fmt_LauncherUpdateBannerNotes", latest, notes);
                UpdateBanner.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateBanner.Visibility = Visibility.Collapsed;
            }

            var img = _engine.Manifest?.Images;
            if (img != null && !string.IsNullOrWhiteSpace(img.File))
                ImagesStatusText.Text = _engine.ImagesNeedSync()
                    ? Strings.Format("Fmt_ImagesUpdateAvailable", img.Version)
                    : Strings.Format("Fmt_ImagesUpToDate", img.Version);
            else
                ImagesStatusText.Text = Strings.Get("Images_NoPack");

            UpdateOriginalAvailability();
        }

        private void PopulateOffline()
        {
            if (_config.CachedDatabases.TryGetValue(LauncherEngine.CommunityId, out var cached))
            {
                CommunityVersionText.Text = Strings.Format("Fmt_CachedVersion", cached.Version ?? Strings.Get("Value_Dash"));
                CommunityStatusText.Text = Strings.Get("Status_NoConnection");
            }
            else
            {
                CommunityVersionText.Text = Strings.Format("Fmt_Version", Strings.Get("Value_Dash"));
                CommunityStatusText.Text = Strings.Get("Status_NoConnection");
            }

            ImagesStatusText.Text = string.IsNullOrWhiteSpace(_config.AppliedImagePackVersion)
                ? Strings.Get("Status_NoConnection")
                : Strings.Format("Fmt_ImagesOffline", _config.AppliedImagePackVersion);

            UpdateBanner.Visibility = Visibility.Collapsed;
            UpdateOriginalAvailability();
        }

        private void UpdateOriginalAvailability()
        {
            if (_engine.HasOriginalBackup())
            {
                OriginalCard.IsEnabled = true;
                OriginalStatusText.Text = Strings.Get("Original_Available");
                OriginalStatusText.Foreground = (Brush)FindResource("TextDim");
            }
            else
            {
                OriginalCard.IsEnabled = false;
                OriginalStatusText.Text = Strings.Get("Original_Unavailable");
                OriginalStatusText.Foreground = (Brush)FindResource("ErrorText");
                if (OriginalCard.IsChecked == true)
                    CommunityCard.IsChecked = true;
            }
        }

        // ---------------- Selección de base ----------------

        private void BaseCard_Checked(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            _config.SelectedBaseId = GetSelectedBaseId();
            try { _configService.Save(_config); } catch { /* no crítico */ }
        }

        private string GetSelectedBaseId() =>
            OriginalCard.IsChecked == true ? LauncherEngine.OriginalId : LauncherEngine.CommunityId;

        // ---------------- JUGAR ----------------

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            if (!InstallationService.IsValidInstall(_config.SunAndMoonPath))
            {
                await ShowDialogAsync(Strings.Get("Dlg_InstallNotFoundTitle"), Strings.Get("Dlg_InstallNotFoundMsg"),
                    Strings.Get("Btn_Close"), null, isError: true);
                return;
            }

            var baseId = GetSelectedBaseId();
            var progress = new Progress<DownloadProgress>(OnProgress);

            SetBusy(true, Strings.Get("Status_Preparing"), indeterminate: true);
            try
            {
                if (_engine.NeedsApply(baseId))
                {
                    if (string.Equals(baseId, LauncherEngine.CommunityId, StringComparison.OrdinalIgnoreCase)
                        && _engine.Manifest == null)
                    {
                        throw new InvalidOperationException(Strings.Get("Err_NoManifestForCommunity"));
                    }

                    SetStatus(Strings.Get("Status_ApplyingBase"));
                    await _engine.ApplyBaseAsync(baseId, progress);
                }

                if (_engine.Manifest != null && _engine.ImagesNeedSync())
                {
                    SetStatus(Strings.Get("Status_SyncingImages"));
                    await _engine.SyncImagesAsync(progress);
                    PopulateFromManifest();
                }

                SetStatus(Strings.Get("Status_Launching"));
                _engine.Play();
                SetStatus(Strings.Get("Status_Launched"));
            }
            catch (SecurityException ex)
            {
                await ShowDialogAsync(Strings.Get("Dlg_IntegrityTitle"),
                    Strings.Get("Dlg_IntegrityMsg") + "\n\n" + ex.Message,
                    Strings.Get("Btn_Close"), null, isError: true);
                SetStatus(Strings.Get("Status_AbortedSecurity"));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(Strings.Get("Dlg_GenericErrorTitle"), ex.Message,
                    Strings.Get("Btn_Close"), null, isError: true);
                SetStatus(Strings.Format("Fmt_ErrorStatus", Short(ex)));
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ---------------- Botones de header ----------------

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

        private void UpdateLinkButton_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(AppConstants.ReleasesPageUrl) { UseShellExecute = true }); }
            catch { /* navegador no disponible */ }
        }

        // ---------------- Overlay: Configuración ----------------

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _settingsPath = _config.SunAndMoonPath;
            SettingsPathBox.Text = _settingsPath ?? Strings.Get("Common_NotConfigured");
            SettingsUrlBox.Text = _config.ManifestUrl ?? AppConstants.DefaultManifestUrl;
            SettingsUrlBox.Tag = null;
            SettingsValidation.Visibility = Visibility.Collapsed;
            SettingsOverlay.Visibility = Visibility.Visible;
        }

        private void SettingsBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Strings.Get("Dialog_OpenExeTitle"),
                Filter = Strings.Get("Dialog_ExeFilter"),
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) != true) return;

            var dir = Path.GetDirectoryName(dlg.FileName);
            if (!InstallationService.IsValidInstall(dir))
            {
                SettingsValidation.Text = Strings.Get("Settings_FolderInvalid");
                SettingsValidation.Visibility = Visibility.Visible;
                return;
            }
            _settingsPath = dir;
            SettingsPathBox.Text = dir;
            SettingsValidation.Visibility = Visibility.Collapsed;
        }

        private void ResetUrl_Click(object sender, RoutedEventArgs e)
        {
            SettingsUrlBox.Text = AppConstants.DefaultManifestUrl;
            SettingsUrlBox.Tag = null;
        }

        private async void SettingsSave_Click(object sender, RoutedEventArgs e)
        {
            var url = (SettingsUrlBox.Text ?? "").Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                SettingsUrlBox.Tag = "invalid";
                SettingsValidation.Text = Strings.Get("Settings_UrlInvalid");
                SettingsValidation.Visibility = Visibility.Visible;
                return;
            }

            _config.ManifestUrl = url;
            _config.SunAndMoonPath = _settingsPath;
            _configService.Save(_config);
            SettingsOverlay.Visibility = Visibility.Collapsed;
            InstallPathText.Text = Strings.Format("Fmt_Install", _config.SunAndMoonPath ?? Strings.Get("Common_NotConfigured"));
            await RefreshAsync();
        }

        private void SettingsCancel_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        // ---------------- Overlay: Diálogo ----------------

        /// <summary>
        /// Muestra un diálogo overlay (no MessageBox) y devuelve true si el usuario eligió la
        /// acción primaria. Con <paramref name="secondaryText"/> null, es un diálogo de un solo botón.
        /// </summary>
        private Task<bool> ShowDialogAsync(string title, string message, string primaryText, string secondaryText, bool isError)
        {
            DialogTitle.Text = title;
            DialogMessage.Text = message;
            DialogPrimaryBtn.Content = primaryText;

            if (string.IsNullOrEmpty(secondaryText))
            {
                DialogSecondaryBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                DialogSecondaryBtn.Content = secondaryText;
                DialogSecondaryBtn.Visibility = Visibility.Visible;
            }

            var accent = (Brush)FindResource(isError ? "RedAccent" : "Gold");
            DialogCard.BorderBrush = accent;
            DialogIconBox.BorderBrush = accent;
            DialogIcon.Foreground = accent;
            DialogIcon.Text = isError ? "✕" : "!";

            DialogOverlay.Visibility = Visibility.Visible;
            _dialogTcs = new TaskCompletionSource<bool>();
            return _dialogTcs.Task;
        }

        private void DialogPrimary_Click(object sender, RoutedEventArgs e)
        {
            DialogOverlay.Visibility = Visibility.Collapsed;
            _dialogTcs?.TrySetResult(true);
        }

        private void DialogSecondary_Click(object sender, RoutedEventArgs e)
        {
            DialogOverlay.Visibility = Visibility.Collapsed;
            _dialogTcs?.TrySetResult(false);
        }

        // ---------------- Helpers de UI ----------------

        private void OnProgress(DownloadProgress p)
        {
            Progress.Visibility = Visibility.Visible;
            if (p.Fraction.HasValue)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = p.Fraction.Value * 100.0;
                SetStatus(Strings.Format("Fmt_DownloadingWithTotal",
                    p.BytesRead / 1048576.0, p.TotalBytes.Value / 1048576.0));
            }
            else
            {
                Progress.IsIndeterminate = true;
                SetStatus(Strings.Format("Fmt_Downloading", p.BytesRead / 1048576.0));
            }
        }

        private void SetBusy(bool busy, string status = null, bool indeterminate = false)
        {
            _busy = busy;
            PlayButton.IsEnabled = !busy;
            RefreshButton.IsEnabled = !busy;
            SettingsButton.IsEnabled = !busy;
            CommunityCard.IsEnabled = !busy;
            OriginalCard.IsEnabled = !busy && _engine.HasOriginalBackup();

            if (status != null) SetStatus(status);

            if (busy)
            {
                Progress.Visibility = Visibility.Visible;
                Progress.IsIndeterminate = indeterminate;
                Progress.Value = 0;
            }
            else
            {
                Progress.Visibility = Visibility.Collapsed;
                Progress.IsIndeterminate = false;
            }
        }

        private void SetStatus(string text) => StatusText.Text = text;

        private static string Short(Exception ex)
        {
            var m = ex.Message;
            return m.Length > 140 ? m.Substring(0, 140) + "…" : m;
        }
    }
}
