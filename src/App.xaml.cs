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
            // Seguridad (spec §8 regla 3): forzar TLS 1.2 explícitamente y NADA por debajo.
            // En .NET Framework el default histórico puede no negociar TLS 1.2 contra Cloudflare;
            // se fija solo 1.2 (no 1.1/1.0) para coincidir con lo que documentamos. Cloudflare
            // negocia 1.2 sin problema. (Tls13 no se usa: el enum no está garantizado en net48.)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            base.OnStartup(e);

#if DEBUG
            // Harness de verificación: L5ArgentinaLauncher.exe --selftest [reportPath]
            if (e.Args.Length > 0 && e.Args[0] == "--selftest")
            {
                Environment.ExitCode = SelfTest.Run(e.Args);
                Shutdown();
                return;
            }

            // Abrir el Buscador directo (testing de UI, sin pasar por MainWindow ni first-run).
            // Usa el catálogo de cache/catalog (respeta L5A_DATA_ROOT).
            if (e.Args.Length > 0 && e.Args[0] == "--buscador")
            {
                var cat = Services.Search.CatalogStore.Load();
                var cfg = new ConfigService().Load();
                new SearchWindow(cat, cfg.SunAndMoonPath).Show();
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
