using System;
using System.IO;
using System.Windows;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Services;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Edición de config: URL del manifest (HTTPS, default hardcodeado restaurable) y carpeta de
    /// Sun and Moon (spec §4.1.6, §8.2, §13). No persiste hasta GUARDAR.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly UserConfig _config;
        private readonly ConfigService _configService = new ConfigService();
        private string _path;

        /// <summary>True si el usuario guardó cambios (la MainWindow puede refrescar).</summary>
        public bool Saved { get; private set; }

        public SettingsWindow(UserConfig config)
        {
            InitializeComponent();
            _config = config;
            _path = config.SunAndMoonPath;
            PathText.Text = _path ?? "(sin configurar)";
            ManifestUrlBox.Text = config.ManifestUrl ?? AppConstants.DefaultManifestUrl;
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
                ShowValidation("Esa carpeta no parece una instalación válida de Sun and Moon.");
                return;
            }
            _path = dir;
            PathText.Text = dir;
            ValidationText.Visibility = Visibility.Collapsed;
        }

        private void ResetUrlButton_Click(object sender, RoutedEventArgs e)
        {
            ManifestUrlBox.Text = AppConstants.DefaultManifestUrl;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var url = (ManifestUrlBox.Text ?? "").Trim();

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                ShowValidation("La URL del manifest no es válida.");
                return;
            }
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                ShowValidation("La URL del manifest debe ser HTTPS.");
                return;
            }

            _config.ManifestUrl = url;
            _config.SunAndMoonPath = _path;
            _configService.Save(_config);
            Saved = true;
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
