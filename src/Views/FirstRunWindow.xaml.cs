using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Resources;
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
                ShowValidation(Strings.Get("FirstRun_FolderInvalid"));
                ContinueButton.IsEnabled = false;
                PathBox.Tag = "invalid";
                return;
            }

            _selectedPath = dir;
            PathBox.Text = dir;
            PathBox.Foreground = (Brush)FindResource("InputText");
            PathBox.Tag = null;
            ValidationPanel.Visibility = Visibility.Collapsed;
            ContinueButton.IsEnabled = true;
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedPath)) return;

            // Si la base actual ya parece comunidad/modificada, el backup "original" no sería
            // limpio (spec §7): pedir confirmación con un overlay (no MessageBox).
            if (_databaseService.LooksLikeCommunityBase(_selectedPath))
            {
                WarnOverlay.Visibility = Visibility.Visible;
                return;
            }
            DoContinue();
        }

        private void WarnYes_Click(object sender, RoutedEventArgs e)
        {
            WarnOverlay.Visibility = Visibility.Collapsed;
            DoContinue();
        }

        private void WarnNo_Click(object sender, RoutedEventArgs e)
        {
            WarnOverlay.Visibility = Visibility.Collapsed;
        }

        private void DoContinue()
        {
            try
            {
                _config.SunAndMoonPath = _selectedPath;
                _databaseService.EnsureOriginalBackup(_selectedPath);
                _config.OriginalBackupCaptured = _databaseService.HasOriginalBackup();
                _configService.Save(_config);
            }
            catch (Exception ex)
            {
                ShowValidation(Strings.Format("Fmt_FirstRun_SaveError", ex.Message));
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
            ValidationPanel.Visibility = Visibility.Visible;
        }
    }
}
