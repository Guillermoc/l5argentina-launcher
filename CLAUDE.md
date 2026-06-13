# CLAUDE.md — guía para agentes en este repo

Launcher WPF (.NET Framework 4.8, SDK-style) para la comunidad L5Argentina. Lanza la app
**Sun and Moon** y sincroniza la base de cartas/imágenes desde un bucket Cloudflare R2.

## Build / run / test

`dotnet` no está en el PATH global de la máquina de dev original; se instaló user-local en
`%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`. En CI/otras máquinas usar `dotnet` normal.

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Debug
dotnet run  --project src/L5ArgentinaLauncher.csproj
dotnet build src/L5ArgentinaLauncher.csproj -c Release          # exe único en bin/Release/net48
src/bin/Debug/net48/L5ArgentinaLauncher.exe --selftest out.txt  # verificación (solo DEBUG)
```

`nuget.config` (raíz) fija nuget.org explícitamente porque el config global estaba vacío.

## Reglas de diseño innegociables (spec §8)

1. **Solo datos, nunca código.** Sin auto-update del exe, sin plugins, sin WebView.
2. **SHA-256** de todo lo descargado, verificado antes de usar (`DownloadService`).
3. **HTTPS siempre.** `file://`/`localhost` solo bajo `#if DEBUG` (escape hatch de testing en
   `ManifestService.EnsureAllowedScheme`).
4. **Confinamiento al origen del manifest** (`ManifestService.ResolveFileUri`): nunca seguir URLs
   absolutas de terceros ni escapar del directorio del manifest.
5. **Zip-slip** obligatorio al extraer imágenes (`ImagePackService`).
6. **Link de update hardcodeado** (`AppConstants.ReleasesPageUrl`), nunca del manifest.

Si tocás red/descarga/extracción, corré `--selftest` y mantené los 27 checks en verde.

## Cosas a saber

- "Original" NO se hostea: es el backup local capturado en el primer arranque (`DatabaseService`,
  spec §7). "Comunidad" se descarga del bucket.
- `AppConstants.DefaultManifestUrl` y `ReleasesPageUrl` son **placeholders `TODO(URL)`**.
- `L5A_DATA_ROOT` (env var) redirige config/caché/backups a otra carpeta — hook de testing/portab.
- Empaquetado: Costura.Fody embebe los DLLs → exe único. Se distribuye solo el `.exe`.
- Noticias = placeholder (v2). El formato de los `.md` y su sección en el manifest está sin definir.
