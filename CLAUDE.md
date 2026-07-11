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
src/bin/Debug/net48/L5ArgentinaLauncher.exe --buscador          # abre el Buscador directo (solo DEBUG)
src/bin/Debug/net48/L5ArgentinaLauncher.exe --mazos             # abre el editor de Mazos directo (solo DEBUG)
```

Ojo: el exe es WinExe — desde PowerShell hay que esperarlo con `Start-Process -Wait` para
leer el exit code del selftest.

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

Si tocás red/descarga/extracción, el motor del Buscador o el DeckRepository, corré
`--selftest` y mantené **todos** los checks en verde (177 hoy: pipeline + catálogo + pool +
stores + 53 golden Buscador + 25 golden deckbuilder + persistencia txt de mazos).

## Cosas a saber

- "Original" NO se hostea: es el backup local capturado en el primer arranque (`DatabaseService`,
  spec §7). "Comunidad" se descarga del bucket.
- `AppConstants.DefaultManifestUrl` y `ReleasesPageUrl` son **placeholders `TODO(URL)`**.
- `L5A_DATA_ROOT` (env var) redirige config/caché/backups a otra carpeta — hook de testing/portab.
- Empaquetado: Costura.Fody embebe los DLLs → exe único. Se distribuye solo el `.exe`.
- Noticias = placeholder (v2). El formato de los `.md` y su sección en el manifest está sin definir.

## Buscador de cartas (ver docs/BUSCADOR.md)

- **Fuente de datos: cards.json/rules.json/filters.json** (los mismos artefactos que la app
  Android), NO el `database.xml` — el Buscador usa campos que no existen en el XML (keywords
  editoriales, legalidades por printing). El `database.xml` sigue siendo blob opaco.
- Motor en memoria (`CardSearchEngine`): port 1:1 del SQL de la app Android, con semánticas
  SQLite replicadas (LIKE = contains ASCII-CI, IN = Ordinal). **No cambiar semántica sin
  regenerar los golden** (`docs/handoff/buscador-launcher/fixtures/golden-tests.json`).
- Los bugs documentados con ⚠️ en las specs del handoff se portan a propósito (paridad);
  los arreglos de UX van en la capa UI (ej.: rareza manda 'P' y 'p').
- El catálogo se sincroniza desde la sección opcional `catalog` del manifest a
  `cache/catalog/`; sin ella, el Buscador usa lo que haya cacheado/copiado a mano ahí.

## Mazos / deckbuilder (ver docs/MAZOS.md)

- `DeckRepository` (`src/Services/Deck/`) es port 1:1 de `DeckRepository.kt`
  (`docs/handoff/deckbuilder-launcher/`): orden de chequeos de `AddCard`, import/export en el
  formato de texto de la app, `NoActiveDeck` con su nombre engañoso incluido. **No cambiar
  semántica sin regenerar los golden** (`fixtures/golden-tests.json`, 25 casos).
- **Persistencia: un .txt por mazo en la carpeta `Decks` de Sun and Moon** (formato del juego:
  `Legality:`, starter sin prefijo, `Nx Nombre`) + sidecar `decksmeta.json` en %APPDATA%.
  La resolución tolerante de nombres (strongholds sin sufijo "(N)", case, "- exp") vive SOLO
  en el parser de txt (`GetCardByNameRelaxed`); `ImportCards` mantiene el match exacto.
  Cada entrada conserva el nombre original del archivo (`SourceName`) y las líneas no
  reconocidas se reescriben verbatim: nunca romperle el mazo al juego.
- No hay motor de validación (paridad con la app): solo rechazos de `AddCard` + indicadores
  blandos. Diferencia deliberada: el launcher SÍ muestra el `AddCardResult` (la app lo ignora).
