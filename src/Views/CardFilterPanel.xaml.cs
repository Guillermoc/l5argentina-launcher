using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Resources;
using L5ArgentinaLauncher.Services.Search;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Panel de filtros sobre cartas, compartido por el Buscador y el editor de Mazos
    /// ([ADR-058](../../docs/DECISIONS.md)). Sale del `CardSearchCriteria`: el control no busca
    /// nada: llena un criteria y avisa que cambió. Quién lo aplica —el catálogo entero o las
    /// cartas de un mazo— es del dueño.
    ///
    /// Los presets NO están acá: guardan estado que solo existe en el Buscador (conjunto activo,
    /// favoritas, orden), así que siguen en su panel.
    /// </summary>
    public partial class CardFilterPanel : UserControl
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

        private readonly List<string> _keywords = new List<string>();
        private readonly List<string> _negKeywords = new List<string>();
        private List<string> _allKwCodes = new List<string>();
        private bool _kwUpdating;
        // true mientras el panel se llena por código (Apply/Clear): los eventos no salen
        private bool _quiet = true;

        private TextBox _forceMin, _forceMax, _chiMin, _chiMax, _costMin, _costMax,
                        _honorMin, _honorMax, _pHonorMin, _pHonorMax, _focusMin, _focusMax;
        private CheckBox _forceSpecial, _costSpecial, _honorNeg, _pHonorNeg;

        /// <summary>Cambió un toggle, chip o combo: acción discreta, se puede recomputar ya.</summary>
        public event EventHandler Changed;

        /// <summary>
        /// Cambió una caja de texto (texto de carta o un tope de stats). Va aparte porque es una
        /// ráfaga de teclas: el dueño decide si le pone debounce.
        /// </summary>
        public event EventHandler TextTyped;

        public CardFilterPanel()
        {
            InitializeComponent();
        }

        /// <summary>Arma chips, sets, filas de stats y el autocomplete de keywords.</summary>
        public void Initialize(CardCatalog catalog)
        {
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
            KwCombo.AddHandler(TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((s, e) => KwComboTextChanged(KwCombo)));
            NegKwCombo.AddHandler(TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((s, e) => KwComboTextChanged(NegKwCombo)));

            _quiet = false;
        }

        /// <summary>
        /// Arranca con todas las secciones plegadas. Lo usa el editor de Mazos: ahí el panel
        /// convive con los filtros del mazo y las etiquetas, y desplegado de entrada los empuja
        /// fuera de la vista.
        /// </summary>
        public void CollapseSections()
        {
            foreach (var section in new[] { SectionKeywords, SectionText, SectionType, SectionClan })
                section.IsExpanded = false;
        }

        /// <summary>
        /// Sección "Otros" (incluir baneadas / solo ocultas). El editor de Mazos la esconde: son
        /// filtros para **navegar el catálogo**, y adentro de un mazo lo único que pueden hacer es
        /// sacar de la vista cartas que el mazo tiene de verdad (ver ADR-058).
        /// </summary>
        public bool ShowOtherSection
        {
            get => SectionOther.Visibility == Visibility.Visible;
            set => SectionOther.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Texto de carta (lo pisa el modo `tx.` de la barra del Buscador).</summary>
        public string CardText
        {
            get => TextSearchBox.Text ?? "";
            set => TextSearchBox.Text = value ?? "";
        }

        /// <summary>¿Hay algún filtro puesto? (para el botón "Limpiar filtros" del dueño).</summary>
        public bool HasAnyFilter
        {
            get
            {
                var c = new CardSearchCriteria();
                Fill(c);
                return c.Types.Count > 0 || c.Clans.Count > 0 || c.Rarities.Count > 0 ||
                       c.Sets.Count > 0 || c.Designators.Count > 0 || c.Keywords.Count > 0 ||
                       c.NegativeKeywords.Count > 0 || c.Text.Length > 0 || c.IncludeBanned ||
                       c.OnlyHidden || HasAnyStat(c);
            }
        }

        private static bool HasAnyStat(CardSearchCriteria c) =>
            c.ForceMin.HasValue || c.ForceMax.HasValue || c.IncludeSpecialForce ||
            c.ChiMin.HasValue || c.ChiMax.HasValue ||
            c.CostMin.HasValue || c.CostMax.HasValue || c.IncludeSpecialCost ||
            c.HonorReqMin.HasValue || c.HonorReqMax.HasValue || c.IncludeNegativeHonorReq ||
            c.PersonalHonorMin.HasValue || c.PersonalHonorMax.HasValue ||
            c.IncludeNegativePersonalHonor || c.FocusMin.HasValue || c.FocusMax.HasValue;

        // ------------------------------------------------------------------ criteria

        /// <summary>Vuelca lo que el panel filtra sobre un criteria. El resto lo pone el dueño.</summary>
        public void Fill(CardSearchCriteria c)
        {
            c.Text = CardText;
            c.Types = CheckedTags(TypeChips);
            c.Clans = CheckedTags(ClanChips);
            // La app filtra rareza case-sensitive y el dataset trae variantes en minúscula
            // (⚠️ SPEC-FILTROS §7): el motor porta el bug, la UI manda ambas.
            c.Rarities = CheckedTags(RarityChips)
                .SelectMany(r => new[] { r, r.ToLowerInvariant() })
                .Distinct().ToList();
            c.Sets = SetChecks.Children.OfType<CheckBox>()
                .Where(x => x.IsChecked == true).Select(x => (string)x.Tag).ToList();
            c.Designators = CheckedTags(DesignatorChips);
            c.Keywords = _keywords.ToList();
            c.NegativeKeywords = _negKeywords.ToList();
            c.KeywordMatchAll = KwMatchAllBox.IsChecked == true;
            c.IncludeActionKeywords = KwActionBox.IsChecked == true;
            c.IncludeSubtitleKeywords = KwSubtitleBox.IsChecked == true;
            c.IncludeBanned = IncludeBannedBox.IsChecked == true;
            c.OnlyHidden = OnlyHiddenBox.IsChecked == true;
            c.ForceMin = ParseIntBox(_forceMin);
            c.ForceMax = ParseIntBox(_forceMax);
            c.IncludeSpecialForce = _forceSpecial.IsChecked == true;
            c.ChiMin = ParseIntBox(_chiMin);
            c.ChiMax = ParseIntBox(_chiMax);
            c.CostMin = ParseIntBox(_costMin);
            c.CostMax = ParseIntBox(_costMax);
            c.IncludeSpecialCost = _costSpecial.IsChecked == true;
            c.HonorReqMin = ParseIntBox(_honorMin);
            c.HonorReqMax = ParseIntBox(_honorMax);
            c.IncludeNegativeHonorReq = _honorNeg.IsChecked == true;
            c.PersonalHonorMin = ParseIntBox(_pHonorMin);
            c.PersonalHonorMax = ParseIntBox(_pHonorMax);
            c.IncludeNegativePersonalHonor = _pHonorNeg.IsChecked == true;
            c.FocusMin = ParseIntBox(_focusMin);
            c.FocusMax = ParseIntBox(_focusMax);
        }

        /// <summary>Vuelca un criteria sobre los controles (inverso de <see cref="Fill"/>), en silencio.</summary>
        public void Apply(CardSearchCriteria c)
        {
            var wasQuiet = _quiet;
            _quiet = true;
            try
            {
                CardText = c.Text ?? "";
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
            }
            finally
            {
                _quiet = wasQuiet;
            }
        }

        /// <summary>Deja el panel sin ningún filtro, en silencio (el dueño recomputa una vez).</summary>
        public void Clear()
        {
            var wasQuiet = _quiet;
            _quiet = true;
            try
            {
                foreach (var chip in TypeChips.Children.OfType<ToggleButton>()
                         .Concat(ClanChips.Children.OfType<ToggleButton>())
                         .Concat(RarityChips.Children.OfType<ToggleButton>())
                         .Concat(DesignatorChips.Children.OfType<ToggleButton>()))
                    chip.IsChecked = false;
                foreach (var check in SetChecks.Children.OfType<CheckBox>())
                    check.IsChecked = false;

                CardText = "";
                IncludeBannedBox.IsChecked = false;
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
            }
            finally
            {
                _quiet = wasQuiet;
            }
        }

        // ------------------------------------------------------------------ barra con prefijos

        /// <summary>
        /// Modos de la barra por prefijo (port de parseBarText de la app; `tx.` se evalúa antes
        /// que `t.`). NAME filtra en vivo; TEXT (`tx.`) filtra el texto de carta en vivo; los modos
        /// KEYWORD/CLAN/TYPE se confirman con Enter.
        /// </summary>
        public enum BarMode { Name, Keyword, Clan, Type, Text }

        /// <summary>
        /// Parsea el prefijo de una barra de búsqueda. Estático y público porque lo usan las dos
        /// ventanas y porque es lo único de acá que se puede verificar de a un caso.
        /// </summary>
        public static BarMode ParseBar(string bar, out string value)
        {
            bar = bar ?? "";
            if (bar.StartsWith("tx.", StringComparison.OrdinalIgnoreCase)) { value = bar.Substring(3); return BarMode.Text; }
            if (bar.StartsWith("k.", StringComparison.OrdinalIgnoreCase)) { value = bar.Substring(2); return BarMode.Keyword; }
            if (bar.StartsWith("c.", StringComparison.OrdinalIgnoreCase)) { value = bar.Substring(2); return BarMode.Clan; }
            if (bar.StartsWith("t.", StringComparison.OrdinalIgnoreCase)) { value = bar.Substring(2); return BarMode.Type; }
            value = bar;
            return BarMode.Name;
        }

        /// <summary>
        /// Aplica el modo confirmado con Enter (`k.`/`c.`/`t.`). Devuelve true si hizo algo, para
        /// que el dueño limpie la barra.
        /// </summary>
        public bool ApplyBarMode(BarMode mode, string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0) return false;
            switch (mode)
            {
                case BarMode.Keyword:
                    AddKeywordCore(_keywords, KwChips, value);
                    if (!_quiet) Changed?.Invoke(this, EventArgs.Empty);
                    return true;
                case BarMode.Clan:
                    return ToggleChipByValue(ClanChips, value);
                case BarMode.Type:
                    return ToggleChipByValue(TypeChips, value);
                default:
                    return false;
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
            match.IsChecked = true; // dispara Checked → Changed
            return true;
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
                chip.Checked += Toggle_Changed;
                chip.Unchecked += Toggle_Changed;
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
                check.Checked += Toggle_Changed;
                check.Unchecked += Toggle_Changed;
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
            row.Children.Add(new TextBlock
            {
                Text = label,
                Width = 92,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("BodySmall"),
                FontSize = 12,
            });

            minBox = new TextBox { Style = (Style)FindResource("SmallNumBox"), ToolTip = Strings.Get("Search_MinTip") };
            maxBox = new TextBox { Style = (Style)FindResource("SmallNumBox"), ToolTip = Strings.Get("Search_MaxTip") };
            minBox.TextChanged += Text_Changed;
            maxBox.TextChanged += Text_Changed;
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
                specialBox.Checked += Toggle_Changed;
                specialBox.Unchecked += Toggle_Changed;
                row.Children.Add(specialBox);
            }
            StatsPanel.Children.Add(row);
        }

        // ------------------------------------------------------------------ eventos

        private void Toggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_quiet) Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Text_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_quiet) TextTyped?.Invoke(this, EventArgs.Empty);
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
            if (_kwUpdating || _quiet) return;
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
            if (!_quiet) Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Agrega la keyword y su chip removible, sin avisar (para lote/preset).</summary>
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
                if (!_quiet) Changed?.Invoke(this, EventArgs.Empty);
            };
            chips.Children.Add(chip);
        }

        // ------------------------------------------------------------------ helpers

        private static List<string> CheckedTags(Panel panel) =>
            panel.Children.OfType<ToggleButton>()
                .Where(t => t.IsChecked == true).Select(t => (string)t.Tag).ToList();

        private static void SetChipsByTags(Panel panel, List<string> tags)
        {
            foreach (var chip in panel.Children.OfType<ToggleButton>())
                chip.IsChecked = tags.Contains((string)chip.Tag, StringComparer.Ordinal);
        }

        /// <summary>Marca la caja como inválida (Tag) en vez de rechazar la tecla mientras se tipea.</summary>
        private static int? ParseIntBox(TextBox box)
        {
            var text = (box.Text ?? "").Trim();
            if (text.Length == 0) { box.Tag = null; return null; }
            if (int.TryParse(text, out var value)) { box.Tag = null; return value; }
            box.Tag = "invalid";
            return null;
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
