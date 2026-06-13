using System.Diagnostics;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>Lanza la instalación de Sun and Moon del usuario (spec §4.1.7).</summary>
    public class GameLauncher
    {
        public void Launch(string installPath)
        {
            var exe = InstallationService.GameExePath(installPath);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = installPath,
                UseShellExecute = true,
            };
            Process.Start(psi);
        }
    }
}
