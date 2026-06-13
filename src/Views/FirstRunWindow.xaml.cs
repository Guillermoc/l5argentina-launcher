using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Services;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Primer arranque (spec §4.1.1, §7): detectar la instalación de Sun and Moon, validar,
    /// advertir si el database.xml ya parece modificado y capturar la base "Original".
    /// </summary>
    public partial class FirstRunWindow : Window
    {
        private readonly UserConfig _config;
        private readonly ConfigService _configService = new ConfigService();
        private readonly DatabaseService _databaseService = new DatabaseService();
        private string _selectedPath;

        public FirstRunWindow(UserConfig config)
        {
            InitializeComponent();
            _config = config;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Elegí Sun and Moon.exe",
                Filter = "Sun and Moon (Sun and Moon.exe)|Sun and Moon.exe|Ejecutables (*.exe)|*.exe",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) != true) return;

            var dir = Path.GetDirectoryName(dlg.FileName);
            if (!InstallationService.IsValidInstall(dir))
            {
                ShowValidation("Esa carpeta no parece una instalación válida de Sun and Moon " +
                               "(falta Sun and Moon.exe o la carpeta StreamingAssets\\Database).");
                ContinueButton.IsEnabled = false;
                return;
            }

            _selectedPath = dir;
            PathText.Text = dir;
            PathText.Foreground = (Brush)FindResource("TextPrimary");
            ValidationText.Visibility = Visibility.Collapsed;
            ContinueButton.IsEnabled = true;
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedPath)) return;

            // Advertencia: si la base actual ya parece comunidad/modificada, el backup "original"
            // no sería limpio (spec §7).
            if (_databaseService.LooksLikeCommunityBase(_selectedPath))
            {
                var r = MessageBox.Show(this,
                    "El database.xml actual parece una base de comunidad o modificada.\n\n" +
                    "Si continuás, esa será la copia que se guarde como tu base \"Original\", y no será " +
                    "una base original limpia. Lo ideal es capturarla sobre una instalación recién bajada " +
                    "de Sun and Moon.\n\n¿Querés continuar igual?",
                    "Base posiblemente modificada", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            try
            {
                _config.SunAndMoonPath = _selectedPath;
                _databaseService.EnsureOriginalBackup(_selectedPath);
                _config.OriginalBackupCaptured = _databaseService.HasOriginalBackup();
                _configService.Save(_config);
            }
            catch (Exception ex)
            {
                ShowValidation("No se pudo guardar la configuración inicial: " + ex.Message);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowValidation(string message)
        {
            ValidationText.Text = message;
            ValidationText.Visibility = Visibility.Visible;
        }
    }
}
