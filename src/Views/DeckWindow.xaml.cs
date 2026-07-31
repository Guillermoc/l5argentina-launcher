using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Resources;
using L5ArgentinaLauncher.Services.Deck;
using L5ArgentinaLauncher.Services.Search;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Editor de mazos. Layout: lista de mazos + filtros a la izquierda (colapsable), cartas del
    /// mazo agrupadas Starter/Dynasty/Fate → tipo en el centro (como la app), detalle de carta o
    /// stats del mazo a la derecha (colapsable). Las cartas nuevas se agregan desde el Buscador
    /// (+/− sobre el mazo activo) o por Importar; acá se ajustan cantidades y se elimina.
    /// Persistencia: un .txt por mazo en la carpeta Decks de Sun and Moon (DeckLibrary).
    /// Ventana única por proceso (DeckWindow.Open).
    /// </summary>
    public partial class DeckWindow : Window
    {
        private enum DeckCardSort { GOLD_COST, NAME, FORCE }

        /// <summary>La dirección es parte de la opción, no un botón aparte (igual que el Buscador).</summary>
        private class SortOption
        {
            public string Label;
            public DeckCardSort Field;
            public bool Ascending;
            public override string ToString() => Label + (Ascending ? "  ↑" : "  ↓");
        }

        private class DeckItemVm
        {
            public string Id;
            public string Title { get; set; }
            public string Subtitle { get; set; }
        }

        /// <summary>Orden de la lista de conjuntos. El default es el trabajo más fresco arriba.</summary>
        private enum DeckSort { Recent, Alphabetical }

        /// <summary>Filtro por estado de armado (solo aplica a mazos, ver `IsDeckComplete`).</summary>
        private enum DeckState { All, Complete, Pending }

        private class ComboItem<T>
        {
            public T Value;
            public string Label;
            public override string ToString() => Label;
        }

        private static DeckWindow _instance;

        private readonly DeckLibrary _library;
        private readonly CardImageResolver _resolver;
        // Para abrir un Buscador desde el editor (mismo catálogo, misma instalación).
        private readonly CardCatalog _catalog;
        private readonly string _installPath;
        private readonly DispatcherTimer _notesTimer;
        private readonly DispatcherTimer _statusTimer;
        // VMs de imagen por carta (GridCell del Buscador: decode lazy + weak ref); el caché
        // evita re-decodificar en cada rebuild del cuerpo (cada +/− lo dispara)
        private readonly Dictionary<string, GridCell> _imageCells =
            new Dictionary<string, GridCell>(StringComparer.Ordinal);
        private string _selectedDeckId;
        private string _selectedCardId;
        // qué solapa está abierta a la izquierda: mazos (.txt de Decks) o listas (.txt de Lists)
        private string _kind = DeckLibrary.DeckType;

        /// <summary>Opacidad de una carta recortada (está en el mazo, no se juega).</summary>
        private const double TrimmedOpacity = 0.42;

        /// <summary>
        /// Cuatro tamaños de celda para la vista Grilla, como en el Buscador. Acá la celda incluye
        /// la barra de acciones al pie, así que la proporción es un poco más alta.
        ///
        /// En el más chico los botones `−`/`+`/`✕` no entran: queda **solo la cantidad**, que es
        /// el dato que no se puede deducir mirando el arte. Para tocar el mazo a ese tamaño está
        /// la vista Lista.
        /// </summary>
        private static readonly double[] ZoomWidths = { 118, 158, 213, 284 };
        private const double CellAspect = 330.0 / 213.0;

        private int _zoom = 2; // 213, el histórico

        private bool IsTinyZoom => _zoom == 0;

        private double CellWidth => ZoomWidths[_zoom];
        private double CellHeight => Math.Round(CellWidth * CellAspect);

        private enum RowAnim { None, Trim, Restore }

        // El cuerpo se reconstruye entero en cada mutación, así que la animación no puede vivir en
        // el elemento viejo: se anota qué carta cambió y la fila nueva arranca animándose desde el
        // estado anterior. Da la lectura de "esta se atenuó" sin diffear el árbol visual.
        private string _animCardId;
        private RowAnim _anim = RowAnim.None;
        // true durante InitializeComponent (los IsChecked=True del XAML disparan Checked antes
        // de que existan los demás controles) y durante los refrescos programáticos
        private bool _syncing = true;

        private DeckRepository Repo => _library.Repo;

        /// <summary>Abre (o trae al frente) la ventana única del editor, opcionalmente en un mazo.</summary>
        public static void Open(CardCatalog catalog, string installPath, string selectDeckId = null)
        {
            if (_instance == null)
            {
                _instance = new DeckWindow(catalog, installPath);
                _instance.Closed += (s, e) => _instance = null;
                _instance.Show();
            }
            else
            {
                if (_instance.WindowState == WindowState.Minimized)
                    _instance.WindowState = WindowState.Normal;
                _instance.Activate();
            }
            if (selectDeckId != null) _instance.SelectDeck(selectDeckId);
        }

        /// <summary>
        /// Abre el **espacio de trabajo**: el editor y un Buscador, en ese orden para que el Buscador
        /// quede al frente. Los dos **se abren** juntos (ADR-052): el Buscador sin editor deja los
        /// `+`/`−` sin destino visible, y tener que abrirlo a mano era un paso de más en el único
        /// flujo real. Devuelve el Buscador nuevo.
        ///
        /// Abrirlos juntos no los ata: cerrar el editor cierra solo el editor. El único dueño de la
        /// aplicación es <see cref="MainWindow"/> (ADR-052 revisado); volver con `Ctrl+D` desde el
        /// Buscador lo reabre en el mismo mazo.
        /// </summary>
        public static SearchWindow OpenWorkspace(CardCatalog catalog, string installPath)
        {
            Open(catalog, installPath);
            var search = new SearchWindow(catalog, installPath);
            // Mismo tamaño de diseño y las dos centradas: sin correr una, coinciden al píxel y
            // parecen una sola ventana.
            WindowSizingFix.CascadeFrom(_instance, search);
            search.Show();
            search.Activate();
            return search;
        }

        public DeckWindow(CardCatalog catalog, string installPath)
        {
            InitializeComponent();
            WindowSizingFix.Attach(this); // maximizado y apertura acotados al área de trabajo
            _library = DeckLibrary.GetOrLoad(catalog, installPath);
            _resolver = new CardImageResolver(installPath);
            _catalog = catalog;
            _installPath = installPath;

            var sortFields = new[]
            {
                new { Key = "Search_Sort_Cost", Field = DeckCardSort.GOLD_COST },
                new { Key = "Search_Sort_Name", Field = DeckCardSort.NAME },
                new { Key = "Search_Sort_Force", Field = DeckCardSort.FORCE },
            };
            SortCombo.ItemsSource = sortFields
                .SelectMany(f => new[]
                {
                    new SortOption { Label = Strings.Get(f.Key), Field = f.Field, Ascending = true },
                    new SortOption { Label = Strings.Get(f.Key), Field = f.Field, Ascending = false },
                })
                .ToList();
            SortCombo.SelectedIndex = 0; // _syncing sigue en true desde la construcción
            UpdateZoomLabel();

            DeckSortCombo.ItemsSource = new[]
            {
                new ComboItem<DeckSort> { Value = DeckSort.Recent, Label = Strings.Get("Deck_Sort_Recent") },
                new ComboItem<DeckSort> { Value = DeckSort.Alphabetical, Label = Strings.Get("Deck_Sort_Alpha") },
            };
            DeckSortCombo.SelectedIndex = 0; // más reciente primero
            DeckStateCombo.ItemsSource = new[]
            {
                new ComboItem<DeckState> { Value = DeckState.All, Label = Strings.Get("Deck_State_All") },
                new ComboItem<DeckState> { Value = DeckState.Complete, Label = Strings.Get("Deck_State_Complete") },
                new ComboItem<DeckState> { Value = DeckState.Pending, Label = Strings.Get("Deck_State_Pending") },
            };
            DeckStateCombo.SelectedIndex = 0;

            _notesTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _notesTimer.Tick += (s, e) =>
            {
                _notesTimer.Stop();
                SaveNotes();
            };
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _statusTimer.Tick += (s, e) =>
            {
                StatusText.Visibility = Visibility.Collapsed;
                _statusTimer.Stop();
            };

            _library.Repo.DeckChanged += OnDeckChanged;
            _library.Repo.DeckListChanged += OnDeckListChanged;
            _library.ActiveDeckChanged += OnActiveDeckChanged;
            _library.ActiveListChanged += OnActiveDeckChanged;
            Closed += (s, e) =>
            {
                _library.Repo.DeckChanged -= OnDeckChanged;
                _library.Repo.DeckListChanged -= OnDeckListChanged;
                _library.ActiveDeckChanged -= OnActiveDeckChanged;
                _library.ActiveListChanged -= OnActiveDeckChanged;
            };

            RefreshDeckList(_library.ActiveDeckId ?? Repo.Decks.FirstOrDefault()?.Id);

            // Sin ningún mazo (instalación nueva, o se borraron todos) la única acción posible es
            // crear uno, y el ＋ vive dentro del picker: se abre solo en vez de dejar la ventana
            // vacía sin decir por dónde empezar. Con al menos un mazo, RefreshDeckList ya abrió uno.
            if (Repo.Decks.Count == 0)
                Loaded += (s2, e2) => DeckPickerBtn.IsChecked = true;
        }

        // ------------------------------------------------------------------ title bar custom

        private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaxButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_StateChanged(object sender, EventArgs e)
        {
            // Ver SearchWindow: el área de trabajo la acota WindowSizingFix, no un margen.
            MaxButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        // ------------------------------------------------------------------ paneles colapsables

        /// <summary>
        /// Colapsa/expande los paneles laterales desde los rieles; el centro absorbe el ancho
        /// liberado. Las columnas de los rieles no se tocan: son la única forma de reabrir.
        /// </summary>
        /// <summary>Mismo criterio que el Buscador: bajo este ancho los paneles no entran.</summary>
        private const double NarrowWidth = 1000;

        private bool _narrow;
        private bool _wantDecks = true, _wantSide = true;

        /// <summary>Mismo mínimo que el Buscador: más angosto, el panel deja de servir.</summary>
        private const double MinPaneWidth = 180;

        // Ancho elegido con el mouse; es lo que se restaura al reabrir el panel.
        private double _decksWidth = 260, _sideWidth = 405;

        private void PaneToggle_Click(object sender, RoutedEventArgs e)
        {
            _wantDecks = DecksPaneBtn.IsChecked == true;
            _wantSide = SidePaneBtn.IsChecked == true;
            ApplyPanes();
        }

        private void ApplyPanes()
        {
            var decksOn = DecksPaneBtn.IsChecked == true;
            DecksPanel.Visibility = decksOn ? Visibility.Visible : Visibility.Collapsed;
            DecksCol.Width = new GridLength(decksOn ? _decksWidth : 0);
            DecksSplitter.IsEnabled = decksOn && !_narrow;

            var sideOn = SidePaneBtn.IsChecked == true;
            SidePanel.Visibility = sideOn ? Visibility.Visible : Visibility.Collapsed;
            SideCol.Width = new GridLength(sideOn ? _sideWidth : 0);
            SideSplitter.IsEnabled = sideOn && !_narrow;
        }

        /// <summary>
        /// Arrastre de las divisorias. Por debajo del mínimo el panel se colapsa en vez de quedar
        /// inservible: empujar el borde hasta el fondo es justo lo que pide un colapso.
        /// </summary>
        private void DecksSplitter_DragDelta(object sender, DragDeltaEventArgs e) =>
            ClampPane(DecksCol, DecksPaneBtn, ref _decksWidth);

        private void SideSplitter_DragDelta(object sender, DragDeltaEventArgs e) =>
            ClampPane(SideCol, SidePaneBtn, ref _sideWidth);

        private void ClampPane(ColumnDefinition column, ToggleButton rail, ref double remembered)
        {
            if (column.ActualWidth < MinPaneWidth)
            {
                rail.IsChecked = false;
                PaneToggle_Click(rail, null);
                return;
            }
            remembered = column.ActualWidth;
        }

        private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            // El GridSplitter convierte la columna central de `*` a píxeles al arrastrar, y ahí la
            // ventana deja de repartir bien el ancho al redimensionar. Se devuelve a estrella.
            CenterCol.Width = new GridLength(1, GridUnitType.Star);
            ApplyPanes();
        }

        /// <summary>
        /// Segundo escalón: con los paneles ya cerrados, el ancho mínimo real lo pone la **barra
        /// de botones**, y en el layout 70/30 la ventana no podía achicarse lo suficiente y se
        /// iba de pantalla. Acá se esconde lo accesorio — los conteos con el "Falta: {slot}", el
        /// zoom y el orden — que es información que igual se ve en el cuerpo del mazo.
        /// </summary>
        private const double CompactWidth = 900;

        private bool _compact;

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveChrome();

        private void ApplyResponsiveChrome()
        {
            ApplyNarrowMode();
            ApplyCompactMode();
        }

        private void ApplyNarrowMode()
        {
            var narrow = ActualWidth < NarrowWidth;
            if (narrow == _narrow) return;
            _narrow = narrow;
            foreach (var rail in new[] { DecksPaneBtn, SidePaneBtn })
            {
                rail.IsEnabled = !narrow;
                rail.ToolTip = narrow ? Strings.Get("Pane_TooNarrow") : null;
            }
            DecksPaneBtn.IsChecked = !narrow && _wantDecks;
            SidePaneBtn.IsChecked = !narrow && _wantSide;
            ApplyPanes();
        }

        private void ApplyCompactMode()
        {
            var compact = ActualWidth < CompactWidth;
            if (compact == _compact) return;
            _compact = compact;
            var hidden = compact ? Visibility.Collapsed : Visibility.Visible;
            // Los conteos y el "Falta: {slot}" son lo primero que se va: no son accionables desde
            // la barra y el cuerpo del mazo los repite (la fila roja bajo Starter).
            DeckCounts.Visibility = hidden;
            SortCombo.Visibility = hidden;
        }

        // ------------------------------------------------------------------ lista de mazos

        private bool IsListKind => _kind == DeckLibrary.ListType;

        private void RefreshDeckList(string selectId)
        {
            // el conjunto que se pide seleccionar manda sobre la solapa abierta: si el Buscador
            // pide una lista, se abre la solapa de listas en vez de no encontrarla
            var requested = selectId != null ? Repo.GetDeckById(selectId) : null;
            if (requested != null) _kind = requested.Type;

            var visible = Repo.Decks.Where(d => d.Type == _kind).ToList();
            RefreshClanCombo(visible);
            var filtered = ApplyDeckListFilters(visible);
            var items = SortDecks(filtered)
                .Select(d => new DeckItemVm
                {
                    Id = d.Id,
                    Title = d.Name,
                    Subtitle = DeckSubtitle(d),
                })
                .ToList();

            _syncing = true;
            TabDecksBtn.IsChecked = !IsListKind;
            TabListsBtn.IsChecked = IsListKind;
            // el estado de armado no significa nada en una lista
            DeckStateCombo.Visibility = IsListKind ? Visibility.Collapsed : Visibility.Visible;
            DeckListHeader.Text = Strings.Get(IsListKind ? "Deck_ListsHeader" : "Deck_ListHeader");
            NewDeckBtn.ToolTip = Strings.Get(IsListKind ? "Deck_NewListTip" : "Deck_NewTip");
            // en una lista no existe el recorte: la carta está o no está
            ShowTrimmedBtn.Visibility = IsListKind ? Visibility.Collapsed : Visibility.Visible;
            RefreshTrimmedLabel();
            DeckList.ItemsSource = items;
            var select = items.FirstOrDefault(i => i.Id == selectId)
                         ?? items.FirstOrDefault(i => i.Id == _selectedDeckId)
                         ?? items.FirstOrDefault();
            DeckList.SelectedItem = select;
            _syncing = false;

            _selectedDeckId = select?.Id;
            ActivateSelected();
            RefreshAll();
        }

        /// <summary>
        /// Seleccionar ES activar (ADR-042): el mazo elegido acá es el destino de los +/− del
        /// Buscador. Con la solapa de listas, lo que se activa es la lista (destino del ≡), sin
        /// tocar el mazo activo: son dos punteros distintos.
        /// </summary>
        private void ActivateSelected()
        {
            if (_selectedDeckId == null) return;
            if (IsListKind) _library.ActiveListId = _selectedDeckId;
            else _library.ActiveDeckId = _selectedDeckId;
        }

        private void KindTab_Click(object sender, RoutedEventArgs e)
        {
            var wanted = ReferenceEquals(sender, TabListsBtn) ? DeckLibrary.ListType : DeckLibrary.DeckType;
            if (_kind == wanted)
            {
                // click sobre la solapa ya abierta: el segmentado no se apaga
                _syncing = true;
                TabDecksBtn.IsChecked = !IsListKind;
                TabListsBtn.IsChecked = IsListKind;
                _syncing = false;
                return;
            }
            _kind = wanted;
            _selectedDeckId = null;
            RefreshDeckList(null);
        }

        // ------------------------------------------------------------------ filtros de la lista

        /// <summary>Nombre, clan y estado. El nombre matchea el del conjunto o el de sus cartas.</summary>
        private List<Models.Deck> ApplyDeckListFilters(List<Models.Deck> decks)
        {
            var search = (DeckSearchBox.Text ?? "").Trim();
            var clan = (DeckClanCombo.SelectedItem as ComboItem<string>)?.Value;
            var state = (DeckStateCombo.SelectedItem as ComboItem<DeckState>)?.Value ?? DeckState.All;

            return decks.Where(d =>
            {
                if (search.Length > 0 &&
                    !CardSearchEngine.LikeContains(d.Name, search) &&
                    !Repo.GetCardsWithCards(d.Id).Any(x => CardSearchEngine.LikeContains(x.Card.Name, search)))
                    return false;
                if (clan != null && !string.Equals(Repo.DeckClan(d.Id), clan, StringComparison.Ordinal))
                    return false;
                if (state != DeckState.All && d.Type != DeckLibrary.ListType)
                {
                    var complete = Repo.IsDeckComplete(d.Id);
                    if (state == DeckState.Complete && !complete) return false;
                    if (state == DeckState.Pending && complete) return false;
                }
                return true;
            }).ToList();
        }

        private IEnumerable<Models.Deck> SortDecks(List<Models.Deck> decks)
        {
            var sort = (DeckSortCombo.SelectedItem as ComboItem<DeckSort>)?.Value ?? DeckSort.Recent;
            return sort == DeckSort.Alphabetical
                ? decks.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                : decks.OrderByDescending(d => _library.UpdatedAt(d))
                       .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Solo los clanes que hay entre los conjuntos visibles, más "todos".</summary>
        private void RefreshClanCombo(List<Models.Deck> decks)
        {
            var previous = (DeckClanCombo.SelectedItem as ComboItem<string>)?.Value;
            var items = new List<ComboItem<string>>
            {
                new ComboItem<string> { Value = null, Label = Strings.Get("Deck_ClanAll") },
            };
            items.AddRange(decks.Select(d => Repo.DeckClan(d.Id))
                .Where(c => c != null)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .Select(c => new ComboItem<string> { Value = c, Label = ClanLabel(c) }));

            var wasSyncing = _syncing;
            _syncing = true;
            DeckClanCombo.ItemsSource = items;
            DeckClanCombo.SelectedItem = items.FirstOrDefault(i => i.Value == previous) ?? items[0];
            _syncing = wasSyncing;
        }

        private void DeckSearch_Changed(object sender, TextChangedEventArgs e) => RefreshDeckListIfReady();

        private void DeckListFilter_Changed(object sender, SelectionChangedEventArgs e) => RefreshDeckListIfReady();

        private void RefreshDeckListIfReady()
        {
            if (_syncing) return;
            RefreshDeckList(_selectedDeckId);
        }

        /// <summary>
        /// Pestañas del panel izquierdo: filtrar el mazo abierto o medirlo. Las dos hablan del mismo
        /// objeto (ADR-051), así que colapsar el panel esconde una sola cosa.
        /// </summary>
        private void PaneTab_Click(object sender, RoutedEventArgs e)
        {
            var stats = ReferenceEquals(sender, PaneStatsBtn);
            PaneStatsBtn.IsChecked = stats;
            PaneFiltersBtn.IsChecked = !stats;
            StatsScroll.Visibility = stats ? Visibility.Visible : Visibility.Collapsed;
            FiltersPane.Visibility = stats ? Visibility.Collapsed : Visibility.Visible;
        }

        // ------------------------------------------------------------------ selector de mazo

        /// <summary>
        /// El picker cuelga del título. Sigue al estado del toggle, no al click, por lo mismo que el
        /// desplegable de zoom: así queda consistente si el IsChecked lo cambia otra cosa.
        /// </summary>
        private void DeckPicker_Checked(object sender, RoutedEventArgs e)
        {
            DeckPickerPopup.IsOpen = true;
            Dispatcher.BeginInvoke(new Action(() => DeckSearchBox.Focus()), DispatcherPriority.Input);
        }

        private void DeckPicker_Unchecked(object sender, RoutedEventArgs e) => DeckPickerPopup.IsOpen = false;

        private void DeckPickerPopup_Closed(object sender, EventArgs e) => DeckPickerBtn.IsChecked = false;

        private string DeckSubtitle(Models.Deck deck)
        {
            // una lista no tiene lados Dynasty/Fate: lo informativo es cuántas cartas junta
            if (deck.Type == DeckLibrary.ListType)
                return Strings.Format("Fmt_ListCount", deck.Cards.Count(c => c.Quantity > 0));
            var clan = Repo.DeckClan(deck.Id);
            var counts = $"{Repo.DynastyCount(deck.Id)}/{Repo.FateCount(deck.Id)}";
            // el ✓ hace visible el mismo criterio por el que filtra el combo de estado
            var done = Repo.IsDeckComplete(deck.Id) ? " · ✓" : "";
            return (clan != null ? ClanLabel(clan) + " · " : "") + counts + done;
        }

        private static string ClanLabel(string clan)
        {
            var label = Strings.Get("Clan_" + clan);
            // sin recurso (facciones menores) → capitalizado crudo
            return label == "Clan_" + clan ? Capitalize(clan) : label;
        }

        private void DeckList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            _selectedDeckId = (DeckList.SelectedItem as DeckItemVm)?.Id;
            ActivateSelected();
            RefreshAll();
            // elegir un mazo es el objetivo del picker: cumplido, se cierra solo
            DeckPickerBtn.IsChecked = false;
        }

        /// <summary>Selecciona un conjunto desde afuera (p. ej. el botón ✎ del Buscador).</summary>
        public void SelectDeck(string deckId)
        {
            var item = (DeckList.ItemsSource as IEnumerable<DeckItemVm>)?.FirstOrDefault(i => i.Id == deckId);
            if (item != null) DeckList.SelectedItem = item; // dispara SelectionChanged
            // está en la otra solapa: cambiarla y volver a intentar
            else if (Repo.GetDeckById(deckId) != null) RefreshDeckList(deckId);
        }

        // ------------------------------------------------------------------ eventos de la biblioteca

        private void OnDeckChanged(Models.Deck deck)
        {
            if (deck.Id != _selectedDeckId) { RefreshDeckList(_selectedDeckId); return; }
            RefreshDeckList(_selectedDeckId); // subtítulos (conteos) + cuerpo + stats
        }

        private void OnDeckListChanged() => RefreshDeckList(_selectedDeckId);

        private void OnActiveDeckChanged() => RefreshDeckList(_selectedDeckId);

        // ------------------------------------------------------------------ barra de acciones

        /// <summary>
        /// Abre otra ventana del Buscador sobre el mismo catálogo, como el Ctrl+N del Buscador.
        /// Es el camino de ida del editor: las cartas nuevas se agregan desde ahí.
        /// </summary>
        private void OpenSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            var search = new SearchWindow(_catalog, _installPath);
            WindowSizingFix.CascadeFrom(this, search);
            search.Show();
        }

        /// <summary>
        /// Acomoda las dos ventanas para armar: el Buscador ocupando el 70 % izquierdo con su
        /// panel de detalle cerrado (la carta se mira en la grilla), y el editor en el 30 %
        /// derecho en vista Lista y con los dos paneles cerrados — a ese ancho no entran, y lo
        /// que importa acá es ver qué cartas tiene el mazo mientras se busca.
        /// Si no hay Buscador abierto, se abre uno.
        /// </summary>
        private void SideBySide_Click(object sender, RoutedEventArgs e)
        {
            var search = Application.Current.Windows.OfType<SearchWindow>().FirstOrDefault()
                         ?? new SearchWindow(_catalog, _installPath);
            if (!search.IsVisible) search.Show();

            var area = SystemParameters.WorkArea;
            // El 30 % puede quedar por debajo del mínimo del editor (en 1366 son 410 DIP). Se
            // reparte a partir de lo que el editor realmente necesita, así la suma sigue dando el
            // área de trabajo y ninguna de las dos ventanas se va de pantalla.
            var deckWidth = Math.Max(MinWidth, Math.Round(area.Width * 0.3));
            var searchWidth = Math.Max(search.MinWidth, area.Width - deckWidth);
            deckWidth = area.Width - searchWidth;

            Place(search, area.Left, area.Top, searchWidth, area.Height);
            Place(this, area.Left + searchWidth, area.Top, deckWidth, area.Height);

            search.ApplySideBySideLayout();

            // editor: vista Lista y paneles cerrados
            _syncing = true;
            ViewListBtn.IsChecked = true;
            ViewGridBtn.IsChecked = false;
            _syncing = false;
            _wantDecks = false;
            _wantSide = false;
            DecksPaneBtn.IsChecked = false;
            SidePaneBtn.IsChecked = false;
            ApplyPanes();
            RebuildIfReady();

            search.Activate();
        }

        private static void Place(Window w, double left, double top, double width, double height)
        {
            if (w.WindowState != WindowState.Normal) w.WindowState = WindowState.Normal;
            w.Left = left;
            w.Top = top;
            w.Width = Math.Max(w.MinWidth, width);
            w.Height = Math.Max(w.MinHeight, height);
        }

        private void DeckMenuBtn_Click(object sender, RoutedEventArgs e) =>
            DeckMenuPopup.IsOpen = !DeckMenuPopup.IsOpen;

        // ------------------------------------------------------------------ refresco general

        private void RefreshAll()
        {
            var deck = _selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null;
            var hasDeck = deck != null;
            foreach (var btn in new[] { ImportBtn, ExportBtn, DuplicateBtn, RenameBtn, DeleteBtn })
                btn.IsEnabled = hasDeck;
            ClearTrimmedBtn.IsEnabled = hasDeck && deck.Type != DeckLibrary.ListType &&
                                        deck.Cards.Any(c => c.Quantity == 0);
            // Imprimir una lista no significa nada: es una selección para filtrar, no un mazo.
            PdfBtn.Visibility = IsListKind ? Visibility.Collapsed : Visibility.Visible;
            PdfBtn.IsEnabled = hasDeck && deck.Cards.Any(c => c.Quantity > 0);

            // Sin mazo abierto no hay nada que filtrar ni que medir: las pestañas se apagan en vez
            // de mostrar dos paneles vacíos que parecen roto. El cuerpo del centro explica qué hacer.
            PaneFiltersBtn.IsEnabled = hasDeck;
            PaneStatsBtn.IsEnabled = hasDeck;
            EmptyPaneHint.Visibility = hasDeck ? Visibility.Collapsed : Visibility.Visible;
            FiltersPane.Visibility = hasDeck && PaneStatsBtn.IsChecked != true
                ? Visibility.Visible : Visibility.Collapsed;
            StatsScroll.Visibility = hasDeck && PaneStatsBtn.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
            RefreshTrimmedLabel();

            DeckTitle.Text = hasDeck ? deck.Name : Strings.Get("Deck_NoDeckSelected");
            // El contenido del picker es un panel, no texto, así que sin esto el control de
            // navegación principal no tiene nombre accesible y un lector de pantalla no dice nada.
            System.Windows.Automation.AutomationProperties.SetName(DeckPickerBtn, DeckTitle.Text);

            RefreshCounts(deck);
            RebuildDeckBody(deck);
            RebuildStats(deck);

            // notas: no pisar mientras el usuario tipea (el guardado tiene debounce)
            if (!NotesBox.IsFocused)
            {
                _syncing = true;
                NotesBox.Text = deck?.Notes ?? "";
                NotesExpander.IsExpanded = !string.IsNullOrWhiteSpace(deck?.Notes);
                _syncing = false;
            }
        }

        /// <summary>Conteos blandos de la app: rojo si &lt;40; nada bloquea (SPEC-VALIDACION §5).</summary>
        private void RefreshCounts(Models.Deck deck)
        {
            DeckCounts.Inlines.Clear();
            if (deck == null) return;
            if (deck.Type == DeckLibrary.ListType)
            {
                // una lista no se valida como mazo: no hay lados ni slots que cubrir, solo
                // cuántas cartas junta (el tope es 1 por carta, ADR-045)
                DeckCounts.Inlines.Add(new System.Windows.Documents.Run(
                    Strings.Format("Fmt_ListCount", deck.Cards.Count(c => c.Quantity > 0))));
                return;
            }
            var red = (Brush)FindResource("ErrorText");
            var total = Repo.TotalCardCount(deck.Id);
            var dynasty = Repo.DynastyCount(deck.Id);
            var fate = Repo.FateCount(deck.Id);

            DeckCounts.Inlines.Add(new System.Windows.Documents.Run(
                Strings.Format("Fmt_DeckTotal", total) + "   "));
            var dRun = new System.Windows.Documents.Run($"Dynasty {dynasty}/{DeckRepository.SideTarget}   ");
            if (dynasty < DeckRepository.SideTarget) dRun.Foreground = red;
            DeckCounts.Inlines.Add(dRun);
            var fRun = new System.Windows.Documents.Run($"Fate {fate}/{DeckRepository.SideTarget}");
            if (fate < DeckRepository.SideTarget) fRun.Foreground = red;
            DeckCounts.Inlines.Add(fRun);

            var uncovered = Repo.UncoveredRequiredSlots(deck.Id);
            if (uncovered.Count > 0)
                DeckCounts.Inlines.Add(new System.Windows.Documents.Run(
                    "   " + Strings.Format("Fmt_DeckMissingSlots",
                        string.Join(", ", uncovered.Select(s => s.Label ?? s.SlotId))))
                { Foreground = red });
        }

        // ------------------------------------------------------------------ cuerpo del mazo

        private void Filter_Changed(object sender, RoutedEventArgs e) => RebuildIfReady();
        private void Filter_Changed(object sender, TextChangedEventArgs e) => RebuildIfReady();
        private void Sort_Changed(object sender, RoutedEventArgs e) => RebuildIfReady();
        private void Sort_Changed(object sender, SelectionChangedEventArgs e) => RebuildIfReady();

        /// <summary>Lista (filas) o Grilla (cartas en imagen), manteniendo las secciones.</summary>
        private void ViewToggle_Click(object sender, RoutedEventArgs e)
        {
            HidePreview();
            var grid = ReferenceEquals(sender, ViewGridBtn);
            ZoomBtn.IsEnabled = grid;
            if (!grid) ZoomPopup.IsOpen = false;
            ViewGridBtn.IsChecked = grid;
            ViewListBtn.IsChecked = !grid;
            RebuildIfReady();
        }

        private void RebuildIfReady()
        {
            if (_syncing) return;
            RebuildDeckBody(_selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null);
        }

        private bool GroupVisible(string group)
        {
            switch (group)
            {
                case "starter": return GroupStarterBtn.IsChecked == true;
                case "dynasty": return GroupDynastyBtn.IsChecked == true;
                case "fate": return GroupFateBtn.IsChecked == true;
                default: return true;
            }
        }

        /// <summary>
        /// Rearma el centro: secciones Starter/Dynasty/Fate → sub-secciones por tipo (orden
        /// canónico TYPE_ORDER_MAP), filas con controles −/qty/+/✕, y avisos "Falta: {slot}"
        /// bajo Starter (blandos, como la app). El orden dentro de cada tipo es el elegido
        /// (coste/nombre/fuerza; solo el campo se invierte, port de sortComparator).
        /// </summary>
        private void RebuildDeckBody(Models.Deck deck)
        {
            DeckBody.Children.Clear();
            if (deck == null)
            {
                DeckBody.Children.Add(BodyText(
                    Strings.Get(IsListKind ? "Deck_NoListSelectedLong" : "Deck_NoDeckSelectedLong"), dim: true));
                return;
            }

            var filter = (FilterBox.Text ?? "").Trim();
            var selected = SortCombo.SelectedItem as SortOption;
            var sort = selected?.Field ?? DeckCardSort.GOLD_COST;
            var asc = selected?.Ascending ?? true;
            var gridView = ViewGridBtn.IsChecked == true;

            // Recortadas (quantity=0): siguen en el mazo pero no se juegan. Se muestran grisadas
            // salvo que se apague el toggle; en una lista no existe el concepto (ADR-046).
            var showTrimmed = ShowTrimmedBtn.IsChecked == true && deck.Type != DeckLibrary.ListType;
            var entries = Repo.GetCardsWithCards(deck.Id)
                .Where(x => x.Entry.Quantity > 0 || (showTrimmed && x.Entry.Quantity == 0))
                .Where(x => filter.Length == 0 || CardSearchEngine.LikeContains(x.Card.Name, filter))
                .ToList();

            foreach (var group in DeckGroups.Order)
            {
                if (!GroupVisible(group)) continue;
                var groupEntries = entries
                    .Where(x => string.Equals(x.Card.DeckGroup, group, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var uncovered = group == "starter" && deck.Type != "list"
                    ? Repo.UncoveredRequiredSlots(deck.Id)
                    : new List<InitialHoldingSlot>();
                if (groupEntries.Count == 0 && uncovered.Count == 0) continue;

                // Los números son siempre los del mazo que se juega: una recortada no suma ni
                // cuenta como carta distinta, aunque se esté viendo.
                DeckBody.Children.Add(GroupHeader(
                    $"{Capitalize(group)} — ({groupEntries.Sum(x => x.Entry.Quantity)}) [{groupEntries.Count(x => x.Entry.Quantity > 0)}]"));

                foreach (var typeGroup in groupEntries
                             .OrderBy(x => DeckGroups.TypeOrderInGroup(group, x.Card.Type))
                             .GroupBy(x => x.Card.Type))
                {
                    DeckBody.Children.Add(TypeHeader(
                        $"{Capitalize(typeGroup.Key)} — ({typeGroup.Sum(x => x.Entry.Quantity)}) [{typeGroup.Count(x => x.Entry.Quantity > 0)}]"));

                    IOrderedEnumerable<(DeckCardEntry Entry, Card Card)> sorted;
                    switch (sort)
                    {
                        case DeckCardSort.NAME:
                            sorted = Order(typeGroup, x => x.Card.Name, asc, StringComparer.Ordinal);
                            break;
                        case DeckCardSort.FORCE:
                            sorted = Order(typeGroup, x => x.Card.Force ?? int.MinValue, asc, Comparer<int>.Default);
                            break;
                        default: // GOLD_COST
                            sorted = Order(typeGroup, x => x.Card.Cost ?? int.MaxValue, asc, Comparer<int>.Default);
                            break;
                    }
                    if (gridView)
                    {
                        // cartas en imagen: ~3 columnas con el layout por defecto (más si se
                        // colapsan paneles), manteniendo la sección por tipo
                        var wrap = new WrapPanel { Margin = new Thickness(4, 2, 0, 4) };
                        foreach (var x in sorted.ThenByName())
                            wrap.Children.Add(CardImageCell(deck, x.Entry, x.Card));
                        DeckBody.Children.Add(wrap);
                    }
                    else
                        foreach (var x in sorted.ThenByName())
                            DeckBody.Children.Add(CardRow(deck, x.Entry, x.Card));
                }

                // Aviso blando de slots de holding inicial sin cubrir (bajo Starter)
                foreach (var slot in uncovered)
                    DeckBody.Children.Add(BodyText(
                        Strings.Format("Fmt_DeckMissingSlots", slot.Label ?? slot.SlotId), error: true));
            }

            // Líneas del txt que no matchearon el catálogo: se conservan y se muestran
            if (deck.Unknown.Count > 0)
            {
                DeckBody.Children.Add(GroupHeader(Strings.Get("Deck_UnknownHeader")));
                foreach (var unknown in deck.Unknown.ToList())
                    DeckBody.Children.Add(UnknownRow(deck, unknown));
            }

            if (DeckBody.Children.Count == 0)
                DeckBody.Children.Add(BodyText(Strings.Get("Deck_EmptyDeck"), dim: true));
        }

        private static IOrderedEnumerable<(DeckCardEntry Entry, Card Card)> Order<TKey>(
            IEnumerable<(DeckCardEntry Entry, Card Card)> source,
            Func<(DeckCardEntry Entry, Card Card), TKey> key, bool asc, IComparer<TKey> comparer) =>
            asc ? source.OrderBy(key, comparer) : source.OrderByDescending(key, comparer);

        private TextBlock GroupHeader(string text) => new TextBlock
        {
            Text = text,
            FontFamily = (FontFamily)FindResource("BrushFont"),
            FontSize = 19,
            Foreground = (Brush)FindResource("GoldSoft"),
            Margin = new Thickness(0, 10, 0, 4),
        };

        private TextBlock TypeHeader(string text) => new TextBlock
        {
            Text = text,
            FontFamily = (FontFamily)FindResource("BodyFont"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = (Brush)FindResource("Gold"),
            Margin = new Thickness(8, 6, 0, 3),
        };

        private TextBlock BodyText(string text, bool dim = false, bool error = false) => new TextBlock
        {
            Text = text,
            FontFamily = (FontFamily)FindResource("BodyFont"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource(error ? "ErrorText" : dim ? "TextDim" : "Text"),
            Margin = new Thickness(8, 4, 0, 2),
        };

        /// <summary>Fila de carta: nombre + mini-stats a la izquierda; − qty/max + ✕ a la derecha.</summary>
        private UIElement CardRow(Models.Deck deck, DeckCardEntry entry, Card card)
        {
            var trimmed = entry.Quantity == 0;
            var row = new Border
            {
                Padding = new Thickness(8, 3, 4, 3),
                CornerRadius = new CornerRadius(5),
                Background = card.Id == _selectedCardId
                    ? (Brush)new SolidColorBrush(Color.FromArgb(0xCC, 0x7D, 0x1F, 0x1C))
                    : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = card.Id,
                Opacity = trimmed ? TrimmedOpacity : 1.0,
                ToolTip = trimmed ? Strings.Get("Deck_TrimmedTip") : null,
            };
            AnimateIfPending(row, card, trimmed);
            row.MouseEnter += (s, e) => { if (card.Id != _selectedCardId) row.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xC9, 0xA9, 0x61)); };
            row.MouseLeave += (s, e) =>
            {
                if (card.Id != _selectedCardId) row.Background = Brushes.Transparent;
                HidePreview();
            };
            row.MouseLeftButtonUp += (s, e) => SelectCard(card);
            // Alt también en la vista Lista: los candidatos se resuelven acá porque la fila no
            // tiene GridCell. Perezoso: solo se paga cuando Alt está apretado.
            row.MouseMove += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) { HidePreview(); return; }
                ShowPreview(_resolver.ResolveCandidates(card, null, fallbackChain: true), card);
            };

            var dock = new DockPanel { LastChildFill = true };

            var delBtn = SmallButton("✕", Strings.Get("Deck_RemoveEntryTip"));
            delBtn.Click += (s, e) => { Repo.DeleteCardEntry(deck.Id, card.Id); };
            DockPanel.SetDock(delBtn, Dock.Right);
            dock.Children.Add(delBtn);

            // En una lista la carta está o no está (tope 1): −/qty/+ no tendrían nada que hacer,
            // así que la única acción es sacarla.
            if (deck.Type != DeckLibrary.ListType)
            {
                var plusBtn = SmallButton("+", trimmed ? Strings.Get("Deck_RestoreTip") : null);
                plusBtn.Click += (s, e) => AddOrRestore(deck, card, trimmed);
                DockPanel.SetDock(plusBtn, Dock.Right);
                dock.Children.Add(plusBtn);

                var qty = new TextBlock
                {
                    Text = $"{entry.Quantity}/{card.MaxCopies}",
                    FontFamily = (FontFamily)FindResource("BodyFont"),
                    FontSize = 12,
                    Foreground = (Brush)FindResource(trimmed ? "TextDim" : "Gold"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(7, 0, 7, 0),
                    MinWidth = 26,
                    TextAlignment = TextAlignment.Center,
                };
                DockPanel.SetDock(qty, Dock.Right);
                dock.Children.Add(qty);

                var minusBtn = SmallButton("−", trimmed ? null : Strings.Get("Deck_TrimTip"));
                minusBtn.IsEnabled = !trimmed; // ya está en 0: el paso siguiente es ✕, no −
                minusBtn.Click += (s, e) => TrimOne(deck, card, entry);
                DockPanel.SetDock(minusBtn, Dock.Right);
                dock.Children.Add(minusBtn);
            }

            var name = new TextBlock
            {
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            name.Inlines.Add(new System.Windows.Documents.Run(card.Name)
            { Foreground = (Brush)FindResource(trimmed ? "TextDim" : "Text") });
            if (trimmed)
                name.Inlines.Add(new System.Windows.Documents.Run("  " + Strings.Get("Deck_TrimmedTag"))
                { Foreground = (Brush)FindResource("TextFaint"), FontSize = 10 });
            var mini = MiniStats(card);
            if (mini.Length > 0)
                name.Inlines.Add(new System.Windows.Documents.Run("   " + mini)
                { Foreground = (Brush)FindResource("TextDim"), FontSize = 11 });
            dock.Children.Add(name);

            row.Child = dock;
            return row;
        }

        /// <summary>
        /// Celda de la vista Grilla: imagen de la carta (decode lazy, mismas rutas candidatas
        /// que el Buscador) con nombre de placeholder detrás, y barra fija −/qty/+/✕ al pie.
        /// </summary>
        private UIElement CardImageCell(Models.Deck deck, DeckCardEntry entry, Card card)
        {
            var isTrimmed = entry.Quantity == 0;
            if (!_imageCells.TryGetValue(card.Id, out var vm))
            {
                vm = new GridCell
                {
                    Card = card,
                    CandidatePaths = _resolver.ResolveCandidates(card, null, fallbackChain: true),
                };
                _imageCells[card.Id] = vm;
            }

            var cell = new Border
            {
                Width = CellWidth,
                Height = CellHeight,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Opacity = isTrimmed ? TrimmedOpacity : 1.0,
                ToolTip = isTrimmed ? Strings.Get("Deck_TrimmedTip") : null,
            };
            cell.MouseLeftButtonUp += (s, e) => SelectCard(card);
            // Alt + pasar por encima = ver la carta grande al lado del cursor (igual que el Buscador)
            cell.MouseMove += (s, e) => ShowPreview(vm, card);
            cell.MouseLeave += (s, e) => HidePreview();
            // click derecho = copiar el arte, igual que en la grilla del Buscador
            cell.MouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                var ok = CardImageClipboard.TryCopy(vm.CandidatePaths);
                ShowStatus(ok ? Strings.Format("Fmt_CopiedImage", card.Name)
                              : Strings.Get("Detail_CopyImageFail"));
            };
            AnimateIfPending(cell, card, isTrimmed);

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var imgHost = new Grid();
            imgHost.Children.Add(new TextBlock
            {
                Text = card.Name,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10),
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 12,
                Foreground = (Brush)FindResource("TextDim"),
            });
            // Igual que la grilla del Buscador: ImageBrush sobre un Border para que el arte
            // respete el CornerRadius (el escaneo es rectangular y le asoman las puntitas
            // blancas), y el marco de la celda solo se dibuja cuando NO hay imagen.
            var art = new Border { CornerRadius = new CornerRadius(7), DataContext = vm };
            var artBrush = new ImageBrush { Stretch = Stretch.UniformToFill };
            // Binding sincrónico a propósito: GridCell.Thumb devuelve lo ya decodificado y encola el
            // resto en ThumbnailLoader. Con IsAsync, WPF despachaba los pedidos a cuentagotas.
            BindingOperations.SetBinding(artBrush, ImageBrush.ImageSourceProperty,
                new Binding(nameof(GridCell.Thumb)) { Source = vm });
            art.Background = artBrush;
            imgHost.Children.Add(art);
            layout.Children.Add(imgHost);

            ApplyCellFrame(cell, art, card.Id == _selectedCardId);

            var bar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x14, 0x0A, 0x05)),
                CornerRadius = new CornerRadius(0, 0, 5, 5),
                Padding = new Thickness(3),
            };
            Grid.SetRow(bar, 1);
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            if (IsTinyZoom)
            {
                // A 118 de ancho no entran los botones. Queda la cantidad, que es lo único que no
                // se deduce mirando la carta; para editar a este tamaño está la vista Lista.
                // En una lista no hay cantidad que mostrar, así que la barra no se dibuja.
                if (deck.Type != DeckLibrary.ListType)
                {
                    actions.Children.Add(new TextBlock
                    {
                        Text = $"{entry.Quantity}/{card.MaxCopies}",
                        FontFamily = (FontFamily)FindResource("BodyFont"),
                        FontSize = 12,
                        Foreground = (Brush)FindResource(isTrimmed ? "TextDim" : "Gold"),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                    bar.Child = actions;
                    layout.Children.Add(bar);
                }
                cell.Child = layout;
                return cell;
            }
            if (deck.Type != DeckLibrary.ListType) // en una lista la carta está o no está
            {
                var minus = SmallButton("−", isTrimmed ? null : Strings.Get("Deck_TrimTip"));
                minus.IsEnabled = !isTrimmed;
                minus.Click += (s, e) => TrimOne(deck, card, entry);
                actions.Children.Add(minus);
                actions.Children.Add(new TextBlock
                {
                    Text = $"{entry.Quantity}/{card.MaxCopies}",
                    FontFamily = (FontFamily)FindResource("BodyFont"),
                    FontSize = 12,
                    Foreground = (Brush)FindResource(isTrimmed ? "TextDim" : "Gold"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(7, 0, 7, 0),
                });
                var plus = SmallButton("+", isTrimmed ? Strings.Get("Deck_RestoreTip") : null);
                plus.Click += (s, e) => AddOrRestore(deck, card, isTrimmed);
                actions.Children.Add(plus);
            }
            var del = SmallButton("✕", Strings.Get("Deck_RemoveEntryTip"));
            del.Margin = new Thickness(deck.Type == DeckLibrary.ListType ? 0 : 12, 0, 0, 0);
            del.Click += (s, e) => Repo.DeleteCardEntry(deck.Id, card.Id);
            actions.Children.Add(del);
            bar.Child = actions;
            layout.Children.Add(bar);

            cell.Child = layout;
            return cell;
        }

        // ------------------------------------------------------------------ recorte

        /// <summary>
        /// `−` en la pantalla de mazo: al llegar a 0 la carta **no** se va, queda recortada
        /// (`keepRowAtZero`, SPEC-DECK-CRUD §4). Conserva su `sortOrder`, así que el `+` la
        /// devuelve a su lugar. Para sacarla de verdad está el `✕`, que es otra acción.
        /// </summary>
        private void TrimOne(Models.Deck deck, Card card, DeckCardEntry entry)
        {
            if (entry.Quantity <= 0) return;
            var willTrim = entry.Quantity == 1; // la última copia: pasa a recortada, no se va
            if (willTrim) MarkAnim(card.Id, RowAnim.Trim);
            Repo.RemoveCard(deck.Id, card.Id, keepRowAtZero: true);
            if (willTrim) ShowStatus(Strings.Format("Fmt_DeckTrimmed", card.Name));
        }

        /// <summary>`+`: sobre una recortada es un rescate; sobre el resto, agregar una copia.</summary>
        private void AddOrRestore(Models.Deck deck, Card card, bool trimmed)
        {
            if (trimmed) MarkAnim(card.Id, RowAnim.Restore);
            ReportAdd(Repo.AddCard(deck.Id, card.Id));
        }

        private void ShowTrimmed_Click(object sender, RoutedEventArgs e) => RebuildIfReady();

        // ------------------------------------------------------------------ zoom y previsualización

        /// <summary>Ctrl+rueda = zoom de la vista Grilla. Sin Ctrl, scrollea.</summary>
        private void DeckBody_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (ViewGridBtn.IsChecked != true) return; // el zoom es de la grilla
            e.Handled = true;
            SetZoom(_zoom + (e.Delta > 0 ? 1 : -1));
        }

        private void Help_Click(object sender, RoutedEventArgs e) =>
            Dialogs.Info(this, Strings.Get("Help_ShortcutsTitle"), Strings.Get("Help_ShortcutsDeck"));

        /// <summary>El tamaño de celda es un desplegable del segmentado de vista, como en el Buscador.</summary>
        // El popup sigue al ESTADO del toggle, no al click: así queda consistente aunque el
        // IsChecked lo cambie otra cosa (el propio Closed del popup, un atajo, automatización).
        private void ZoomBtn_Checked(object sender, RoutedEventArgs e)
        {
            BuildZoomOptions();
            ZoomPopup.IsOpen = true;
        }

        private void ZoomBtn_Unchecked(object sender, RoutedEventArgs e) => ZoomPopup.IsOpen = false;

        private void ZoomPopup_Closed(object sender, EventArgs e) => ZoomBtn.IsChecked = false;

        private static readonly string[] ZoomLabelKeys =
            { "Search_ZoomTiny", "Search_ZoomSmall", "Search_ZoomNormal", "Search_ZoomLarge" };

        private void BuildZoomOptions()
        {
            ZoomOptions.Children.Clear();
            for (var i = 0; i < ZoomWidths.Length; i++)
            {
                var level = i;
                var row = new Button
                {
                    Style = (Style)FindResource("MenuRow"),
                    Content = (level == _zoom ? "• " : "    ") + Strings.Get(ZoomLabelKeys[level]),
                };
                row.Click += (s, ev) => { ZoomPopup.IsOpen = false; SetZoom(level); };
                ZoomOptions.Children.Add(row);
            }
        }

        private void UpdateZoomLabel() => ZoomBtn.Content = Strings.Get(ZoomLabelKeys[_zoom]) + "  ▾";

        private void SetZoom(int zoom)
        {
            zoom = Math.Max(0, Math.Min(ZoomWidths.Length - 1, zoom));
            if (zoom == _zoom) return;
            _zoom = zoom;
            UpdateZoomLabel();
            // las celdas se construyen en código (no hay plantilla que re-medir): se rearma
            RebuildIfReady();
        }

        private string _previewCardId;

        /// <summary>
        /// Con **Alt** apretado, la carta se abre grande al lado del cursor con el arte original.
        /// El modificador se lee en el MouseMove por lo mismo que en el Buscador: Alt llega como
        /// `Key.System` y compite con la activación de menús.
        /// </summary>
        private void ShowPreview(GridCell vm, Card card) => ShowPreview(vm?.CandidatePaths, card);

        /// <summary>
        /// Variante por rutas: la vista Lista no arma `GridCell`, así que resuelve los candidatos
        /// en el momento. Es la misma previsualización, no una segunda.
        /// </summary>
        private void ShowPreview(List<string> candidates, Card card)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) { HidePreview(); return; }
            if (card == null) { HidePreview(); return; }
            if (HoverPreview.IsOpen && _previewCardId == card.Id) return;
            var image = CardImageClipboard.LoadFull(candidates);
            if (image == null) { HidePreview(); return; }
            _previewCardId = card.Id;
            HoverPreviewImage.Source = image;
            HoverPreviewPlacer.Place(HoverPreview, this);
            HoverPreview.IsOpen = true;
        }

        private void HidePreview()
        {
            if (!HoverPreview.IsOpen) return;
            HoverPreview.IsOpen = false;
            HoverPreviewImage.Source = null;
            _previewCardId = null;
        }

        /// <summary>"Recortadas (N)" como la quick bar de la app; sin ninguna, solo la etiqueta.</summary>
        private void RefreshTrimmedLabel()
        {
            var deck = _selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null;
            var n = deck == null || deck.Type == DeckLibrary.ListType
                ? 0 : deck.Cards.Count(c => c.Quantity == 0);
            ShowTrimmedBtn.Content = n > 0
                ? Strings.Format("Fmt_ShowTrimmed", n)
                : Strings.Get("Deck_ShowTrimmed");
        }

        /// <summary>Saca del mazo todas las recortadas de una (`removeAllTrimmed` de la app).</summary>
        private void ClearTrimmed_Click(object sender, RoutedEventArgs e)
        {
            DeckMenuPopup.IsOpen = false;
            var deck = _selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null;
            if (deck == null) return;
            var count = deck.Cards.Count(c => c.Quantity == 0);
            if (count == 0) { ShowStatus(Strings.Get("Deck_NoTrimmed")); return; }
            if (!Dialogs.Confirm(this, Strings.Get("Deck_ClearTrimmedMenu"),
                                 Strings.Format("Fmt_ClearTrimmedConfirm", count),
                                 Strings.Get("Btn_Delete"))) return;
            Repo.RemoveAllTrimmed(deck.Id);
        }

        private void MarkAnim(string cardId, RowAnim kind)
        {
            _animCardId = cardId;
            _anim = kind;
        }

        /// <summary>
        /// Anima la fila recién construida desde el estado anterior hasta el nuevo, para que
        /// recortar y rescatar se **vean** en vez de aparecer ya resueltos. Se consume una sola
        /// vez: el resto del cuerpo se reconstruye sin animar.
        /// </summary>
        private void AnimateIfPending(UIElement element, Card card, bool trimmed)
        {
            if (_anim == RowAnim.None || card.Id != _animCardId) return;
            var from = _anim == RowAnim.Trim ? 1.0 : TrimmedOpacity;
            var to = trimmed ? TrimmedOpacity : 1.0;
            _anim = RowAnim.None;
            _animCardId = null;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
        }

        private static string MiniStats(Card card)
        {
            var parts = new List<string>();
            if (card.DisplayCost.Length > 0) parts.Add("©" + card.DisplayCost);
            if (card.DisplayForce.Length > 0 || card.DisplayChi.Length > 0)
                parts.Add((card.DisplayForce.Length > 0 ? card.DisplayForce : "–") + "F/" +
                          (card.DisplayChi.Length > 0 ? card.DisplayChi : "–") + "C");
            if (card.DisplayFocus.Length > 0) parts.Add("focus " + card.DisplayFocus);
            return string.Join("  ", parts);
        }

        private UIElement UnknownRow(Models.Deck deck, UnknownDeckLine unknown)
        {
            var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 2, 4, 2) };
            var delBtn = SmallButton("✕", Strings.Get("Deck_RemoveEntryTip"));
            delBtn.Click += (s, e) =>
            {
                deck.Unknown.Remove(unknown);
                _library.SaveDeck(deck);
                RebuildDeckBody(deck);
            };
            DockPanel.SetDock(delBtn, Dock.Right);
            dock.Children.Add(delBtn);
            dock.Children.Add(new TextBlock
            {
                Text = unknown.Raw,
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 12,
                Foreground = (Brush)FindResource("TextDim"),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Strings.Get("Deck_UnknownTip"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            return dock;
        }

        private Button SmallButton(string content, string tooltip)
        {
            var btn = new Button
            {
                Content = content,
                Style = (Style)FindResource("GhostButton"),
                Height = 22,
                FontSize = 12,
                Padding = new Thickness(7, 0, 7, 0),
                Margin = new Thickness(3, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (tooltip != null) btn.ToolTip = tooltip;
            return btn;
        }

        /// <summary>
        /// Los AddCardResult distintos de Success se informan (divergencia deliberada: la app
        /// los ignora y la carta simplemente no incrementa — SPEC-VALIDACION §6).
        /// </summary>
        private void ReportAdd(AddCardResult result)
        {
            switch (result.Kind)
            {
                case AddCardResultKind.Banned: ShowStatus(Strings.Get("Deck_AddBanned")); break;
                case AddCardResultKind.LimitReached: ShowStatus(Strings.Format("Fmt_DeckLimit", result.Max)); break;
                case AddCardResultKind.SlotAlreadyCovered: ShowStatus(Strings.Get("Deck_AddSlotCovered")); break;
                case AddCardResultKind.NoActiveDeck: ShowStatus(Strings.Get("Deck_AddNotFound")); break;
            }
        }

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        // ------------------------------------------------------------------ panel derecho

        private void SelectCard(Card card)
        {
            _selectedCardId = card.Id;
            Detail.ShowCard(card, _resolver.ResolveCandidates(card, null, fallbackChain: true));
            RebuildDeckBody(_selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null);
        }

        // ------------------------------------------------------------------ stats del mazo

        /// <summary>
        /// Stats del StatsModal de la app (SPEC-STATS-INICIALES §3) + honor inicial (§1, la única
        /// fórmula existente: startingHonor del primer stronghold). Solo cuenta quantity &gt; 0.
        /// </summary>
        private void RebuildStats(Models.Deck deck)
        {
            StatsBody.Children.Clear();
            if (deck == null) return;
            var entries = Repo.GetCardsWithCards(deck.Id).Where(x => x.Entry.Quantity > 0).ToList();

            // Honor inicial (solo si hay stronghold; 0 puede ser legítimo — Barracks of the Damned)
            if (entries.Any(x => x.Card.Type == "stronghold"))
                StatsBody.Children.Add(BodyText(
                    Strings.Get("Deck_Stats_InitialHonor") + ": " + Repo.InitialHonor(deck.Id)));

            // Conteos por grupo/tipo
            StatsBody.Children.Add(GroupHeader(Strings.Get("Deck_Stats_Counts")));
            foreach (var group in DeckGroups.Order)
            {
                var groupEntries = entries
                    .Where(x => string.Equals(x.Card.DeckGroup, group, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (groupEntries.Count == 0) continue;
                StatsBody.Children.Add(BodyText($"{Capitalize(group)}: {groupEntries.Sum(x => x.Entry.Quantity)}"));
                foreach (var typeGroup in groupEntries
                             .OrderBy(x => DeckGroups.TypeOrderInGroup(group, x.Card.Type))
                             .GroupBy(x => x.Card.Type))
                    StatsBody.Children.Add(BodyText(
                        $"    {Capitalize(typeGroup.Key)}: {typeGroup.Sum(x => x.Entry.Quantity)}", dim: true));
            }

            // Curva de coste por tipo (personalities/holdings/followers/items/spells, como la app)
            foreach (var type in new[] { "personality", "holding", "follower", "item", "spell" })
            {
                var ofType = entries.Where(x => x.Card.Type == type).ToList();
                if (ofType.Count == 0) continue;
                StatsBody.Children.Add(GroupHeader(
                    Strings.Get("Deck_Stats_CostCurve") + " — " + Capitalize(type)));
                var dist = ofType.GroupBy(x => x.Card.Cost)
                    .Select(g => new { Cost = g.Key, Count = g.Sum(x => x.Entry.Quantity) })
                    .OrderBy(g => g.Cost ?? int.MaxValue) // null (coste variable ★) al final
                    .ToList();
                var maxCount = dist.Max(d => d.Count);
                foreach (var d in dist)
                    StatsBody.Children.Add(BarRow(d.Cost?.ToString() ?? "★", d.Count, maxCount));

                if (type == "personality")
                {
                    var median = MedianCost(ofType);
                    if (median != null)
                        StatsBody.Children.Add(BodyText(
                            Strings.Get("Deck_Stats_Median") + ": " + median, dim: true));
                }
            }

            // Top 5 keywords ponderadas por cantidad (excluye Unique/Experienced)
            var kwTypes = new HashSet<string>(StringComparer.Ordinal)
                { "personality", "follower", "item", "spell" };
            var keywords = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var x in entries.Where(x => kwTypes.Contains(x.Card.Type)))
                foreach (var kw in CardSearchEngine.SplitPipe(x.Card.Keywords))
                {
                    if (kw == "Unique" || kw == "Experienced") continue;
                    keywords.TryGetValue(kw, out var n);
                    keywords[kw] = n + x.Entry.Quantity;
                }
            if (keywords.Count > 0)
            {
                StatsBody.Children.Add(GroupHeader(Strings.Get("Deck_Stats_TopKeywords")));
                foreach (var kv in keywords.OrderByDescending(kv => kv.Value)
                             .ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(5))
                    StatsBody.Children.Add(BodyText($"{kv.Key}: {kv.Value}"));
            }

            // Foco promedio Fate: Σ(focus×qty)/Σ(qty), un decimal
            var fateFocus = entries.Where(x =>
                string.Equals(x.Card.DeckGroup, "fate", StringComparison.OrdinalIgnoreCase) &&
                x.Card.Focus != null).ToList();
            if (fateFocus.Count > 0)
            {
                var totalQty = fateFocus.Sum(x => x.Entry.Quantity);
                var avg = fateFocus.Sum(x => (double)x.Card.Focus.Value * x.Entry.Quantity) / totalQty;
                StatsBody.Children.Add(GroupHeader(Strings.Get("Deck_Stats_AvgFocus")));
                StatsBody.Children.Add(BodyText(avg.ToString("0.0")));
            }
        }

        /// <summary>Mediana de coste de la app: expande por qty, descarta null, división ENTERA.</summary>
        private static int? MedianCost(List<(DeckCardEntry Entry, Card Card)> ofType)
        {
            var costs = new List<int>();
            foreach (var x in ofType)
                if (x.Card.Cost != null)
                    for (int i = 0; i < x.Entry.Quantity; i++) costs.Add(x.Card.Cost.Value);
            if (costs.Count == 0) return null;
            costs.Sort();
            return costs.Count % 2 == 1
                ? costs[costs.Count / 2]
                : (costs[costs.Count / 2 - 1] + costs[costs.Count / 2]) / 2;
        }

        private UIElement BarRow(string label, int count, int maxCount)
        {
            var dock = new DockPanel { Margin = new Thickness(8, 2, 0, 2), LastChildFill = false };
            dock.Children.Add(new TextBlock
            {
                Text = label,
                Width = 24,
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 12,
                Foreground = (Brush)FindResource("TextDim"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            dock.Children.Add(new Rectangle
            {
                Height = 11,
                Width = maxCount > 0 ? Math.Max(4, 180.0 * count / maxCount) : 4,
                RadiusX = 2,
                RadiusY = 2,
                Fill = (Brush)FindResource("Gold"),
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center,
            });
            dock.Children.Add(new TextBlock
            {
                Text = "  " + count,
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 12,
                Foreground = (Brush)FindResource("Text"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            return dock;
        }

        // ------------------------------------------------------------------ notas

        private void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing || _selectedDeckId == null) return;
            _notesTimer.Stop();
            _notesTimer.Start();
        }

        private void SaveNotes()
        {
            if (_selectedDeckId == null) return;
            var deck = Repo.GetDeckById(_selectedDeckId);
            if (deck == null) return;
            var text = NotesBox.Text;
            if ((deck.Notes ?? "") == (string.IsNullOrWhiteSpace(text) ? "" : text)) return;
            Repo.UpdateNotes(_selectedDeckId, text);
        }

        // ------------------------------------------------------------------ acciones de mazo

        private void NewDeck_Click(object sender, RoutedEventArgs e)
        {
            var name = Prompt(Strings.Get(IsListKind ? "Search_ListCreateTitle" : "Deck_New"),
                              Strings.Get(IsListKind ? "Search_ListCreatePrompt" : "Deck_NewNamePrompt"), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            // se activa solo, como la app
            var id = IsListKind ? _library.CreateList(name.Trim()) : _library.CreateDeck(name.Trim());
            RefreshDeckList(id);
        }


        private void RenameBtn_Click(object sender, RoutedEventArgs e)
        {
            DeckMenuPopup.IsOpen = false;
            var deck = _selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null;
            if (deck == null) return;
            var name = Prompt(Strings.Get("Deck_Rename"), Strings.Get("Deck_RenamePrompt"), deck.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == deck.Name) return;
            _library.RenameDeck(deck.Id, name.Trim());
        }

        private void DuplicateBtn_Click(object sender, RoutedEventArgs e)
        {
            DeckMenuPopup.IsOpen = false;
            if (_selectedDeckId == null) return;
            var copyId = _library.DuplicateDeck(_selectedDeckId);
            if (copyId != null) RefreshDeckList(copyId);
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            DeckMenuPopup.IsOpen = false;
            var deck = _selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null;
            if (deck == null) return;
            var isList = deck.Type == DeckLibrary.ListType;
            if (!Dialogs.Confirm(this, Strings.Get(isList ? "Search_ListDeleteTitle" : "Deck_Delete"),
                                 Strings.Format(isList ? "Fmt_ListDeleteConfirm" : "Fmt_DeckDeleteConfirm", deck.Name),
                                 Strings.Get("Btn_Delete"))) return;
            _library.DeleteDeck(deck.Id);
            _selectedDeckId = null;
            RefreshDeckList(null);
        }

        private void ImportBtn_Click(object sender, RoutedEventArgs e)
        {
            DeckMenuPopup.IsOpen = false;
            if (_selectedDeckId == null) return;
            string text = null;
            try { text = Clipboard.GetText(); } catch { }
            if (string.IsNullOrWhiteSpace(text))
            {
                Dialogs.Info(this, Strings.Get("Deck_Import"), Strings.Get("Deck_ImportEmptyClipboard"));
                return;
            }
            var result = Repo.ImportCards(_selectedDeckId, text);
            var message = Strings.Format("Fmt_DeckImportResult", result.Imported, result.NotFound);
            if (result.NotFoundNames.Count > 0)
            {
                var names = result.NotFoundNames.Take(10).ToList();
                message += "\n" + string.Join("\n", names);
                if (result.NotFoundNames.Count > names.Count) message += "\n…";
            }
            Dialogs.Info(this, Strings.Get("Deck_Import"), message);
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            DeckMenuPopup.IsOpen = false;
            if (_selectedDeckId == null) return;
            try
            {
                Clipboard.SetText(Repo.ExportDeck(_selectedDeckId));
                ShowStatus(Strings.Get("Deck_Exported"));
            }
            catch
            {
                // portapapeles ocupado por otra app → reintento manual del usuario
            }
        }

        /// <summary>
        /// PDF de impresión. Solo tiene sentido sobre un mazo: una lista es una selección para
        /// filtrar, no algo que se imprima (ADR-045).
        /// </summary>
        private void PdfBtn_Click(object sender, RoutedEventArgs e)
        {
            DeckMenuPopup.IsOpen = false;
            if (_selectedDeckId == null) return;
            if (!Repo.GetCardsWithCards(_selectedDeckId).Any(x => x.Entry.Quantity > 0))
            {
                ShowStatus(Strings.Get("Deck_PdfNoCards"));
                return;
            }
            new PdfExportWindow(this, Repo, _resolver, _selectedDeckId).ShowDialog();
        }

        private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            DeckMenuPopup.IsOpen = false;
            try
            {
                var dir = IsListKind ? _library.ListsDir : _library.DecksDir;
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch { }
        }

        /// <summary>
        /// Marco de la celda: es el placeholder, así que solo se ve mientras el arte no cargó.
        /// La selección se pone como valor local y por eso gana siempre sobre el Style.
        /// </summary>
        private void ApplyCellFrame(Border cell, Border art, bool selected)
        {
            if (selected)
            {
                cell.BorderBrush = (Brush)FindResource("Gold");
                cell.Background = Brushes.Transparent;
                return;
            }
            var style = new Style(typeof(Border));
            style.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
            var noArt = new DataTrigger
            {
                Binding = new Binding("Background.ImageSource") { Source = art },
                Value = null,
            };
            noArt.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush(Color.FromArgb(0x33, 0xC9, 0xA9, 0x61))));
            noArt.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(0x4D, 0x14, 0x0A, 0x05))));
            style.Triggers.Add(noArt);
            cell.Style = style;
        }

        // ------------------------------------------------------------------ prompt mínimo

        private string Prompt(string title, string label, string initial) =>
            Dialogs.Prompt(this, title, label, initial);

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    internal static class DeckWindowLinq
    {
        /// <summary>Desempate estable por nombre (el sortComparator de la app termina en thenBy name).</summary>
        public static IEnumerable<(DeckCardEntry Entry, Card Card)> ThenByName(
            this IOrderedEnumerable<(DeckCardEntry Entry, Card Card)> source) =>
            source.ThenBy(x => x.Card.Name, StringComparer.Ordinal);
    }
}
