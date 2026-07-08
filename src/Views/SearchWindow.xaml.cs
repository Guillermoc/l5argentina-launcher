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
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Resources;
using L5ArgentinaLauncher.Services.Search;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Buscador de cartas. Patrón desktop estándar: panel de filtros persistente (aplican en
    /// vivo, sin modal ni botón Aplicar — el motor en memoria responde al instante), resultados
    /// en tabla o grilla de miniaturas (toggle Lista/Grilla) y panel de detalle con la imagen.
    /// La semántica de los filtros es la del motor portado de la app Android (CardSearchEngine);
    /// los ajustes de UX documentados se hacen acá (p. ej. rareza manda ambas variantes de case).
    /// Favoritas/ocultas persisten en usercards.json (UserCardDataStore). Fuera de eso es solo
    /// lectura: no toca red ni la instalación.
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

        private const double GridCellOuterWidth = 202; // 188 + margen 5+5 + borde

        private readonly CardSearchEngine _engine;

        /// <summary>
        /// Raíces contra las que se resuelven los paths relativos de imagen
        /// (images/cards/&lt;set&gt;/&lt;id&gt;.jpg): la instalación configurada de Sun and Moon y,
        /// como fallback, la carpeta del propio exe (caso típico: el launcher copiado dentro
        /// de la carpeta del juego).
        /// </summary>
        private readonly List<string> _imageRoots = new List<string>();
        private readonly UserCardDataStore _store = UserCardDataStore.Load();
        private readonly List<string> _keywords = new List<string>();
        private readonly List<string> _negKeywords = new List<string>();
        private List<string> _allKwCodes = new List<string>();
        private bool _initializing = true;
        private bool _kwUpdating;

        private List<Card> _results = new List<Card>();
        private CardSearchCriteria _lastCriteria = new CardSearchCriteria();
        private List<GridCell> _gridCells = new List<GridCell>();
        private GridCell _selectedCell;
        private int _gridCols;
        private bool _gridDirty = true;
        private Card _detailCard;

        private TextBox _forceMin, _forceMax, _chiMin, _chiMax, _costMin, _costMax,
                        _honorMin, _honorMax, _pHonorMin, _pHonorMax, _focusMin, _focusMax;
        private CheckBox _forceSpecial, _costSpecial, _honorNeg, _pHonorNeg;

        private class SortOption
        {
            public string Label;
            public SortField Field;
            public override string ToString() => Label;
        }

        public SearchWindow(CardCatalog catalog, string installPath)
        {
            InitializeComponent();
            _engine = new CardSearchEngine(catalog);
            if (!string.IsNullOrEmpty(installPath)) _imageRoots.Add(installPath);
            try
            {
                var exeDir = AppContext.BaseDirectory;
                if (!string.IsNullOrEmpty(exeDir) && !_imageRoots.Contains(exeDir)) _imageRoots.Add(exeDir);
            }
            catch { /* sin fallback de exe */ }

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
                new SortOption { Label = Strings.Get("Search_Sort_Name"), Field = SortField.NAME },
                new SortOption { Label = Strings.Get("Search_Sort_Cost"), Field = SortField.COST },
                new SortOption { Label = Strings.Get("Search_Sort_Force"), Field = SortField.FORCE },
                new SortOption { Label = Strings.Get("Search_Sort_Chi"), Field = SortField.CHI },
                new SortOption { Label = Strings.Get("Search_Sort_PHonor"), Field = SortField.PERSONAL_HONOR },
                new SortOption { Label = Strings.Get("Search_Sort_Set"), Field = SortField.SET },
            };
            SortCombo.SelectedIndex = 0;

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
            // Con WindowStyle=None, maximizado desborda el borde de resize: compensar con margen.
            var maximized = WindowState == WindowState.Maximized;
            RootLayout.Margin = new Thickness(maximized ? 7 : 0);
            MaxButton.Content = maximized ? "❐" : "□";
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
            minBox.TextChanged += Filter_Changed;
            maxBox.TextChanged += Filter_Changed;
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
        private void Filter_Changed(object sender, TextChangedEventArgs e) => Recompute();
        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => Recompute();

        private void Recompute()
        {
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

        private CardSearchCriteria BuildCriteria()
        {
            var rarities = CheckedTags(RarityChips)
                // La app filtra rareza case-sensitive y el dataset trae variantes en minúscula
                // (⚠️ SPEC-FILTROS §7): el motor porta el bug, la UI manda ambas.
                .SelectMany(r => new[] { r, r.ToLowerInvariant() })
                .Distinct().ToList();

            return new CardSearchCriteria
            {
                Name = NameBox.Text ?? "",
                Text = TextSearchBox.Text ?? "",
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
                OnlyFavorites = OnlyFavoritesBox.IsChecked == true,
                IncludeHidden = IncludeHiddenBox.IsChecked == true,
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
                CandidatePaths = ResolveCandidates(v.Card, v.ImagePath, fallbackChain: !expand),
            }).ToList();

            if (reselectCardId != null)
            {
                var again = _gridCells.FirstOrDefault(c => c.Card.Id == reselectCardId);
                if (again != null) { again.IsSelected = true; _selectedCell = again; }
            }

            _gridDirty = false;
            ChunkGrid();
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

        private void GridCell_Click(object sender, MouseButtonEventArgs e)
        {
            var cell = (sender as FrameworkElement)?.DataContext as GridCell;
            if (cell == null) return;
            if (_selectedCell != null) _selectedCell.IsSelected = false;
            cell.IsSelected = true;
            _selectedCell = cell;

            DetailEmpty.Visibility = Visibility.Collapsed;
            DetailScroll.Visibility = Visibility.Visible;
            ShowDetail(cell.Card, cell.ImagePath);
        }

        private List<string> ResolveCandidates(Card card, string preferredPath, bool fallbackChain)
        {
            var result = new List<string>();
            if (_imageRoots.Count == 0) return result;

            var relatives = new List<string>();
            if (!string.IsNullOrEmpty(preferredPath)) relatives.Add(preferredPath);
            if (fallbackChain)
            {
                if (!string.IsNullOrEmpty(card.ImagePath)) relatives.Add(card.ImagePath);
                relatives.AddRange(CardSearchEngine.SplitPipe(card.PrintingImages));
            }

            foreach (var rel in relatives.Distinct())
                foreach (var alias in WithSetFolderAliases(rel))
                    foreach (var root in _imageRoots)
                    {
                        try { result.Add(Path.GetFullPath(Path.Combine(root, alias.Replace('/', '\\')))); }
                        catch { /* ruta inválida en datos → se ignora */ }
                    }
            return result;
        }

        /// <summary>
        /// Alias de carpeta de set: la DB referencia las promos como images/cards/P/… pero
        /// Sun and Moon las guarda en images/cards/Promo/…. Se prueban ambas (y a la inversa,
        /// por si algún pack usa la otra convención).
        /// </summary>
        private static IEnumerable<string> WithSetFolderAliases(string rel)
        {
            yield return rel;
            const string prefix = "images/cards/";
            var i = rel.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (i < 0) yield break;
            var start = i + prefix.Length;
            var j = rel.IndexOf('/', start);
            if (j < 0) yield break;
            var folder = rel.Substring(start, j - start);
            if (folder == "P")
                yield return rel.Substring(0, start) + "Promo" + rel.Substring(j);
            else if (folder == "Promo")
                yield return rel.Substring(0, start) + "P" + rel.Substring(j);
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
            OnlyFavoritesBox.IsChecked = false;
            IncludeHiddenBox.IsChecked = false;
            OnlyHiddenBox.IsChecked = false;
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
            if (value.Length == 0 || list.Contains(value)) { combo.Text = ""; return; }

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
            combo.Text = "";
            combo.IsDropDownOpen = false;
            Recompute();
        }

        // ------------------------------------------------------------------ teclado

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                NameBox.Focus();
                NameBox.SelectAll();
                e.Handled = true;
            }
        }

        private void NameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                NameBox.Text = "";
                e.Handled = true;
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

        // ------------------------------------------------------------------ favoritas / ocultas

        private void FavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_detailCard == null) return;
            _store.ToggleFavorite(_detailCard.Id);
            UpdateDetailActions();
            Recompute(); // por si "Solo favoritas" está activo
        }

        private void HideBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_detailCard == null) return;
            _store.ToggleHidden(_detailCard.Id);
            UpdateDetailActions();
            Recompute(); // por default las ocultas desaparecen del resultado
        }

        private void UpdateDetailActions()
        {
            if (_detailCard == null) return;
            FavBtn.Content = _store.Data.Favorites.Contains(_detailCard.Id)
                ? Strings.Get("Search_Fav_Remove") : Strings.Get("Search_Fav_Add");
            HideBtn.Content = _store.Data.Hidden.Contains(_detailCard.Id)
                ? Strings.Get("Search_Unhide") : Strings.Get("Search_Hide");
        }

        // ------------------------------------------------------------------ detalle

        private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var card = ResultsList.SelectedItem as Card;
            if (card == null)
            {
                DetailScroll.Visibility = Visibility.Collapsed;
                DetailEmpty.Visibility = Visibility.Visible;
                return;
            }
            DetailEmpty.Visibility = Visibility.Collapsed;
            DetailScroll.Visibility = Visibility.Visible;
            ShowDetail(card);
        }

        private void ShowDetail(Card card, string preferredPath = null)
        {
            _detailCard = card;
            var candidates = ResolveCandidates(card, preferredPath, fallbackChain: true);
            CardImage.Source = LoadFromCandidates(candidates);
            UpdateImageDiagnostic(card, candidates);
            UpdateDetailActions();

            DetailName.Text = card.Name;
            DetailMeta.Text = JoinNonEmpty(" · ", Capitalize(card.Type), Capitalize(card.Clan),
                card.Rarity.Length > 0 ? Strings.Get("Rarity_" + card.Rarity.ToUpperInvariant()) : null);
            DetailKeywords.Text = card.DisplayKeywords;
            DetailKeywords.Visibility = card.DisplayKeywords.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            DetailText.Inlines.Clear();
            foreach (var inline in CardTextRenderer.ToInlines(card.Text))
                DetailText.Inlines.Add(inline);
            DetailText.Visibility = string.IsNullOrEmpty(card.Text) ? Visibility.Collapsed : Visibility.Visible;

            DetailFlavor.Inlines.Clear();
            foreach (var inline in CardTextRenderer.ToInlines(card.Flavor))
                DetailFlavor.Inlines.Add(inline);
            DetailFlavor.Visibility = string.IsNullOrEmpty(card.Flavor) ? Visibility.Collapsed : Visibility.Visible;

            DetailNote.Text = card.Note ?? "";
            DetailNoteBox.Visibility = string.IsNullOrEmpty(card.Note) ? Visibility.Collapsed : Visibility.Visible;

            DetailStats.Text = BuildStatsLine(card);
            DetailStats.Visibility = DetailStats.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            DetailSets.Text = Strings.Format("Fmt_DetailSets", card.DisplaySets);
            DetailLegality.Text = card.DisplayLegality.Length > 0
                ? Strings.Format("Fmt_DetailLegality", card.DisplayLegality) : "";
            DetailArtist.Text = string.IsNullOrEmpty(card.Artist)
                ? "" : Strings.Format("Fmt_DetailArtist", card.Artist);

            // Pastilla de estado (BANNED/UNIQUE etc.), data-driven desde rules.json: borde,
            // rombito y texto del color de la regla sobre fondo claro fijo (LabelPillBg) —
            // divergencia deliberada del 15% de alfa de la app: sobre el arte no se leía.
            if (!string.IsNullOrEmpty(card.LabelText))
            {
                var labelColor = ParseColor(card.LabelColor) ?? Colors.White;
                var solid = SolidBrush(labelColor);
                BannedPillText.Text = card.LabelText;
                BannedPillText.Foreground = solid;
                BannedPillDiamond.Foreground = solid;
                BannedPill.BorderBrush = solid;
                BannedPill.Visibility = Visibility.Visible;
            }
            else BannedPill.Visibility = Visibility.Collapsed;

            // Marco alrededor de la imagen (highlightColor): relleno al 12% + borde sólido de 2
            // + padding 3 — mismo patrón que CardListItem/CardViewerModal de la app.
            var highlightColor = ParseColor(card.HighlightColor);
            if (highlightColor != null)
            {
                CardImageFrame.BorderBrush = SolidBrush(highlightColor.Value);
                CardImageFrame.Background = SolidBrush(WithAlpha(highlightColor.Value, 0.12));
                CardImageFrame.BorderThickness = new Thickness(2);
                CardImageFrame.Padding = new Thickness(3);
            }
            else
            {
                CardImageFrame.BorderThickness = new Thickness(0);
                CardImageFrame.Background = null;
                CardImageFrame.Padding = new Thickness(0);
            }

            DetailScroll.ScrollToTop();
        }

        private string BuildStatsLine(Card card)
        {
            var parts = new List<string>();
            void Add(string key, string value)
            {
                if (!string.IsNullOrEmpty(value)) parts.Add(Strings.Get(key) + " " + value);
            }
            Add("Search_StatForce", card.DisplayForce);
            Add("Search_StatChi", card.DisplayChi);
            Add("Search_StatCost", card.DisplayCost);
            Add("Search_StatHonor", card.DisplayHonor);
            Add("Search_StatPHonor", card.DisplayPersonalHonor);
            Add("Search_StatFocus", card.DisplayFocus);
            Add("Search_StatGold", card.GoldProductionSpecial ? "*" : card.GoldProduction?.ToString());
            Add("Search_StatProvince", card.ProvinceStrength?.ToString());
            Add("Search_StatStartHonor", card.StartingHonor?.ToString());
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// Carga la primera imagen existente de la lista de candidatos (cadena de fallback de la
        /// app: impresión pedida → imagePath preferido → cada printing_images). Sin imagen queda
        /// el detalle en texto como placeholder. Solo lectura de archivos locales.
        /// </summary>
        private static ImageSource LoadFromCandidates(List<string> candidates)
        {
            foreach (var full in candidates)
            {
                try
                {
                    if (!File.Exists(full)) continue;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(full);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch
                {
                    // archivo corrupto/no imagen → probar el siguiente printing
                }
            }
            return null;
        }

        /// <summary>
        /// Si la imagen no apareció, muestra en el detalle dónde se la buscó — sin esto,
        /// "no se ven las imágenes" es indebuggeable a distancia.
        /// </summary>
        private void UpdateImageDiagnostic(Card card, List<string> candidates)
        {
            if (CardImage.Source != null)
            {
                DetailImageMiss.Visibility = Visibility.Collapsed;
                return;
            }
            string message;
            if (string.IsNullOrEmpty(card.ImagePath) && string.IsNullOrEmpty(card.PrintingImages))
                message = Strings.Get("Search_NoImagePath");
            else if (candidates.Count == 0)
                message = Strings.Get("Search_NoImageRoots");
            else
                message = Strings.Format("Fmt_ImageMissing", candidates[0]);
            DetailImageMiss.Text = message;
            DetailImageMiss.Visibility = Visibility.Visible;
        }

        /// <summary>parseHexColor de la app: hex inválido → null (el caller decide el fallback White).</summary>
        internal static Color? ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }

        internal static Color WithAlpha(Color color, double alpha) =>
            Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);

        private static Brush SolidBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static string JoinNonEmpty(string sep, params string[] parts) =>
            string.Join(sep, parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// Celda de la grilla de miniaturas. INPC solo para la selección (las celdas se recrean por
    /// búsqueda; el thumbnail se decodifica lazy con IsAsync y se retiene débil para que la
    /// memoria no crezca con miles de cartas — la virtualización re-decodifica al volver a scrollear).
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
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        private WeakReference<BitmapImage> _thumb;

        /// <summary>Se lee vía Binding IsAsync (thread pool): la decodificación no bloquea la UI.</summary>
        public BitmapImage Thumb
        {
            get
            {
                if (_thumb != null && _thumb.TryGetTarget(out var cached)) return cached;
                foreach (var path in CandidatePaths)
                {
                    try
                    {
                        if (!File.Exists(path)) continue;
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 280;
                        bmp.EndInit();
                        bmp.Freeze();
                        _thumb = new WeakReference<BitmapImage>(bmp);
                        return bmp;
                    }
                    catch
                    {
                        // archivo corrupto → probar el siguiente candidato
                    }
                }
                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Hex de rules.json → SolidColorBrush (fallback White, como parseHexColor de la app).
    /// ConverterParameter opcional = alfa ("0.15" para el fondo de la pastilla).
    /// </summary>
    public class HexBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var color = SearchWindow.ParseColor(value as string) ?? Colors.White;
            if (parameter is string s &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha))
                color = SearchWindow.WithAlpha(color, alpha);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
