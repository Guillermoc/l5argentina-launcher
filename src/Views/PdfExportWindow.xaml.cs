using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Resources;
using L5ArgentinaLauncher.Services.Deck;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Pantalla previa del PDF de impresión (port de <c>PdfExportScreen.kt</c>): lista las cartas
    /// del mazo con un contador de copias a imprimir independiente del mazo, más el tamaño de hoja
    /// y el margen. La generación en sí vive en <see cref="PdfExporter"/>.
    ///
    /// Las recortadas (quantity = 0) no aparecen: no se juegan, así que no se imprimen (ADR-046).
    /// </summary>
    public partial class PdfExportWindow : Window
    {
        private class PrintRow
        {
            public Card Card;
            public int DeckQuantity;
            public int PrintQuantity;
            public TextBlock QuantityText;
            public Border Container;
        }

        private readonly CardImageResolver _resolver;
        private readonly string _deckName;
        private readonly List<PrintRow> _rows = new List<PrintRow>();

        private PdfPageSize _pageSize = PdfPageSize.A4;

        public PdfExportWindow(Window owner, DeckRepository repo, CardImageResolver resolver, string deckId)
        {
            InitializeComponent();
            Owner = owner;
            _resolver = resolver;

            var deck = repo.GetDeckById(deckId);
            _deckName = deck?.Name ?? "";
            Title = Strings.Format("Fmt_PdfWindowTitle", _deckName);
            TitleText.Text = Title;

            // Mismo orden que el editor y que la app: grupo → tipo dentro del grupo → alfabético.
            var entries = repo.GetCardsWithCards(deckId)
                .Where(x => x.Entry.Quantity > 0)
                .OrderBy(x => DeckGroups.GroupOrder(x.Card.DeckGroup))
                .ThenBy(x => DeckGroups.TypeOrderInGroup(x.Card.DeckGroup, x.Card.Type))
                .ThenBy(x => x.Card.Name, StringComparer.Ordinal)
                .ToList();

            foreach (var entry in entries) CardList.Children.Add(BuildRow(entry.Card, entry.Entry.Quantity));

            A4Btn.IsChecked = true;
            RefreshSummary();

            MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
        }

        // ------------------------------------------------------------------ filas

        private UIElement BuildRow(Card card, int deckQuantity)
        {
            var row = new PrintRow { Card = card, DeckQuantity = deckQuantity, PrintQuantity = deckQuantity };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = card.Name,
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 13.5,
                Foreground = (Brush)FindResource("Text"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = Subtitle(card),
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 11.5,
                Foreground = (Brush)FindResource("TextDim"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            grid.Children.Add(text);

            // Cantidad real en el mazo: referencia fija, para saber de qué se está partiendo.
            var deckQty = new TextBlock
            {
                Text = "×" + deckQuantity.ToString(CultureInfo.InvariantCulture),
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 11.5,
                Foreground = (Brush)FindResource("TextDim"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            Grid.SetColumn(deckQty, 1);
            grid.Children.Add(deckQty);

            var stepper = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var minus = StepButton("−");
            minus.Click += (s, e) => { SetQuantity(row, row.PrintQuantity - 1); };
            row.QuantityText = new TextBlock
            {
                Text = row.PrintQuantity.ToString(CultureInfo.InvariantCulture),
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 14,
                Foreground = (Brush)FindResource("Text"),
                MinWidth = 22,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var plus = StepButton("+");
            plus.Click += (s, e) => { SetQuantity(row, row.PrintQuantity + 1); };
            stepper.Children.Add(minus);
            stepper.Children.Add(row.QuantityText);
            stepper.Children.Add(plus);
            Grid.SetColumn(stepper, 2);
            grid.Children.Add(stepper);

            row.Container = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0x2A, 0x17, 0x10)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 8, 7),
                Margin = new Thickness(0, 0, 0, 6),
                Child = grid,
            };
            _rows.Add(row);
            ApplyRowState(row);
            return row.Container;
        }

        private Button StepButton(string glyph) => new Button
        {
            Content = glyph,
            Style = (Style)FindResource("StepBtn"),
        };

        /// <summary>Tipo · primer set, igual que el subtítulo de la app.</summary>
        private static string Subtitle(Card card)
        {
            var sets = Services.Search.CardSearchEngine.SplitPipe(card.Sets);
            var first = sets.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            return string.IsNullOrWhiteSpace(first) ? card.Type : card.Type + "  ·  " + first;
        }

        private void SetQuantity(PrintRow row, int quantity)
        {
            row.PrintQuantity = Math.Max(0, quantity);
            row.QuantityText.Text = row.PrintQuantity.ToString(CultureInfo.InvariantCulture);
            ApplyRowState(row);
            RefreshSummary();
        }

        /// <summary>En 0 la fila se apaga: se ve de un vistazo qué quedó fuera de la impresión.</summary>
        private static void ApplyRowState(PrintRow row) =>
            row.Container.Opacity = row.PrintQuantity == 0 ? 0.42 : 1.0;

        // ------------------------------------------------------------------ opciones

        private void PageSize_Click(object sender, RoutedEventArgs e)
        {
            _pageSize = ReferenceEquals(sender, LetterBtn) ? PdfPageSize.Letter : PdfPageSize.A4;
            A4Btn.IsChecked = _pageSize == PdfPageSize.A4;
            LetterBtn.IsChecked = _pageSize == PdfPageSize.Letter;
            RefreshSummary();
        }

        private void Margin_Click(object sender, RoutedEventArgs e) => RefreshSummary();

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows) SetQuantity(row, row.DeckQuantity);
        }

        private void RefreshSummary()
        {
            var total = _rows.Sum(r => r.PrintQuantity);
            var pages = PdfExporter.PageCount(total);
            SummaryText.Text = Strings.Format("Fmt_PdfSummary", total, pages);
            GenerateBtn.IsEnabled = total > 0;
        }

        // ------------------------------------------------------------------ generar

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            var items = _rows
                .Where(r => r.PrintQuantity > 0)
                .Select(r => new PdfExportItem
                {
                    CardName = r.Card.Name,
                    CandidatePaths = _resolver.ResolveCandidates(r.Card, null, fallbackChain: true),
                    Copies = r.PrintQuantity,
                })
                .ToList();
            if (items.Count == 0) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Strings.Get("Pdf_SaveTitle"),
                FileName = PdfExporter.SuggestFileName(_deckName, DateTime.Now),
                Filter = Strings.Get("Pdf_SaveFilter"),
                DefaultExt = ".pdf",
                AddExtension = true,
            };
            if (dialog.ShowDialog(this) != true) return;

            int pages;
            Cursor = System.Windows.Input.Cursors.Wait;
            GenerateBtn.IsEnabled = false;
            try
            {
                pages = PdfExporter.Write(dialog.FileName, items, _pageSize, MarginCheck.IsChecked == true);
            }
            catch (Exception ex)
            {
                Dialogs.Info(this, Strings.Get("Pdf_WindowTitle"), Strings.Format("Fmt_PdfError", ex.Message));
                return;
            }
            finally
            {
                Cursor = null;
                RefreshSummary();
            }

            var missing = items.Count(i => !i.CandidatePaths.Any(SafeExists));
            var message = Strings.Format("Fmt_PdfDone", pages, Path.GetFileName(dialog.FileName));
            if (missing > 0) message += "\n" + Strings.Format("Fmt_PdfMissingImages", missing);

            if (Dialogs.Ask(this, Strings.Get("Pdf_WindowTitle"), message, Strings.Get("Pdf_Open")))
            {
                try { Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
                catch { /* sin visor de PDF asociado → el archivo igual quedó escrito */ }
            }
            Close();
        }

        private static bool SafeExists(string path)
        {
            try { return File.Exists(path); } catch { return false; }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
