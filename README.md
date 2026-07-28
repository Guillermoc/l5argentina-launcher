# L5Argentina Launcher

Launcher de escritorio (Windows) para la comunidad **L5Argentina**, que juega *Legend of the Five
Rings* en la app **Sun and Moon**. Resuelve el problema de mantener sincronizada, entre ~25 personas,
nuestra base de cartas curada: en lugar de copiar archivos a mano, el launcher chequea si hay una base
nueva en nuestro bucket, deja **elegir con qué base jugar**, la aplica de forma segura y lanza el juego.

Versión actual: **0.6.0**.

> **Documentación:** mapa completo en [docs/README.md](docs/README.md) · estado actual del código en
> [docs/index/](docs/index/) (empezar por
> [OVERVIEW](docs/index/OVERVIEW.md)) · el proyecto entero de corrido en
> [docs/technical/technical-specification.md](docs/technical/technical-specification.md) ·
> por qué está hecho así en [docs/DECISIONS.md](docs/DECISIONS.md) · qué falta en
> [docs/ROADMAP.md](docs/ROADMAP.md) · guía para usuarios en [docs/USUARIOS.md](docs/USUARIOS.md).

---

## Estado de las features

| Feature | Estado | Desde | Detalle |
|---|---|---|---|
| Detección de la instalación + captura de la base Original | ✅ | v0.1.0 | [index/sincronizacion.md](docs/index/sincronizacion.md) |
| Sincronizar y aplicar la base (Comunidad / Original) | ✅ | v0.1.0 | [index/sincronizacion.md](docs/index/sincronizacion.md) |
| Pack de imágenes propio de la comunidad | ✅ | v0.1.0 | [index/sincronizacion.md](docs/index/sincronizacion.md) |
| Aviso de versión nueva del launcher | ✅ | v0.1.0 | [DECISIONS ADR-009](docs/DECISIONS.md#adr-009) |
| Configuración (carpeta del juego, URL del manifest) + modo offline | ✅ | v0.1.0 | [index/sincronizacion.md](docs/index/sincronizacion.md) |
| Buscador de cartas (filtros, grilla/tabla, detalle) | ✅ | v0.2.0 | [index/buscador.md](docs/index/buscador.md) |
| Favoritas, ocultas, listas y presets de filtros | ✅ | v0.2.0 | [index/buscador.md](docs/index/buscador.md) — las listas cambiaron de forma en v0.6.0 |
| Packs de imágenes oficiales por arco (pool) | ✅ | v0.2.0 | [index/sincronizacion.md](docs/index/sincronizacion.md) |
| Mazos / deckbuilder (editor + integración en el Buscador) | ✅ | v0.3.0 | [index/mazos.md](docs/index/mazos.md) |
| Mazos guardados como `.txt` en la carpeta `Decks` del juego | ✅ | v0.3.0 | [DECISIONS ADR-022](docs/DECISIONS.md#adr-022) |
| Un solo mazo activo, compartido por todas las ventanas | ✅ | v0.4.0 | [DECISIONS ADR-042](docs/DECISIONS.md#adr-042) |
| `Ctrl+click` para agregar al mazo, con ayuda visual | ✅ | v0.4.0 | [DECISIONS ADR-041](docs/DECISIONS.md#adr-041) |
| Copiar el arte de la carta al portapapeles | ✅ | v0.4.0 | [index/ui.md](docs/index/ui.md) |
| Rediseño de controles (rieles, pestañas, menús, diálogos) | ✅ | v0.4.0 | [DECISIONS ADR-035](docs/DECISIONS.md#adr-035) |
| Listas editables como mazos (`.txt` en `Lists`, tope 1 por carta) | ✅ | v0.6.0 | [DECISIONS ADR-045](docs/DECISIONS.md#adr-045) |
| Filtrar el Buscador por un mazo, una lista o las ocultas | ✅ | v0.6.0 | [index/buscador.md](docs/index/buscador.md) |
| Estado del usuario referenciado por nombre de carta, no por id | ✅ | v0.6.0 | [DECISIONS ADR-044](docs/DECISIONS.md#adr-044) |
| Cartas recortadas en el mazo (bajar a 0 sin perderlas) | ✅ | v0.6.0 | [DECISIONS ADR-046](docs/DECISIONS.md#adr-046) |
| Abrir mazos con orden, filtro por clan y estado (terminado/pendiente) | ✅ | v0.6.0 | [DECISIONS ADR-047](docs/DECISIONS.md#adr-047) |
| Resumen de filtros activos y copiar imagen con click derecho | ✅ | v0.6.0 | [index/buscador.md](docs/index/buscador.md) |
| Zoom de grilla en 4 tamaños, previsualización con Alt y atajos (`?`) | ✅ | v0.6.0 | [index/ui.md](docs/index/ui.md) |
| Ventanas lado a lado y adaptación a ventanas angostas | ✅ | v0.6.0 | [index/ui.md](docs/index/ui.md) |
| Firma del ejecutable (SignPath) | ⏳ esperando aprobación OSS | — | [ROADMAP §1](docs/ROADMAP.md) |
| Noticias y torneos | ⬜ placeholder en UI, sin implementar | — | [functional/noticias-v2-borrador.md](docs/functional/noticias-v2-borrador.md) |
| Firma del `manifest.json` | ⏸️ diferido con criterio | — | [DECISIONS ADR-015](docs/DECISIONS.md#adr-015) |

---

## Qué hace

- **Detecta la instalación de Sun and Moon.** En el primer arranque pide el `Sun and Moon.exe` y valida
  que la instalación sea correcta. Guarda la ruta en la config del usuario.
- **Respalda tu base "Original".** En ese primer arranque copia tu `database.xml` actual a un backup
  propio, que después se ofrece como la base **Original** (sin nuestras modificaciones). Si detecta que
  la base actual ya parece modificada, avisa.
- **Chequea actualizaciones.** Al abrir, descarga el `manifest.json` del bucket y compara versiones/hashes
  contra lo que hay en caché local.
- **Deja elegir la base:**
  - **Comunidad** (`s_extended`): la base curada por nosotros. Se descarga del bucket (viene comprimida
    en `.zip`; el launcher la verifica, extrae el `database.xml` y lo aplica).
  - **Original**: el backup local capturado en el primer arranque.
  - Recuerda la última elección.
- **Aplica la base de forma segura.** Antes de pisar el `database.xml` vigente, hace un backup con
  timestamp; verifica el **SHA-256** de lo descargado contra el manifest antes de tocar nada.
- **Sincroniza las imágenes propias.** Descarga (solo si cambió) el pack de imágenes de las cartas que no
  están en los packs clásicos, verifica su SHA-256 y lo extrae sobre la carpeta de imágenes con
  **protección zip-slip**.
- **Lanza Sun and Moon** (la instalación del usuario).
- **Buscador de cartas** (desde 0.2.0): ventana propia con la **misma semántica de filtros que la app
  Android L5Argentina** — el motor es un port 1:1 verificado contra 53 *golden tests* generados con la
  lógica real de la app. Búsqueda por nombre y texto de carta, filtros por keywords (con autocomplete),
  tipo, clan, designador, stats, edición (agrupada por arcos), rareza y baneadas; resultados en tabla o
  **grilla de miniaturas** con expansión por impresión; favoritas/ocultas persistentes; detalle con el
  arte de la carta, texto renderizado y etiquetas UNIQUE/BANNED data-driven del `rules.json`. El catálogo
  (cards_db/rules/filters) se sincroniza solo desde el manifest de producción del CDN de la app.
  Desde 0.6.0 suma **listas de cartas** y la posibilidad de filtrar por un conjunto cualquiera —una
  lista, un mazo o tus ocultas—, resumen de los filtros activos al lado del contador, **cuatro tamaños
  de grilla** (`Ctrl + rueda`), **Alt + pasar el mouse** para ver la carta en grande, click derecho para
  copiar el arte y un `?` con todos los atajos.
- **Packs de imágenes opcionales** (desde 0.2.0): menú para descargar el arte oficial de cartas por arco
  (Lotus, Samurai, Celestial, Emperor, Ivory, Twenty Festivals, Promo) desde el CDN de la app; cada pack
  se verifica por SHA-256 y se instala sobre la carpeta `images` de Sun and Moon — sirve al juego y al
  Buscador. El pack **Samurai Extended** no aparece ahí: es el nuestro, se aplica solo durante la
  sincronización y se muestra en la barra de "Imágenes Samurai Extended". Antes salía en los dos lados
  y se descargaba dos veces.
- **Mazos / deckbuilder** (desde 0.3.0): editor de mazos, port 1:1 del `DeckRepository` de la app
  Android (mismo orden de validaciones: baneada → slot → tope de copias). **Persiste como .txt en la
  carpeta `Decks` de Sun and Moon** — el mazo que armás acá se juega directo, sin exportar nada — con un
  sidecar (`decksmeta.json`) para lo que el formato del juego no representa (notas, orden, mazo y lista
  activos, cartas recortadas).
  Ventana propia (crear/renombrar/duplicar/borrar mazos, importar/exportar por portapapeles, vista lista
  o grilla, agrupado Starter/Dynasty/Fate). **El mazo seleccionado ahí es el mazo activo** de todas las
  ventanas del Buscador, que muestran su nombre y sus conteos en la barra y agregan cartas con `+`,
  doble click o **Ctrl+click** sobre la carta. A diferencia de la app, acá **sí se muestra** por qué una
  carta no entró (baneada, slot cubierto, tope alcanzado).
  Desde 0.6.0 el mismo editor abre también **listas** —material de armado, 1 copia por carta, `.txt` en
  la carpeta `Lists`—, bajar una carta a 0 la deja **recortada** en vez de borrarla, la lista de mazos
  se ordena y se filtra por clan y por **estado de armado**, y un botón acomoda el Buscador y el editor
  **lado a lado** para armar mirando las dos cosas.
- **Tu estado se guarda por nombre de carta, no por id** (desde 0.6.0). El id lo asigna quien genera la
  base de la comunidad y puede reasignarse entre versiones, así que guardarlo hacía que una favorita
  pudiera desaparecer —o pasar a ser otra carta— al actualizar. Al abrir el Buscador por primera vez
  después de actualizar, el `usercards.json` se migra solo y el archivo viejo queda al lado como
  `usercards.v1.json`.
- **Avisa si hay un launcher nuevo.** Si el manifest indica una versión más nueva, muestra un banner con
  link a la página de descargas (el link va **hardcodeado en el exe**, nunca se toma del manifest).
- **Configuración:** permite cambiar la carpeta de Sun and Moon y la URL del manifest (con default
  hardcodeado y restaurable). Modo **offline** elegante si el bucket no está disponible.
- **Noticias y torneos:** sección presente como *placeholder* (funcionalidad v2).

---

## Seguridad (resumen)

El principio rector: **el launcher solo descarga datos (XML, JSON, ZIP), nunca código.** Detalle:

- **Integridad por SHA-256**: se verifica el hash de cada archivo descargado *antes* de usarlo; si no
  coincide, aborta sin tocar la instalación.
- **HTTPS obligatorio** y **TLS 1.2+** forzado.
- **Confinamiento al origen del manifest**: los archivos se resuelven como relativos al manifest; nunca
  se siguen URLs absolutas a otros dominios ni rutas que escapen de su directorio. El manifest del pool
  (CDN de la app) usa URLs absolutas: se aceptan **solo si son del mismo origen** que ese manifest.
- **Extracción zip-slip-safe** de todos los packs de imágenes.
- **Sin auto-update del propio exe**: el link de descarga está hardcodeado, no viene del manifest.
- **Backups** antes de cada sobrescritura (la Original y copias con timestamp).

Las reglas vigentes y cómo se verifican están en
[docs/design/seguridad.md](docs/design/seguridad.md); el threat model completo, en la spec funcional
v1.0 (§8–§10).

---

## UI

WPF con estética **Rokugán / pergamino** (rojo lacado para acción, oro decorativo, tipografía de marca
L5RTitles). Ventana principal **fija 810×540 (3:2)**, no redimensionable, con barra de título propia (el
diseño se compone a 1080×720 y se escala con un `Viewbox`). El **Buscador** y el **editor de Mazos**
son ventanas aparte, redimensionables y maximizables, con el mismo tema y chrome propio; se adaptan al
ancho cerrando sus paneles laterales y escondiendo lo accesorio de la barra cuando quedan angostas.
Diálogos de error/confirmación, Configuración y Packs de imágenes son *overlays* dentro de la ventana
(no `MessageBox` del sistema).

**Textos (i18n):** todos los textos viven en [src/Resources/Strings.resx](src/Resources/Strings.resx)
(español, cultura neutral). En XAML se usan con `{loc:Loc Clave}` y en C# con `Strings.Get/Format`.
Para agregar otro idioma: crear `Strings.<cultura>.resx` con las mismas claves y setear
`CurrentUICulture` — sin tocar código.

---

## Requisitos

- **Para usar el launcher:** Windows 10/11 con .NET Framework 4.8 (viene preinstalado). Nada más.
- **Para compilar:** .NET SDK 8.x (`dotnet`). No requiere Visual Studio.

## Compilar y correr

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Debug     # compilar
dotnet run  --project src/L5ArgentinaLauncher.csproj     # correr la ventana
```

### Release (un solo .exe)

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Release
# Resultado: src/bin/Release/net48/L5ArgentinaLauncher.exe  (~7 MB, autocontenido vía Costura)
```

Para distribuir alcanza con **ese único `.exe`** (no necesita los `.dll`, `.pdb` ni `.exe.config` que
quedan al lado).

### Self-test (solo Debug)

Un harness de verificación monta un bucket+instalación falsos en `%TEMP%` y ejercita el pipeline real
(hash, confinamiento de origen, zip-slip, backups, base zippeada, catálogo y packs del pool) sin tocar
tu instalación — **219 checks**, incluidos los **53 golden tests de paridad** del motor del Buscador y
los **25 golden tests** del deckbuilder, ambos contra la lógica real de la app Android:

```sh
src/bin/Debug/net48/L5ArgentinaLauncher.exe --selftest C:\ruta\reporte.txt
src/bin/Debug/net48/L5ArgentinaLauncher.exe --buscador   # abre el Buscador directo (testing de UI)
src/bin/Debug/net48/L5ArgentinaLauncher.exe --mazos      # abre el editor de Mazos directo (testing de UI)
```

> Hook de testing: la variable de entorno `L5A_DATA_ROOT` redirige config/caché/backups a otra carpeta.

Los datos de los golden tests (y el harness para regenerarlos) están en [tests/](tests/); el detalle
de qué cubre cada bloque, en [docs/index/build-verificacion.md](docs/index/build-verificacion.md).

---

## Configuración del usuario (rutas)

- Config: `%APPDATA%\L5Argentina Launcher\config.json`
- Favoritas/ocultas del Buscador: `%APPDATA%\L5Argentina Launcher\usercards.json` (por **nombre**
  de carta, no por id — así sobrevive a los cambios de base)
- Presets de filtros del Buscador: `%APPDATA%\L5Argentina Launcher\presets.json`
- Sidecar de mazos y listas (notas, orden, mazo/lista activos): `%APPDATA%\L5Argentina Launcher\decksmeta.json`
  — los mazos en sí son `.txt` en la carpeta `Decks` de la instalación de Sun and Moon, y las listas
  `.txt` en `Lists`, al lado (el juego no las lee).
- Caché de descargas: `%LOCALAPPDATA%\L5Argentina Launcher\cache\`
- Backups: `%LOCALAPPDATA%\L5Argentina Launcher\backups\` (la Original + copias con timestamp)

## URLs de producción

En [src/AppConstants.cs](src/AppConstants.cs):

| Constante | Valor |
|---|---|
| `DefaultManifestUrl` | `https://pub-4ab8e43f10604d7fa0f9402a8259a855.r2.dev/sunandmoon/manifest.json` |
| `PoolManifestUrl` | `https://pub-4ab8e43f10604d7fa0f9402a8259a855.r2.dev/production/manifest.json` (CDN de la app: catálogo del Buscador + packs de imágenes; **hardcodeado**) |
| `ReleasesPageUrl` | `https://github.com/Guillermoc/l5argentina-launcher/releases` (link del aviso de update, **hardcodeado a propósito**) |

El formato del `manifest.json`, el del pool y un ejemplo con hashes reales están en
[docs/technical/contratos/](docs/technical/contratos/).

---

## Distribución y firma

- El `.exe` se publica en **GitHub Releases** (no en el bucket — spec §9), vía
  [.github/workflows/release.yml](.github/workflows/release.yml) al pushear un tag `vX.Y.Z`.
- El **SHA-256 del exe** se publica como segundo canal (web/Discord).
- **Firma de código:** este proyecto usa firma de código gratuita para open source provista por
  **[SignPath.io](https://signpath.io)**, con un certificado de la
  **[SignPath Foundation](https://signpath.org)**. (En trámite de aprobación; hasta entonces el exe sale
  sin firmar y el primer arranque muestra el aviso de **SmartScreen** "aplicación no reconocida" — ver
  [docs/USUARIOS.md](docs/USUARIOS.md).)

---

## Arquitectura

> Vista resumida. El mapa de archivos completo y siempre al día está en
> [docs/index/OVERVIEW.md](docs/index/OVERVIEW.md).

```
src/
├─ App.xaml(.cs)              arranque, TLS 1.2+, primer arranque, flags --selftest/--buscador/--mazos
├─ MainWindow.xaml(.cs)       selección de base, banner de update, sync, JUGAR, overlays (config/packs)
├─ AppConstants.cs            URLs, versión, rutas de %APPDATA%/%LOCALAPPDATA%
├─ Themes/Theme.xaml          paleta, fuentes, estilos y fondo (guía de estilos L5Argentina)
├─ Resources/
│  ├─ Strings.resx            TODOS los textos de UI (i18n, español)
│  ├─ Strings.cs              accesor (ResourceManager)
│  └─ LocExtension.cs         markup extension {loc:Loc}
├─ Models/                    Manifest, PoolManifest, UserConfig, CardCatalog, DeckModels
├─ Views/
│  ├─ FirstRunWindow          primer arranque (detección + captura de la Original)
│  ├─ SearchWindow            Buscador de cartas (filtros vivos, tabla/grilla, detalle, mazo activo)
│  ├─ DeckWindow              editor de Mazos y listas (solapas Abrir/Filtros, Starter/Dynasty/Fate, recorte)
│  ├─ CardDetailPanel         detalle de carta compartido entre Buscador y editor de Mazos
│  ├─ CardUiStyles            estilos XAML compartidos (chips, checkboxes, celdas) entre ambas ventanas
│  ├─ CardImageClipboard      arte a tamaño completo al portapapeles y a la previsualización con Alt
│  └─ CardTextRenderer        HTML acotado de cartas → Inlines (sin WebView)
└─ Services/
   ├─ ManifestService         fetch + parseo + resolución confinada (relativa y same-origin)
   ├─ DownloadService         descarga + verificación SHA-256
   ├─ DatabaseService         backup original + backup timestamped + aplicar base
   ├─ ImagePackService        extracción de packs con protección zip-slip
   ├─ ZipUtil                 extracción del database.xml desde la base .zip
   ├─ GameLauncher            Process.Start de Sun and Moon
   ├─ ConfigService           %APPDATA% config
   ├─ LauncherEngine          coordinador del flujo (bases, imágenes, catálogo, packs del pool)
   ├─ NewsService             placeholder (v2)
   ├─ Search/
   │  ├─ CatalogImporter      port del importer de la app (cards.json + rules + filters)
   │  ├─ CardSearchEngine     motor de filtros (port 1:1, semánticas SQLite replicadas)
   │  ├─ CardSearchCriteria   criterios (compatible con los golden tests)
   │  ├─ CatalogStore         carga del catálogo desde cache/catalog
   │  ├─ UserCardDataStore    favoritas/ocultas persistentes (por nombre de carta)
   │  └─ FilterPresetStore    presets de filtros guardados con nombre
   └─ Deck/
      ├─ DeckRepository       port 1:1 de DeckRepository.kt (AddCard, import/export, CRUD, conteos)
      ├─ SunAndMoonDeckFile   lee/escribe el formato .txt de la carpeta Decks del juego
      └─ DeckLibrary          un .txt por mazo en Decks y por lista en Lists + sidecar decksmeta.json
```

Stack: **WPF sobre .NET Framework 4.8**, csproj SDK-style compilado con el **.NET SDK 8**, empaquetado en
un único `.exe` con **Costura.Fody**. JSON con `System.Text.Json`.
