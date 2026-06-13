using System;
using System.Diagnostics;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Services;
using L5ArgentinaLauncher.Views;

namespace L5ArgentinaLauncher
{
    public partial class MainWindow : Window
    {
        private readonly UserConfig _config;
        private readonly ConfigService _configService = new ConfigService();
        private readonly LauncherEngine _engine;
        private bool _initializing = true;
        private bool _busy;

        public MainWindow(UserConfig config)
        {
            InitializeComponent();
            _config = config;
            _engine = new LauncherEngine(config);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LauncherVersionText.Text = "Launcher v" + AppConstants.LauncherVersion;
            InstallPathText.Text = "Instalación: " + (_config.SunAndMoonPath ?? "(sin configurar)");

            // Restaurar la última base elegida (default comunidad).
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
            SetBusy(true, "Buscando actualizaciones…", indeterminate: true);
            try
            {
                await _engine.LoadManifestAsync();
                PopulateFromManifest();
                SetStatus("Listo.");
            }
            catch (Exception ex)
            {
                PopulateOffline();
                SetStatus("Sin conexión al manifest — modo offline. Podés jugar con lo que ya tenés. (" + Short(ex) + ")");
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
                CommunityVersionText.Text = "Versión: " + (community.Version ?? "—");
                CommunityStatusText.Text = _engine.CommunityNeedsDownload() ? "Actualización disponible" : "Descargada";
            }
            else
            {
                CommunityVersionText.Text = "Versión: —";
                CommunityStatusText.Text = "No está en el manifest";
            }

            // Banner de update del launcher (link hardcodeado, nunca del manifest).
            if (_engine.LauncherUpdateAvailable())
            {
                var latest = _engine.Manifest.Launcher.LatestVersion;
                var notes = _engine.Manifest.Launcher.Notes;
                UpdateBannerText.Text = string.IsNullOrWhiteSpace(notes)
                    ? $"Hay una versión nueva del launcher (v{latest}). Tenés v{AppConstants.LauncherVersion}."
                    : $"Hay una versión nueva del launcher (v{latest}). {notes}";
                UpdateBanner.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateBanner.Visibility = Visibility.Collapsed;
            }

            // Pack de imágenes.
            var img = _engine.Manifest?.Images;
            if (img != null && !string.IsNullOrWhiteSpace(img.File))
                ImagesStatusText.Text = _engine.ImagesNeedSync()
                    ? $"actualización disponible (v{img.Version})"
                    : $"al día (v{img.Version})";
            else
                ImagesStatusText.Text = "sin pack en el manifest";

            UpdateOriginalAvailability();
        }

        private void PopulateOffline()
        {
            if (_config.CachedDatabases.TryGetValue(LauncherEngine.CommunityId, out var cached))
            {
                CommunityVersionText.Text = "Versión en caché: " + (cached.Version ?? "—");
                CommunityStatusText.Text = "sin conexión";
            }
            else
            {
                CommunityVersionText.Text = "Versión: —";
                CommunityStatusText.Text = "sin conexión";
            }

            ImagesStatusText.Text = string.IsNullOrWhiteSpace(_config.AppliedImagePackVersion)
                ? "sin conexión"
                : $"v{_config.AppliedImagePackVersion} (sin conexión)";

            UpdateBanner.Visibility = Visibility.Collapsed;
            UpdateOriginalAvailability();
        }

        private void UpdateOriginalAvailability()
        {
            if (_engine.HasOriginalBackup())
            {
                OriginalCard.IsEnabled = true;
                OriginalStatusText.Text = "Respaldo local disponible";
                OriginalStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
            }
            else
            {
                OriginalCard.IsEnabled = false;
                OriginalStatusText.Text = "No disponible (no se capturó backup)";
                OriginalStatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerRed");
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
                MessageBox.Show(this, "La carpeta de Sun and Moon no es válida. Revisala en Configuración.",
                    "Instalación no encontrada", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var baseId = GetSelectedBaseId();
            var progress = new Progress<DownloadProgress>(OnProgress);

            SetBusy(true, "Preparando…", indeterminate: true);
            try
            {
                if (_engine.NeedsApply(baseId))
                {
                    if (string.Equals(baseId, LauncherEngine.CommunityId, StringComparison.OrdinalIgnoreCase)
                        && _engine.Manifest == null)
                    {
                        throw new InvalidOperationException(
                            "No hay conexión al manifest, así que no puedo bajar ni actualizar la base Comunidad. " +
                            "Reintentá con internet, o jugá con la base Original.");
                    }

                    SetStatus("Aplicando base…");
                    await _engine.ApplyBaseAsync(baseId, progress);
                }

                if (_engine.Manifest != null && _engine.ImagesNeedSync())
                {
                    SetStatus("Sincronizando imágenes propias…");
                    await _engine.SyncImagesAsync(progress);
                    PopulateFromManifest();
                }

                SetStatus("Iniciando Sun and Moon…");
                _engine.Play();
                SetStatus("Sun and Moon iniciado. ¡A jugar!");
            }
            catch (SecurityException ex)
            {
                MessageBox.Show(this,
                    "Se abortó por seguridad (verificación de integridad fallida). No se modificó tu instalación.\n\n" + ex.Message,
                    "Verificación fallida", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Operación abortada por seguridad.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "No se pudo completar", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Error: " + Short(ex));
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ---------------- Botones secundarios ----------------

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(_config) { Owner = this };
            dlg.ShowDialog();
            if (dlg.Saved)
            {
                InstallPathText.Text = "Instalación: " + (_config.SunAndMoonPath ?? "(sin configurar)");
                await RefreshAsync();
            }
        }

        private void UpdateLinkButton_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(AppConstants.ReleasesPageUrl) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo abrir el navegador: " + ex.Message); }
        }

        // ---------------- Helpers de UI ----------------

        private void OnProgress(DownloadProgress p)
        {
            Progress.Visibility = Visibility.Visible;
            if (p.Fraction.HasValue)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = p.Fraction.Value * 100.0;
                SetStatus($"Descargando… {p.BytesRead / 1048576.0:0.0} MB de {p.TotalBytes.Value / 1048576.0:0.0} MB");
            }
            else
            {
                Progress.IsIndeterminate = true;
                SetStatus($"Descargando… {p.BytesRead / 1048576.0:0.0} MB");
            }
        }

        private void SetBusy(bool busy, string status = null, bool indeterminate = false)
        {
            _busy = busy;
            PlayButton.IsEnabled = !busy;
            RefreshButton.IsEnabled = !busy;
            SettingsButton.IsEnabled = !busy;
            CommunityCard.IsEnabled = !busy && true;
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
