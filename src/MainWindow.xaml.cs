using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
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

        // Búsqueda de novedades en segundo plano (ADR-064). No usa _busy: la pantalla sigue viva
        // mientras corre. JUGAR espera solo a _baseTask (manifest + base bajada) y el listado de
        // packs solo a _manifestsTask; el catálogo del Buscador no lo espera nadie.
        private bool _checking;
        private CancellationTokenSource _checkCts;
        private Task _checkTask = Task.CompletedTask;
        private Task _manifestsTask = Task.CompletedTask;
        private Task _baseTask = Task.CompletedTask;

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

        private void Window_Loaded(object sender, RoutedEventArgs e)
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

            StartCheck();
        }

        // ---------------- Búsqueda de novedades (segundo plano) ----------------

        /// <summary>
        /// Busca novedades **sin bloquear la pantalla** ([ADR-064](../docs/DECISIONS.md)). Antes todo
        /// el chequeo corría bajo <c>SetBusy</c>: con una red lenta, JUGAR, el Buscador y hasta la
        /// elección de base quedaban apagados hasta que respondieran los dos manifests y bajara el
        /// catálogo. Ahora la única espera es la que hace falta: JUGAR aguarda a que la base esté.
        /// </summary>
        private void StartCheck()
        {
            if (_busy || _checking) return;
            _checkCts = new CancellationTokenSource();
            _checkTask = CheckAsync(_checkCts.Token);
        }

        private async Task CheckAsync(CancellationToken ct)
        {
            _checking = true;
            RefreshButton.IsEnabled = false;
            ShowBackgroundProgress();
            SetBackgroundStatus(Strings.Get("Status_Checking"));
            UpdatePendingStrip();
            UpdatePacksSummary();

            // Los dos manifests en paralelo: no dependen uno del otro.
            var bucket = _engine.LoadManifestAsync(ct);
            var pool = _engine.LoadPoolManifestAsync(ct: ct);
            _manifestsTask = IgnoreErrors(Task.WhenAll(bucket, pool));
            _baseTask = PrepareBaseAsync(bucket, ct);

            try
            {
                await _baseTask;
                await IgnoreErrors(pool);
                if (!ct.IsCancellationRequested)
                    await SyncCatalogQuietAsync(ct);
            }
            finally
            {
                _checking = false;
                // Sin manifest del bucket, PrepareBaseAsync ya dejó la pantalla en modo offline.
                if (!ct.IsCancellationRequested && bucket.Status == TaskStatus.RanToCompletion)
                {
                    // Recién ahora se sabe todo: base, pool y packs.
                    UpdatePendingStrip();
                    UpdatePacksSummary();
                    SetBackgroundStatus(Strings.Get("Status_Ready"));
                }
                RefreshButton.IsEnabled = !_busy;
                if (!_busy) Progress.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Manifest del bucket y, apenas está, la base Comunidad a la caché. Nunca tira: un error acá
        /// deja la pantalla en modo offline o con la base por bajar, y JUGAR la reintenta (y ahí sí
        /// muestra el diálogo, por ejemplo el de integridad).
        /// </summary>
        private async Task PrepareBaseAsync(Task bucket, CancellationToken ct)
        {
            try
            {
                await bucket;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                PopulateOffline();
                SetBackgroundStatus(Strings.Format("Fmt_Offline", Short(ex)));
                return;
            }
            PopulateFromManifest();
            if (!_engine.CommunityNeedsDownload()) return;

            try
            {
                SetBackgroundStatus(Strings.Get("Status_DownloadingBase"));
                await _engine.DownloadCommunityAsync(new Progress<DownloadProgress>(OnProgress), ct);
                PopulateFromManifest();
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                SetBackgroundStatus(Strings.Format("Fmt_BaseDownloadFailed", Short(ex)));
            }
        }

        private static async Task IgnoreErrors(Task task)
        {
            try { await task; } catch { /* lo informa quien mira la tarea original */ }
        }

        /// <summary>
        /// Sincroniza el catálogo del Buscador. Fuente primaria: el manifest de production del
        /// pool (cards_db/rules/filters, mismos artefactos que la app), que ya cargó
        /// <see cref="CheckAsync"/>. Fallback: la sección `catalog` opcional del manifest del
        /// launcher. No es crítico para jugar: cualquier error se ignora y el Buscador usa lo que
        /// haya en caché.
        /// </summary>
        private async Task SyncCatalogQuietAsync(CancellationToken ct)
        {
            // El catálogo no lo espera nadie: su progreso no le gana la barra a lo que pidió el usuario.
            var progress = new Progress<DownloadProgress>(p => { if (!_busy) OnProgress(p); });
            if (_engine.PoolManifestLoaded)
            {
                try
                {
                    if (_engine.CatalogNeedsSyncFromPool())
                    {
                        SetBackgroundStatus(Strings.Get("Status_SyncingCatalog"));
                        if (await _engine.SyncCatalogFromPoolAsync(progress, ct))
                            _catalog = null; // invalidar el catálogo cargado; se relee al abrir el Buscador
                    }
                    return;
                }
                catch
                {
                    // pool no disponible → probar la sección catalog del manifest del launcher
                }
            }
            try
            {
                if (!_engine.CatalogNeedsSync()) return;
                SetBackgroundStatus(Strings.Get("Status_SyncingCatalog"));
                if (await _engine.SyncCatalogAsync(progress, ct))
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

            UpdateOriginalAvailability();
            UpdatePendingStrip();
            UpdatePacksSummary();
        }

        /// <summary>
        /// Aviso de novedades del header: **siempre** dice algo, incluso cuando no hay nada. Antes
        /// cada cosa informaba su propio estado por separado y los packs solo adentro del modal, así
        /// que "no veo nada raro" no distinguía entre estar al día y no haber mirado.
        /// </summary>
        private void UpdatePendingStrip()
        {
            if (_checking)
            {
                // Con la búsqueda a medias, cualquier "todo al día" sería prematuro.
                PendingText.Text = Strings.Get("Pending_Checking");
                PendingText.Foreground = (Brush)FindResource("TextDim");
                PendingDot.Fill = (Brush)FindResource("GoldDim");
                return;
            }

            var pending = _engine.GetPendingSummary();
            // Sin el manifest del pool no hay lista de packs, y "ningún pack pendiente" no es lo
            // mismo que "no sé": el aviso sirve justamente para poder confiar en el "todo al día".
            var packsKnown = _engine.PoolManifestLoaded;

            if (!pending.AnyData)
            {
                PendingText.Text = packsKnown
                    ? Strings.Get("Pending_AllUpToDate")
                    : Strings.Get("Pending_UpToDateNoPacks");
                PendingText.Foreground = (Brush)FindResource("TextDim");
                PendingDot.Fill = (Brush)FindResource("GoldDim");
                return;
            }

            var parts = new List<string>();
            if (pending.CommunityBase) parts.Add(Strings.Get("Pending_CommunityBase"));
            if (pending.ImagePacks == 1) parts.Add(Strings.Get("Pending_PacksOne"));
            else if (pending.ImagePacks > 1) parts.Add(Strings.Format("Fmt_PendingPacksMany", pending.ImagePacks));
            if (!packsKnown) parts.Add(Strings.Get("Pending_PacksUnknown"));

            PendingText.Text = Strings.Format("Fmt_Pending", string.Join(", ", parts));
            PendingText.Foreground = (Brush)FindResource("Text");
            PendingDot.Fill = (Brush)FindResource("Gold");
        }

        /// <summary>
        /// Resumen de **todos** los packs de imágenes. Antes esta fila mostraba un solo pack —el de
        /// la comunidad, con el nombre fijo en el resx— al lado del botón que abre el listado de los
        /// otros: se leía como si hablara de todos, y con un segundo pack de la comunidad habría
        /// seguido nombrando al primero (ADR-055).
        /// </summary>
        private void UpdatePacksSummary(bool offline = false)
        {
            UpdatePacksAllButton();
            if (!_engine.PoolManifestLoaded)
            {
                PacksSummaryText.Text = Strings.Get(offline ? "Packs_SummaryOffline"
                    : _checking ? "Packs_SummaryChecking" : "Packs_SummaryUnknown");
                PacksSummaryText.Foreground = (Brush)FindResource("TextFaint");
                return;
            }

            var packs = _engine.ImagePacks;
            var downloaded = packs.Count(_engine.PackWasApplied);
            var pending = packs.Count(_engine.PackIsPending);

            if (pending == 0)
            {
                PacksSummaryText.Text = Strings.Format("Fmt_PacksSummary", downloaded, packs.Count);
                PacksSummaryText.Foreground = (Brush)FindResource("TextFaint");
            }
            else
            {
                PacksSummaryText.Text = pending == 1
                    ? Strings.Format("Fmt_PacksSummaryPendingOne", downloaded, packs.Count)
                    : Strings.Format("Fmt_PacksSummaryPendingMany", downloaded, packs.Count, pending);
                PacksSummaryText.Foreground = (Brush)FindResource("Gold");
            }
        }

        /// <summary>
        /// "Descargar novedades (N)" baja exactamente lo que cuenta el aviso —<see
        /// cref="LauncherEngine.PackIsPending"/>—, no todo lo que falta: un arco viejo en 1.0 que
        /// nunca se bajó es catálogo opcional y sigue en el listado. Sin nada pendiente, no se muestra.
        /// </summary>
        private void UpdatePacksAllButton()
        {
            var pending = _engine.PoolManifestLoaded ? _engine.ImagePacks.Count(_engine.PackIsPending) : 0;
            PacksAllButton.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
            PacksAllButton.Content = Strings.Format("Fmt_PacksDownloadAll", pending);
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

            UpdateBanner.Visibility = Visibility.Collapsed;
            // Sin manifest no se puede afirmar ni que hay novedades ni que no las hay: decirlo.
            PendingText.Text = Strings.Get("Pending_Unknown");
            PendingText.Foreground = (Brush)FindResource("TextDim");
            PendingDot.Fill = (Brush)FindResource("GoldDim");
            UpdatePacksSummary(offline: true);
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
                // Si la búsqueda de novedades sigue, la base puede estar bajando: se la espera a ella
                // (no al catálogo). Los manifests tienen tope, así que esto no cuelga.
                if (!_baseTask.IsCompleted)
                {
                    SetStatus(Strings.Get("Status_WaitingBase"));
                    await _baseTask;
                }

                if (_engine.NeedsApply(baseId))
                {
                    if (string.Equals(baseId, LauncherEngine.CommunityId, StringComparison.OrdinalIgnoreCase)
                        && _engine.Manifest == null)
                    {
                        throw new InvalidOperationException(Strings.Get("Err_NoManifestForCommunity"));
                    }

                    SetStatus(Strings.Get("Status_ApplyingBase"));
                    // Con la base ya en caché no hay await real adentro: sin Task.Run, descomprimir
                    // y copiar el database.xml corría en el hilo de la UI.
                    await Task.Run(() => _engine.ApplyBaseAsync(baseId, progress));
                    if (_engine.Manifest != null) PopulateFromManifest();
                }

                // Los packs de imágenes ya no se aplican acá, ni siquiera el recomendado: se bajan a
                // mano desde el listado y la pantalla avisa cuando falta alguno (ADR-064).

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

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => StartCheck();

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

                // Buscador y Mazos van juntos (ADR-052): el editor es el destino de los +/-.
                _searchWindow = Views.DeckWindow.OpenWorkspace(_catalog, _config.SunAndMoonPath);
                _searchWindow.Closed += (s2, e2) => _searchWindow = null;
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
                // El pool ya lo trae la búsqueda de novedades: se lo espera si está en camino y se lo
                // vuelve a pedir solo si no llegó. Antes se pedía de nuevo en cada apertura.
                await _manifestsTask;
                if (!_engine.PoolManifestLoaded)
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
            // El pack que señala el manifest del bucket es el recomendado. Se dice acá, en la fila, en
            // vez de separarlo: es un pack de imágenes como los demás (ADR-055) y, desde ADR-064,
            // también se baja a mano.
            if (_engine.IsBundledPack(pack))
                info.Children.Add(new TextBlock
                {
                    Text = Strings.Get("Packs_Bundled"),
                    Style = (Style)FindResource("BodySmall"),
                    Foreground = (Brush)FindResource("GoldDim"),
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
            else if (_engine.PackIsPending(pack))
            {
                // Republicado, un pack que antes no estaba, o el recomendado sin bajar.
                status.Text = Strings.Get(_engine.PackIsNew(pack) ? "Packs_BrandNew"
                    : _engine.IsBundledPack(pack) ? "Packs_RecommendedMissing" : "Packs_PendingNew");
                status.Foreground = (Brush)FindResource("Gold");
                button.Content = Strings.Get("Btn_PackDownload");
            }
            else
            {
                // Versión inicial y ya existía: se puede bajar cuando se quiera, pero no es una
                // novedad y por eso no se destaca (ni cuenta en el aviso de la pantalla principal).
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

            try
            {
                await ApplyPackWithProgressAsync(pack, PackLabel(pack.Id));
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
                BuildPacksList();     // re-render de estados + re-habilita botones
                UpdatePendingStrip(); // el pack que se acaba de aplicar deja de ser pendiente
                UpdatePacksSummary();
            }
        }

        /// <summary>
        /// Baja y aplica un pack mostrando el avance con <paramref name="label"/> adelante, en el
        /// listado y en la pantalla principal. Lo comparten el botón de cada fila y el de bajar todas
        /// las novedades; los errores los decide quien llama.
        /// </summary>
        private Task ApplyPackWithProgressAsync(PoolPackage pack, string label)
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                OnProgress(p);
                var text = p.Fraction.HasValue
                    ? Strings.Format("Fmt_PackProgress", label, p.BytesRead / 1048576.0, p.TotalBytes.Value / 1048576.0)
                    : Strings.Format("Fmt_PackProgressNoTotal", label, p.BytesRead / 1048576.0);
                PacksStatus.Text = text;
                SetStatus(text);
            });
            return _engine.ApplyPoolImagePackAsync(pack, progress);
        }

        /// <summary>
        /// Baja de una todos los packs pendientes, de a uno. Un hash que no coincide corta todo —es
        /// la misma alarma que con un pack suelto—; cualquier otro error se anota y se sigue con el
        /// siguiente, para que un pack caído no deje a los demás sin bajar.
        /// </summary>
        private async void PacksAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || !_engine.PoolManifestLoaded) return;
            var pending = _engine.ImagePacks.Where(_engine.PackIsPending).ToList();
            if (pending.Count == 0) return;

            if (!InstallationService.IsValidInstall(_config.SunAndMoonPath))
            {
                await ShowDialogAsync(Strings.Get("Dlg_InstallNotFoundTitle"), Strings.Get("Dlg_InstallNotFoundMsg"),
                    Strings.Get("Btn_Close"), null, isError: true);
                return;
            }

            var totalMb = pending.Sum(p => p.SizeBytes) / 1048576.0;
            if (!await ShowDialogAsync(Strings.Get("Dlg_PacksAllTitle"),
                    Strings.Format("Fmt_PacksAllConfirm", pending.Count, totalMb),
                    Strings.Get("Btn_PacksAllYes"), Strings.Get("Btn_Cancel"), isError: false))
                return;

            var failed = new List<string>();
            SetBusy(true, Strings.Get("Status_Preparing"), indeterminate: true);
            try
            {
                for (var i = 0; i < pending.Count; i++)
                {
                    var pack = pending[i];
                    var label = Strings.Format("Fmt_PacksAllStep", i + 1, pending.Count, PackLabel(pack.Id));
                    SetStatus(label);
                    try
                    {
                        await ApplyPackWithProgressAsync(pack, label);
                    }
                    catch (SecurityException ex)
                    {
                        await ShowDialogAsync(Strings.Get("Dlg_IntegrityTitle"),
                            Strings.Get("Dlg_IntegrityMsg") + "\n\n" + ex.Message,
                            Strings.Get("Btn_Close"), null, isError: true);
                        SetStatus(Strings.Get("Status_AbortedSecurity"));
                        return;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(PackLabel(pack.Id) + ": " + Short(ex));
                    }
                }

                var done = pending.Count - failed.Count;
                if (failed.Count == 0)
                {
                    SetStatus(Strings.Format("Fmt_PacksAllDone", done));
                }
                else
                {
                    SetStatus(Strings.Format("Fmt_PacksAllPartial", done, pending.Count));
                    await ShowDialogAsync(Strings.Get("Dlg_GenericErrorTitle"),
                        Strings.Format("Fmt_PacksAllFailed", string.Join("\n", failed)),
                        Strings.Get("Btn_Close"), null, isError: true);
                }
            }
            finally
            {
                SetBusy(false);
                UpdatePendingStrip();
                UpdatePacksSummary();
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
            // Lo que estuviera bajando venía del manifest anterior: se corta y se vuelve a buscar.
            _checkCts?.Cancel();
            await IgnoreErrors(_checkTask);
            StartCheck();
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
            RefreshButton.IsEnabled = !busy && !_checking;
            SettingsButton.IsEnabled = !busy;
            SearchButton.IsEnabled = !busy || _searchWindow != null;
            PacksButton.IsEnabled = !busy;
            PacksAllButton.IsEnabled = !busy;
            CommunityCard.IsEnabled = !busy;
            OriginalCard.IsEnabled = !busy && _engine.HasOriginalBackup();

            if (status != null) SetStatus(status);

            if (busy)
            {
                Progress.Visibility = Visibility.Visible;
                Progress.IsIndeterminate = indeterminate;
                Progress.Value = 0;
            }
            else if (_checking)
            {
                ShowBackgroundProgress(); // la búsqueda de novedades sigue: la barra vuelve a ser suya
            }
            else
            {
                Progress.Visibility = Visibility.Collapsed;
                Progress.IsIndeterminate = false;
            }
        }

        private void ShowBackgroundProgress()
        {
            if (_busy) return;
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
        }

        private void SetStatus(string text) => StatusText.Text = text;

        /// <summary>
        /// Status de la búsqueda en segundo plano: no pisa el de algo que el usuario pidió (JUGAR,
        /// un pack, abrir el Buscador), que es el que está mirando.
        /// </summary>
        private void SetBackgroundStatus(string text)
        {
            if (!_busy) SetStatus(text);
        }

        private static string Short(Exception ex)
        {
            var m = ex.Message;
            return m.Length > 140 ? m.Substring(0, 140) + "…" : m;
        }
    }
}
