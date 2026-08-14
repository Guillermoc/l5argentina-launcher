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
        // Filtros del Buscador aplicados a las cartas del mazo (ADR-058): el motor y los datos de
        // usuario son los mismos; lo único distinto es sobre qué se aplica el resultado.
        private readonly CardSearchEngine _engine;
        private readonly UserCardDataStore _store;
        // Tipear en el panel (texto de carta, topes de stats) es una ráfaga de teclas y cada una
        // rearmaría el cuerpo entero: se espera a que pare, como en el Buscador.
        private readonly DispatcherTimer _filterDebounce;
        // VMs de imagen por carta (GridCell del Buscador: decode lazy + weak ref); el caché
        // evita re-decodificar en cada rebuild del cuerpo (cada +/− lo dispara)
        private readonly Dictionary<string, GridCell> _imageCells =
            new Dictionary<string, GridCell>(StringComparer.Ordinal);
        private string _selectedDeckId;
        private string _selectedCardId;
        // qué solapa está abierta a la izquierda: mazos (.txt de Decks) o listas (.txt de Lists)
        private string _kind = DeckLibrary.DeckType;
        // Etiquetas marcadas en el panel de filtros. Son del mazo abierto, así que se limpian al
        // cambiar de mazo (_tagFilterDeckId es quien lo detecta).
        private readonly HashSet<string> _tagFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _tagFilterDeckId;
        // Clan del stronghold del mazo abierto: toda la regla de ajuste de oro es relativa a él
        // (ADR-059). Se cachea por rebuild — resolverlo recorre las cartas del mazo y el cuerpo lo
        // necesita una vez por carta.
        private string _deckClan;

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
            _engine = new CardSearchEngine(catalog);
            // después del library, como en el Buscador: la migración v1 de usercards.json necesita
            // el catálogo para traducir ids a nombres
            _store = UserCardDataStore.Load(_library);

            // Mismo panel que el Buscador, sobre las cartas del mazo (ADR-058). Arranca plegado:
            // acá comparte lugar con los filtros del mazo y las etiquetas.
            CardFilters.Initialize(catalog);
            CardFilters.CollapseSections();
            CardFilters.ShowOtherSection = false;
            CardFilters.Changed += (s, e) => { _filterDebounce.Stop(); OnCardFiltersChanged(); };
            CardFilters.TextTyped += (s, e) => { _filterDebounce.Stop(); _filterDebounce.Start(); };
            _filterDebounce = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _filterDebounce.Tick += (s, e) => { _filterDebounce.Stop(); OnCardFiltersChanged(); };

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
            RefreshTags(); // antes del cuerpo: puede limpiar el filtro al cambiar de mazo
            RefreshGold(); // ídem: el modo es del mazo, no de la ventana
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

        // ------------------------------------------------------------------ filtros del Buscador

        /// <summary>
        /// Cambió algo del panel compartido: rearma el cuerpo y actualiza el botón de limpiar,
        /// que solo aparece cuando hay algo que limpiar (mismo criterio que el Buscador).
        /// </summary>
        private void OnCardFiltersChanged()
        {
            RefreshClearFiltersBtn();
            RebuildIfReady();
        }

        private void RefreshClearFiltersBtn() =>
            ClearCardFiltersBtn.Visibility = CardFilters.HasAnyFilter
                ? Visibility.Visible : Visibility.Collapsed;

        private void ClearCardFilters_Click(object sender, RoutedEventArgs e)
        {
            CardFilters.Clear();
            OnCardFiltersChanged();
        }

        /// <summary>
        /// Los prefijos de la barra del Buscador también acá: `tx.` filtra el texto de la carta en
        /// vivo y `k.`/`c.`/`t.` + Enter agregan keyword o prenden el chip de clan/tipo del panel.
        /// </summary>
        private void FilterBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { FilterBox.Text = ""; e.Handled = true; return; }
            if (e.Key != Key.Enter) return;
            var mode = CardFilterPanel.ParseBar(FilterBox.Text, out var value);
            if (mode == CardFilterPanel.BarMode.Name || mode == CardFilterPanel.BarMode.Text) return;
            if (CardFilters.ApplyBarMode(mode, value)) FilterBox.Text = "";
            e.Handled = true;
        }

        /// <summary>
        /// Ids de las cartas que pasan los filtros del panel, o **null** si no hay ninguno puesto
        /// (y entonces no se filtra nada). Se corre el mismo motor que el Buscador contra el
        /// catálogo entero y después se intersecta con el mazo: así la semántica es exactamente la
        /// misma y no hay una segunda implementación que se despegue.
        ///
        /// Dos criterios se fuerzan: baneadas y ocultas **siempre entran**. Son filtros para
        /// navegar el catálogo; adentro de un mazo lo único que podrían hacer es esconder cartas
        /// que el mazo tiene de verdad (ADR-058).
        /// </summary>
        private HashSet<string> FilteredCardIds(CardFilterPanel.BarMode barMode, string barValue)
        {
            var textMode = barMode == CardFilterPanel.BarMode.Text && barValue.Trim().Length > 0;
            if (!CardFilters.HasAnyFilter && !textMode) return null;

            var criteria = new CardSearchCriteria();
            CardFilters.Fill(criteria);
            if (textMode) criteria.Text = barValue;
            criteria.IncludeBanned = true;
            criteria.IncludeHidden = true;
            return new HashSet<string>(
                _engine.Search(criteria, _store.Data).Select(c => c.Id), StringComparer.Ordinal);
        }
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
        /// Rearma el centro. Filtra (texto, recortadas, etiquetas marcadas) y después agrupa por
        /// el eje elegido: **tipo** —Starter/Dynasty/Fate → tipo, el canónico de la app— o
        /// **etiqueta** (ADR-057). Las filas llevan los controles −/qty/+/✕ y los avisos
        /// "Falta: {slot}" son blandos, como la app.
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

            _deckClan = Repo.DeckClan(deck.Id);

            // La caja acepta los prefijos del Buscador: en modo operador (tx./k./c./t.) no filtra
            // por nombre, o "k.bushi" no matchearía ningún nombre y el mazo se vería vacío.
            var barMode = CardFilterPanel.ParseBar(FilterBox.Text, out var barValue);
            var filter = barMode == CardFilterPanel.BarMode.Name ? barValue.Trim() : "";
            var allowed = FilteredCardIds(barMode, barValue);
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
                .Where(x => allowed == null || allowed.Contains(x.Card.Id))
                .Where(x => DeckTags.Matches(deck, x.Card.Id, _tagFilter, TagAllBtn.IsChecked == true))
                .ToList();

            if (GroupByTagBtn.IsChecked == true) BuildTagSections(deck, entries, sort, asc, gridView);
            else BuildTypeSections(deck, entries, sort, asc, gridView);

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

        /// <summary>Agrupado canónico de la app: Starter/Dynasty/Fate → tipo.</summary>
        private void BuildTypeSections(Models.Deck deck, List<(DeckCardEntry Entry, Card Card)> entries,
                                       DeckCardSort sort, bool asc, bool gridView)
        {
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
                    AddCards(deck, SortEntries(deck, typeGroup, sort, asc), gridView);
                }

                // Aviso blando de slots de holding inicial sin cubrir (bajo Starter)
                foreach (var slot in uncovered)
                    DeckBody.Children.Add(BodyText(
                        Strings.Format("Fmt_DeckMissingSlots", slot.Label ?? slot.SlotId), error: true));
            }
        }

        /// <summary>
        /// Agrupado por etiqueta (ADR-057): una sección por etiqueta, en el orden en que se
        /// crearon, y las sin clasificar al final. **Una carta está en todas sus pilas**: la que
        /// quita honor y pide magistrados aparece en las dos, porque la pregunta al armar es
        /// "cuánto deshonor tengo" y una pila que se saltea las compartidas contesta de menos. Por
        /// eso los conteos no suman el mazo, y la cabecera dice cuántas están además en otra pila.
        /// Los chips Starter/Dynasty/Fate siguen filtrando: son un filtro, no el eje.
        /// </summary>
        private void BuildTagSections(Models.Deck deck, List<(DeckCardEntry Entry, Card Card)> entries,
                                      DeckCardSort sort, bool asc, bool gridView)
        {
            var visible = SortEntries(deck, entries.Where(x => GroupVisible(x.Card.DeckGroup)), sort, asc);
            foreach (var section in DeckTags.Sections(deck, visible, x => x.Card.Id))
            {
                var title = section.Tag ?? Strings.Get("Deck_TagUntagged");
                var header = $"{title} — ({section.Items.Sum(x => x.Entry.Quantity)})" +
                             $" [{section.Items.Count(x => x.Entry.Quantity > 0)}]";
                if (section.Shared > 0) header += Strings.Format("Fmt_TagShared", section.Shared);
                DeckBody.Children.Add(GroupHeader(header));
                AddCards(deck, section.Items, gridView);
            }

            // El aviso de slots sin cubrir es del mazo entero, no de una pila: va al pie.
            if (deck.Type != DeckLibrary.ListType)
                foreach (var slot in Repo.UncoveredRequiredSlots(deck.Id))
                    DeckBody.Children.Add(BodyText(
                        Strings.Format("Fmt_DeckMissingSlots", slot.Label ?? slot.SlotId), error: true));
        }

        /// <summary>Filas o celdas, según la vista. Las cartas ya vienen ordenadas.</summary>
        private void AddCards(Models.Deck deck, IEnumerable<(DeckCardEntry Entry, Card Card)> cards,
                              bool gridView)
        {
            if (gridView)
            {
                // cartas en imagen: ~3 columnas con el layout por defecto (más si se colapsan
                // paneles), manteniendo la sección
                var wrap = new WrapPanel { Margin = new Thickness(4, 2, 0, 4) };
                foreach (var x in cards)
                    wrap.Children.Add(CardImageCell(deck, x.Entry, x.Card));
                DeckBody.Children.Add(wrap);
            }
            else
                foreach (var x in cards)
                    DeckBody.Children.Add(CardRow(deck, x.Entry, x.Card));
        }

        /// <summary>
        /// Orden dentro de una sección: solo el campo se invierte (port de sortComparator), y el
        /// desempate siempre es por nombre. Por coste ordena por el que se PAGA cuando el ajuste
        /// de oro está prendido (ADR-059); apagado, `Effective` da 0 y la clave es la de siempre.
        /// </summary>
        private IEnumerable<(DeckCardEntry Entry, Card Card)> SortEntries(
            Models.Deck deck, IEnumerable<(DeckCardEntry Entry, Card Card)> source,
            DeckCardSort sort, bool asc)
        {
            switch (sort)
            {
                case DeckCardSort.NAME:
                    return Order(source, x => x.Card.Name, asc, StringComparer.Ordinal).ThenByName();
                case DeckCardSort.FORCE:
                    return Order(source, x => x.Card.Force ?? int.MinValue, asc, Comparer<int>.Default).ThenByName();
                default: // GOLD_COST
                    return Order(source, x => DeckGold.EffectiveCost(deck, x.Card, _deckClan) ?? int.MaxValue,
                                 asc, Comparer<int>.Default).ThenByName();
            }
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
            // Click derecho = menú de la carta (etiquetas). Selecciona primero, para que el panel
            // de la derecha muestre de qué carta se está hablando.
            row.MouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                SelectCard(card);
                ShowCardMenu(deck, card, null);
            };
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
            var mini = MiniStats(card, DeckGold.Effective(deck, card, _deckClan));
            if (mini.Length > 0)
                name.Inlines.Add(new System.Windows.Documents.Run("   " + mini)
                { Foreground = (Brush)FindResource("TextDim"), FontSize = 11 });

            // Puntitos de las etiquetas, antes del nombre: en la vista por tipo son la única
            // señal de que esta carta está en dos pilas.
            var marks = TagMarks(deck, card.Id, 8);
            if (marks != null)
            {
                DockPanel.SetDock(marks, Dock.Left);
                dock.Children.Add(marks);
            }
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
            // click derecho = menú de la carta: sus etiquetas y, al pie, copiar el arte (que era
            // lo que hacía el click derecho antes de que existieran las etiquetas)
            cell.MouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                SelectCard(card);
                ShowCardMenu(deck, card, vm);
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

            // Las etiquetas, sobre el arte y con fondo propio: sobre una carta clara los puntos
            // solos no se leen. Encima del arte porque se agrega después.
            var marks = TagMarks(deck, card.Id, IsTinyZoom ? 7 : 9);
            if (marks != null)
                imgHost.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x14, 0x0A, 0x05)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 3, 0, 3), // el último punto ya trae su margen
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(5),
                    Child = marks,
                });

            // Ajuste de oro (ADR-059): pastilla discreta contra el borde derecho y a media altura.
            // Va sobre el arte y no en la barra de acciones porque es un dato de la carta, no una
            // acción — y ahí abajo competiría con la cantidad, que es lo que se toca.
            var gold = DeckGold.Effective(deck, card, _deckClan);
            if (gold != 0)
            {
                var effective = DeckGold.EffectiveCost(deck, card, _deckClan);
                imgHost.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x14, 0x0A, 0x05)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(5, 1, 5, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = effective == null ? null
                        : Strings.Format("Fmt_GoldCellTip", card.DisplayCost, effective.Value),
                    Child = new TextBlock
                    {
                        Text = DeckGold.Format(gold),
                        FontFamily = (FontFamily)FindResource("BodyFont"),
                        FontSize = IsTinyZoom ? 10 : 12,
                        Foreground = (Brush)FindResource("GoldSoft"),
                    },
                });
            }
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

        // ------------------------------------------------------------------ etiquetas (ADR-057)

        private Models.Deck CurrentDeck =>
            _selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null;

        /// <summary>
        /// Rearma los dos lados de las etiquetas: los chips que filtran (panel izquierdo) y los que
        /// asignan a la carta seleccionada (panel derecho). Las etiquetas son **del mazo**, así que
        /// al cambiar de mazo el filtro se limpia en vez de arrastrar nombres que ahí no existen.
        /// </summary>
        private void RefreshTags()
        {
            var deck = CurrentDeck;
            if (deck?.Id != _tagFilterDeckId)
            {
                _tagFilter.Clear();
                _tagFilterDeckId = deck?.Id;
            }
            else _tagFilter.RemoveWhere(t => DeckTags.Find(deck, t) == null); // borradas mientras tanto

            NewTagBtn.IsEnabled = deck != null;
            GroupByTagBtn.IsEnabled = deck != null && deck.Tags.Count > 0;
            // Sin etiquetas no hay eje por el cual agrupar: el segmentado vuelve a Tipo solo.
            if (GroupByTagBtn.IsEnabled != true && GroupByTagBtn.IsChecked == true)
            {
                GroupByTagBtn.IsChecked = false;
                GroupByTypeBtn.IsChecked = true;
            }

            TagChips.Children.Clear();
            if (deck != null)
                foreach (var tag in deck.Tags)
                {
                    var chip = TagChip(tag, $"{tag.Name}  {DeckTags.CardCount(deck, tag.Name)}",
                                       _tagFilter.Contains(tag.Name));
                    chip.ToolTip = Strings.Get("Deck_TagChipTip");
                    chip.Click += (s, e) =>
                    {
                        if (chip.IsChecked == true) _tagFilter.Add(tag.Name);
                        else _tagFilter.Remove(tag.Name);
                        RefreshTagMatchRow();
                        RefreshTagsHeader(deck);
                        RebuildIfReady();
                    };
                    // El menú de la etiqueta (renombrar / eliminar) cuelga de su propio chip: es
                    // donde está el objeto, y así no hace falta un ⋯ por chip.
                    chip.MouseRightButtonUp += (s, e) => { e.Handled = true; ShowTagMenu(deck, tag); };
                    TagChips.Children.Add(chip);
                }

            RefreshTagMatchRow();
            TagsHint.Text = Strings.Get(deck == null || deck.Tags.Count == 0
                ? "Deck_TagsHint" : "Deck_TagsHintUse");
            RefreshTagsHeader(deck);
            RefreshTagAssign();
        }

        /// <summary>
        /// La cabecera de la sección dice cuántas etiquetas hay y, si se está filtrando por
        /// alguna, lo avisa: la sección arranca **plegada**, así que sin esto se puede quedar
        /// filtrando con la única señal de que "faltan cartas".
        /// </summary>
        private void RefreshTagsHeader(Models.Deck deck)
        {
            var total = deck?.Tags.Count ?? 0;
            TagsSection.Header =
                _tagFilter.Count > 0 ? Strings.Format("Fmt_TagsHeaderFiltering", _tagFilter.Count)
                : total > 0 ? Strings.Format("Fmt_TagsHeaderCount", total)
                : Strings.Get("Deck_TagsHeader");
        }

        /// <summary>El modo unión/intersección solo significa algo con dos o más marcadas.</summary>
        private void RefreshTagMatchRow() =>
            TagMatchRow.Visibility = _tagFilter.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Chips de la carta seleccionada: tildar uno la pone o la saca de esa pila.</summary>
        private void RefreshTagAssign()
        {
            var deck = CurrentDeck;
            var card = _selectedCardId != null ? Repo.GetCardById(_selectedCardId) : null;
            var inDeck = deck != null && card != null &&
                         deck.Cards.Any(e => string.Equals(e.CardId, card.Id, StringComparison.Ordinal));
            TagAssignPane.Visibility = inDeck ? Visibility.Visible : Visibility.Collapsed;
            TagAssignChips.Children.Clear();
            if (!inDeck) return;

            foreach (var tag in deck.Tags)
            {
                var chip = TagChip(tag, tag.Name, DeckTags.Has(deck, card.Id, tag.Name));
                chip.Click += (s, e) =>
                {
                    DeckTags.Set(deck, card.Id, tag.Name, chip.IsChecked == true);
                    AfterTagsChanged();
                };
                // el mismo menú que en el panel de filtros: el chip de una etiqueta es la etiqueta,
                // esté donde esté
                chip.MouseRightButtonUp += (s, e) => { e.Handled = true; ShowTagMenu(deck, tag); };
                TagAssignChips.Children.Add(chip);
            }
            var add = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = Strings.Get("Deck_NewTagChip"),
                Height = 26,
                FontSize = 12,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = Strings.Get("Deck_NewTagTip"),
            };
            add.Click += (s, e) => CreateTagFor(card.Id);
            TagAssignChips.Children.Add(add);
        }

        /// <summary>Chip con el punto del color de la etiqueta (el mismo que llevan las cartas).</summary>
        private ToggleButton TagChip(DeckTag tag, string text, bool on)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(TagDot(tag, 9));
            content.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var chip = new ToggleButton
            {
                Style = (Style)FindResource("FilterChip"),
                Content = content,
                IsChecked = on,
                MaxWidth = 224,
            };
            // El contenido es un panel (punto + texto), así que sin esto el chip no tiene nombre
            // accesible y un lector de pantalla no dice de qué etiqueta se trata.
            System.Windows.Automation.AutomationProperties.SetName(chip, text);
            return chip;
        }

        private Ellipse TagDot(DeckTag tag, double size) => new Ellipse
        {
            Width = size,
            Height = size,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = TagBrush(tag),
        };

        private static Brush TagBrush(DeckTag tag) => ColorBrush(tag?.Color);

        private static Brush ColorBrush(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex ?? "#7F8C8D")); }
            catch { return Brushes.Gray; }
        }

        /// <summary>
        /// Puntitos de las etiquetas de una carta. Van también en la vista por tipo: es lo que hace
        /// visible el solapamiento sin tener que cambiar de agrupación.
        /// </summary>
        private UIElement TagMarks(Models.Deck deck, string cardId, double size)
        {
            var tags = DeckTags.Of(deck, cardId);
            if (tags.Count == 0) return null;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var name in tags)
                panel.Children.Add(TagDot(DeckTags.Find(deck, name), size));
            panel.ToolTip = string.Join(" · ", tags);
            return panel;
        }

        private void GroupBy_Click(object sender, RoutedEventArgs e)
        {
            var byTag = ReferenceEquals(sender, GroupByTagBtn);
            GroupByTagBtn.IsChecked = byTag;
            GroupByTypeBtn.IsChecked = !byTag;
            RebuildIfReady();
        }

        private void TagMatch_Click(object sender, RoutedEventArgs e)
        {
            var all = ReferenceEquals(sender, TagAllBtn);
            TagAllBtn.IsChecked = all;
            TagAnyBtn.IsChecked = !all;
            RebuildIfReady();
        }

        private void NewTag_Click(object sender, RoutedEventArgs e) => CreateTagFor(null);

        /// <summary>
        /// Crea una etiqueta y —si vino de una carta— se la pone de una: crear la pila y no meter
        /// la carta que la motivó sería dejar el gesto por la mitad.
        /// </summary>
        private void CreateTagFor(string cardId)
        {
            var deck = CurrentDeck;
            if (deck == null) return;
            var name = Dialogs.Prompt(this, Strings.Get("Deck_NewTagTitle"),
                                      Strings.Get("Deck_NewTagLabel"), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            var existed = DeckTags.Find(deck, name) != null;
            var tag = DeckTags.Add(deck, name);
            if (tag == null) return;
            if (existed) ShowStatus(Strings.Format("Fmt_TagExists", tag.Name));
            if (cardId != null) DeckTags.Set(deck, cardId, tag.Name, true);
            AfterTagsChanged();
        }

        /// <summary>
        /// Las etiquetas viven en el sidecar: se guardan sin reescribir el .txt del juego, y no
        /// pasan por DeckChanged (el mazo que se juega no cambió).
        /// </summary>
        private void AfterTagsChanged()
        {
            _library.SaveMeta();
            RefreshTags();
            RebuildIfReady();
        }

        /// <summary>
        /// Menú de la etiqueta: elegir su color, renombrarla (arrastra las asignaciones) o
        /// eliminarla. El color va arriba y como muestras, no como una fila más: es lo único del
        /// menú que se elige mirando, no leyendo.
        /// </summary>
        private void ShowTagMenu(Models.Deck deck, DeckTag tag)
        {
            TagMenuItems.Children.Clear();
            TagMenuItems.Children.Add(new TextBlock
            {
                Text = Strings.Get("Deck_TagColor"),
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextDim"),
                Margin = new Thickness(14, 3, 14, 5),
            });
            TagMenuItems.Children.Add(ColorSwatches(deck, tag));
            TagMenuItems.Children.Add(new Rectangle
            {
                Height = 1,
                Margin = new Thickness(10, 2, 10, 5),
                Fill = (Brush)FindResource("OutlineSoft"),
            });
            TagMenuItems.Children.Add(MenuRow(Strings.Get("Deck_TagRename"), () =>
            {
                var name = Dialogs.Prompt(this, Strings.Get("Deck_TagRename"),
                                          Strings.Get("Deck_NewTagLabel"), tag.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                var previous = tag.Name;
                if (!DeckTags.Rename(deck, previous, name))
                {
                    ShowStatus(Strings.Format("Fmt_TagExists", name.Trim()));
                    return;
                }
                if (_tagFilter.Remove(previous)) _tagFilter.Add(tag.Name);
                AfterTagsChanged();
            }));
            TagMenuItems.Children.Add(MenuRow(Strings.Get("Deck_TagDelete"), () =>
            {
                var n = DeckTags.CardCount(deck, tag.Name);
                if (n > 0 && !Dialogs.Confirm(this, Strings.Get("Deck_TagDelete"),
                        Strings.Format("Fmt_TagDeleteConfirm", tag.Name, n),
                        Strings.Get("Btn_Delete"))) return;
                DeckTags.Delete(deck, tag.Name);
                _tagFilter.Remove(tag.Name);
                AfterTagsChanged();
            }, error: true));
            TagMenuPopup.IsOpen = true;
        }

        /// <summary>
        /// Menú de una carta del mazo: **copiar el arte arriba de todo y siempre**, después el
        /// ajuste de oro si el modo está prendido, y al pie sus etiquetas.
        ///
        /// Copiar va fijo en el primer renglón porque es lo único del menú que no depende del
        /// mazo: las etiquetas cambian de mazo en mazo y el bloque de oro puede no estar, así que
        /// si copiar quedara al final se movería de lugar y habría que buscarlo con la vista.
        /// También está en la vista Lista, que no tiene celda: las rutas candidatas se resuelven
        /// igual que para la previsualización con Alt.
        /// </summary>
        private void ShowCardMenu(Models.Deck deck, Card card, GridCell cell)
        {
            CardMenuItems.Children.Clear();
            var candidates = cell?.CandidatePaths ??
                             _resolver.ResolveCandidates(card, null, fallbackChain: true);
            CardMenuItems.Children.Add(MenuRow(Strings.Get("Detail_CopyImage"), () =>
            {
                var ok = CardImageClipboard.TryCopy(candidates);
                ShowStatus(ok ? Strings.Format("Fmt_CopiedImage", card.Name)
                              : Strings.Get("Detail_CopyImageFail"));
            }));
            CardMenuItems.Children.Add(MenuSeparator());

            if (deck.GoldAdjustOn)
            {
                CardMenuItems.Children.Add(GoldMenuBlock(deck, card));
                CardMenuItems.Children.Add(MenuSeparator());
            }

            foreach (var tag in deck.Tags)
            {
                var on = DeckTags.Has(deck, card.Id, tag.Name);
                CardMenuItems.Children.Add(MenuRow((on ? "✓  " : "     ") + tag.Name, () =>
                {
                    DeckTags.Toggle(deck, card.Id, tag.Name);
                    AfterTagsChanged();
                }, dot: tag));
            }
            CardMenuItems.Children.Add(MenuRow(Strings.Get("Deck_NewTagChip"), () => CreateTagFor(card.Id)));
            CardMenuPopup.IsOpen = true;
        }

        private Rectangle MenuSeparator() => new Rectangle
        {
            Height = 1,
            Margin = new Thickness(10, 5, 10, 5),
            Fill = (Brush)FindResource("OutlineSoft"),
        };

        /// <summary>
        /// Las diez muestras de la paleta, con la actual marcada. Elegir una la aplica y cierra el
        /// menú: es una acción, no un formulario que haya que confirmar.
        /// </summary>
        private UIElement ColorSwatches(Models.Deck deck, DeckTag tag)
        {
            // 135 = cinco muestras por fila: diez en dos filas parejas en vez de 7+3
            var wrap = new WrapPanel { Margin = new Thickness(14, 0, 8, 3), MaxWidth = 135 };
            foreach (var color in DeckTags.Palette)
            {
                var current = string.Equals(color, tag.Color, StringComparison.OrdinalIgnoreCase);
                var swatch = new Button
                {
                    Style = (Style)FindResource("ColorSwatch"),
                    Background = ColorBrush(color),
                    BorderThickness = new Thickness(current ? 3 : 1),
                    BorderBrush = (Brush)FindResource(current ? "Gold" : "OutlineSoft"),
                };
                System.Windows.Automation.AutomationProperties.SetName(swatch, color);
                swatch.Click += (s, e) =>
                {
                    TagMenuPopup.IsOpen = false;
                    if (DeckTags.SetColor(deck, tag.Name, color)) AfterTagsChanged();
                };
                wrap.Children.Add(swatch);
            }
            return wrap;
        }

        /// <summary>Fila de menú de los popups armados en código (mismo estilo que el menú ⋯).</summary>
        private Button MenuRow(string text, Action action, bool error = false, DeckTag dot = null)
        {
            var row = new Button { Style = (Style)FindResource("MenuRow") };
            if (dot == null) row.Content = text;
            else
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(TagDot(dot, 9));
                content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
                row.Content = content;
                System.Windows.Automation.AutomationProperties.SetName(row, text);
            }
            if (error) row.Foreground = (Brush)FindResource("ErrorText");
            row.Click += (s, e) =>
            {
                CardMenuPopup.IsOpen = false;
                TagMenuPopup.IsOpen = false;
                action();
            };
            return row;
        }

        // ------------------------------------------------------------------ ajuste de oro (ADR-059)

        /// <summary>
        /// El chip prende el modo en ESTE mazo (queda guardado en el sidecar). Escucha
        /// `Checked`/`Unchecked` y no `Click` para que también valga cuando el estado lo cambia
        /// otra cosa —un lector de pantalla, automatización— y no solo el mouse; los refrescos
        /// programáticos se filtran con `_syncing`, como el resto de la ventana.
        /// </summary>
        private void GoldAdjust_Click(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            var deck = CurrentDeck;
            if (deck == null) return;
            deck.GoldAdjustOn = GoldAdjustBtn.IsChecked == true;
            AfterGoldChanged();
        }

        /// <summary>
        /// Borra los ajustes puestos a mano: el mazo entero vuelve a lo que dice la regla. Se
        /// confirma porque es trabajo del usuario que no se puede deshacer.
        /// </summary>
        private void GoldReset_Click(object sender, RoutedEventArgs e)
        {
            var deck = CurrentDeck;
            var manual = DeckGold.OverrideCount(deck);
            if (manual == 0) return;
            if (!Dialogs.Confirm(this, Strings.Get("Deck_GoldResetTitle"),
                    Strings.Format("Fmt_GoldResetConfirm", manual),
                    Strings.Get("Btn_YesContinue"))) return;
            DeckGold.ClearOverrides(deck);
            AfterGoldChanged();
        }

        /// <summary>
        /// El ajuste de oro vive en el sidecar, como las etiquetas: se guarda sin reescribir el
        /// .txt del juego (el juego no sabe de esto). Rearma también las stats, que con el modo
        /// prendido miden coste ajustado.
        /// </summary>
        private void AfterGoldChanged()
        {
            _library.SaveMeta();
            RefreshGold();
            RebuildIfReady();
            RebuildStats(CurrentDeck);
        }

        /// <summary>
        /// Estado del bloque. El modo es del mazo, así que al cambiar de mazo el chip sigue lo que
        /// ese mazo tenía guardado. La línea de abajo dice qué regla está corriendo —el número
        /// sobre las cartas no explica de dónde sale— y el botón de volver a la regla aparece solo
        /// si hay algo puesto a mano que borrar.
        /// </summary>
        private void RefreshGold()
        {
            var deck = CurrentDeck;
            GoldAdjustBtn.IsEnabled = deck != null;
            var wasSyncing = _syncing;
            _syncing = true; // el chip lo estamos poniendo nosotros: no es el usuario prendiéndolo
            GoldAdjustBtn.IsChecked = deck != null && deck.GoldAdjustOn;
            _syncing = wasSyncing;

            var on = deck != null && deck.GoldAdjustOn;
            GoldAdjustRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;

            var clan = Repo.DeckClan(deck.Id);
            GoldAdjustHint.Text = clan == null
                ? Strings.Get("Deck_GoldRuleNoStronghold")
                : Strings.Format("Fmt_GoldRule", ClanLabel(clan));
            var manual = DeckGold.OverrideCount(deck);
            GoldResetBtn.Visibility = manual > 0 ? Visibility.Visible : Visibility.Collapsed;
            GoldResetBtn.Content = Strings.Format("Fmt_GoldReset", manual);
        }

        /// <summary>
        /// Bloque de ajuste de oro del menú de la carta: la tira −3…+3 con el valor vigente
        /// marcado y, si lo puso el usuario, la vuelta a la regla. El encabezado dice si el número
        /// es automático o a mano, que es lo único que la tira no muestra: elegir el mismo valor
        /// que la regla es un estado distinto de dejarla decidir, y solo el segundo se mueve si
        /// cambia el stronghold.
        /// </summary>
        private UIElement GoldMenuBlock(Models.Deck deck, Card card)
        {
            var manual = DeckGold.OverrideOf(deck, card.Id);
            var current = DeckGold.Effective(deck, card, _deckClan);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = Strings.Get(manual != null ? "Deck_GoldMenuManual" : "Deck_GoldMenuAuto"),
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextDim"),
                Margin = new Thickness(14, 3, 14, 5),
            });

            var strip = new WrapPanel { Margin = new Thickness(14, 0, 8, 0), MaxWidth = 224 };
            for (var value = -DeckGold.MaxAdjust; value <= DeckGold.MaxAdjust; value++)
            {
                var adjust = value;
                var btn = new ToggleButton
                {
                    Style = (Style)FindResource("FilterChip"),
                    Content = DeckGold.Format(adjust),
                    IsChecked = adjust == current,
                    Width = 26,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 4, 4),
                };
                btn.Click += (s, e) =>
                {
                    CardMenuPopup.IsOpen = false;
                    DeckGold.Set(deck, card.Id, adjust);
                    AfterGoldChanged();
                };
                strip.Children.Add(btn);
            }
            panel.Children.Add(strip);

            if (manual != null)
                panel.Children.Add(MenuRow(
                    Strings.Format("Fmt_GoldAuto", DeckGold.Format(DeckGold.RuleFor(card, _deckClan))),
                    () => { DeckGold.Set(deck, card.Id, null); AfterGoldChanged(); }));
            return panel;
        }

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

        /// <summary>
        /// Mini-stats de la fila. Con ajuste de oro el coste impreso NO se reemplaza: se le
        /// agrega el ajuste al lado (©8 +2). El impreso es lo que dice la carta y lo que se busca
        /// cuando se la tiene en la mano; el ajuste es una decisión del mazo.
        /// </summary>
        private static string MiniStats(Card card, int gold)
        {
            var parts = new List<string>();
            if (card.DisplayCost.Length > 0)
                parts.Add("©" + card.DisplayCost + (gold != 0 ? " " + DeckGold.Format(gold) : ""));
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
            RefreshTagAssign(); // los chips de abajo son de ESTA carta
            RebuildDeckBody(CurrentDeck);
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

            // Curva de coste por tipo (personalities/holdings/followers/items/spells, como la app).
            // Con el ajuste de oro prendido la curva mide lo que se PAGA (ADR-059): medirla sobre
            // el impreso mientras la lista se ordena por el ajustado sería contarse dos historias.
            var deckClan = Repo.DeckClan(deck.Id);
            Func<Card, int?> costOf = card => deck.GoldAdjustOn
                ? DeckGold.EffectiveCost(deck, card, deckClan)
                : card.Cost;
            foreach (var type in new[] { "personality", "holding", "follower", "item", "spell" })
            {
                var ofType = entries.Where(x => x.Card.Type == type).ToList();
                if (ofType.Count == 0) continue;
                StatsBody.Children.Add(GroupHeader(
                    Strings.Get("Deck_Stats_CostCurve") + " — " + Capitalize(type) +
                    (deck.GoldAdjustOn ? Strings.Get("Deck_Stats_Adjusted") : "")));
                var dist = ofType.GroupBy(x => costOf(x.Card))
                    .Select(g => new { Cost = g.Key, Count = g.Sum(x => x.Entry.Quantity) })
                    .OrderBy(g => g.Cost ?? int.MaxValue) // null (coste variable ★) al final
                    .ToList();
                var maxCount = dist.Max(d => d.Count);
                foreach (var d in dist)
                    StatsBody.Children.Add(BarRow(d.Cost?.ToString() ?? "★", d.Count, maxCount));

                if (type == "personality")
                {
                    var median = MedianCost(ofType, costOf);
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
        private static int? MedianCost(List<(DeckCardEntry Entry, Card Card)> ofType, Func<Card, int?> costOf)
        {
            var costs = new List<int>();
            foreach (var x in ofType)
            {
                var cost = costOf(x.Card);
                if (cost != null)
                    for (int i = 0; i < x.Entry.Quantity; i++) costs.Add(cost.Value);
            }
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
