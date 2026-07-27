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

        private const double GridCellOuterWidth = 240; // 226 + margen 5+5 + borde

        private readonly CardSearchEngine _engine;
        private readonly CardImageResolver _resolver;
        private readonly UserCardDataStore _store = UserCardDataStore.Load();
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

        private class SortOption
        {
            public string Label;
            public SortField Field;
            public override string ToString() => Label;
        }

        /// <summary>Ítem del selector de lista activa de la barra (null = todas las cartas).</summary>
        private class ListChoice
        {
            public string Name; // null = sin lista
            public string Label;
            public override string ToString() => Label;
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

            Title = Strings.Format("Fmt_SearchWindowTitle", catalog.CardsVersion ?? "?");

            BuildChips(TypeChips, TypeValues.Select(t => Tuple.Create(Capitalize(t), t)));
            BuildChips(ClanChips, ClanValues.Select(c => Tuple.Create(Strings.Get("Clan_" + c), c)));
            BuildChips(RarityChips, RarityValues.Select(r => Tuple.Create(r, r)),
                       tooltipFor: r => Strings.Get("Rarity_" + r));
            BuildChips(DesignatorChips, DesignatorValues.Select(d => Tuple.Create(d, d)));
            BuildSetChecks(catalog);
            BuildStatsRows();

            _allKwCodes = catalog.KeywordCodes.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            KwCombo.ItemsSource = _allKwCodes;
            NegKwCombo.ItemsSource = _allKwCodes;
            // Autocomplete estilo app: matching por "contiene" mientras se tipea
            KwCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => KwComboTextChanged(KwCombo)));
            NegKwCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => KwComboTextChanged(NegKwCombo)));

            SortCombo.ItemsSource = new[]
            {
                // los 6 de la app primero; después los extras del launcher (cabeceras)
                new SortOption { Label = Strings.Get("Search_Sort_Name"), Field = SortField.NAME },
                new SortOption { Label = Strings.Get("Search_Sort_Cost"), Field = SortField.COST },
                new SortOption { Label = Strings.Get("Search_Sort_Force"), Field = SortField.FORCE },
                new SortOption { Label = Strings.Get("Search_Sort_Chi"), Field = SortField.CHI },
                new SortOption { Label = Strings.Get("Search_Sort_PHonor"), Field = SortField.PERSONAL_HONOR },
                new SortOption { Label = Strings.Get("Search_Sort_Set"), Field = SortField.SET },
                new SortOption { Label = Strings.Get("Search_Sort_Type"), Field = SortField.TYPE },
                new SortOption { Label = Strings.Get("Search_Sort_Clan"), Field = SortField.CLAN },
                new SortOption { Label = Strings.Get("Search_Sort_Rarity"), Field = SortField.RARITY },
                new SortOption { Label = Strings.Get("Search_Sort_Honor"), Field = SortField.HONOR_REQ },
                new SortOption { Label = Strings.Get("Search_Sort_Focus"), Field = SortField.FOCUS },
            };
            SortCombo.SelectedIndex = 0;

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
            _library.Repo.DeckListChanged += OnDeckListChanged;
            _library.Repo.DeckChanged += OnDeckContentChanged;
            Closed += (s, e) =>
            {
                _library.ActiveDeckChanged -= OnActiveDeckChanged;
                _library.Repo.DeckListChanged -= OnDeckListChanged;
                _library.Repo.DeckChanged -= OnDeckContentChanged;
            };

            RefreshListFilterCombo();
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
            _results = _engine.Search(_lastCriteria, _store.Data);

            var selectedId = (ResultsList.SelectedItem as Card)?.Id ?? _selectedCell?.Card.Id;
            ResultsList.ItemsSource = _results;
            ResultCount.Text = Strings.Format("Fmt_SearchResults", _results.Count);
            UpdateSortDirLabel();

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

            // "Solo lista": muestra únicamente las cartas de la lista activa (p. ej. armar el
            // mazo de ninjas desde la lista de ninjas). La lista se elige en la barra.
            var listId = QuickListBtn.IsChecked == true ? SelectedListName : null;

            return new CardSearchCriteria
            {
                Name = barMode == BarMode.Name ? barValue : "",
                Text = barMode == BarMode.Text ? barValue : (TextSearchBox.Text ?? ""),
                ListId = listId,
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
                SortField = (SortCombo.SelectedItem as SortOption)?.Field ?? SortField.NAME,
                SortAscending = SortDirBtn.IsChecked != true,
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

        private void UpdateSortDirLabel()
        {
            var desc = SortDirBtn.IsChecked == true;
            SortDirBtn.Content = desc ? "↓" : "↑";
        }

        /// <summary>Click en una cabecera de la tabla: ordena por esa columna; repetir invierte.</summary>
        private void ResultsHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header?.Column == null || !_headerSort.TryGetValue(header.Column, out var field)) return;
            var current = (SortCombo.SelectedItem as SortOption)?.Field ?? SortField.NAME;
            _initializing = true;
            if (current == field)
                SortDirBtn.IsChecked = SortDirBtn.IsChecked != true;
            else
            {
                SortCombo.SelectedItem = (SortCombo.ItemsSource as IEnumerable<SortOption>)
                    ?.FirstOrDefault(o => o.Field == field);
                SortDirBtn.IsChecked = false;
            }
            _initializing = false;
            Recompute();
        }

        // ------------------------------------------------------------------ paneles colapsables

        /// <summary>
        /// Colapsa/expande los paneles laterales desde los rieles; el centro absorbe el ancho
        /// liberado. Las columnas de los rieles no se tocan: son la única forma de reabrir.
        /// </summary>
        private void PaneToggle_Click(object sender, RoutedEventArgs e)
        {
            var filtersOn = FiltersPaneBtn.IsChecked == true;
            FiltersPanel.Visibility = filtersOn ? Visibility.Visible : Visibility.Collapsed;
            FiltersCol.Width = new GridLength(filtersOn ? 270 : 0);

            var detailOn = DetailPaneBtn.IsChecked == true;
            DetailPanelBorder.Visibility = detailOn ? Visibility.Visible : Visibility.Collapsed;
            DetailCol.Width = new GridLength(detailOn ? 405 : 0);
        }

        // ------------------------------------------------------------------ vista lista / grilla

        private void ViewToggle_Click(object sender, RoutedEventArgs e)
        {
            var grid = ReferenceEquals(sender, ViewGridBtn);
            // exclusividad manual (par de chips segmentados)
            ViewGridBtn.IsChecked = grid;
            ViewListBtn.IsChecked = !grid;
            ExpandPrintsBtn.IsEnabled = grid;

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

        private void Window_KeyUp(object sender, KeyEventArgs e) => SyncAddModifier();

        // Sin esto el modo queda pegado si se suelta Ctrl con la ventana en segundo plano
        // (p.ej. Alt+Tab con Ctrl apretado): el KeyUp nunca llega.
        private void Window_Deactivated(object sender, EventArgs e) => IsAddModifierDown = false;

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
            SortDirBtn.IsChecked = false;

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
                if (editBox != null) editBox.CaretIndex = Math.Min(caret, text.Length);

                var exact = matches.Any(m => string.Equals(m, text, StringComparison.OrdinalIgnoreCase));
                combo.IsDropDownOpen = text.Length > 0 && matches.Count > 0 && !exact;
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
                .FirstOrDefault(o => o.Field == c.SortField) ?? SortCombo.Items[0];
            SortDirBtn.IsChecked = !c.SortAscending;

            if (c.ListId != null && _store.Data.Lists.ContainsKey(c.ListId))
            {
                SelectListInFilterCombo(c.ListId);
                QuickListBtn.IsChecked = true;
            }
            else QuickListBtn.IsChecked = false;

            _initializing = false;
            Recompute();
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

        private void OnDeckListChanged() => RefreshActiveDeckLabel();

        private void OnDeckContentChanged(Models.Deck deck)
        {
            if (deck.Id != _library.ActiveDeckId) return;
            RefreshActiveDeckLabel();
            RefreshCellStates();
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

        /// <summary>Lista activa elegida en la barra (null = todas las cartas).</summary>
        private string SelectedListName => (ListFilterCombo.SelectedItem as ListChoice)?.Name;

        /// <summary>Rearma el selector de lista activa ("Todas las cartas" + listas).</summary>
        private void RefreshListFilterCombo(string selectName = null)
        {
            var previous = selectName ?? SelectedListName;
            var items = new List<ListChoice>
            {
                new ListChoice { Name = null, Label = Strings.Get("Search_ListFilterAll") },
            };
            items.AddRange(_store.ListNames.Select(n => new ListChoice { Name = n, Label = n }));

            _listComboSyncing = true;
            ListFilterCombo.ItemsSource = items;
            ListFilterCombo.SelectedItem = items.FirstOrDefault(i => i.Name == previous) ?? items[0];
            _listComboSyncing = false;
            ListDeleteBtn.IsEnabled = SelectedListName != null;
        }

        private void SelectListInFilterCombo(string name)
        {
            var match = (ListFilterCombo.ItemsSource as IEnumerable<ListChoice>)
                ?.FirstOrDefault(i => i.Name == name);
            if (match == null) return;
            _listComboSyncing = true;
            ListFilterCombo.SelectedItem = match;
            _listComboSyncing = false;
        }

        private void ListFilterCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing || _listComboSyncing) return;
            RefreshCellStates(); // el botón de lista de las celdas apunta a la lista nueva
            if (QuickListBtn.IsChecked == true) Recompute();
        }

        /// <summary>Toggle en la lista activa (botón ≡ de las celdas de la grilla).</summary>
        private void ToggleInActiveList(Card card)
        {
            if (card == null) return;
            var list = SelectedListName;
            if (list == null)
            {
                ShowDeckFeedback(Strings.Get("Search_NoListSelected"), error: true);
                return;
            }
            var added = _store.ToggleInList(list, card.Id);
            ShowDeckFeedback(
                Strings.Format(added ? "Fmt_ListAdd2" : "Fmt_ListRemove2", card.Name, list),
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
            // Si ya existía, CreateList devuelve false: en vez de no hacer nada en silencio,
            // la dejamos activa igual — el usuario pidió trabajar sobre esa lista.
            _store.CreateList(name);
            // queda como lista activa para ir cargándola con el botón de las celdas
            RefreshListFilterCombo(name);
            RefreshCellStates();
        }

        /// <summary>Borra la lista activa (la elegida en la barra).</summary>
        private void ListDelete_Click(object sender, RoutedEventArgs e)
        {
            var list = SelectedListName;
            if (list == null) return;
            if (!Dialogs.Confirm(this, Strings.Get("Search_ListDeleteTitle"),
                                 Strings.Format("Fmt_ListDeleteConfirm", list),
                                 Strings.Get("Btn_Delete"))) return;
            _store.DeleteList(list);
            RefreshListFilterCombo();
            RefreshCellStates();
            if (QuickListBtn.IsChecked == true) Recompute();
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
            var list = SelectedListName;
            foreach (var cell in _gridCells)
            {
                var qty = deck?.Cards.FirstOrDefault(en =>
                    string.Equals(en.CardId, cell.Card.Id, StringComparison.Ordinal))?.Quantity ?? 0;
                cell.DeckQtyText = qty > 0
                    ? qty + "/" + (deck.Type == "list" ? 1 : cell.Card.MaxCopies)
                    : "";
                cell.IsFav = _store.Data.Favorites.Contains(cell.Card.Id);
                cell.IsHidden = _store.Data.Hidden.Contains(cell.Card.Id);
                cell.InList = list != null && _store.ListContains(list, cell.Card.Id);
            }
        }

        private void UpdateDetailActions()
        {
            var hasCard = _detailCard != null;
            var list = SelectedListName;
            FavBtn.IsEnabled = hasCard;
            HideBtn.IsEnabled = hasCard;
            AddToDeckBtn.IsEnabled = hasCard && _library.ActiveDeckId != null;
            RemoveFromDeckBtn.IsEnabled = AddToDeckBtn.IsEnabled;
            // sin lista activa no hay a dónde agregar: el botón se apaga en vez de avisar al clickear
            ListBtn.IsEnabled = hasCard && list != null;
            FavBtn.Content = hasCard && _store.Data.Favorites.Contains(_detailCard.Id)
                ? Strings.Get("Search_Fav_Remove") : Strings.Get("Search_Fav_Add");
            HideBtn.Content = hasCard && _store.Data.Hidden.Contains(_detailCard.Id)
                ? Strings.Get("Search_Unhide") : Strings.Get("Search_Hide");
            ListBtn.Content = hasCard && list != null && _store.ListContains(list, _detailCard.Id)
                ? Strings.Get("Search_ListRemove") : Strings.Get("Search_ListAdd");
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
        private const int Capacity = 400;
        private static readonly object _gate = new object();
        private static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapImage>>> _map =
            new Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapImage>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<KeyValuePair<string, BitmapImage>> _lru =
            new LinkedList<KeyValuePair<string, BitmapImage>>();

        /// <summary>
        /// Primera imagen existente de la cadena de candidatos (preserva la prioridad del
        /// resolver): si el candidato ya está cacheado se devuelve sin tocar disco; si no,
        /// se decodifica el primero que exista en disco.
        /// </summary>
        public static BitmapImage Get(IEnumerable<string> candidatePaths)
        {
            foreach (var path in candidatePaths)
            {
                lock (_gate)
                {
                    if (_map.TryGetValue(path, out var hit))
                    {
                        _lru.Remove(hit);
                        _lru.AddFirst(hit);
                        return hit.Value.Value;
                    }
                }

                if (!File.Exists(path)) continue;
                var bmp = Decode(path);
                if (bmp == null) continue; // archivo corrupto → probar el siguiente candidato

                lock (_gate)
                {
                    if (!_map.ContainsKey(path))
                    {
                        var node = _lru.AddFirst(new KeyValuePair<string, BitmapImage>(path, bmp));
                        _map[path] = node;
                        while (_map.Count > Capacity)
                        {
                            var oldest = _lru.Last;
                            _lru.RemoveLast();
                            _map.Remove(oldest.Value.Key);
                        }
                    }
                }
                return bmp;
            }
            return null;
        }

        private static BitmapImage Decode(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 340; // ~ancho de celda (226) en pantallas 1.5x
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
