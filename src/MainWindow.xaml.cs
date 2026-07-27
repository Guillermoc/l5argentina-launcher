using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        // true una vez que el usuario confirmó el cierre: evita re-preguntar cuando el Shutdown
        // vuelve a pasar por Closing.
        private bool _closeConfirmed;

        private string _settingsPath;
        private TaskCompletionSource<bool> _dialogTcs;

        private Views.SearchWindow _searchWindow;
        private CardCatalog _catalog; // catálogo del Buscador cargado (se invalida al re-sincronizar)

        public MainWindow(UserConfig config)
        {
            InitializeComponent();
            _config = config;
            _engine = new LauncherEngine(config);
        }

        // ---------------- Title bar custom ----------------

        private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// Cerrar el launcher cierra la aplicación entera, Buscador y Mazos incluidos, y por eso
        /// pregunta. Las otras ventanas se cierran solas y sin confirmar: cerrar el Buscador no
        /// tiene consecuencia sobre nada más.
        ///
        /// El overlay de confirmación es asíncrono y <c>Closing</c> es sincrónico, así que el
        /// primer paso siempre cancela el cierre y lo reintenta después de la respuesta.
        /// </summary>
        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closeConfirmed) return;
            e.Cancel = true;

            var message = _busy
                ? Strings.Get("Close_ConfirmBusy")
                : Strings.Get("Close_ConfirmMessage");
            if (!await ShowDialogAsync(Strings.Get("Close_ConfirmTitle"), message,
                                       Strings.Get("Close_ConfirmYes"), Strings.Get("Btn_Cancel"),
                                       isError: _busy))
                return;

            _closeConfirmed = true;
            Application.Current.Shutdown(); // se lleva Buscador y Mazos con él
        }

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
                await SyncCatalogQuietAsync();
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

        /// <summary>
        /// Sincroniza el catálogo del Buscador. Fuente primaria: el manifest de production del
        /// pool (cards_db/rules/filters, mismos artefactos que la app). Fallback: la sección
        /// `catalog` opcional del manifest del launcher. No es crítico para jugar: cualquier
        /// error se ignora y el Buscador usa lo que haya en caché.
        /// </summary>
        private async Task SyncCatalogQuietAsync()
        {
            var progress = new Progress<DownloadProgress>(OnProgress);
            try
            {
                await _engine.LoadPoolManifestAsync();
                if (_engine.CatalogNeedsSyncFromPool())
                {
                    SetStatus(Strings.Get("Status_SyncingCatalog"));
                    if (await _engine.SyncCatalogFromPoolAsync(progress))
                        _catalog = null; // invalidar el catálogo cargado; se relee al abrir el Buscador
                }
                return;
            }
            catch
            {
                // pool no disponible → probar la sección catalog del manifest del launcher
            }
            try
            {
                if (!_engine.CatalogNeedsSync()) return;
                SetStatus(Strings.Get("Status_SyncingCatalog"));
                if (await _engine.SyncCatalogAsync(progress))
                    _catalog = null;
            }
            catch
            {
                // catálogo desactualizado ≠ bloqueo
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

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_searchWindow != null)
            {
                if (_searchWindow.WindowState == WindowState.Minimized)
                    _searchWindow.WindowState = WindowState.Normal;
                _searchWindow.Activate();
                return;
            }
            if (_busy) return;

            if (!Services.Search.CatalogStore.IsAvailable())
            {
                await ShowDialogAsync(Strings.Get("Dlg_NoCatalogTitle"), Strings.Get("Dlg_NoCatalogMsg"),
                    Strings.Get("Btn_Close"), null, isError: false);
                return;
            }

            SetBusy(true, Strings.Get("Status_LoadingCatalog"), indeterminate: true);
            try
            {
                if (_catalog == null)
                    _catalog = await Task.Run(() => Services.Search.CatalogStore.Load());

                _searchWindow = new Views.SearchWindow(_catalog, _config.SunAndMoonPath) { Owner = null };
                _searchWindow.Closed += (s2, e2) => _searchWindow = null;
                _searchWindow.Show();
                SetStatus(Strings.Get("Status_Ready"));
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

        private void UpdateLinkButton_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(AppConstants.ReleasesPageUrl) { UseShellExecute = true }); }
            catch { /* navegador no disponible */ }
        }

        // ---------------- Overlay: Packs de imágenes ----------------

        /// <summary>Status de la ventana principal antes de abrir el overlay de packs, para
        /// restaurarlo al cerrar (si no, queda colgado el "Consultando…"/"Pack aplicado…").</summary>
        private string _statusBeforePacks;

        private async void PacksButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _statusBeforePacks = StatusText.Text;
            SetBusy(true, Strings.Get("Status_LoadingPacks"), indeterminate: true);
            try
            {
                await _engine.LoadPoolManifestAsync();
            }
            catch (Exception ex)
            {
                SetStatus(_statusBeforePacks);
                await ShowDialogAsync(Strings.Get("Dlg_GenericErrorTitle"),
                    Strings.Format("Fmt_PacksLoadError", Short(ex)),
                    Strings.Get("Btn_Close"), null, isError: true);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            BuildPacksList();
            PacksStatus.Text = "";
            PacksOverlay.Visibility = Visibility.Visible;
        }

        private void PacksClose_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return; // no cerrar en medio de una descarga
            PacksOverlay.Visibility = Visibility.Collapsed;
            SetStatus(_statusBeforePacks ?? Strings.Get("Status_Ready"));
        }

        private void BuildPacksList()
        {
            PacksList.Children.Clear();
            foreach (var pack in _engine.ImagePacks)
                PacksList.Children.Add(BuildPackRow(pack));
        }

        private UIElement BuildPackRow(PoolPackage pack)
        {
            var row = new Border
            {
                Background = (Brush)FindResource("SurfaceGlassSoft"),
                CornerRadius = new CornerRadius(8),
                BorderBrush = (Brush)FindResource("OutlineSoft"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 9, 14, 9),
                Margin = new Thickness(0, 0, 0, 8),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = PackLabel(pack.Id),
                Style = (Style)FindResource("Body"),
                Foreground = (Brush)FindResource("Text"),
            });
            info.Children.Add(new TextBlock
            {
                Text = Strings.Format("Fmt_PackMeta", pack.Version, pack.SizeBytes / 1048576.0),
                Style = (Style)FindResource("BodySmall"),
            });
            grid.Children.Add(info);

            var status = new TextBlock
            {
                Style = (Style)FindResource("BodySmall"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 14, 0),
                TextAlignment = TextAlignment.Right,
            };
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);

            var button = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Height = 30,
                FontSize = 14,
                MinWidth = 110,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);

            UpdatePackRowState(pack, status, button);
            button.Click += async (s, e) => await DownloadPackAsync(pack);

            row.Child = grid;
            return row;
        }

        private void UpdatePackRowState(PoolPackage pack, TextBlock status, Button button)
        {
            if (_engine.PackIsCurrent(pack))
            {
                status.Text = Strings.Format("Fmt_PackApplied", pack.Version);
                status.Foreground = (Brush)FindResource("Gold");
                button.Content = Strings.Get("Btn_PackReapply");
            }
            else if (_engine.PackWasApplied(pack))
            {
                status.Text = Strings.Get("Packs_UpdateAvailable");
                status.Foreground = (Brush)FindResource("Gold");
                button.Content = Strings.Get("Btn_PackUpdate");
            }
            else
            {
                status.Text = Strings.Get("Packs_NotInstalled");
                status.Foreground = (Brush)FindResource("TextFaint");
                button.Content = Strings.Get("Btn_PackDownload");
            }
        }

        private async Task DownloadPackAsync(PoolPackage pack)
        {
            if (_busy) return;

            if (!InstallationService.IsValidInstall(_config.SunAndMoonPath))
            {
                await ShowDialogAsync(Strings.Get("Dlg_InstallNotFoundTitle"), Strings.Get("Dlg_InstallNotFoundMsg"),
                    Strings.Get("Btn_Close"), null, isError: true);
                return;
            }

            SetBusy(true, Strings.Format("Fmt_DownloadingPack", PackLabel(pack.Id)), indeterminate: true);
            SetPackButtonsEnabled(false);
            var progress = new Progress<DownloadProgress>(p =>
            {
                OnProgress(p);
                PacksStatus.Text = p.Fraction.HasValue
                    ? Strings.Format("Fmt_PackProgress", PackLabel(pack.Id), p.BytesRead / 1048576.0, p.TotalBytes.Value / 1048576.0)
                    : Strings.Format("Fmt_PackProgressNoTotal", PackLabel(pack.Id), p.BytesRead / 1048576.0);
            });

            try
            {
                await _engine.ApplyPoolImagePackAsync(pack, progress);
                PacksStatus.Text = Strings.Format("Fmt_PackDone", PackLabel(pack.Id));
                SetStatus(Strings.Format("Fmt_PackDone", PackLabel(pack.Id)));
                // El pack puede haber traído imágenes que el Buscador resuelva distinto → nada
                // que invalidar (lee del disco al mostrar), pero refrescamos estados de la lista.
            }
            catch (SecurityException ex)
            {
                PacksStatus.Text = "";
                await ShowDialogAsync(Strings.Get("Dlg_IntegrityTitle"),
                    Strings.Get("Dlg_IntegrityMsg") + "\n\n" + ex.Message,
                    Strings.Get("Btn_Close"), null, isError: true);
                SetStatus(Strings.Get("Status_AbortedSecurity"));
            }
            catch (Exception ex)
            {
                PacksStatus.Text = "";
                await ShowDialogAsync(Strings.Get("Dlg_GenericErrorTitle"), ex.Message,
                    Strings.Get("Btn_Close"), null, isError: true);
                SetStatus(Strings.Format("Fmt_ErrorStatus", Short(ex)));
            }
            finally
            {
                SetBusy(false);
                BuildPacksList(); // re-render de estados + re-habilita botones
            }
        }

        private void SetPackButtonsEnabled(bool enabled)
        {
            foreach (var child in PacksList.Children)
                if (child is Border b && b.Child is Grid g)
                    foreach (var inner in g.Children)
                        if (inner is Button btn) btn.IsEnabled = enabled;
        }

        /// <summary>Nombre amigable del pack: Pack_&lt;id&gt; del resx; ids desconocidos quedan tal cual.</summary>
        private static string PackLabel(string id)
        {
            var key = "Pack_" + id;
            var label = Strings.Get(key);
            return label == key ? id : label;
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
            SearchButton.IsEnabled = !busy || _searchWindow != null;
            PacksButton.IsEnabled = !busy;
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
