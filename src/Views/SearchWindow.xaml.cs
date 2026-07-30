using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Resources;
using L5ArgentinaLauncher.Services.Deck;
using L5ArgentinaLauncher.Services.Search;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Buscador de cartas. Patrón desktop estándar: panel de filtros persistente (aplican en
    /// vivo, sin modal ni botón Aplicar), resultados en grilla de miniaturas (default) o tabla
    /// con cabeceras ordenables, y panel derecho con el mazo/lista activa + detalle de carta.
    /// Ambos paneles laterales son colapsables (cada colapso libera ~1 columna de cartas).
    /// La semántica de los filtros es la del motor portado de la app Android (CardSearchEngine);
    /// los +/− operan sobre el mazo activo vía DeckRepository (mismas validaciones que la app,
    /// pero acá el resultado SÍ se muestra — en la app se ignora en silencio).
    /// </summary>
    public partial class SearchWindow : Window
    {
        private static readonly string[] TypeValues =
        {
            // CardType.kt sin "wind" (la UI de la app también lo omite; no hay cartas wind)
            "personality", "holding", "event", "strategy", "item", "spell", "follower",
            "ring", "ancestor", "stronghold", "sensei", "region", "celestial",
        };

        private static readonly string[] ClanValues =
            { "crab", "crane", "dragon", "lion", "mantis", "phoenix", "scorpion", "spider", "unicorn", "noclan" };

        private static readonly string[] RarityValues = { "P", "R", "U", "C", "F" };

        private static readonly string[] DesignatorValues =
            { "Open", "Limited", "Engage", "Battle", "Dynasty", "Interrupt", "Reaction" };

        // Agrupamiento visual en arcos (FilterModal de la app): cada uno de estos códigos abre
        // un arco nuevo en el orden canónico; lo anterior al primero es el arco Lotus. No hay
        // clasificación explícita set→arco: un set nuevo cae solo en el tramo que le toca.
        private static readonly string[] ArcSeparatorCodes =
            { "Samurai", "Celestial", "Emperor", "Ivory", "TwentyFestivals", "SExMRP", "P" };

        private static readonly string[] ArcLabelKeys =
        {
            "Arc_Lotus", "Arc_Samurai", "Arc_Celestial", "Arc_Emperor", "Arc_Ivory",
            "Arc_TwentyFestivals", "Arc_SamuraiExtended", "Arc_Promo",
        };

        /// <summary>
        /// Cuatro tamaños de celda para la grilla. 226×322 es el histórico y sigue siendo el
        /// default; el grande sirve para mirar el arte y los dos chicos para barrer muchas cartas
        /// de un vistazo. La proporción es la de una carta escaneada (≈1.4248), así que solo se
        /// elige el ancho.
        ///
        /// El más chico (124) **no entra la barra de acciones de la celda**, que mide ~160 con sus
        /// cinco botones: en ese nivel se esconde (ver <see cref="IsTinyZoom"/>). No se pierde
        /// nada — Ctrl+click, click derecho, Alt y el panel de detalle siguen haciendo todo.
        /// </summary>
        private static readonly double[] ZoomWidths = { 124, 168, 226, 302 };
        private const double CardAspect = 322.0 / 226.0;
        private const double CellMargin = 14; // margen 5+5 + borde 2+2

        private int _zoom = 2; // 226, el histórico

        private double CellWidthFor(int zoom) => ZoomWidths[zoom];
        private double GridCellOuterWidth => CellWidthFor(_zoom) + CellMargin;

        private readonly CardSearchEngine _engine;
        private readonly CardImageResolver _resolver;
        // se carga en el ctor: resuelve nombres de carta contra el catálogo del library (ADR-044)
        private readonly UserCardDataStore _store;
        private readonly FilterPresetStore _presets = FilterPresetStore.Load();
        private readonly DeckLibrary _library;

        // Referencias originales del ctor, para abrir más ventanas con Ctrl+N (los "tabs" de la app)
        private readonly CardCatalog _catalogRef;
        private readonly string _installPathRef;
        private readonly List<string> _keywords = new List<string>();
        private readonly List<string> _negKeywords = new List<string>();
        private List<string> _allKwCodes = new List<string>();
        private bool _initializing = true;
        private bool _kwUpdating;
        private bool _listComboSyncing;
        // el desplegable de conjuntos tiene dos niveles: listas (default) y mazos ("Usar mazo…")
        private bool _showingDecks;
        private SetChoice _activeSet;

        private List<Card> _results = new List<Card>();
        private CardSearchCriteria _lastCriteria = new CardSearchCriteria();
        private List<GridCell> _gridCells = new List<GridCell>();
        private GridCell _selectedCell;
        private int _gridCols;
        private bool _gridDirty = true;
        private Card _detailCard;
        private Dictionary<GridViewColumn, SortField> _headerSort;
        private readonly DispatcherTimer _feedbackTimer;

        // Debounce de los filtros de texto libre (nombre / texto de carta / stats): cada
        // pulsación reinicia el timer y recién al parar de tipear se corre la búsqueda. Sin
        // esto, cada tecla dispara un Search + BuildGrid sobre los ~6300 registros en el hilo
        // de UI (freeze con ruedita al tipear o al borrar de golpe un filtro largo).
        private readonly DispatcherTimer _searchDebounce;

        private TextBox _forceMin, _forceMax, _chiMin, _chiMax, _costMin, _costMax,
                        _honorMin, _honorMax, _pHonorMin, _pHonorMax, _focusMin, _focusMax;
        private CheckBox _forceSpecial, _costSpecial, _honorNeg, _pHonorNeg;

        /// <summary>
        /// Una opción de orden **incluye su dirección**: antes eran dos controles (el combo y un
        /// `↑`/`↓` aparte) para un solo concepto, y había que mirar los dos para saber cómo estaba
        /// ordenado. Cada campo aparece dos veces, ascendente y descendente.
        /// </summary>
        private class SortOption
        {
            public string Label;
            public SortField Field;
            public bool Ascending;
            public override string ToString() => Label + (Ascending ? "  ↑" : "  ↓");
        }

        /// <summary>
        /// Qué es el conjunto activo del selector de la barra. Las dos últimas no son conjuntos:
        /// son las entradas que cambian de nivel el desplegable (listas ⇄ mazos).
        /// </summary>
        private enum SetKind { None, List, Deck, Hidden, ShowDecks, BackToLists, NewList, DeleteList }

        /// <summary>Ítem del selector de conjunto activo de la barra.</summary>
        private class SetChoice
        {
            public SetKind Kind;
            public string Id;    // id de sesión del mazo/lista (null en los conjuntos virtuales)
            public string Name;  // nombre persistible (el que va al preset)
            public string Label;
            public override string ToString() => Label;

            /// <summary>
            /// Las acciones del desplegable no seleccionan un conjunto: navegan o abren un diálogo.
            /// Crear y borrar viven acá y no como botones al lado del combo porque son operaciones
            /// sobre lo que el combo lista, y se usan una vez cada tanto: dos botones permanentes
            /// para eso era caro. Es el mismo patrón que ya usaba "Usar mazo…".
            /// </summary>
            public bool IsAction => Kind == SetKind.ShowDecks || Kind == SetKind.BackToLists ||
                                    Kind == SetKind.NewList || Kind == SetKind.DeleteList;

            /// <summary>Solo las listas se cargan con el ≡ de las celdas.</summary>
            public bool IsToggleable => Kind == SetKind.List;
        }

        public SearchWindow(CardCatalog catalog, string installPath)
        {
            InitializeComponent();
            WindowSizingFix.Attach(this); // maximizado y apertura acotados al área de trabajo
            _engine = new CardSearchEngine(catalog);
            _catalogRef = catalog;
            _installPathRef = installPath;
            _resolver = new CardImageResolver(installPath);
            _library = DeckLibrary.GetOrLoad(catalog, installPath);
            // después del library: la migración v1 de usercards.json necesita el catálogo para
            // traducir ids a nombres, y la carpeta Lists para volcar ahí las listas viejas
            _store = UserCardDataStore.Load(_library);

            Title = Strings.Format("Fmt_SearchWindowTitle", catalog.CardsVersion ?? "?");

            BuildChips(TypeChips, TypeValues.Select(t => Tuple.Create(Capitalize(t), t)));
            BuildChips(ClanChips, ClanValues.Select(c => Tuple.Create(Strings.Get("Clan_" + c), c)));
            BuildChips(RarityChips, RarityValues.Select(r => Tuple.Create(r, r)),
                       tooltipFor: r => Strings.Get("Rarity_" + r));
            BuildChips(DesignatorChips, DesignatorValues.Select(d => Tuple.Create(d, d)));
            BuildSetChecks(catalog);
            BuildStatsRows();

            // El chip de holdings iniciales solo existe si el catálogo trae cartas elegibles
            // (hasInitialHoldings de la app): sin filters.json no hay ninguna y sobraría.
            QuickHoldingsBtn.Visibility = catalog.Cards.Any(c => c.InitialHoldingSlot != null)
                ? Visibility.Visible : Visibility.Collapsed;

            _allKwCodes = catalog.KeywordCodes.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            KwCombo.ItemsSource = _allKwCodes;
            NegKwCombo.ItemsSource = _allKwCodes;
            // Autocomplete estilo app: matching por "contiene" mientras se tipea
            KwCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => KwComboTextChanged(KwCombo)));
            NegKwCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => KwComboTextChanged(NegKwCombo)));

            // los 6 de la app primero; después los extras del launcher (cabeceras)
            var sortFields = new[]
            {
                new { Key = "Search_Sort_Name", Field = SortField.NAME },
                new { Key = "Search_Sort_Cost", Field = SortField.COST },
                new { Key = "Search_Sort_Force", Field = SortField.FORCE },
                new { Key = "Search_Sort_Chi", Field = SortField.CHI },
                new { Key = "Search_Sort_PHonor", Field = SortField.PERSONAL_HONOR },
                new { Key = "Search_Sort_Set", Field = SortField.SET },
                new { Key = "Search_Sort_Type", Field = SortField.TYPE },
                new { Key = "Search_Sort_Clan", Field = SortField.CLAN },
                new { Key = "Search_Sort_Rarity", Field = SortField.RARITY },
                new { Key = "Search_Sort_Honor", Field = SortField.HONOR_REQ },
                new { Key = "Search_Sort_Focus", Field = SortField.FOCUS },
            };
            SortCombo.ItemsSource = sortFields
                .SelectMany(f => new[]
                {
                    new SortOption { Label = Strings.Get(f.Key), Field = f.Field, Ascending = true },
                    new SortOption { Label = Strings.Get(f.Key), Field = f.Field, Ascending = false },
                })
                .ToList();
            SortCombo.SelectedIndex = 0;
            UpdateZoomLabel();

            // Cabeceras de la tabla → campo de orden (click = ordenar; 2º click = invertir)
            var gv = (GridView)ResultsList.View;
            _headerSort = new Dictionary<GridViewColumn, SortField>
            {
                { gv.Columns[0], SortField.NAME },
                { gv.Columns[1], SortField.TYPE },
                { gv.Columns[2], SortField.CLAN },
                { gv.Columns[3], SortField.RARITY },
                { gv.Columns[4], SortField.COST },
                { gv.Columns[5], SortField.FORCE },
                { gv.Columns[6], SortField.CHI },
                { gv.Columns[7], SortField.HONOR_REQ },
                { gv.Columns[8], SortField.PERSONAL_HONOR },
                { gv.Columns[9], SortField.SET },
            };

            _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _feedbackTimer.Tick += (s, e) =>
            {
                DeckFeedback.Visibility = Visibility.Collapsed;
                _feedbackTimer.Stop();
            };

            _searchDebounce = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _searchDebounce.Tick += (s, e) => { _searchDebounce.Stop(); Recompute(); };

            _library.ActiveDeckChanged += OnActiveDeckChanged;
            _library.ActiveListChanged += OnActiveListChanged;
            _library.Repo.DeckListChanged += OnDeckListChanged;
            _library.Repo.DeckChanged += OnDeckContentChanged;
            Closed += (s, e) =>
            {
                _library.ActiveDeckChanged -= OnActiveDeckChanged;
                _library.ActiveListChanged -= OnActiveListChanged;
                _library.Repo.DeckListChanged -= OnDeckListChanged;
                _library.Repo.DeckChanged -= OnDeckContentChanged;
            };

            // el conjunto activo arranca en la lista que quedó activa la última vez
            _activeSet = ChoiceForList(_library.ActiveList);
            RefreshSetCombo();
            RefreshActiveDeckLabel();
            RefreshPresetCombo(null);
            UpdateDetailActions();

            _initializing = false;
            Recompute();
            Loaded += (s, e) => NameBox.Focus();
        }

        // ------------------------------------------------------------------ title bar custom

        private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaxButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_StateChanged(object sender, EventArgs e)
        {
            // El maximizado ya lo acota WindowSizingFix al área de trabajo del monitor, así que
            // no hace falta el margen que antes compensaba el desborde del borde de resize.
            MaxButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        // ------------------------------------------------------------------ armado de controles

        private void BuildChips(Panel panel, IEnumerable<Tuple<string, string>> items,
                                Func<string, string> tooltipFor = null)
        {
            foreach (var item in items)
            {
                var chip = new ToggleButton
                {
                    Content = item.Item1,
                    Tag = item.Item2,
                    Style = (Style)FindResource("FilterChip"),
                };
                if (tooltipFor != null) chip.ToolTip = tooltipFor(item.Item2);
                chip.Checked += Filter_Changed;
                chip.Unchecked += Filter_Changed;
                panel.Children.Add(chip);
            }
        }

        private void BuildSetChecks(CardCatalog catalog)
        {
            // Orden canónico del selector de Edición (presortSets, igual que la app),
            // con títulos de arco cada vez que aparece un código separador.
            var arcHeaderAdded = false;
            foreach (var set in CardSearchEngine.PresortSets(catalog.Sets))
            {
                var sepIdx = Array.IndexOf(ArcSeparatorCodes, set.Code);
                if (sepIdx >= 0)
                {
                    AddArcHeader(sepIdx + 1);
                    arcHeaderAdded = true;
                }
                else if (!arcHeaderAdded)
                {
                    AddArcHeader(0); // todo lo previo al primer separador = arco Lotus
                    arcHeaderAdded = true;
                }

                var check = new CheckBox
                {
                    Content = set.DisplayName,
                    Tag = set.Code,
                    ToolTip = set.Code,
                    Style = (Style)FindResource("FilterCheck"),
                    Margin = new Thickness(8, 3, 0, 3),
                };
                check.Checked += Filter_Changed;
                check.Unchecked += Filter_Changed;
                SetChecks.Children.Add(check);
            }
        }

        private void AddArcHeader(int arcIdx)
        {
            if (arcIdx >= ArcLabelKeys.Length) return;
            SetChecks.Children.Add(new TextBlock
            {
                Text = Strings.Get(ArcLabelKeys[arcIdx]),
                FontFamily = (FontFamily)FindResource("BrushFont"),
                FontSize = 14,
                Foreground = (Brush)FindResource("GoldSoft"),
                Margin = new Thickness(0, SetChecks.Children.Count == 0 ? 2 : 10, 0, 3),
            });
        }

        private void BuildStatsRows()
        {
            AddStatRow(Strings.Get("Search_StatForce"), out _forceMin, out _forceMax, "+*",
                       Strings.Get("Search_SpecialForceTip"), out _forceSpecial);
            AddStatRow(Strings.Get("Search_StatChi"), out _chiMin, out _chiMax, null, null, out _);
            AddStatRow(Strings.Get("Search_StatCost"), out _costMin, out _costMax, "*",
                       Strings.Get("Search_SpecialCostTip"), out _costSpecial);
            AddStatRow(Strings.Get("Search_StatHonor"), out _honorMin, out _honorMax, "−",
                       Strings.Get("Search_NegativeHonorTip"), out _honorNeg);
            AddStatRow(Strings.Get("Search_StatPHonor"), out _pHonorMin, out _pHonorMax, "−",
                       Strings.Get("Search_NegativeHonorTip"), out _pHonorNeg);
            AddStatRow(Strings.Get("Search_StatFocus"), out _focusMin, out _focusMax, null, null, out _);
        }

        private void AddStatRow(string label, out TextBox minBox, out TextBox maxBox,
                                string specialLabel, string specialTip, out CheckBox specialBox)
        {
            // LastChildFill=false: sin él, el último control de la fila (max o check) se centra
            // en el espacio sobrante y las filas de 2 cajas quedan desalineadas frente a las de 3.
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7), LastChildFill = false };
            var text = new TextBlock
            {
                Text = label,
                Width = 92,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("BodySmall"),
                FontSize = 12,
            };
            row.Children.Add(text);

            minBox = new TextBox { Style = (Style)FindResource("SmallNumBox"), ToolTip = Strings.Get("Search_MinTip") };
            maxBox = new TextBox { Style = (Style)FindResource("SmallNumBox"), ToolTip = Strings.Get("Search_MaxTip") };
            minBox.TextChanged += FilterText_Changed;
            maxBox.TextChanged += FilterText_Changed;
            row.Children.Add(minBox);
            row.Children.Add(maxBox);

            specialBox = null;
            if (specialLabel != null)
            {
                specialBox = new CheckBox
                {
                    Content = specialLabel,
                    ToolTip = specialTip,
                    Style = (Style)FindResource("FilterCheck"),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                specialBox.Checked += Filter_Changed;
                specialBox.Unchecked += Filter_Changed;
                row.Children.Add(specialBox);
            }
            StatsPanel.Children.Add(row);
        }

        // ------------------------------------------------------------------ criteria y búsqueda

        private void Filter_Changed(object sender, RoutedEventArgs e) => Recompute();
        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => Recompute();

        /// <summary>
        /// Filtros de texto libre: no busca en el acto, arma (reinicia) el debounce. La búsqueda
        /// corre en el Tick, una sola vez, cuando el usuario para de tipear. Los toggles/combos
        /// siguen filtrando inmediatos (son acciones discretas, no una ráfaga de teclas).
        /// </summary>
        private void FilterText_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private void Recompute()
        {
            _searchDebounce?.Stop(); // un recompute directo cancela el debounce pendiente
            if (_initializing) return;

            _lastCriteria = BuildCriteria();
            // El conjunto activo se materializa acá, en la copia que va al motor: para el motor
            // sigue siendo "una lista con estos ids" (lo que verifican los golden), venga de una
            // lista, de un mazo o de las favoritas.
            var user = _lastCriteria.ListId == null
                ? _store.Data
                : _store.Data.WithList(_lastCriteria.ListId, ActiveSetCardIds());
            _results = _engine.Search(_lastCriteria, user);

            var selectedId = (ResultsList.SelectedItem as Card)?.Id ?? _selectedCell?.Card.Id;
            ResultsList.ItemsSource = _results;
            ResultCount.Text = Strings.Format("Fmt_SearchResults", _results.Count);
            RefreshFilterSummary();

            _gridDirty = true;
            if (CardGrid.Visibility == Visibility.Visible) BuildGrid(selectedId);

            if (selectedId != null)
            {
                var again = _results.FirstOrDefault(c => c.Id == selectedId);
                if (again != null && ResultsList.Visibility == Visibility.Visible)
                {
                    ResultsList.SelectedItem = again;
                    ResultsList.ScrollIntoView(again);
                }
            }
        }

        /// <summary>
        /// Modos de la barra por prefijo (port de parseBarText de la app; tx. se evalúa antes
        /// que t.). NAME filtra en vivo; TEXT (tx.) filtra el texto de carta en vivo; los modos
        /// KEYWORD/CLAN/TYPE se confirman con Enter.
        /// </summary>
        private enum BarMode { Name, Keyword, Clan, Type, Text }

        private BarMode ParseBar(out string value)
        {
            var bar = NameBox.Text ?? "";
            if (bar.StartsWith("tx.", StringComparison.OrdinalIgnoreCase)) { value = bar.Substring(3); return BarMode.Text; }
            if (bar.StartsWith("k.", StringComparison.OrdinalIgnoreCase)) { value = bar.Substring(2); return BarMode.Keyword; }
            if (bar.StartsWith("c.", StringComparison.OrdinalIgnoreCase)) { value = bar.Substring(2); return BarMode.Clan; }
            if (bar.StartsWith("t.", StringComparison.OrdinalIgnoreCase)) { value = bar.Substring(2); return BarMode.Type; }
            value = bar;
            return BarMode.Name;
        }

        private CardSearchCriteria BuildCriteria()
        {
            var rarities = CheckedTags(RarityChips)
                // La app filtra rareza case-sensitive y el dataset trae variantes en minúscula
                // (⚠️ SPEC-FILTROS §7): el motor porta el bug, la UI manda ambas.
                .SelectMany(r => new[] { r, r.ToLowerInvariant() })
                .Distinct().ToList();

            // En modo operador la barra no filtra por nombre; en modo tx. pisa el texto del panel
            var barMode = ParseBar(out var barValue);

            // "Solo conjunto": muestra únicamente las cartas del conjunto activo (p. ej. armar el
            // mazo de ninjas desde la lista de ninjas, o filtrar por lo que ya está en un mazo).
            // El conjunto se elige en el desplegable de la barra.
            var set = QuickListBtn.IsChecked == true ? ActiveSet : null;

            return new CardSearchCriteria
            {
                Name = barMode == BarMode.Name ? barValue : "",
                Text = barMode == BarMode.Text ? barValue : (TextSearchBox.Text ?? ""),
                ListId = set?.Name,
                ListKind = set == null ? null : set.Kind.ToString().ToLowerInvariant(),
                Types = CheckedTags(TypeChips),
                Clans = CheckedTags(ClanChips),
                Rarities = rarities,
                Sets = SetChecks.Children.OfType<CheckBox>()
                    .Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList(),
                Designators = CheckedTags(DesignatorChips),
                Keywords = _keywords.ToList(),
                NegativeKeywords = _negKeywords.ToList(),
                KeywordMatchAll = KwMatchAllBox.IsChecked == true,
                IncludeActionKeywords = KwActionBox.IsChecked == true,
                IncludeSubtitleKeywords = KwSubtitleBox.IsChecked == true,
                IncludeBanned = IncludeBannedBox.IsChecked == true,
                OnlyInitialHoldings = QuickHoldingsBtn.IsChecked == true,
                OnlyFavorites = QuickFavBtn.IsChecked == true,
                IncludeHidden = QuickHiddenBtn.IsChecked == true,
                OnlyHidden = OnlyHiddenBox.IsChecked == true,
                ForceMin = ParseIntBox(_forceMin),
                ForceMax = ParseIntBox(_forceMax),
                IncludeSpecialForce = _forceSpecial.IsChecked == true,
                ChiMin = ParseIntBox(_chiMin),
                ChiMax = ParseIntBox(_chiMax),
                CostMin = ParseIntBox(_costMin),
                CostMax = ParseIntBox(_costMax),
                IncludeSpecialCost = _costSpecial.IsChecked == true,
                HonorReqMin = ParseIntBox(_honorMin),
                HonorReqMax = ParseIntBox(_honorMax),
                IncludeNegativeHonorReq = _honorNeg.IsChecked == true,
                PersonalHonorMin = ParseIntBox(_pHonorMin),
                PersonalHonorMax = ParseIntBox(_pHonorMax),
                IncludeNegativePersonalHonor = _pHonorNeg.IsChecked == true,
                FocusMin = ParseIntBox(_focusMin),
                FocusMax = ParseIntBox(_focusMax),
                SortField = SelectedSort?.Field ?? SortField.NAME,
                SortAscending = SelectedSort?.Ascending ?? true,
            };
        }

        private static List<string> CheckedTags(Panel panel) =>
            panel.Children.OfType<ToggleButton>()
                .Where(t => t.IsChecked == true).Select(t => (string)t.Tag).ToList();

        private static int? ParseIntBox(TextBox box)
        {
            var text = (box.Text ?? "").Trim();
            if (text.Length == 0) { box.Tag = null; return null; }
            if (int.TryParse(text, out var value)) { box.Tag = null; return value; }
            box.Tag = "invalid";
            return null;
        }

        // ------------------------------------------------------------------ resumen de filtros

        /// <summary>
        /// Compacto de los criterios activos al lado del contador (`summaryOf` de la app). Sin
        /// esto, con el panel de filtros colapsado no hay forma de saber qué está filtrando.
        /// </summary>
        private void RefreshFilterSummary()
        {
            var parts = SummaryParts(_lastCriteria);
            FilterSummary.Text = parts.Count == 0 ? "" : "· " + string.Join(" · ", parts);
            // en compacto el resumen no entra; el chip de "Solo lista" y los toggles siguen a la vista
            FilterSummary.Visibility = parts.Count == 0 || _compact
                ? Visibility.Collapsed : Visibility.Visible;
            // "Limpiar" solo aparece cuando hay algo que limpiar: un botón permanente que la mitad
            // del tiempo no hace nada ocupa el lugar de arranque de la fila de filtros sin decir
            // nada. Ahora su presencia ES la señal de que hay filtros puestos.
            ClearFiltersBtn.Visibility = parts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Click en el resumen: el texto completo, porque en la barra va recortado.</summary>
        private void FilterSummary_Click(object sender, MouseButtonEventArgs e)
        {
            var parts = SummaryParts(_lastCriteria);
            if (parts.Count == 0) return;
            Dialogs.Info(this, Strings.Get("Search_FilterSummaryTitle"), string.Join("\n", parts));
        }

        private List<string> SummaryParts(CardSearchCriteria c)
        {
            var parts = new List<string>();
            void Add(string s) { if (!string.IsNullOrWhiteSpace(s)) parts.Add(s); }

            if (!string.IsNullOrWhiteSpace(c.Name)) Add("\"" + c.Name.Trim() + "\"");
            if (!string.IsNullOrWhiteSpace(c.Text)) Add(Strings.Get("Search_Sum_Text") + " \"" + c.Text.Trim() + "\"");
            if (c.Types.Any()) Add(string.Join("/", c.Types));
            // las rarezas van duplicadas en mayúscula+minúscula por el bug portado: se muestran una vez
            if (c.Clans.Any()) Add(string.Join("/", c.Clans.Select(x => Strings.Get("Clan_" + x))));
            if (c.Rarities.Any())
                Add(string.Join("/", c.Rarities.Select(r => r.ToUpperInvariant()).Distinct()));
            if (c.Sets.Any())
                Add(c.Sets.Count <= 3
                    ? string.Join("/", c.Sets)
                    : Strings.Format("Fmt_Sum_Sets", c.Sets.Count));
            if (c.Designators.Any()) Add(string.Join("/", c.Designators));
            if (c.Keywords.Any())
                Add((c.KeywordMatchAll ? "" : Strings.Get("Search_Sum_AnyKw") + " ") + string.Join("+", c.Keywords));
            if (c.NegativeKeywords.Any()) Add("−" + string.Join(" −", c.NegativeKeywords));

            Add(Range("F", c.ForceMin, c.ForceMax));
            Add(Range("Chi", c.ChiMin, c.ChiMax));
            Add(Range("©", c.CostMin, c.CostMax));
            Add(Range("H", c.HonorReqMin, c.HonorReqMax));
            Add(Range("HP", c.PersonalHonorMin, c.PersonalHonorMax));
            Add(Range(Strings.Get("Search_Sum_Focus"), c.FocusMin, c.FocusMax));

            if (c.IncludeBanned) Add(Strings.Get("Search_Sum_Banned"));
            if (c.OnlyInitialHoldings) Add(Strings.Get("Search_OnlyInitialHoldings"));
            if (c.OnlyFavorites) Add(Strings.Get("Search_OnlyFavorites"));
            if (c.OnlyHidden) Add(Strings.Get("Search_OnlyHidden"));
            else if (c.IncludeHidden) Add(Strings.Get("Search_ShowHidden"));
            if (c.ListId != null) Add(Strings.Format("Fmt_Sum_Set", _activeSet?.Label ?? c.ListId));
            return parts;
        }

        /// <summary>"F≥5" / "F≤3" / "F 2-5"; vacío si no hay rango.</summary>
        private static string Range(string label, int? min, int? max)
        {
            if (min == null && max == null) return null;
            if (min != null && max != null) return $"{label} {min}-{max}";
            return min != null ? $"{label}≥{min}" : $"{label}≤{max}";
        }

        private SortOption SelectedSort => SortCombo.SelectedItem as SortOption;

        /// <summary>Selecciona la opción (campo, dirección) sin disparar un recálculo de más.</summary>
        private void SelectSort(SortField field, bool ascending)
        {
            var match = (SortCombo.ItemsSource as IEnumerable<SortOption>)
                ?.FirstOrDefault(o => o.Field == field && o.Ascending == ascending);
            if (match != null) SortCombo.SelectedItem = match;
        }

        /// <summary>Click en una cabecera de la tabla: ordena por esa columna; repetir invierte.</summary>
        private void ResultsHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header?.Column == null || !_headerSort.TryGetValue(header.Column, out var field)) return;
            var current = SelectedSort;
            _initializing = true;
            // repetir la misma cabecera invierte; cambiar de columna arranca ascendente
            SelectSort(field, current != null && current.Field == field ? !current.Ascending : true);
            _initializing = false;
            Recompute();
        }

        // ------------------------------------------------------------------ paneles colapsables

        /// <summary>
        /// Colapsa/expande los paneles laterales desde los rieles; el centro absorbe el ancho
        /// liberado. Las columnas de los rieles no se tocan: son la única forma de reabrir.
        /// </summary>
        /// <summary>
        /// Debajo de este ancho los paneles laterales se cierran solos y los rieles se apagan:
        /// con 270 + 405 de paneles más el centro no queda una grilla usable, y dejar abrir un
        /// panel que no entra es peor que no dejar.
        /// </summary>
        private const double NarrowWidth = 1000;

        private bool _narrow;
        // Lo que el usuario eligió cuando la ventana era ancha, para restaurarlo al volver.
        private bool _wantFilters = true, _wantDetail = true;

        /// <summary>
        /// Por debajo de esto un panel deja de ser usable (los chips de filtro se parten, el
        /// detalle no entra), así que arrastrar más allá lo colapsa en vez de dejarlo inservible.
        /// </summary>
        private const double MinPaneWidth = 180;

        // Ancho elegido por el usuario con el mouse; es lo que se restaura al reabrir el panel.
        private double _filtersWidth = 270, _detailWidth = 405;

        private void PaneToggle_Click(object sender, RoutedEventArgs e)
        {
            _wantFilters = FiltersPaneBtn.IsChecked == true;
            _wantDetail = DetailPaneBtn.IsChecked == true;
            ApplyPanes();
        }

        private void ApplyPanes()
        {
            var filtersOn = FiltersPaneBtn.IsChecked == true;
            FiltersPanel.Visibility = filtersOn ? Visibility.Visible : Visibility.Collapsed;
            FiltersCol.Width = new GridLength(filtersOn ? _filtersWidth : 0);
            FiltersSplitter.IsEnabled = filtersOn && !_narrow;

            var detailOn = DetailPaneBtn.IsChecked == true;
            DetailPanelBorder.Visibility = detailOn ? Visibility.Visible : Visibility.Collapsed;
            DetailCol.Width = new GridLength(detailOn ? _detailWidth : 0);
            DetailSplitter.IsEnabled = detailOn && !_narrow;
        }

        /// <summary>
        /// Arrastre de la divisoria izquierda. El `GridSplitter` ya movió la columna; acá solo se
        /// decide si el panel sigue teniendo sentido: por debajo del mínimo se colapsa y se corta
        /// el arrastre, que es lo que el usuario está pidiendo cuando empuja el borde hasta el fondo.
        /// </summary>
        private void FiltersSplitter_DragDelta(object sender, DragDeltaEventArgs e) =>
            ClampPane(FiltersCol, FiltersPaneBtn, ref _filtersWidth);

        private void DetailSplitter_DragDelta(object sender, DragDeltaEventArgs e) =>
            ClampPane(DetailCol, DetailPaneBtn, ref _detailWidth);

        private void ClampPane(ColumnDefinition column, ToggleButton rail, ref double remembered)
        {
            if (column.ActualWidth < MinPaneWidth)
            {
                // se recuerda el último ancho *válido*, no el pisado: al reabrir vuelve a algo usable
                rail.IsChecked = false;
                PaneToggle_Click(rail, null);
                return;
            }
            remembered = column.ActualWidth;
        }

        /// <summary>Al soltar, la columna queda fija en píxeles: el centro (`*`) absorbe el resto.</summary>
        private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            // El GridSplitter convierte la columna central de `*` a píxeles al arrastrar, y ahí la
            // ventana deja de repartir bien el ancho al redimensionar. Se devuelve a estrella.
            CenterCol.Width = new GridLength(1, GridUnitType.Star);
            ApplyPanes();
        }

        /// <summary>
        /// Segundo escalón, más abajo que <see cref="NarrowWidth"/>: acá ya no alcanza con cerrar
        /// los paneles, porque el **piso lo pone la barra de botones**. Se esconde lo accesorio
        /// —el bloque del mazo activo y el zoom— para que la ventana pueda seguir angostándose.
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
            foreach (var rail in new[] { FiltersPaneBtn, DetailPaneBtn })
            {
                rail.IsEnabled = !narrow;
                rail.ToolTip = narrow ? Strings.Get("Pane_TooNarrow") : null;
            }
            FiltersPaneBtn.IsChecked = !narrow && _wantFilters;
            DetailPaneBtn.IsChecked = !narrow && _wantDetail;
            ApplyPanes();
        }

        private void ApplyCompactMode()
        {
            var compact = ActualWidth < CompactWidth;
            if (compact == _compact) return;
            _compact = compact;
            var hidden = compact ? Visibility.Collapsed : Visibility.Visible;
            // el mazo activo se sigue viendo entero en la ventana de Mazos
            ActiveDeckBlock.Visibility = hidden;
            FilterSummary.Visibility = compact ? Visibility.Collapsed
                : (FilterSummary.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed);
            // El zoom ya no se esconde: dejó de ser dos botones y ahora es parte del segmentado de
            // vista, así que sacarlo dejaría el grupo cortado por la mitad. El que sí ocupa (un chip
            // con etiqueta larga) es "Expandir impresiones", y esa es una sub-opción de la grilla.
            ExpandPrintsBtn.Visibility = hidden;
        }

        /// <summary>
        /// Deja la ventana lista para trabajar al lado del editor: sin panel de detalle (el mazo
        /// se ve en la otra ventana) y con los filtros abiertos si entran. La usa el botón de
        /// reacomodar del editor.
        /// </summary>
        public void ApplySideBySideLayout()
        {
            _wantDetail = false;
            _wantFilters = true;
            DetailPaneBtn.IsChecked = false;
            FiltersPaneBtn.IsChecked = !_narrow;
            ApplyPanes();
        }

        // ------------------------------------------------------------------ vista lista / grilla

        private void ViewToggle_Click(object sender, RoutedEventArgs e)
        {
            var grid = ReferenceEquals(sender, ViewGridBtn);
            // exclusividad manual (par de chips segmentados)
            ViewGridBtn.IsChecked = grid;
            ViewListBtn.IsChecked = !grid;
            ExpandPrintsBtn.IsEnabled = grid;
            // el zoom es de la grilla: en vista Lista no aplica
            ZoomBtn.IsEnabled = grid;
            if (!grid) ZoomPopup.IsOpen = false;

            var selectedId = _selectedCell?.Card.Id ?? (ResultsList.SelectedItem as Card)?.Id;
            CardGrid.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
            ResultsList.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;

            if (grid)
            {
                if (_gridDirty) BuildGrid(selectedId);
            }
            else if (selectedId != null)
            {
                var again = _results.FirstOrDefault(c => c.Id == selectedId);
                if (again != null)
                {
                    ResultsList.SelectedItem = again;
                    ResultsList.ScrollIntoView(again);
                }
            }
        }

        private void ExpandPrints_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            _gridDirty = true;
            if (CardGrid.Visibility == Visibility.Visible)
                BuildGrid(_selectedCell?.Card.Id);
        }

        /// <summary>Reconstruye las celdas de la grilla desde los resultados actuales.</summary>
        private void BuildGrid(string reselectCardId)
        {
            var expand = ExpandPrintsBtn.IsChecked == true;
            var entries = _engine.ExpandForDisplay(_results, _lastCriteria, expand);

            _selectedCell = null;
            _gridCells = entries.Select(v => new GridCell
            {
                Card = v.Card,
                ImagePath = v.ImagePath,
                // Expandido: solo esa impresión (sin cadena de fallback — cada celda ES una
                // impresión). Sin expandir: cadena de fallback de la app.
                CandidatePaths = _resolver.ResolveCandidates(v.Card, v.ImagePath, fallbackChain: !expand),
            }).ToList();

            if (reselectCardId != null)
            {
                var again = _gridCells.FirstOrDefault(c => c.Card.Id == reselectCardId);
                if (again != null) { again.IsSelected = true; _selectedCell = again; }
            }

            RefreshCellStates(); // estado inicial de la barra de acciones de cada celda

            _gridDirty = false;
            ChunkGrid();
            ScrollGridToSelected(); // sync de scroll al entrar a la grilla / recomputar
        }

        private void ChunkGrid()
        {
            var width = CardGrid.ActualWidth;
            var cols = width > GridCellOuterWidth
                ? Math.Max(2, (int)((width - 20) / GridCellOuterWidth))
                : Math.Max(2, _gridCols);
            _gridCols = cols;

            var rows = new List<List<GridCell>>((_gridCells.Count + cols - 1) / cols);
            for (int i = 0; i < _gridCells.Count; i += cols)
                rows.Add(_gridCells.GetRange(i, Math.Min(cols, _gridCells.Count - i)));
            CardGrid.ItemsSource = rows;
        }

        private void CardGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_gridCells.Count == 0) return;
            var width = CardGrid.ActualWidth;
            if (width <= GridCellOuterWidth) return;
            var cols = Math.Max(2, (int)((width - 20) / GridCellOuterWidth));
            if (cols != _gridCols) ChunkGrid();
        }

        /// <summary>
        /// True mientras se mantiene Ctrl: las celdas de la grilla se prenden y la de abajo del
        /// mouse muestra el overlay "Agregar al mazo". Es una DP de la ventana y no un flag por
        /// celda a propósito: un solo cambio y todas las celdas realizadas reaccionan por binding,
        /// en vez de recorrer miles de VMs en cada tecla.
        /// </summary>
        // Tamaño de celda como DP: la plantilla de la grilla se bindea contra la ventana, así un
        // solo cambio re-mide todas las celdas realizadas sin recorrer las VMs.
        /// <summary>
        /// Zoom mínimo: la celda es más angosta que la barra de acciones, así que la barra se
        /// esconde. DP y no un campo porque lo consume la plantilla de la grilla por binding.
        /// </summary>
        public static readonly DependencyProperty IsTinyZoomProperty =
            DependencyProperty.Register(nameof(IsTinyZoom), typeof(bool), typeof(SearchWindow),
                new PropertyMetadata(false));

        public bool IsTinyZoom
        {
            get => (bool)GetValue(IsTinyZoomProperty);
            set => SetValue(IsTinyZoomProperty, value);
        }

        public static readonly DependencyProperty CellWidthProperty =
            DependencyProperty.Register(nameof(CellWidth), typeof(double), typeof(SearchWindow),
                new PropertyMetadata(226.0));

        public double CellWidth
        {
            get => (double)GetValue(CellWidthProperty);
            set => SetValue(CellWidthProperty, value);
        }

        public static readonly DependencyProperty CellHeightProperty =
            DependencyProperty.Register(nameof(CellHeight), typeof(double), typeof(SearchWindow),
                new PropertyMetadata(322.0));

        public double CellHeight
        {
            get => (double)GetValue(CellHeightProperty);
            set => SetValue(CellHeightProperty, value);
        }

        private void Help_Click(object sender, RoutedEventArgs e) =>
            Dialogs.Info(this, Strings.Get("Help_ShortcutsTitle"), Strings.Get("Help_ShortcutsSearch"));

        /// <summary>
        /// El zoom dejó de ser dos botones (`－`/`＋`) para ser un desplegable pegado a Lista|Grilla:
        /// es una propiedad de la vista, igual que ellas, y dos botones permanentes para cuatro
        /// escalones eran caros para lo que aportaban. Los atajos (Ctrl+rueda) siguen igual.
        /// </summary>
        // El popup sigue al ESTADO del toggle, no al click: así queda consistente aunque el
        // IsChecked lo cambie otra cosa (el propio Closed del popup, un atajo, automatización).
        private void ZoomBtn_Checked(object sender, RoutedEventArgs e)
        {
            BuildZoomOptions();
            ZoomPopup.IsOpen = true;
        }

        private void ZoomBtn_Unchecked(object sender, RoutedEventArgs e) => ZoomPopup.IsOpen = false;

        private void ZoomPopup_Closed(object sender, EventArgs e) => ZoomBtn.IsChecked = false;

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

        private static readonly string[] ZoomLabelKeys =
            { "Search_ZoomTiny", "Search_ZoomSmall", "Search_ZoomNormal", "Search_ZoomLarge" };

        /// <summary>El botón muestra el tamaño actual: sin eso el desplegable no dice en qué está.</summary>
        private void UpdateZoomLabel() => ZoomBtn.Content = Strings.Get(ZoomLabelKeys[_zoom]) + "  ▾";

        /// <summary>Ctrl+rueda = zoom, como en cualquier visor. Sin Ctrl, la rueda scrollea.</summary>
        private void CardGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            e.Handled = true;
            SetZoom(_zoom + (e.Delta > 0 ? 1 : -1));
        }

        /// <summary>
        /// Cambia el tamaño de celda. **Lo único que hace falta rehacer es el agrupado en filas**,
        /// porque cambia la cantidad de columnas: el tamaño en sí viaja por binding a las celdas
        /// ya realizadas, y las miniaturas no dependen del zoom.
        ///
        /// Antes esto reconstruía las ~6400 VMs con `BuildGrid` (re-expandir + miles de
        /// `Path.GetFullPath`), invalidaba el `Thumb` de todas y **vaciaba el caché de
        /// miniaturas**, así que cada cambio de zoom volvía a decodificar desde disco todo lo
        /// visible. Eso era el tirón y el "cargando".
        /// </summary>
        private void SetZoom(int zoom)
        {
            zoom = Math.Max(0, Math.Min(ZoomWidths.Length - 1, zoom));
            if (zoom == _zoom) return;
            _zoom = zoom;
            CellWidth = CellWidthFor(zoom);
            CellHeight = Math.Round(CellWidth * CardAspect);
            IsTinyZoom = zoom == 0;
            UpdateZoomLabel();

            if (CardGrid.Visibility != Visibility.Visible) return;
            ChunkGrid();             // reagrupa las MISMAS VMs con el ancho de fila nuevo
            ScrollGridToSelected();
        }

        public static readonly DependencyProperty IsAddModifierDownProperty =
            DependencyProperty.Register(nameof(IsAddModifierDown), typeof(bool), typeof(SearchWindow),
                new PropertyMetadata(false));

        public bool IsAddModifierDown
        {
            get => (bool)GetValue(IsAddModifierDownProperty);
            set => SetValue(IsAddModifierDownProperty, value);
        }

        private void SyncAddModifier() =>
            IsAddModifierDown = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            SyncAddModifier();
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) HidePreview();
        }

        // Sin esto el modo queda pegado si se suelta Ctrl con la ventana en segundo plano
        // (p.ej. Alt+Tab con Ctrl apretado): el KeyUp nunca llega.
        private void Window_Deactivated(object sender, EventArgs e)
        {
            IsAddModifierDown = false;
            HidePreview();
        }

        private void GridCell_Click(object sender, MouseButtonEventArgs e)
        {
            var cell = (sender as FrameworkElement)?.DataContext as GridCell;
            if (cell == null) return;
            SelectGridCell(cell);
            CardGrid.Focus(); // habilita la navegación por flechas
        }

        /// <summary>
        /// Doble click en una celda = agregar la carta al mazo activo. **Ctrl+click** hace lo mismo
        /// con un solo click y sin apuntar al `+` chiquito de la barra de acciones: mientras Ctrl
        /// está apretado el overlay tapa la celda entera, así que el click cae siempre acá.
        /// </summary>
        /// <summary>
        /// Click derecho sobre una celda = copiar el arte al portapapeles. La celda no se
        /// selecciona: es una acción sobre esa carta, no una forma de navegar.
        /// </summary>
        private void GridCell_CopyImage(object sender, MouseButtonEventArgs e)
        {
            var cell = (sender as FrameworkElement)?.DataContext as GridCell;
            if (cell == null) return;
            e.Handled = true;
            var ok = CardImageClipboard.TryCopy(cell.CandidatePaths);
            ShowDeckFeedback(
                ok ? Strings.Format("Fmt_CopiedImage", cell.Card.Name) : Strings.Get("Detail_CopyImageFail"),
                error: !ok);
        }

        // ------------------------------------------------------------------ previsualización (Alt)

        private string _previewCardId;

        /// <summary>
        /// Con **Alt** apretado, pasar sobre una carta la abre grande al lado del cursor. Se lee
        /// el modificador en el MouseMove en vez de escuchar la tecla: Alt en WPF llega como
        /// `Key.System` y compite con la activación de menús; preguntarlo acá es más simple y no
        /// deja el estado pegado si se suelta la tecla fuera de la ventana.
        /// </summary>
        private void GridCell_MouseMove(object sender, MouseEventArgs e)
        {
            var cell = (sender as FrameworkElement)?.DataContext as GridCell;
            if (cell == null) return;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) { HidePreview(); return; }
            if (HoverPreview.IsOpen && _previewCardId == cell.Card.Id) return;

            // el original, no la miniatura: la gracia de esto es mirar el arte
            var image = CardImageClipboard.LoadFull(cell.CandidatePaths);
            if (image == null) { HidePreview(); return; }
            _previewCardId = cell.Card.Id;
            HoverPreviewImage.Source = image;
            HoverPreviewPlacer.Place(HoverPreview, this);
            HoverPreview.IsOpen = true;
        }

        private void GridCell_MouseLeave(object sender, MouseEventArgs e) => HidePreview();

        /// <summary>
        /// Alt sobre la vista Lista. Va sobre el `ListView` y no sobre cada fila porque las filas
        /// están virtualizadas (se reciclan): enganchar el evento en el contenedor y buscar la fila
        /// bajo el cursor es lo que sobrevive al reciclado. Los candidatos se resuelven acá porque
        /// la tabla bindea `Card` directo, sin `GridCell` que los traiga.
        /// </summary>
        private void ResultsList_MouseMove(object sender, MouseEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) { HidePreview(); return; }
            var card = RowCardAt(e.OriginalSource as DependencyObject);
            if (card == null) { HidePreview(); return; }
            if (HoverPreview.IsOpen && _previewCardId == card.Id) return;

            var image = CardImageClipboard.LoadFull(
                _resolver.ResolveCandidates(card, null, fallbackChain: true));
            if (image == null) { HidePreview(); return; }
            _previewCardId = card.Id;
            HoverPreviewImage.Source = image;
            HoverPreviewPlacer.Place(HoverPreview, this);
            HoverPreview.IsOpen = true;
        }

        private void ResultsList_MouseLeave(object sender, MouseEventArgs e) => HidePreview();

        /// <summary>Sube por el árbol visual hasta la fila y devuelve su carta (null fuera de una).</summary>
        private static Card RowCardAt(DependencyObject source)
        {
            while (source != null && !(source is ListViewItem))
            {
                if (source is System.Windows.Media.Visual || source is System.Windows.Media.Media3D.Visual3D)
                    source = VisualTreeHelper.GetParent(source);
                else
                    source = LogicalTreeHelper.GetParent(source);
            }
            return (source as ListViewItem)?.DataContext as Card;
        }

        private void HidePreview()
        {
            if (!HoverPreview.IsOpen) return;
            HoverPreview.IsOpen = false;
            HoverPreviewImage.Source = null;
            _previewCardId = null;
        }

        private void GridCell_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var cell = (sender as FrameworkElement)?.DataContext as GridCell;
            if (cell == null) return;

            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (!ctrl && e.ClickCount != 2) return;

            SelectGridCell(cell);
            CardGrid.Focus();
            ApplyDeckDelta(cell.Card, +1);
            e.Handled = true;
        }

        private void SelectGridCell(GridCell cell)
        {
            if (_selectedCell != null) _selectedCell.IsSelected = false;
            cell.IsSelected = true;
            _selectedCell = cell;
            ShowDetail(cell.Card, cell.ImagePath);
        }

        /// <summary>Navegación por teclado en la grilla: flechas, Inicio/Fin.</summary>
        private void CardGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_gridCells.Count == 0 || _gridCols <= 0) return;
            var current = _selectedCell != null ? _gridCells.IndexOf(_selectedCell) : -1;
            int next;
            switch (e.Key)
            {
                case Key.Left: next = current < 0 ? 0 : current - 1; break;
                case Key.Right: next = current < 0 ? 0 : current + 1; break;
                case Key.Up: next = current < 0 ? 0 : current - _gridCols; break;
                case Key.Down: next = current < 0 ? 0 : current + _gridCols; break;
                case Key.Home: next = 0; break;
                case Key.End: next = _gridCells.Count - 1; break;
                default: return;
            }
            e.Handled = true;
            next = Math.Max(0, Math.Min(_gridCells.Count - 1, next));
            if (next == current) return;
            SelectGridCell(_gridCells[next]);
            ScrollGridToSelected();
        }

        private void ScrollGridToSelected()
        {
            if (_selectedCell == null || _gridCols <= 0) return;
            var idx = _gridCells.IndexOf(_selectedCell);
            if (idx < 0) return;
            var row = idx / _gridCols;
            if (row < CardGrid.Items.Count) CardGrid.ScrollIntoView(CardGrid.Items[row]);
        }

        // ------------------------------------------------------------------ limpiar

        /// <summary>Resetea todos los filtros preservando el nombre tipeado (clearFiltersQuick de la app).</summary>
        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _initializing = true;

            foreach (var chip in TypeChips.Children.OfType<ToggleButton>()
                     .Concat(ClanChips.Children.OfType<ToggleButton>())
                     .Concat(RarityChips.Children.OfType<ToggleButton>())
                     .Concat(DesignatorChips.Children.OfType<ToggleButton>()))
                chip.IsChecked = false;
            foreach (var check in SetChecks.Children.OfType<CheckBox>())
                check.IsChecked = false;

            TextSearchBox.Text = "";
            IncludeBannedBox.IsChecked = false;
            OnlyHiddenBox.IsChecked = false;
            QuickHoldingsBtn.IsChecked = false;
            QuickFavBtn.IsChecked = false;
            QuickHiddenBtn.IsChecked = false;
            QuickListBtn.IsChecked = false; // la selección de mazo/lista se conserva; el filtro se apaga
            _keywords.Clear();
            _negKeywords.Clear();
            KwChips.Children.Clear();
            NegKwChips.Children.Clear();
            KwCombo.Text = "";
            NegKwCombo.Text = "";
            KwMatchAllBox.IsChecked = true;
            KwActionBox.IsChecked = true;
            KwSubtitleBox.IsChecked = true;

            foreach (var box in new[] { _forceMin, _forceMax, _chiMin, _chiMax, _costMin, _costMax,
                                        _honorMin, _honorMax, _pHonorMin, _pHonorMax, _focusMin, _focusMax })
                box.Text = "";
            foreach (var check in new[] { _forceSpecial, _costSpecial, _honorNeg, _pHonorNeg })
                check.IsChecked = false;

            SortCombo.SelectedIndex = 0;

            _initializing = false;
            Recompute();
        }

        // ------------------------------------------------------------------ keywords

        private void KwAdd_Click(object sender, RoutedEventArgs e) => AddKeyword(KwCombo, _keywords, KwChips);
        private void NegKwAdd_Click(object sender, RoutedEventArgs e) => AddKeyword(NegKwCombo, _negKeywords, NegKwChips);

        private void KwCombo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { AddKeyword(KwCombo, _keywords, KwChips); e.Handled = true; }
        }

        private void NegKwCombo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { AddKeyword(NegKwCombo, _negKeywords, NegKwChips); e.Handled = true; }
        }

        /// <summary>
        /// Autocomplete por "contiene" (como las sugerencias de la app): filtra los códigos con
        /// el fragmento tipeado y abre el dropdown. Con match exacto no lo reabre (evita que el
        /// dropdown vuelva a saltar después de elegir una sugerencia).
        /// </summary>
        private void KwComboTextChanged(ComboBox combo)
        {
            if (_kwUpdating || _initializing) return;
            _kwUpdating = true;
            try
            {
                var text = combo.Text ?? "";
                var editBox = combo.Template?.FindName("PART_EditableTextBox", combo) as TextBox;
                var caret = editBox?.CaretIndex ?? text.Length;

                var matches = text.Length == 0
                    ? _allKwCodes
                    : _allKwCodes.Where(k => k.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                combo.SelectedItem = null;
                combo.ItemsSource = matches;
                combo.Text = text;

                var exact = matches.Any(m => string.Equals(m, text, StringComparison.OrdinalIgnoreCase));
                combo.IsDropDownOpen = text.Length > 0 && matches.Count > 0 && !exact;

                // El caret se restaura DESPUÉS de tocar el desplegable, no antes: al abrirlo, el
                // ComboBox re-emite su texto sobre el editor y lo deja **entero seleccionado**, así
                // que la tecla siguiente lo reemplazaba en vez de agregarse (se tipeaba "bushi" y
                // quedaba "ushi"). Colapsar el caret acá deshace esa selección.
                if (editBox != null) editBox.Select(Math.Min(caret, text.Length), 0);
            }
            finally
            {
                _kwUpdating = false;
            }
        }

        private void AddKeyword(ComboBox combo, List<string> list, Panel chips)
        {
            var value = (combo.Text ?? "").Trim();
            combo.Text = "";
            combo.IsDropDownOpen = false;
            if (value.Length == 0) return;
            AddKeywordCore(list, chips, value);
            Recompute();
        }

        /// <summary>Agrega la keyword y su chip removible, sin recomputar (para lote/preset).</summary>
        private void AddKeywordCore(List<string> list, Panel chips, string value)
        {
            if (value.Length == 0 || list.Contains(value)) return;
            list.Add(value);
            var chip = new Button
            {
                Content = value,
                Tag = value,
                Style = (Style)FindResource("RemovableChip"),
            };
            chip.Click += (s, e) =>
            {
                list.Remove((string)chip.Tag);
                chips.Children.Remove(chip);
                Recompute();
            };
            chips.Children.Add(chip);
        }

        // ------------------------------------------------------------------ teclado

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            SyncAddModifier();
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                NameBox.Focus();
                NameBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // "Tabs" a la desktop: otra ventana del Buscador sobre el mismo catálogo
                new SearchWindow(_catalogRef, _installPathRef).Show();
                e.Handled = true;
            }
            else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // El botón "Mazos" salió de la barra (era el gemelo del nombre del mazo, que ya
                // abre la ventana). Esto cubre el caso en que la barra está achicada y el bloque
                // del mazo activo no se ve.
                DecksBtn_Click(sender, e);
                e.Handled = true;
            }
        }

        /// <summary>Activa el chip cuyo Tag coincide (exacto, o prefijo si es único).</summary>
        private static bool ToggleChipByValue(Panel panel, string value)
        {
            var chips = panel.Children.OfType<ToggleButton>().ToList();
            var match = chips.FirstOrDefault(c =>
                string.Equals((string)c.Tag, value, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                var byPrefix = chips.Where(c =>
                    ((string)c.Tag).StartsWith(value, StringComparison.OrdinalIgnoreCase)).ToList();
                if (byPrefix.Count == 1) match = byPrefix[0];
            }
            if (match == null) return false;
            match.IsChecked = true;
            return true;
        }

        private void NameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                NameBox.Text = "";
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                // Confirmación de los modos operador: k. agrega keyword, c./t. togglean el chip
                var mode = ParseBar(out var value);
                value = value.Trim();
                if (value.Length == 0) return;
                switch (mode)
                {
                    case BarMode.Keyword:
                        AddKeywordCore(_keywords, KwChips, value);
                        NameBox.Text = "";
                        Recompute();
                        e.Handled = true;
                        break;
                    case BarMode.Clan:
                        if (ToggleChipByValue(ClanChips, value)) NameBox.Text = "";
                        e.Handled = true;
                        break;
                    case BarMode.Type:
                        if (ToggleChipByValue(TypeChips, value)) NameBox.Text = "";
                        e.Handled = true;
                        break;
                }
            }
            else if (e.Key == Key.Down && ResultsList.Visibility == Visibility.Visible && ResultsList.Items.Count > 0)
            {
                if (ResultsList.SelectedIndex < 0) ResultsList.SelectedIndex = 0;
                var item = ResultsList.ItemContainerGenerator.ContainerFromIndex(ResultsList.SelectedIndex) as ListViewItem;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                item?.Focus();
                e.Handled = true;
            }
        }

        // ------------------------------------------------------------------ presets de filtros

        private void RefreshPresetCombo(string selectName)
        {
            var names = _presets.Names;
            PresetCombo.ItemsSource = names;
            if (selectName != null && names.Contains(selectName)) PresetCombo.SelectedItem = selectName;
        }

        private void PresetSave_Click(object sender, RoutedEventArgs e) => SavePresetFromBox();
        private void PresetNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { SavePresetFromBox(); e.Handled = true; }
        }

        private void SavePresetFromBox()
        {
            var name = (PresetNameBox.Text ?? "").Trim();
            if (name.Length == 0) return;
            _presets.Save(name, BuildCriteria());
            PresetNameBox.Text = "";
            RefreshPresetCombo(name);
        }

        private void PresetLoad_Click(object sender, RoutedEventArgs e)
        {
            var name = PresetCombo.SelectedItem as string;
            var criteria = name != null ? _presets.Get(name) : null;
            if (criteria != null) ApplyCriteriaToControls(criteria);
        }

        private void PresetDelete_Click(object sender, RoutedEventArgs e)
        {
            var name = PresetCombo.SelectedItem as string;
            if (name == null) return;
            _presets.Delete(name);
            RefreshPresetCombo(null);
        }

        /// <summary>Vuelca un criteria guardado sobre todos los controles (inverso de BuildCriteria).</summary>
        private void ApplyCriteriaToControls(CardSearchCriteria c)
        {
            _initializing = true;

            NameBox.Text = c.Name ?? "";
            TextSearchBox.Text = c.Text ?? "";
            SetChipsByTags(TypeChips, c.Types);
            SetChipsByTags(ClanChips, c.Clans);
            // Los chips de rareza son mayúsculas; el criteria guardado trae ambas variantes
            SetChipsByTags(RarityChips, c.Rarities.Select(r => r.ToUpperInvariant()).Distinct().ToList());
            SetChipsByTags(DesignatorChips, c.Designators);
            foreach (var check in SetChecks.Children.OfType<CheckBox>())
                check.IsChecked = c.Sets.Contains((string)check.Tag, StringComparer.Ordinal);

            _keywords.Clear();
            KwChips.Children.Clear();
            foreach (var kw in c.Keywords) AddKeywordCore(_keywords, KwChips, kw);
            _negKeywords.Clear();
            NegKwChips.Children.Clear();
            foreach (var kw in c.NegativeKeywords) AddKeywordCore(_negKeywords, NegKwChips, kw);
            KwMatchAllBox.IsChecked = c.KeywordMatchAll;
            KwActionBox.IsChecked = c.IncludeActionKeywords;
            KwSubtitleBox.IsChecked = c.IncludeSubtitleKeywords;

            IncludeBannedBox.IsChecked = c.IncludeBanned;
            QuickHoldingsBtn.IsChecked = c.OnlyInitialHoldings;
            QuickFavBtn.IsChecked = c.OnlyFavorites;
            QuickHiddenBtn.IsChecked = c.IncludeHidden;
            OnlyHiddenBox.IsChecked = c.OnlyHidden;

            _forceMin.Text = c.ForceMin?.ToString() ?? "";
            _forceMax.Text = c.ForceMax?.ToString() ?? "";
            _forceSpecial.IsChecked = c.IncludeSpecialForce;
            _chiMin.Text = c.ChiMin?.ToString() ?? "";
            _chiMax.Text = c.ChiMax?.ToString() ?? "";
            _costMin.Text = c.CostMin?.ToString() ?? "";
            _costMax.Text = c.CostMax?.ToString() ?? "";
            _costSpecial.IsChecked = c.IncludeSpecialCost;
            _honorMin.Text = c.HonorReqMin?.ToString() ?? "";
            _honorMax.Text = c.HonorReqMax?.ToString() ?? "";
            _honorNeg.IsChecked = c.IncludeNegativeHonorReq;
            _pHonorMin.Text = c.PersonalHonorMin?.ToString() ?? "";
            _pHonorMax.Text = c.PersonalHonorMax?.ToString() ?? "";
            _pHonorNeg.IsChecked = c.IncludeNegativePersonalHonor;
            _focusMin.Text = c.FocusMin?.ToString() ?? "";
            _focusMax.Text = c.FocusMax?.ToString() ?? "";

            SortCombo.SelectedItem = (SortCombo.ItemsSource as IEnumerable<SortOption>)?
                .FirstOrDefault(o => o.Field == c.SortField && o.Ascending == c.SortAscending)
                ?? SortCombo.Items[0];

            var restored = ResolveSetFromCriteria(c);
            if (restored != null)
            {
                _activeSet = restored;
                _showingDecks = restored.Kind == SetKind.Deck;
                RefreshSetCombo();
            }
            QuickListBtn.IsChecked = restored != null;

            _initializing = false;
            Recompute();
            // Un preset guardado contra un conjunto que después se borró daba "0 resultados" sin
            // explicación: ahora se dice, y el filtro por conjunto queda apagado.
            if (restored == null && c.ListId != null)
                ShowDeckFeedback(Strings.Format("Fmt_SetGone", c.ListId), error: true);
        }

        /// <summary>
        /// Vuelve a resolver el conjunto guardado en un preset. Se guarda kind + nombre (nunca el
        /// id de sesión de un mazo, que cambia en cada arranque); si el conjunto ya no existe,
        /// devuelve null.
        /// </summary>
        private SetChoice ResolveSetFromCriteria(CardSearchCriteria c)
        {
            if (c.ListId == null) return null;
            switch (c.ListKind)
            {
                case "deck":
                    return ChoiceForDeck(_library.Decks.FirstOrDefault(d =>
                        string.Equals(d.Name, c.ListId, StringComparison.OrdinalIgnoreCase)));
                case "hidden":
                    return new SetChoice { Kind = SetKind.Hidden, Name = "hidden", Label = Strings.Get("Search_SetHidden") };
                default: // null o "list": los presets viejos no traían kind y siempre eran listas
                    return ChoiceForList(_library.Lists.FirstOrDefault(d =>
                        string.Equals(d.Name, c.ListId, StringComparison.OrdinalIgnoreCase)));
            }
        }

        private static void SetChipsByTags(Panel panel, List<string> tags)
        {
            foreach (var chip in panel.Children.OfType<ToggleButton>())
                chip.IsChecked = tags.Contains((string)chip.Tag, StringComparer.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ mazo activo

        private void OnActiveDeckChanged()
        {
            RefreshActiveDeckLabel();
            RefreshCellStates();
            UpdateDetailActions();
        }

        /// <summary>Se creó/borró/renombró un mazo o una lista (posiblemente en otra ventana).</summary>
        private void OnDeckListChanged()
        {
            RefreshActiveDeckLabel();
            RefreshSetCombo();
        }

        /// <summary>La lista activa es global: otra ventana pudo haberla cambiado.</summary>
        private void OnActiveListChanged()
        {
            var list = _library.ActiveList;
            // si acá estamos filtrando por un mazo, no se lo pisamos: el mazo elegido como
            // conjunto es una decisión de esta ventana, la lista activa es del ≡
            if (_activeSet?.Kind == SetKind.Deck) return;
            var choice = ChoiceForList(list);
            if (SameSet(choice, _activeSet)) return;
            _activeSet = choice;
            _showingDecks = false;
            RefreshSetCombo();
            RefreshCellStates();
            UpdateDetailActions();
            if (QuickListBtn.IsChecked == true) Recompute();
        }

        private void OnDeckContentChanged(Models.Deck deck)
        {
            if (deck.Id == _library.ActiveDeckId)
            {
                RefreshActiveDeckLabel();
                RefreshCellStates();
            }
            // cambió el conjunto por el que estamos filtrando → cambian los resultados
            if (_activeSet != null && deck.Id == _activeSet.Id)
            {
                RefreshCellStates();
                UpdateDetailActions();
                if (QuickListBtn.IsChecked == true) Recompute();
            }
        }

        /// <summary>
        /// Nombre + conteos del mazo activo en la barra. El Buscador ya no elige mazo: el activo es
        /// el que está seleccionado en la ventana de Mazos, y acá solo se refleja (ADR-042).
        /// Los conteos son los blandos de la app: Dynasty/Fate en rojo si &lt; 40, sin bloquear nada.
        /// </summary>
        private void RefreshActiveDeckLabel()
        {
            var deckId = _library.ActiveDeckId;
            var deck = deckId != null ? _library.Repo.GetDeckById(deckId) : null;
            ActiveDeckCounts.Inlines.Clear();

            if (deck == null)
            {
                ActiveDeckName.Text = Strings.Get("Search_NoDeckSelected");
                ActiveDeckName.Foreground = (Brush)FindResource("TextFaint");
                return;
            }

            ActiveDeckName.Text = deck.Name;
            ActiveDeckName.Foreground = (Brush)FindResource("Gold");

            var red = (Brush)FindResource("ErrorText");
            var total = _library.Repo.TotalCardCount(deckId);
            var dynasty = _library.Repo.DynastyCount(deckId);
            var fate = _library.Repo.FateCount(deckId);

            ActiveDeckCounts.Inlines.Add(new System.Windows.Documents.Run(
                Strings.Format("Fmt_DeckTotal", total) + "  ·  "));
            var dynastyRun = new System.Windows.Documents.Run(
                $"D {dynasty}/{Services.Deck.DeckRepository.SideTarget}");
            if (dynasty < Services.Deck.DeckRepository.SideTarget) dynastyRun.Foreground = red;
            ActiveDeckCounts.Inlines.Add(dynastyRun);
            ActiveDeckCounts.Inlines.Add(new System.Windows.Documents.Run("  ·  "));
            var fateRun = new System.Windows.Documents.Run(
                $"F {fate}/{Services.Deck.DeckRepository.SideTarget}");
            if (fate < Services.Deck.DeckRepository.SideTarget) fateRun.Foreground = red;
            ActiveDeckCounts.Inlines.Add(fateRun);

            // Los slots sin cubrir van al tooltip: en una barra de una línea no entran, pero
            // perderlos del todo sería perder el aviso de "te falta el stronghold".
            var uncovered = _library.Repo.UncoveredRequiredSlots(deckId);
            ActiveDeckCounts.ToolTip = uncovered.Count > 0
                ? Strings.Format("Fmt_DeckMissingSlots",
                    string.Join(", ", uncovered.Select(x => x.Label ?? x.SlotId)))
                : Strings.Get("Search_ActiveDeckCountsTip");
            if (uncovered.Count > 0) ActiveDeckCounts.Inlines.Add(
                new System.Windows.Documents.Run("  ·  !") { Foreground = red });
        }

        private void AddToDeckBtn_Click(object sender, RoutedEventArgs e) => ApplyDeckDelta(_detailCard, +1);
        private void RemoveFromDeckBtn_Click(object sender, RoutedEventArgs e) => ApplyDeckDelta(_detailCard, -1);

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // solo si el doble click cayó sobre una fila (no sobre la cabecera de columna)
            if ((e.OriginalSource as FrameworkElement)?.DataContext is Card card
                && ReferenceEquals(card, ResultsList.SelectedItem))
                ApplyDeckDelta(card, +1);
        }

        /// <summary>
        /// +/− sobre el MAZO ACTIVO, con las validaciones del DeckRepository de la app
        /// (banned/slot/tope). A diferencia de la app (que ignora el resultado), acá se informa.
        /// </summary>
        private void ApplyDeckDelta(Card card, int delta)
        {
            if (card == null) return;
            var deckId = _library.ActiveDeckId;
            if (deckId == null)
            {
                ShowDeckFeedback(Strings.Get("Deck_NoSelection"), error: true);
                return;
            }

            if (delta > 0)
            {
                var result = _library.Repo.AddCard(deckId, card.Id);
                switch (result.Kind)
                {
                    case AddCardResultKind.Success:
                        var qty = _library.Repo.GetDeckCard(deckId, card.Id)?.Quantity ?? 1;
                        ShowDeckFeedback(Strings.Format("Fmt_DeckAdded", card.Name, qty), error: false);
                        break;
                    case AddCardResultKind.Banned:
                        ShowDeckFeedback(Strings.Get("Deck_AddBanned"), error: true);
                        break;
                    case AddCardResultKind.LimitReached:
                        ShowDeckFeedback(Strings.Format("Fmt_DeckLimit", result.Max), error: true);
                        break;
                    case AddCardResultKind.SlotAlreadyCovered:
                        ShowDeckFeedback(Strings.Get("Deck_AddSlotCovered"), error: true);
                        break;
                    default:
                        ShowDeckFeedback(Strings.Get("Deck_AddNotFound"), error: true);
                        break;
                }
            }
            else
            {
                // como el Buscador de la app: al llegar a 0 la fila se borra (sin recorte)
                _library.Repo.RemoveCard(deckId, card.Id, keepRowAtZero: false);
                ShowDeckFeedback(Strings.Format("Fmt_DeckRemoved", card.Name), error: false);
            }
            // el refresco del panel y de las celdas llega por Repo.DeckChanged
        }

        private void ShowDeckFeedback(string message, bool error)
        {
            DeckFeedback.Text = message;
            DeckFeedback.Foreground = (Brush)FindResource(error ? "ErrorText" : "Gold");
            DeckFeedback.Visibility = Visibility.Visible;
            _feedbackTimer.Stop();
            _feedbackTimer.Start();
        }

        private void DecksBtn_Click(object sender, RoutedEventArgs e) =>
            DeckWindow.Open(_catalogRef, _installPathRef, _library.ActiveDeckId);

        // ------------------------------------------------------------------ lista activa (filtro)

        /// <summary>Conjunto activo elegido en la barra (null = sin conjunto).</summary>
        private SetChoice ActiveSet => _activeSet != null && _activeSet.Kind != SetKind.None ? _activeSet : null;

        /// <summary>Lista activa, si el conjunto elegido es una lista (destino del ≡).</summary>
        private Models.Deck ActiveListOrNull =>
            _activeSet?.Kind == SetKind.List ? _library.Repo.GetDeckById(_activeSet.Id) : null;

        private SetChoice ChoiceForList(Models.Deck list) =>
            list == null ? null
                : new SetChoice { Kind = SetKind.List, Id = list.Id, Name = list.Name, Label = list.Name };

        private SetChoice ChoiceForDeck(Models.Deck deck) =>
            deck == null ? null
                : new SetChoice
                {
                    Kind = SetKind.Deck, Id = deck.Id, Name = deck.Name,
                    Label = Strings.Format("Fmt_SetDeckChoice", deck.Name),
                };

        /// <summary>
        /// Rearma el desplegable de conjuntos. Tiene dos niveles porque los mazos suelen ser
        /// muchos y no son lo que se elige habitualmente: el nivel de arriba ofrece las listas
        /// (más favoritas y ocultas) y una entrada "Usar mazo…"; recién esa entrada baja al nivel
        /// de los mazos, con un "volver" para salir.
        /// </summary>
        private void RefreshSetCombo()
        {
            var items = new List<SetChoice>();
            if (_showingDecks)
            {
                items.Add(new SetChoice { Kind = SetKind.BackToLists, Label = Strings.Get("Search_SetBackToLists") });
                items.AddRange(_library.Decks
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(ChoiceForDeck));
            }
            else
            {
                items.Add(new SetChoice { Kind = SetKind.None, Label = Strings.Get("Search_ListFilterAll") });
                items.AddRange(_library.Lists
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(ChoiceForList));
                // Favoritas salió del selector: ya tiene su propio chip "Solo favoritas" y su ★
                // por celda, y como conjunto no aportaba nada que no estuviera a un click.
                items.Add(new SetChoice { Kind = SetKind.Hidden, Name = "hidden", Label = Strings.Get("Search_SetHidden") });
                items.Add(new SetChoice { Kind = SetKind.ShowDecks, Label = Strings.Get("Search_SetUseDeck") });
                items.Add(new SetChoice { Kind = SetKind.NewList, Label = Strings.Get("Search_SetNewList") });
                if (_activeSet?.Kind == SetKind.List)
                    items.Add(new SetChoice { Kind = SetKind.DeleteList, Label = Strings.Get("Search_SetDeleteList") });
            }

            // el conjunto activo se re-referencia contra los ítems nuevos (los objetos cambian
            // en cada rebuild, y el ComboBox selecciona por referencia)
            var current = items.FirstOrDefault(i => SameSet(i, _activeSet)) ?? items[0];
            _listComboSyncing = true;
            SetFilterCombo.ItemsSource = items;
            SetFilterCombo.SelectedItem = current;
            _listComboSyncing = false;
            _activeSet = current.IsAction ? _activeSet : current;
        }

        /// <summary>
        /// Devuelve la selección del combo al conjunto activo. Hace falta después de elegir una
        /// acción: el ítem "＋ Lista nueva…" queda seleccionado en el ComboBox aunque no sea un
        /// conjunto, y sin esto la barra mostraría esa leyenda como si fuera el filtro puesto.
        /// </summary>
        private void ReselectSet()
        {
            var items = SetFilterCombo.ItemsSource as IEnumerable<SetChoice>;
            var current = items?.FirstOrDefault(i => SameSet(i, _activeSet)) ?? items?.FirstOrDefault();
            if (current == null) return;
            _listComboSyncing = true;
            SetFilterCombo.SelectedItem = current;
            _listComboSyncing = false;
        }

        private static bool SameSet(SetChoice a, SetChoice b) =>
            a != null && b != null && a.Kind == b.Kind &&
            string.Equals(a.Name, b.Name, StringComparison.Ordinal);


        private void SetFilterCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing || _listComboSyncing) return;
            var choice = SetFilterCombo.SelectedItem as SetChoice;
            if (choice == null) return;

            if (choice.IsAction)
            {
                if (choice.Kind == SetKind.NewList) { ReselectSet(); ListCreate_Click(sender, e); return; }
                if (choice.Kind == SetKind.DeleteList) { ReselectSet(); ListDelete_Click(sender, e); return; }

                _showingDecks = choice.Kind == SetKind.ShowDecks;
                RefreshSetCombo();
                // el usuario pidió ver la otra lista de opciones: se la abrimos, en vez de
                // obligarlo a un segundo click sobre el mismo combo
                Dispatcher.BeginInvoke(new Action(() => SetFilterCombo.IsDropDownOpen = true),
                                       DispatcherPriority.Input);
                return;
            }

            _activeSet = choice;
            // la lista activa es global (la comparten las ventanas del Buscador); un mazo o las
            // favoritas como conjunto de filtro no cambian a dónde escribe el ≡
            if (choice.Kind == SetKind.List) _library.ActiveListId = choice.Id;
            RefreshCellStates(); // el ≡ de las celdas apunta al conjunto nuevo
            UpdateDetailActions();
            if (QuickListBtn.IsChecked == true) Recompute();
        }

        /// <summary>Toggle en la lista activa (botón ≡ de las celdas de la grilla).</summary>
        private void ToggleInActiveList(Card card)
        {
            if (card == null) return;
            var list = ActiveListOrNull;
            if (list == null)
            {
                ShowDeckFeedback(Strings.Get(_activeSet == null || _activeSet.Kind == SetKind.None
                    ? "Search_NoListSelected" : "Search_SetNotAList"), error: true);
                return;
            }
            var added = _library.Repo.ToggleInList(list.Id, card.Id);
            ShowDeckFeedback(
                Strings.Format(added ? "Fmt_ListAdd2" : "Fmt_ListRemove2", card.Name, list.Name),
                error: false);
            RefreshCellStates();
            if (QuickListBtn.IsChecked == true) Recompute();
        }

        /// <summary>Nueva lista: el nombre se pide con el prompt de la app, no con un campo fijo.</summary>
        private void ListCreate_Click(object sender, RoutedEventArgs e)
        {
            var name = (Dialogs.Prompt(this, Strings.Get("Search_ListCreateTitle"),
                                       Strings.Get("Search_ListCreatePrompt"), "") ?? "").Trim();
            if (name.Length == 0) return;
            var id = _library.CreateList(name);
            // queda como conjunto activo para ir cargándola con el ≡ de las celdas
            _showingDecks = false;
            _activeSet = ChoiceForList(_library.Repo.GetDeckById(id));
            RefreshSetCombo();
            RefreshCellStates();
            UpdateDetailActions();
        }

        /// <summary>Borra la lista activa (la elegida en la barra). Solo listas.</summary>
        private void ListDelete_Click(object sender, RoutedEventArgs e)
        {
            var list = ActiveListOrNull;
            if (list == null) return;
            if (!Dialogs.Confirm(this, Strings.Get("Search_ListDeleteTitle"),
                                 Strings.Format("Fmt_ListDeleteConfirm", list.Name),
                                 Strings.Get("Btn_Delete"))) return;
            _library.DeleteDeck(list.Id);
            _activeSet = null;
            RefreshSetCombo();
            RefreshCellStates();
            if (QuickListBtn.IsChecked == true) Recompute();
        }

        /// <summary>
        /// Cartas del conjunto activo. Es lo único que el motor necesita saber de un conjunto:
        /// de ahí que un mazo, una lista y las favoritas sean intercambiables como filtro.
        /// </summary>
        private IEnumerable<string> ActiveSetCardIds()
        {
            switch (_activeSet?.Kind)
            {
                case SetKind.List:
                case SetKind.Deck:
                    var deck = _library.Repo.GetDeckById(_activeSet.Id);
                    return deck == null
                        ? Enumerable.Empty<string>()
                        : deck.Cards.Where(en => en.Quantity > 0).Select(en => en.CardId);
                case SetKind.Hidden: return _store.Data.Hidden;
                default: return Enumerable.Empty<string>();
            }
        }

        // ---------------------------------------- favoritas / ocultas / acciones de celda

        private void FavBtn_Click(object sender, RoutedEventArgs e) => ToggleFavorite(_detailCard);
        private void HideBtn_Click(object sender, RoutedEventArgs e) => ToggleHidden(_detailCard);

        private void ToggleFavorite(Card card)
        {
            if (card == null) return;
            _store.ToggleFavorite(card.Id);
            UpdateDetailActions();
            RefreshCellStates();
            Recompute(); // por si "Solo favoritas" está activo
        }

        private void ToggleHidden(Card card)
        {
            if (card == null) return;
            _store.ToggleHidden(card.Id);
            UpdateDetailActions();
            RefreshCellStates();
            Recompute(); // por default las ocultas desaparecen del resultado
        }

        private static Card CellCard(object sender) =>
            ((sender as FrameworkElement)?.DataContext as GridCell)?.Card;

        private void CellPlus_Click(object sender, RoutedEventArgs e) => ApplyDeckDelta(CellCard(sender), +1);
        private void CellMinus_Click(object sender, RoutedEventArgs e) => ApplyDeckDelta(CellCard(sender), -1);
        private void CellFav_Click(object sender, RoutedEventArgs e) => ToggleFavorite(CellCard(sender));
        private void CellHide_Click(object sender, RoutedEventArgs e) => ToggleHidden(CellCard(sender));
        private void CellList_Click(object sender, RoutedEventArgs e) => ToggleInActiveList(CellCard(sender));

        /// <summary>
        /// Estado de la barra de acciones de cada celda: cantidad en el mazo activo y flags de
        /// favorita/oculta/lista activa (INPC: solo las celdas realizadas re-renderizan).
        /// </summary>
        private void RefreshCellStates()
        {
            var deck = _library.ActiveDeck;
            var list = ActiveListOrNull;
            foreach (var cell in _gridCells)
            {
                var qty = deck?.Cards.FirstOrDefault(en =>
                    string.Equals(en.CardId, cell.Card.Id, StringComparison.Ordinal))?.Quantity ?? 0;
                cell.DeckQtyText = qty > 0
                    ? qty + "/" + (deck.Type == DeckLibrary.ListType ? 1 : cell.Card.MaxCopies)
                    : "";
                cell.IsFav = _store.Data.Favorites.Contains(cell.Card.Id);
                cell.IsHidden = _store.Data.Hidden.Contains(cell.Card.Id);
                cell.InList = list != null &&
                    list.Cards.Any(en => string.Equals(en.CardId, cell.Card.Id, StringComparison.Ordinal));
            }
        }

        private void UpdateDetailActions()
        {
            var hasCard = _detailCard != null;
            var list = ActiveListOrNull;
            FavBtn.IsEnabled = hasCard;
            HideBtn.IsEnabled = hasCard;
            AddToDeckBtn.IsEnabled = hasCard && _library.ActiveDeckId != null;
            RemoveFromDeckBtn.IsEnabled = AddToDeckBtn.IsEnabled;
            // sin lista activa no hay a dónde agregar: el botón se apaga en vez de avisar al
            // clickear (con un mazo o las favoritas como conjunto, el ≡ no aplica: los mazos
            // tienen sus +/− y las favoritas su ★)
            ListBtn.IsEnabled = hasCard && list != null;
            // El texto dice qué hace el click; el Tag dice en qué estado está la carta. Sin lo
            // segundo la píldora se veía igual puesta que sin poner, y el ★ de la celda sí marca.
            var isFav = hasCard && _store.Data.Favorites.Contains(_detailCard.Id);
            FavBtn.Content = isFav ? Strings.Get("Search_Fav_Remove") : Strings.Get("Search_Fav_Add");
            FavBtn.Tag = isFav ? "on" : null;

            var isHidden = hasCard && _store.Data.Hidden.Contains(_detailCard.Id);
            HideBtn.Content = isHidden ? Strings.Get("Search_Unhide") : Strings.Get("Search_Hide");
            HideBtn.Tag = isHidden ? "off-warn" : null;

            var inList = hasCard && list != null &&
                list.Cards.Any(en => string.Equals(en.CardId, _detailCard.Id, StringComparison.Ordinal));
            ListBtn.Content = inList ? Strings.Get("Search_ListRemove") : Strings.Get("Search_ListAdd");
            ListBtn.Tag = inList ? "on" : null;
        }

        private void ListBtn_Click(object sender, RoutedEventArgs e)
        {
            ToggleInActiveList(_detailCard);
            UpdateDetailActions();
        }

        // ------------------------------------------------------------------ detalle

        private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var card = ResultsList.SelectedItem as Card;
            if (card == null)
            {
                _detailCard = null;
                Detail.Clear();
                UpdateDetailActions();
                return;
            }
            ShowDetail(card);
        }

        /// <summary>Muestra la carta y salta a la pestaña Carta (gesto explícito de "ver carta").</summary>
        private void ShowDetail(Card card, string preferredPath = null)
        {
            ShowDetailSilent(card, preferredPath);
        }

        /// <summary>Actualiza el detalle sin cambiar la pestaña (filas del panel de mazo).</summary>
        private void ShowDetailSilent(Card card, string preferredPath = null)
        {
            _detailCard = card;
            Detail.ShowCard(card, _resolver.ResolveCandidates(card, preferredPath, fallbackChain: true));
            UpdateDetailActions();
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    /// <summary>
    /// Celda de la grilla de miniaturas. INPC para la selección y el estado de la barra de
    /// acciones (las celdas se recrean por búsqueda). El thumbnail se decodifica lazy vía
    /// Binding IsAsync y se sirve desde <see cref="ThumbnailCache"/> (LRU compartida y
    /// acotada): al reciclar contenedores en scroll rápido, una celda ya vista vuelve al
    /// instante en vez de re-decodificar y parpadear en blanco.
    /// </summary>
    public class GridCell : INotifyPropertyChanged
    {
        public Card Card { get; set; }
        public string ImagePath { get; set; }
        public List<string> CandidatePaths { get; set; } = new List<string>();

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; Notify(nameof(IsSelected)); } }
        }

        // ----- estado de la barra de acciones (lo refresca SearchWindow.RefreshCellStates) -----

        private string _deckQtyText = "";
        /// <summary>"2/3" si la carta está en el mazo activo; "" si no.</summary>
        public string DeckQtyText
        {
            get => _deckQtyText;
            set { if (_deckQtyText != value) { _deckQtyText = value; Notify(nameof(DeckQtyText)); } }
        }

        private bool _isFav;
        public bool IsFav
        {
            get => _isFav;
            set { if (_isFav != value) { _isFav = value; Notify(nameof(IsFav)); } }
        }

        private bool _isHidden;
        public bool IsHidden
        {
            get => _isHidden;
            set { if (_isHidden != value) { _isHidden = value; Notify(nameof(IsHidden)); } }
        }

        private bool _inList;
        /// <summary>Está en la lista activa elegida en la barra.</summary>
        public bool InList
        {
            get => _inList;
            set { if (_inList != value) { _inList = value; Notify(nameof(InList)); } }
        }

        private void Notify(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>Se lee vía Binding IsAsync (thread pool): la decodificación no bloquea la UI.</summary>
        public BitmapImage Thumb => ThumbnailCache.Get(CandidatePaths);

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Caché LRU acotada de miniaturas decodificadas, compartida por todas las celdas (y
    /// ventanas). Reemplaza el <see cref="WeakReference"/> por-celda: el weak-ref dejaba que
    /// el GC se llevara los thumbnails y forzaba re-decode, que con contenedores reciclados
    /// se veía como celdas en blanco al volver a scrollear. El LRU los retiene con un tope
    /// fijo (~<see cref="Capacity"/> × ~0.6 MB a 340px ≈ 250 MB en el peor caso). Thread-safe:
    /// el Binding IsAsync lee <see cref="Get"/> desde el thread pool.
    /// </summary>
    internal static class ThumbnailCache
    {
        // Una sola resolución para los cuatro niveles de zoom, a propósito. Variarla por nivel
        // obligaba a vaciar el caché en cada cambio —y a redecodificar desde disco todo lo
        // visible—, que es exactamente lo que hacía que el zoom se trabara. 400 px alcanza para el
        // nivel más grande (302 DIP → 1.32x a 100 %, 1.06x a 125 %).
        private const int DecodeWidth = 400;

        // Dos niveles, porque el catálogo tiene ~6400 cartas y una miniatura decodificada pesa
        // ~0,87 MB: un LRU fuerte lo bastante grande para no redecodificar al scrollear costaría
        // cientos de MB retenidos para siempre.
        //   · fuerte: las últimas StrongCapacity (dos o tres pantallas en cualquier zoom) → ~104 MB
        //     tope, y son las que garantizan scroll fluido en las dos direcciones;
        //   · débil: el resto sigue accesible **mientras el GC no las haya reclamado**, así que
        //     volver a una zona ya vista suele no tocar disco, pero bajo presión de memoria el
        //     runtime se las lleva en vez de que el proceso siga creciendo.
        // Nunca es peor que un LRU puro del mismo tamaño: lo desalojado no se pierde, se degrada.
        private const int StrongCapacity = 120;

        private static readonly object _gate = new object();
        private static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapImage>>> _strong =
            new Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapImage>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<KeyValuePair<string, BitmapImage>> _lru =
            new LinkedList<KeyValuePair<string, BitmapImage>>();
        private static readonly Dictionary<string, WeakReference<BitmapImage>> _weak =
            new Dictionary<string, WeakReference<BitmapImage>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Primera imagen existente de la cadena de candidatos (preserva la prioridad del
        /// resolver): si el candidato ya está en alguno de los dos niveles se devuelve sin tocar
        /// disco; si no, se decodifica el primero que exista.
        /// </summary>
        public static BitmapImage Get(IEnumerable<string> candidatePaths)
        {
            foreach (var path in candidatePaths)
            {
                var cached = Lookup(path);
                if (cached != null) return cached;

                if (!File.Exists(path)) continue;
                var bmp = Decode(path);
                if (bmp == null) continue; // archivo corrupto → probar el siguiente candidato
                Store(path, bmp);
                return bmp;
            }
            return null;
        }

        private static BitmapImage Lookup(string path)
        {
            lock (_gate)
            {
                if (_strong.TryGetValue(path, out var hit))
                {
                    _lru.Remove(hit);
                    _lru.AddFirst(hit);
                    return hit.Value.Value;
                }
                // sigue viva porque la referencia una celda realizada, o porque el GC no pasó:
                // se recupera al nivel fuerte, que es donde se está mirando ahora
                if (_weak.TryGetValue(path, out var weak) && weak.TryGetTarget(out var alive))
                {
                    _weak.Remove(path);
                    Promote(path, alive);
                    return alive;
                }
                return null;
            }
        }

        private static void Store(string path, BitmapImage bmp)
        {
            lock (_gate)
            {
                if (_strong.ContainsKey(path)) return;
                _weak.Remove(path);
                Promote(path, bmp);
            }
        }

        /// <summary>Entra al nivel fuerte; lo que se pasa de tope baja al débil. Bajo el lock.</summary>
        private static void Promote(string path, BitmapImage bmp)
        {
            var node = _lru.AddFirst(new KeyValuePair<string, BitmapImage>(path, bmp));
            _strong[path] = node;
            while (_strong.Count > StrongCapacity)
            {
                var oldest = _lru.Last;
                _lru.RemoveLast();
                _strong.Remove(oldest.Value.Key);
                _weak[oldest.Value.Key] = new WeakReference<BitmapImage>(oldest.Value.Value);
            }
            // el diccionario débil no se purga en cada inserción (sería recorrerlo entero): se
            // limpia cuando junta bastante basura, que es cuando empieza a costar memoria propia
            if (_weak.Count > StrongCapacity * 8) PurgeWeak();
        }

        private static void PurgeWeak()
        {
            var dead = _weak.Where(kv => !kv.Value.TryGetTarget(out _)).Select(kv => kv.Key).ToList();
            foreach (var key in dead) _weak.Remove(key);
        }

        private static BitmapImage Decode(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = DecodeWidth;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Hex de rules.json → SolidColorBrush (fallback White, como parseHexColor de la app).
    /// ConverterParameter opcional = alfa ("0.15" para el fondo de la pastilla).
    /// </summary>
    public class HexBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var color = ColorUtil.ParseColor(value as string) ?? Colors.White;
            if (parameter is string s &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha))
                color = ColorUtil.WithAlpha(color, alpha);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
