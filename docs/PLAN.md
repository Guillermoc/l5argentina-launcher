# Plan de implementación — L5Argentina Launcher (v1 / MVP)

> Plan aprobado para construir la v1 completa. La spec de diseño vive en
> [`launcher-spec.md`](./launcher-spec.md) y la guía visual en [`Style_guide.md`](./Style_guide.md).

## Estado del entorno (al iniciar)

- **No** había `dotnet` SDK → se instala user-local (sin admin) en `%LOCALAPPDATA%\Microsoft\dotnet`.
- .NET Framework **4.8.1** runtime presente (lo que necesitan los 25 usuarios para correr el exe).
- VS 2019 BuildTools (MSBuild 16.11) y VS Code presentes. Git presente.

## Fase 0 — Toolchain + scaffolding

- Instalar .NET SDK (canal 8.0) user-local.
- `git init` + `.gitignore`.
- csproj SDK-style `net48`, `UseWPF=true`. NuGet: `System.Text.Json`, `Costura.Fody`,
  `Microsoft.NETFramework.ReferenceAssemblies` (compilar net48 sin Developer Pack del sistema).
  `Markdig` se agrega recién para noticias v2.
- Estructura `src/` (`Services/`, `Models/`, `Assets/`, `Themes/`, `Views/`), `docs/`, `.github/workflows/`.
- Tema WPF desde la guía de estilos (pergamino oscuro, marfil, rojo lacado, oro; fuente L5RTitles).
- `AppConstants.cs`: `DefaultManifestUrl` y `ReleasesPageUrl` como **placeholders con TODO**; `LauncherVersion` embebida.

## Fase 1 — Config + primer arranque + shell de UI

- `UserConfig` + `ConfigService` (`%APPDATA%\L5Argentina Launcher\config.json`).
- Primer arranque: elegir carpeta de Sun and Moon; validar `Sun and Moon.exe` y
  `…\StreamingAssets\Database\`; advertir si `database.xml` ya parece modificado (heurística `s_extended`).
- `MainWindow` con marca: selector de base, estados de update / image pack / noticias (placeholder), botón Jugar.

## Fase 2 — Red: manifest + descargas + hash

- TLS 1.2+ forzado al arranque.
- `ManifestService`: descarga/parseo; resuelve `file` relativos al origen del manifest; rechaza `http://`.
  Escape hatch solo en DEBUG (`file://`/`localhost`) para testear sin bucket real.
- Models: `Manifest`, `LauncherInfo`, `DatabaseEntry`, `ImagePackEntry`, `NewsEntry`.
- `DownloadService`: `HttpClient` → caché en `%LOCALAPPDATA%\…\cache\`, verificación SHA-256 antes de usar.

## Fase 3 — Aplicar base

- `DatabaseService`: backup de la original en 1er arranque; backup con timestamp antes de cada
  sobrescritura; copia de la base elegida a `StreamingAssets\Database\database.xml`. Recuerda elección.

## Fase 4 — Pack de imágenes

- `ImagePackService`: descarga solo si cambió `version`/`sha256`; verifica SHA-256; **extracción
  entrada-por-entrada con protección zip-slip** confinada a `<SunAndMoon>\images\`; merge; registra versión.

## Fase 5 — Lanzar + aviso de update

- `GameLauncher`: `Process.Start` de la instalación del usuario.
- Aviso si `launcher.latest_version` > versión embebida → banner con link **hardcodeado** a Releases.

## Fase 6 — Empaquetado + docs + CI

- Costura → single-exe (`dotnet publish -c Release`).
- docs: README, instrucciones SmartScreen, nota de primer arranque.
- `.github/workflows/`: build + Release + SignPath (scaffold).

## Deferred / placeholders

- **Noticias v2**: `NewsService` stub + sección placeholder en UI; formato `.md` + manifest a definir.
- **URLs reales** del bucket R2 y de Releases: constantes con TODO hasta definirlas.

## Verificación

Build + ventana real en cada fase. Red/DB/zip se prueban con manifest+bucket falso local (escape hatch
DEBUG). Integración con el bucket real queda para cuando existan las URLs.
