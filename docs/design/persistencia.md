# Diseño transversal — persistencia y estado local

Dónde vive cada cosa que el launcher guarda, y con qué criterio. Aplica a todos los dominios: si se
agrega estado nuevo, se decide acá primero.

---

## Mapa

| Qué | Dónde | Formato | Escribe |
|---|---|---|---|
| Config del usuario | `%APPDATA%\L5Argentina Launcher\config.json` | JSON (`UserConfig`) | `ConfigService` |
| Favoritas / ocultas / listas | `%APPDATA%\...\usercards.json` | JSON | `UserCardDataStore` |
| Presets de filtros | `%APPDATA%\...\presets.json` | JSON (nombres compatibles con los golden) | `FilterPresetStore` |
| Metadata de mazos | `%APPDATA%\...\decksmeta.json` | JSON, indexado por **nombre de archivo** | `DeckLibrary` |
| **Los mazos** | `<SunAndMoon>\Decks\<Nombre>.txt` | formato del juego | `DeckLibrary` + `SunAndMoonDeckFile` |
| Descargas verificadas | `%LOCALAPPDATA%\...\cache\` (`catalog\`, `packs\`) | archivos tal cual | `DownloadService` |
| Base Original | `%LOCALAPPDATA%\...\backups\database.original.xml` | XML | `DatabaseService` |
| Backups con timestamp | `%LOCALAPPDATA%\...\backups\database.<stamp>.xml` | XML | `DatabaseService` |
| Base activa | `<SunAndMoon>\Sun and Moon_Data\StreamingAssets\Database\database.xml` | XML | `DatabaseService` |
| Imágenes | `<SunAndMoon>\images\cards\<set>\` | JPG | `ImagePackService` |

**Todas** las rutas salen de `AppConstants` (o de `InstallationService` / `DeckLibrary` para las del
juego). Ninguna se arma a mano en otro lado. Las filas de `%APPDATA%`/`%LOCALAPPDATA%` respetan
`L5A_DATA_ROOT`; las de `<SunAndMoon>` **no** (criterio 0).

---

## Criterios

**0. Hay dos raíces y `L5A_DATA_ROOT` solo mueve una.** Lo que es **del launcher** (config, caché,
backups, `usercards.json`, `presets.json`, `decksmeta.json`) cuelga del perfil del usuario y sí se
redirige. Lo que es **del juego** (`database.xml`, `images\`, los `.txt` de `Decks\`) cuelga de la
instalación configurada en `sunAndMoonPath` y **no** se redirige nunca: si `Decks` se moviera, el
juego dejaría de leer el mazo y se perdería el sentido de [ADR-022](../DECISIONS.md#adr-022).
Único fallback: `DeckLibrary.ResolveDecksDir` cae en `%APPDATA%\...\Decks` **solo** cuando no hay
instalación configurada.

> **Consecuencia para pruebas manuales:** `L5A_DATA_ROOT` **no** protege los mazos del usuario. Para
> probar mazos a mano hay que apuntar `sunAndMoonPath` a una copia del juego, o dejarlo vacío para
> caer en el fallback. El self-test no tiene este problema: le pasa la carpeta de mazos explícita.

Ninguna de las dos raíces depende de dónde esté el `.exe`. Moverlo no cambia ninguna ruta: la única
ruta relativa al ejecutable es el fallback de **lectura** de imágenes del Buscador (para el caso de
que alguien lo haya copiado dentro de la carpeta del juego).

**1. `AppConstants` es la única fuente de rutas.** Porque `L5A_DATA_ROOT` redirige el árbol de estado
propio ([ADR-010](../DECISIONS.md#adr-010)): si una ruta se construye por fuera, el self-test deja de
estar aislado y el modo portable se rompe en silencio. Las rutas del juego salen de
`InstallationService` y de `DeckLibrary.ResolveDecksDir`, por el mismo motivo.

**2. `%APPDATA%` = configuración del usuario; `%LOCALAPPDATA%` = datos regenerables.** Lo que se
puede volver a bajar (caché) o lo que es un respaldo pesado (backups) no viaja con el perfil.

**3. Escrituras atómicas.** Config: `.tmp` + replace. Descargas: `.part` + verificación de hash +
replace. Nunca queda un archivo a medias que un arranque posterior tome por bueno.

**4. El estado "¿hay que redescargar?" se guarda, no se recalcula.** `config.json` conserva
`version`+`sha256` de cada cosa aplicada (`cachedDatabases`, `cachedCatalogFiles`,
`appliedImagePacks`, `appliedImagePack*`). Se compara contra el manifest, sin re-hashear la caché en
cada arranque.

**5. Los archivos del juego mandan sobre el estado propio.** Los mazos son `.txt` del juego
([ADR-022](../DECISIONS.md#adr-022)) y el `decksmeta.json` es un **sidecar**: si se pierde o se
corrompe, se regenera y no se pierde ningún mazo. Por eso se indexa por nombre de archivo y no por
un id interno.

**6. Nunca se destruye contenido ajeno.** El `database.xml` se respalda antes de cada sobrescritura;
la Original se captura una sola vez y no se pisa nunca; las líneas de un `.txt` que no se entienden
se reescriben verbatim ([ADR-024](../DECISIONS.md#adr-024)); los packs de imágenes se **fusionan**
con los existentes (agregan/actualizan, no reemplazan la carpeta).

**7. Los zips grandes no se conservan.** Los packs de imágenes del pool (hasta ~280 MB) se borran
después de aplicarse; lo que queda es el registro por hash en `config.json`.

---

## Al agregar estado nuevo

1. Agregar la ruta a `AppConstants` (no hardcodear en el servicio).
2. Decidir `%APPDATA%` (config del usuario) vs `%LOCALAPPDATA%` (regenerable).
3. Si es JSON de usuario: modelo con `[JsonPropertyName]` explícito y tolerancia a archivo ausente o
   corrupto (arrancar con valores por defecto, nunca crashear).
4. Si afecta el pipeline de descarga/aplicación: sumar checks al self-test.
5. Anotarlo acá y en [../index/OVERVIEW.md](../index/OVERVIEW.md).
