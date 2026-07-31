# Diseño transversal — persistencia y estado local

Dónde vive cada cosa que el launcher guarda, y con qué criterio. Aplica a todos los dominios: si se
agrega estado nuevo, se decide acá primero.

---

## Mapa

| Qué | Dónde | Formato | Escribe |
|---|---|---|---|
| Config del usuario | `%APPDATA%\L5Argentina Launcher\config.json` | JSON (`UserConfig`) | `ConfigService` |
| Favoritas / ocultas | `%APPDATA%\...\usercards.json` | JSON v2, **por nombre de carta** | `UserCardDataStore` |
| Presets de filtros | `%APPDATA%\...\presets.json` | JSON (nombres compatibles con los golden) | `FilterPresetStore` |
| Metadata de mazos y listas | `%APPDATA%\...\decksmeta.json` | JSON, indexado por **nombre de archivo** (listas prefijadas `list:`); incluye las **recortadas**, por nombre de carta | `DeckLibrary` |
| **Los mazos** | `<SunAndMoon>\Decks\<Nombre>.txt` | formato del juego | `DeckLibrary` + `SunAndMoonDeckFile` |
| **Las listas** | `<SunAndMoon>\Lists\<Nombre>.txt` | mismo formato, 1 copia por carta | `DeckLibrary` + `SunAndMoonDeckFile` |
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
redirige. Lo que es **del juego** (`database.xml`, `images\`, los `.txt` de `Decks\` y `Lists\`)
cuelga de la instalación configurada en `sunAndMoonPath` y **no** se redirige nunca: si `Decks` se
moviera, el juego dejaría de leer el mazo y se perdería el sentido de
[ADR-022](../DECISIONS.md#adr-022). `Lists\` va **al lado** de `Decks\`, no adentro, para que el
juego no pueda tomar una lista por un mazo ([ADR-045](../DECISIONS.md#adr-045)); acompaña a los
mazos aunque el juego no la lea, porque es material del mismo mazo y viaja con él.
Único fallback: `ResolveDecksDir`/`ResolveListsDir` caen en `%APPDATA%\...\Decks|Lists` **solo**
cuando no hay instalación configurada.

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

**4 bis. `knownImagePacks` guarda lo que ya se vio, no lo que se tiene.** Es la única entrada de
`config.json` que no responde "¿está aplicado?" sino "¿esto es nuevo?"
([ADR-056](../DECISIONS.md#adr-056)). Se **siembra** con todos los packs la primera vez que se ve el
pool, así estrenar la versión no marca como novedad los arcos de siempre; por eso `null` (sin sembrar)
y lista vacía significan cosas distintas y el default **no** se inicializa.

**5. Los archivos del juego mandan sobre el estado propio.** Los mazos son `.txt` del juego
([ADR-022](../DECISIONS.md#adr-022)) y el `decksmeta.json` es un **sidecar**: si se pierde o se
corrompe, se regenera y no se pierde ningún mazo. Por eso se indexa por nombre de archivo y no por
un id interno.

**6. Nunca se destruye contenido ajeno.** El `database.xml` se respalda antes de cada sobrescritura;
la Original se captura una sola vez y no se pisa nunca; las líneas de un `.txt` que no se entienden
se reescriben verbatim ([ADR-024](../DECISIONS.md#adr-024)); los packs de imágenes se **fusionan**
con los existentes (agregan/actualizan, no reemplazan la carpeta).

**7. Una carta se referencia por NOMBRE, nunca por id** ([ADR-044](../DECISIONS.md#adr-044)). El
`id` sale del `cards_db.json` y lo reasigna quien genera la base: guardarlo hace que, al entrar una
base nueva, una favorita desaparezca o —peor, y en silencio— pase a ser otra carta. El nombre es lo
único que el juego y el launcher acuerdan. En memoria se usa el id; la traducción ocurre al cargar
y al guardar. Aplica a **todo** estado nuevo que referencie cartas.

**8. Los zips grandes no se conservan.** Los packs de imágenes del pool (hasta ~280 MB) se borran
después de aplicarse; lo que queda es el registro por hash en `config.json`.

---

## Al agregar estado nuevo

1. Agregar la ruta a `AppConstants` (no hardcodear en el servicio).
2. Decidir `%APPDATA%` (config del usuario) vs `%LOCALAPPDATA%` (regenerable).
3. Si es JSON de usuario: modelo con `[JsonPropertyName]` explícito y tolerancia a archivo ausente o
   corrupto (arrancar con valores por defecto, nunca crashear).
3b. **Si referencia cartas, se guarda por nombre** (criterio 7). Si el formato cambia, versionarlo
   con un campo `version` y migrar en la carga, respaldando el archivo viejo al lado.
4. Si afecta el pipeline de descarga/aplicación: sumar checks al self-test.
5. Anotarlo acá y en [../index/OVERVIEW.md](../index/OVERVIEW.md).
