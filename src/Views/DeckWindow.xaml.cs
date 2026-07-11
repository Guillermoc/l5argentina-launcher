using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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

        private class SortOption
        {
            public string Label;
            public DeckCardSort Field;
            public override string ToString() => Label;
        }

        private class DeckItemVm
        {
            public string Id;
            public string Title { get; set; }
            public string Subtitle { get; set; }
        }

        private static DeckWindow _instance;

        private readonly DeckLibrary _library;
        private readonly CardImageResolver _resolver;
        private readonly DispatcherTimer _notesTimer;
        private readonly DispatcherTimer _statusTimer;
        // VMs de imagen por carta (GridCell del Buscador: decode lazy + weak ref); el caché
        // evita re-decodificar en cada rebuild del cuerpo (cada +/− lo dispara)
        private readonly Dictionary<string, GridCell> _imageCells =
            new Dictionary<string, GridCell>(StringComparer.Ordinal);
        private string _selectedDeckId;
        private string _selectedCardId;
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

        public DeckWindow(CardCatalog catalog, string installPath)
        {
            InitializeComponent();
            _library = DeckLibrary.GetOrLoad(catalog, installPath);
            _resolver = new CardImageResolver(installPath);

            SortCombo.ItemsSource = new[]
            {
                new SortOption { Label = Strings.Get("Search_Sort_Cost"), Field = DeckCardSort.GOLD_COST },
                new SortOption { Label = Strings.Get("Search_Sort_Name"), Field = DeckCardSort.NAME },
                new SortOption { Label = Strings.Get("Search_Sort_Force"), Field = DeckCardSort.FORCE },
            };
            SortCombo.SelectedIndex = 0; // _syncing sigue en true desde la construcción

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
            Closed += (s, e) =>
            {
                _library.Repo.DeckChanged -= OnDeckChanged;
                _library.Repo.DeckListChanged -= OnDeckListChanged;
                _library.ActiveDeckChanged -= OnActiveDeckChanged;
            };

            RefreshDeckList(_library.ActiveDeckId ?? Repo.Decks.FirstOrDefault()?.Id);
        }

        // ------------------------------------------------------------------ title bar custom

        private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaxButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_StateChanged(object sender, EventArgs e)
        {
            var maximized = WindowState == WindowState.Maximized;
            RootLayout.Margin = new Thickness(maximized ? 7 : 0);
            MaxButton.Content = maximized ? "❐" : "□";
        }

        // ------------------------------------------------------------------ paneles colapsables

        private void PaneToggle_Click(object sender, RoutedEventArgs e)
        {
            var decksOn = DecksPaneBtn.IsChecked == true;
            DecksPanel.Visibility = decksOn ? Visibility.Visible : Visibility.Collapsed;
            DecksCol.Width = new GridLength(decksOn ? 260 : 0);
            DecksGap.Width = new GridLength(decksOn ? 10 : 0);

            var sideOn = SidePaneBtn.IsChecked == true;
            SidePanel.Visibility = sideOn ? Visibility.Visible : Visibility.Collapsed;
            SideCol.Width = new GridLength(sideOn ? 405 : 0);
            SideGap.Width = new GridLength(sideOn ? 10 : 0);
        }

        // ------------------------------------------------------------------ lista de mazos

        private void RefreshDeckList(string selectId)
        {
            var items = Repo.Decks
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new DeckItemVm
                {
                    Id = d.Id,
                    Title = (d.Id == _library.ActiveDeckId ? "● " : "") + d.Name,
                    Subtitle = DeckSubtitle(d),
                })
                .ToList();

            _syncing = true;
            DeckList.ItemsSource = items;
            var select = items.FirstOrDefault(i => i.Id == selectId)
                         ?? items.FirstOrDefault(i => i.Id == _selectedDeckId)
                         ?? items.FirstOrDefault();
            DeckList.SelectedItem = select;
            _syncing = false;

            _selectedDeckId = select?.Id;
            RefreshAll();
        }

        private string DeckSubtitle(Models.Deck deck)
        {
            var clan = Repo.DeckClan(deck.Id);
            var counts = $"{Repo.DynastyCount(deck.Id)}/{Repo.FateCount(deck.Id)}";
            return (clan != null ? ClanLabel(clan) + " · " : "") + counts;
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
            RefreshAll();
        }

        /// <summary>Selecciona un mazo desde afuera (p. ej. el botón ✎ del Buscador).</summary>
        public void SelectDeck(string deckId)
        {
            var item = (DeckList.ItemsSource as IEnumerable<DeckItemVm>)?.FirstOrDefault(i => i.Id == deckId);
            if (item != null) DeckList.SelectedItem = item; // dispara SelectionChanged
        }

        // ------------------------------------------------------------------ eventos de la biblioteca

        private void OnDeckChanged(Models.Deck deck)
        {
            if (deck.Id != _selectedDeckId) { RefreshDeckList(_selectedDeckId); return; }
            RefreshDeckList(_selectedDeckId); // subtítulos (conteos) + cuerpo + stats
        }

        private void OnDeckListChanged() => RefreshDeckList(_selectedDeckId);

        private void OnActiveDeckChanged() => RefreshDeckList(_selectedDeckId);

        // ------------------------------------------------------------------ refresco general

        private void RefreshAll()
        {
            var deck = _selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null;
            var hasDeck = deck != null;
            foreach (var btn in new[] { ImportBtn, ExportBtn, DuplicateBtn, RenameBtn, DeleteBtn })
                btn.IsEnabled = hasDeck;

            DeckTitle.Text = hasDeck ? deck.Name : Strings.Get("Deck_NoDeckSelected");
            var isActive = hasDeck && _library.ActiveDeckId == deck.Id;
            ActiveBadge.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            ActivateBtn.Visibility = hasDeck && !isActive ? Visibility.Visible : Visibility.Collapsed;

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
            var grid = ReferenceEquals(sender, ViewGridBtn);
            ViewGridBtn.IsChecked = grid;
            ViewListBtn.IsChecked = !grid;
            RebuildIfReady();
        }

        private void RebuildIfReady()
        {
            if (_syncing) return;
            SortDirBtn.Content = SortDirBtn.IsChecked == true ? "↓" : "↑";
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
                DeckBody.Children.Add(BodyText(Strings.Get("Deck_NoDeckSelectedLong"), dim: true));
                return;
            }

            var filter = (FilterBox.Text ?? "").Trim();
            var sort = (SortCombo.SelectedItem as SortOption)?.Field ?? DeckCardSort.GOLD_COST;
            var asc = SortDirBtn.IsChecked != true;
            var gridView = ViewGridBtn.IsChecked == true;

            var entries = Repo.GetCardsWithCards(deck.Id)
                .Where(x => x.Entry.Quantity > 0)
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

                DeckBody.Children.Add(GroupHeader(
                    $"{Capitalize(group)} — ({groupEntries.Sum(x => x.Entry.Quantity)}) [{groupEntries.Count}]"));

                foreach (var typeGroup in groupEntries
                             .OrderBy(x => DeckGroups.TypeOrderInGroup(group, x.Card.Type))
                             .GroupBy(x => x.Card.Type))
                {
                    DeckBody.Children.Add(TypeHeader(
                        $"{Capitalize(typeGroup.Key)} — ({typeGroup.Sum(x => x.Entry.Quantity)}) [{typeGroup.Count()}]"));

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
            var row = new Border
            {
                Padding = new Thickness(8, 3, 4, 3),
                CornerRadius = new CornerRadius(5),
                Background = card.Id == _selectedCardId
                    ? (Brush)new SolidColorBrush(Color.FromArgb(0xCC, 0x7D, 0x1F, 0x1C))
                    : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = card.Id,
            };
            row.MouseEnter += (s, e) => { if (card.Id != _selectedCardId) row.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xC9, 0xA9, 0x61)); };
            row.MouseLeave += (s, e) => { if (card.Id != _selectedCardId) row.Background = Brushes.Transparent; };
            row.MouseLeftButtonUp += (s, e) => SelectCard(card);

            var dock = new DockPanel { LastChildFill = true };

            var delBtn = SmallButton("✕", Strings.Get("Deck_RemoveEntryTip"));
            delBtn.Click += (s, e) => { Repo.DeleteCardEntry(deck.Id, card.Id); };
            DockPanel.SetDock(delBtn, Dock.Right);
            dock.Children.Add(delBtn);

            var plusBtn = SmallButton("+", null);
            plusBtn.Click += (s, e) => ReportAdd(Repo.AddCard(deck.Id, card.Id));
            DockPanel.SetDock(plusBtn, Dock.Right);
            dock.Children.Add(plusBtn);

            var max = deck.Type == "list" ? 1 : card.MaxCopies;
            var qty = new TextBlock
            {
                Text = $"{entry.Quantity}/{max}",
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 12,
                Foreground = (Brush)FindResource("Gold"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 7, 0),
                MinWidth = 26,
                TextAlignment = TextAlignment.Center,
            };
            DockPanel.SetDock(qty, Dock.Right);
            dock.Children.Add(qty);

            var minusBtn = SmallButton("−", null);
            minusBtn.Click += (s, e) => Repo.RemoveCard(deck.Id, card.Id); // al llegar a 0 la fila se borra
            DockPanel.SetDock(minusBtn, Dock.Right);
            dock.Children.Add(minusBtn);

            var name = new TextBlock
            {
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            name.Inlines.Add(new System.Windows.Documents.Run(card.Name)
            { Foreground = (Brush)FindResource("Text") });
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
                Width = 213,
                Height = 330,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(0x4D, 0x14, 0x0A, 0x05)),
                BorderThickness = new Thickness(2),
                BorderBrush = card.Id == _selectedCardId
                    ? (Brush)FindResource("Gold")
                    : new SolidColorBrush(Color.FromArgb(0x33, 0xC9, 0xA9, 0x61)),
                Cursor = Cursors.Hand,
            };
            cell.MouseLeftButtonUp += (s, e) => SelectCard(card);

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
            var img = new Image { Stretch = Stretch.Uniform, DataContext = vm };
            img.SetBinding(Image.SourceProperty, new Binding(nameof(GridCell.Thumb)) { IsAsync = true });
            imgHost.Children.Add(img);
            layout.Children.Add(imgHost);

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
            var minus = SmallButton("−", null);
            minus.Click += (s, e) => Repo.RemoveCard(deck.Id, card.Id);
            actions.Children.Add(minus);
            var max = deck.Type == "list" ? 1 : card.MaxCopies;
            actions.Children.Add(new TextBlock
            {
                Text = $"{entry.Quantity}/{max}",
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 12,
                Foreground = (Brush)FindResource("Gold"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 7, 0),
            });
            var plus = SmallButton("+", null);
            plus.Click += (s, e) => ReportAdd(Repo.AddCard(deck.Id, card.Id));
            actions.Children.Add(plus);
            var del = SmallButton("✕", Strings.Get("Deck_RemoveEntryTip"));
            del.Margin = new Thickness(12, 0, 0, 0);
            del.Click += (s, e) => Repo.DeleteCardEntry(deck.Id, card.Id);
            actions.Children.Add(del);
            bar.Child = actions;
            layout.Children.Add(bar);

            cell.Child = layout;
            return cell;
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

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            var stats = ReferenceEquals(sender, TabStatsBtn);
            TabStatsBtn.IsChecked = stats;
            TabCardBtn.IsChecked = !stats;
            StatsScroll.Visibility = stats ? Visibility.Visible : Visibility.Collapsed;
            Detail.Visibility = stats ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SelectCard(Card card)
        {
            _selectedCardId = card.Id;
            Detail.ShowCard(card, _resolver.ResolveCandidates(card, null, fallbackChain: true));
            TabCardBtn.IsChecked = true;
            Tab_Click(TabCardBtn, null);
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
            var name = Prompt(Strings.Get("Deck_New"), Strings.Get("Deck_NewNamePrompt"), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            var id = _library.CreateDeck(name.Trim()); // se activa solo, como la app
            RefreshDeckList(id);
        }

        private void ActivateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeckId != null) _library.ActiveDeckId = _selectedDeckId;
        }

        private void RenameBtn_Click(object sender, RoutedEventArgs e)
        {
            var deck = _selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null;
            if (deck == null) return;
            var name = Prompt(Strings.Get("Deck_Rename"), Strings.Get("Deck_RenamePrompt"), deck.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == deck.Name) return;
            _library.RenameDeck(deck.Id, name.Trim());
        }

        private void DuplicateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeckId == null) return;
            var copyId = _library.DuplicateDeck(_selectedDeckId);
            if (copyId != null) RefreshDeckList(copyId);
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var deck = _selectedDeckId != null ? Repo.GetDeckById(_selectedDeckId) : null;
            if (deck == null) return;
            var confirm = MessageBox.Show(this,
                Strings.Format("Fmt_DeckDeleteConfirm", deck.Name),
                Strings.Get("Deck_Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
            _library.DeleteDeck(deck.Id);
            _selectedDeckId = null;
            RefreshDeckList(null);
        }

        private void ImportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeckId == null) return;
            string text = null;
            try { text = Clipboard.GetText(); } catch { }
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, Strings.Get("Deck_ImportEmptyClipboard"),
                    Strings.Get("Deck_Import"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(this, message, Strings.Get("Deck_Import"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
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

        private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_library.DecksDir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + _library.DecksDir + "\"");
            }
            catch { }
        }

        // ------------------------------------------------------------------ prompt mínimo

        /// <summary>Diálogo modal chico de una línea (nombre de mazo), con la estética de la app.</summary>
        private string Prompt(string title, string label, string initial)
        {
            var box = new TextBox
            {
                Text = initial ?? "",
                Style = (Style)FindResource("InvertedTextBox"),
                Height = 32,
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 14),
            };
            var ok = new Button
            {
                Content = Strings.Get("Deck_PromptOk"),
                Style = (Style)FindResource("GhostButton"),
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                IsDefault = true,
            };
            var cancel = new Button
            {
                Content = Strings.Get("Deck_PromptCancel"),
                Style = (Style)FindResource("GhostButton"),
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true,
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 14,
                Foreground = (Brush)FindResource("Text"),
            });
            panel.Children.Add(box);
            panel.Children.Add(buttons);

            var dialog = new Window
            {
                Title = title,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 340,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = (Brush)FindResource("Surface"),
                Content = new Border
                {
                    BorderBrush = (Brush)FindResource("GoldDim"),
                    BorderThickness = new Thickness(1),
                    Child = panel,
                },
            };
            dialog.MouseLeftButtonDown += (s, e) => { try { dialog.DragMove(); } catch { } };
            ok.Click += (s, e) => { dialog.DialogResult = true; };
            dialog.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            return dialog.ShowDialog() == true ? box.Text : null;
        }

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
