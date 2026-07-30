# OVERVIEW — estado actual del proyecto

Índice raíz del launcher. **Se lee al empezar cualquier sesión**, junto con
[../ROADMAP.md](../ROADMAP.md). Regla: leer los índices antes que el código fuente.

Última actualización: **v0.6.0** (2026-07-28).

---

## Datos rápidos

| | |
|---|---|
| **Qué es** | Launcher de escritorio (Windows) que sincroniza la base de cartas curada de L5Argentina y lanza la app **Sun and Moon**. Incluye Buscador de cartas y editor de Mazos. |
| **Stack** | WPF sobre **.NET Framework 4.8** (`net48`), csproj SDK-style, compilado con la CLI `dotnet`. JSON con `System.Text.Json`. |
| **Empaquetado** | **Costura.Fody** embebe los DLLs → un único `.exe` (~7 MB). Se distribuye solo el exe. |
| **Arquitectura en una línea** | UI (WPF, 5 ventanas) → `LauncherEngine` coordina → `Services/` (red, hash, archivos) + motores portados 1:1 de la app Android (`Services/Search`, `Services/Deck`). |
| **Datos** | Bucket Cloudflare R2 (dos manifests: el del launcher y el del pool/CDN de la app). El launcher **solo descarga datos, nunca código**. |
| **Versión** | `AppConstants.LauncherVersion` = `0.6.0` (y `<Version>` del csproj — se bumpean juntos). |
| **Verificación** | `--selftest`: la batería completa en verde (pipeline + 53 golden del Buscador + 25 del deckbuilder). Conteo al día en [build-verificacion.md](build-verificacion.md). |
| **Idioma de UI** | Español, 100 % en `src/Resources/Strings.resx` (i18n listo, sin segundo idioma hoy). |

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Debug
dotnet run  --project src/L5ArgentinaLauncher.csproj
src/bin/Debug/net48/L5ArgentinaLauncher.exe --selftest out.txt   # solo DEBUG
```

Detalle de build, flags de debug y CI: [build-verificacion.md](build-verificacion.md).

---

## Índices por dominio

Fuera de las capas: el código en [`../../src/`](../../src/), los datos de test en
[`../../tests/`](../../tests/) (golden tests + harness de regeneración), la guía para jugadores en
[`../USUARIOS.md`](../USUARIOS.md) y los assets de marca originales en [`../things/`](../things/)
(fuentes, logos y fondos; los que compila la app están copiados en `src/Assets/`).
Mapa completo de la documentación: [`../README.md`](../README.md).

| Índice | Cubre |
|---|---|
| [sincronizacion.md](sincronizacion.md) | Manifests, descargas + SHA-256, aplicar base, packs de imágenes, catálogo, config del usuario, primer arranque, lanzar el juego. |
| [buscador.md](buscador.md) | Motor de búsqueda portado de la app Android, catálogo (cards/rules/filters), ventana del Buscador, favoritas/ocultas/presets, conjunto activo como filtro. |
| [mazos.md](mazos.md) | `DeckRepository` portado, mazos y listas como `.txt` (`Decks\` y `Lists\`), editor con solapa Mazos \| Listas, integración con el Buscador. |
| [ui.md](ui.md) | Ventanas, tema/estética, i18n (`Strings.resx`), estilos compartidos, render de texto de cartas, resolución de imágenes. |
| [build-verificacion.md](build-verificacion.md) | csproj/Costura, self-test, CI, release y firma, hooks de testing. |

---

## Mapa de archivos fuente

Todo cuelga de `src/`. Los `.xaml.cs` de las ventanas grandes concentran la lógica de UI
(convención vigente: **code-behind, sin MVVM** — ver [ui.md](ui.md)).

### Arranque y constantes

| Archivo | Qué hace |
|---|---|
| `App.xaml(.cs)` | Arranque: fuerza **TLS 1.2**, rutea `--selftest` / `--buscador` / `--mazos` (solo DEBUG), decide primer arranque vs `MainWindow`. |
| `AppConstants.cs` | Versión embebida, URLs de producción (manifest, pool, releases) y **todas** las rutas de estado (`%APPDATA%`, `%LOCALAPPDATA%`), con el override `L5A_DATA_ROOT`. |
| `SelfTest.cs` | Harness de verificación (solo DEBUG): corre el pipeline real + los golden tests de paridad. |

### UI: ventanas y piezas compartidas (`Views/` y raíz)

Solo las cuatro primeras son ventanas (`Window`); el resto son controles y recursos compartidos.
Configuración y Packs de imágenes no son ventanas: son *overlays* dentro de `MainWindow`.

| Archivo | Qué hace |
|---|---|
| `MainWindow.xaml(.cs)` | Ventana principal: selección de base, banner de update, sincronización, JUGAR, overlays de Configuración y Packs de imágenes. |
| `Views/FirstRunWindow.xaml(.cs)` | Primer arranque: elegir `Sun and Moon.exe`, validar la instalación, capturar la base **Original**. |
| `Views/SearchWindow.xaml(.cs)` | Buscador: filtros en vivo, tabla/grilla virtualizada, panel derecho Carta \| Mazo, selector de conjunto activo y presets. |
| `Views/DeckWindow.xaml(.cs)` | Editor de Mazos **y listas** (solapa Mazos \| Listas): grupos Starter/Dynasty/Fate, cartas recortadas, importar/exportar, notas. |
| `Views/PdfExportWindow.xaml(.cs)` | Pantalla previa del PDF de impresión: copias por carta, A4/Carta, margen de 1 mm. |
| `Views/CardDetailPanel.xaml(.cs)` | Detalle de carta compartido entre Buscador y editor (+ `CardImageResolver`). |
| `Views/CardUiStyles.xaml` | Estilos XAML compartidos entre ambas ventanas (chips, checkboxes, celdas). |
| `Views/CardTextRenderer.cs` | HTML acotado del texto de carta → `Inline`s de WPF. **Nunca** WebView. |
| `Views/CardImageClipboard.cs` | Arte a tamaño completo al portapapeles (click derecho) y para la previsualización con Alt: re-decodifica el archivo, no copia la miniatura de la grilla. |
| `Views/Dialogs.cs` | `Prompt` / `Confirm` / `Ask` / `Info` con la estética de la app, para Buscador y Mazos (que no tienen overlay propio). Nada de `MessageBox`. |
| `Views/HoverPreviewPlacer.cs` | Ubica la previsualización con Alt garantizando que **el cursor nunca quede adentro** del popup (el volteo de `Placement=Mouse` lo metía debajo del mouse contra el borde derecho y arrancaba un parpadeo infinito) — [ADR-052](../DECISIONS.md#adr-052). |
| `Views/WindowSizingFix.cs` | Acota Buscador y Mazos al **área de trabajo** del monitor al abrir y al maximizar (`WM_GETMINMAXINFO`): sin esto quedaban debajo de la barra de tareas. |
| `Themes/Theme.xaml` | Paleta, fuentes, fondo, scrollbar temática — estilos de toda la app. |

### Servicios de red / archivos (`Services/`)

| Archivo | Qué hace |
|---|---|
| `LauncherEngine.cs` | Coordinador del flujo: manifest, ¿hay que bajar?, aplicar base, imágenes, packs del pool, catálogo, lanzar. La UI orquesta y muestra; la lógica está acá. |
| `ManifestService.cs` | Fetch + parseo de los dos manifests. `EnsureAllowedScheme` (HTTPS; `file://`/localhost solo DEBUG), `ResolveFileUri` (relativo + confinado al directorio del manifest), `ResolveSameOriginUrl` (absolutas del pool, mismo origen). |
| `DownloadService.cs` | Descarga a `.part`, calcula SHA-256 al vuelo, verifica y recién ahí promueve el archivo final. Reporta progreso. |
| `DatabaseService.cs` | Captura de la base **Original** (primer arranque), backup con timestamp antes de cada sobrescritura, copia de la base elegida. Heurística "parece base de comunidad" por stream-scan de `s_extended`. |
| `ImagePackService.cs` | Extracción de packs sobre `<SunAndMoon>\images\` con **protección zip-slip** entrada por entrada. |
| `ZipUtil.cs` | Extrae el `database.xml` de una base `.zip`; exige **exactamente un** `.xml`. |
| `InstallationService.cs` | Rutas dentro de la instalación de Sun and Moon y validación (`Sun and Moon.exe` + carpeta `Database`). |
| `ConfigService.cs` | Lee/escribe `config.json` con escritura atómica (`.tmp` + replace). |
| `GameLauncher.cs` | `Process.Start` del exe del usuario. |
| `HashUtil.cs` / `VersionUtil.cs` | SHA-256 de archivo + comparación case-insensitive; comparación numérica de versiones. |
| `NewsService.cs` | Stub de noticias (v2, no implementado). |

### Buscador (`Services/Search/`, ver [buscador.md](buscador.md))

| Archivo | Qué hace |
|---|---|
| `CatalogImporter.cs` | Port del importer de la app: `cards.json` + `rules.json` + `filters.json` → catálogo en memoria. |
| `CardSearchEngine.cs` | Motor de filtros: port 1:1 del SQL de la app con semánticas SQLite replicadas. |
| `CardSearchCriteria.cs` | Criterios de búsqueda (nombres JSON compatibles con los golden). |
| `CatalogStore.cs` | Encuentra y carga el trío versionado más alto en `cache/catalog/`. |
| `UserCardDataStore.cs` | Favoritas / ocultas → `usercards.json` v2, **por nombre de carta** ([ADR-044](../DECISIONS.md#adr-044)); migra el v1 y manda sus listas a `Lists\`. |
| `FilterPresetStore.cs` | Presets de filtros con nombre → `presets.json`. |

### Mazos (`Services/Deck/`, ver [mazos.md](mazos.md))

| Archivo | Qué hace |
|---|---|
| `DeckRepository.cs` | Port 1:1 de `DeckRepository.kt`: `AddCard`, quantities, import/export, CRUD, espejos de UI. |
| `SunAndMoonDeckFile.cs` | Parse/write del formato `.txt` de la carpeta `Decks` del juego (tolerante a nombres). |
| `DeckLibrary.cs` | Un `.txt` por conjunto: mazos en `Decks`, listas en `Lists` + sidecar `decksmeta.json`; mazo y lista activos compartidos por proceso. |
| `PdfExporter.cs` | Emite el PDF de impresión a mano (sin librería): grilla 3×3 a tamaño real, JPEG embebidos sin recomprimir ([ADR-049](../DECISIONS.md#adr-049)). |

### Modelos y recursos

| Archivo | Qué hace |
|---|---|
| `Models/Manifest.cs` | Manifest del launcher (`launcher`, `databases`, `images`, `catalog`, `news`). |
| `Models/PoolManifest.cs` | Manifest del CDN de la app: lista plana de `packages` con URL absoluta + SHA-256. |
| `Models/UserConfig.cs` | `config.json`: instalación, URL de manifest, base aplicada/seleccionada, hashes cacheados, packs aplicados. |
| `Models/CardCatalog.cs` | `Card` (espejo de la tabla `cards` de la app) + catálogo + slots de holding inicial. |
| `Models/DeckModels.cs` | `Deck`/`DeckCardEntry`, `AddCardResult`, `ImportResult`, `DeckGroups`. |
| `Resources/Strings.resx` | **Todos** los textos de UI. `Strings.cs` (accesor) + `LocExtension.cs` (`{loc:Loc Clave}`). |
| `Assets/` | Fondos, logos, íconos y fuentes (`l5rtitles.ttf`, `playfair_display.ttf`) embebidos como recursos WPF. |

---

## Datos externos que consume

| Origen | Qué trae | Constante |
|---|---|---|
| Manifest del launcher (bucket R2) | Base Comunidad (`.zip`/`.xml`), pack de imágenes propio, versión del launcher, sección opcional `catalog`. | `AppConstants.DefaultManifestUrl` (editable por el usuario) |
| Manifest del pool (CDN de la app Android, canal `production`) | Packs de imágenes oficiales por arco + catálogo del Buscador (`cards_db`/`rules`/`filters`). **Fuente primaria del catálogo**; la sección `catalog` del manifest del launcher es solo respaldo ([ADR-021](../DECISIONS.md#adr-021)). | `AppConstants.PoolManifestUrl` (**hardcodeado**) |
| Página de Releases | Link del aviso "hay versión nueva". | `AppConstants.ReleasesPageUrl` (**hardcodeado**, nunca del manifest) |

Contrato de formato y ejemplo real: [../technical/contratos/manifest.example.json](../technical/contratos/manifest.example.json).

---

## Dónde vive el estado del usuario

**Dos raíces, con reglas distintas.** Lo que es del launcher cuelga del perfil del usuario y lo
redirige `L5A_DATA_ROOT` (hook de testing/portabilidad). Lo que es del **juego** —`database.xml`,
`images\` y los `.txt` de `Decks\` y `Lists\`— cuelga de la instalación configurada
(`sunAndMoonPath`) y **no** se redirige: moverlo rompería que el juego lea el mazo
([ADR-022](../DECISIONS.md#adr-022)). Único fallback: sin instalación configurada, mazos y listas
caen en `%APPDATA%\...\Decks` y `%APPDATA%\...\Lists`.

Nada depende de dónde esté el `.exe`: moverlo no cambia ninguna ruta.

| Ruta | Contenido |
|---|---|
| `%APPDATA%\L5Argentina Launcher\config.json` | Instalación, URL del manifest, base aplicada, hashes cacheados, packs aplicados. |
| `%APPDATA%\...\usercards.json` | Favoritas y ocultas del Buscador, **por nombre de carta** ([ADR-044](../DECISIONS.md#adr-044)). |
| `%APPDATA%\...\presets.json` | Presets de filtros con nombre. |
| `%APPDATA%\...\decksmeta.json` | Sidecar de mazos y listas (notas, orden, mazo y lista activos, cartas recortadas). |
| `%LOCALAPPDATA%\...\cache\` | Descargas verificadas (`catalog/`, `packs/`). |
| `%LOCALAPPDATA%\...\backups\` | `database.original.xml` + copias con timestamp. |
| `<SunAndMoon>\Decks\*.txt` | **Los mazos** (los mismos archivos que carga el juego). **No** los redirige `L5A_DATA_ROOT`. |
| `<SunAndMoon>\Lists\*.txt` | **Las listas** (mismo formato, 1 copia por carta; el juego no las lee). Tampoco se redirigen. |

Detalle y criterios: [../design/persistencia.md](../design/persistencia.md).

---

## Qué NO hace (no-objetivos vigentes)

- No se auto-actualiza (avisa y linkea, con URL hardcodeada).
- No descarga ni ejecuta código; sin plugins, sin WebView.
- No redistribuye Sun and Moon: lanza la instalación del usuario.
- No genera ni parsea el `database.xml` (blob opaco). El Buscador usa `cards.json`/`rules.json`/
  `filters.json` porque el XML no tiene keywords editoriales ni legalidades por printing
  ([ADR-016](../DECISIONS.md#adr-016)) — no unificar las fuentes.
- No valida mazos más allá de lo que valida la app Android (paridad deliberada).
