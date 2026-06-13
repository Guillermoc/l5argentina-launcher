using System;
using System.Net;
using System.Windows;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Services;
using L5ArgentinaLauncher.Views;

namespace L5ArgentinaLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Seguridad (spec §8 regla 3): forzar TLS 1.2+ explícitamente. En .NET Framework
            // el default histórico puede no negociar TLS 1.2 contra Cloudflare R2/Pages.
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            base.OnStartup(e);

#if DEBUG
            // Harness de verificación: L5ArgentinaLauncher.exe --selftest [reportPath]
            if (e.Args.Length > 0 && e.Args[0] == "--selftest")
            {
                Environment.ExitCode = SelfTest.Run(e.Args);
                Shutdown();
                return;
            }
#endif

            var configService = new ConfigService();
            UserConfig config = configService.Load();

            // Primer arranque / instalación inválida: pedir la carpeta de Sun and Moon y
            // capturar la base "Original" antes de tocar nada (spec §4.1.1, §7).
            if (!InstallationService.IsValidInstall(config.SunAndMoonPath))
            {
                var firstRun = new FirstRunWindow(config);
                if (firstRun.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            new MainWindow(config).Show();
        }
    }
}
