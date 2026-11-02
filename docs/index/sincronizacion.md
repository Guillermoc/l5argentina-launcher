# Índice — sincronización de datos (manifests, descargas, bases, imágenes)

Estado actual del pipeline que va del bucket a la instalación de Sun and Moon.
Dominio: `src/Services/*.cs` (raíz), `src/Models/Manifest.cs`, `PoolManifest.cs`, `UserConfig.cs`,
`src/MainWindow.xaml.cs`, `src/Views/FirstRunWindow.xaml.cs`.

Reglas innegociables que gobiernan todo este dominio: [../design/seguridad.md](../design/seguridad.md).

---

## Piezas

| Pieza | Archivo | Qué hace |
|---|---|---|
| Coordinador | `Services/LauncherEngine.cs` | Único punto que decide "¿hay que bajar algo?" y aplica. Mantiene `Config` + `Manifest` cargado. |
| Manifests | `Services/ManifestService.cs` | Fetch/parse de los dos manifests + las tres validaciones de origen. |
| Descarga | `Services/DownloadService.cs` | `.part` → SHA-256 → promoción atómica. Progreso por `IProgress<DownloadProgress>`. |
| Bases | `Services/DatabaseService.cs` | Backup Original, backup con timestamp, copia de la base activa. |
| Zip de base | `Services/ZipUtil.cs` | `ExtractSingleXml`: exige exactamente un `.xml` dentro del zip. |
| Packs | `Services/ImagePackService.cs` | Extracción con zip-slip check sobre `<SunAndMoon>\images\`. |
| Instalación | `Services/InstallationService.cs` | Rutas del juego + validación de instalación. |
| Config | `Services/ConfigService.cs` | `config.json` con escritura atómica. |
| Utilidades | `Services/HashUtil.cs`, `VersionUtil.cs` | Hash de archivo / comparación numérica de versiones. |
| Lanzar | `Services/GameLauncher.cs` | `Process.Start` del exe del usuario. |

---

## Los dos manifests

**1. Manifest del launcher** (`AppConstants.DefaultManifestUrl`, editable por el usuario).
Rutas `file` **relativas**, resueltas con `ManifestService.ResolveFileUri`: mismo esquema+host+puerto
y confinadas al directorio del manifest. Secciones:

- `launcher.latest_version` → banner de update (compara contra `AppConstants.LauncherVersion`).
- `databases[]` → la entrada con `id == "community"` es la única que se descarga (la Original es local).
- `images` → **el pack recomendado**, no "el pack de la comunidad" ([ADR-055](../DECISIONS.md#adr-055)).
  Hasta [ADR-064](../DECISIONS.md#adr-064) se aplicaba solo al jugar; hoy se baja a mano como los demás. Está publicado también en el pool y se lo reconoce ahí por
  hash ([ADR-048](../DECISIONS.md#adr-048)); qué packs existen lo dice el pool, no esta sección.
- `catalog` (opcional) → `cards`/`rules`/`filters`; hoy es **fallback** del pool.
- `news[]` → modelado, no usado (v2).

**2. Manifest del pool** (`AppConstants.PoolManifestUrl`, **hardcodeado**) — el CDN de la app Android,
canal `production`. Lista plana de `packages` con **URL absoluta** + `sha256`. Se validan con
`ManifestService.ResolveSameOriginUrl` (mismo origen que el manifest, sin exigir mismo directorio:
el manifest vive en `/production/` y los archivos en `/pool/`). De acá salen:

- los packs de imágenes oficiales por arco (`type: "images"`),
- el catálogo del Buscador (`type: "database" | "rules" | "filters"`) — **fuente primaria**.

---

## Flujos

### Primer arranque (`FirstRunWindow`)

1. `InstallationService.IsValidInstall` falla → se abre la ventana.
2. El usuario elige `Sun and Moon.exe`; se valida exe + `Sun and Moon_Data\StreamingAssets\Database\`.
3. `DatabaseService.LooksLikeCommunityBase` (stream-scan de `s_extended`, sin parsear) → si da true,
   se advierte que la copia "Original" puede no ser limpia.
4. `EnsureOriginalBackup` copia el `database.xml` actual a `backups\database.original.xml`.
   **Solo la primera vez**: si el archivo existe, no se pisa nunca.

### Buscar novedades (MainWindow, en segundo plano)

**No bloquea la pantalla** ([ADR-064](../DECISIONS.md#adr-064)): `CheckAsync` corre sin `SetBusy`, así
que JUGAR, el Buscador, Configuración y la elección de base siguen habilitados; solo se apaga
"Buscar actualizaciones" mientras dura. Cada manifest tiene un tope de **15 s**
(`ManifestService.FetchTimeout`, aparte de los 5 min del `HttpClient`, que son para los packs).

**Regla:** la base Comunidad y el catálogo del Buscador se bajan solos; **todos los packs de imágenes
son manuales** y la pantalla avisa cuando falta alguno. Pisar el `database.xml` sigue siendo de JUGAR
(con el juego abierto no se toca).

1. `LoadManifestAsync` (launcher) y `LoadPoolManifestAsync` (pool), **en paralelo**.
2. Apenas está el del bucket (`PrepareBaseAsync`): `LauncherUpdateAvailable()` → banner con link a
   `ReleasesPageUrl` (hardcodeado), y si `CommunityNeedsDownload()`, `DownloadCommunityAsync` baja la
   base a la caché **sin aplicarla**. Un error acá no muestra diálogo: JUGAR la reintenta.
3. `CommunityNeedsDownload()` / `ImagesNeedSync()` / `CatalogNeedsSyncFromPool()` comparan el hash del
   manifest contra el guardado en `config.json` (no contra el archivo: no se re-hashea la caché).
4. `SyncCatalogFromPoolAsync` baja `cards_db-X.Y.Z.zip` / `rules-X.Y.Z.json` / `filters-X.Y.Z.json` a
   `cache/catalog/` con **nombre versionado** (la URL del pool no lleva versión) y borra las versiones
   viejas. Si el pool no responde, `SyncCatalogAsync` hace lo mismo desde la sección `catalog`.
5. Al final, el aviso del header (`GetPendingSummary`) y el resumen de packs. Mientras dura la
   búsqueda dicen "Verificando…" en vez de un "todo al día" prematuro.

Quién espera qué: JUGAR espera `_baseTask` (manifest + base bajada), el listado de packs espera
`_manifestsTask` (y ya no vuelve a pedir el pool si está cargado), el catálogo no lo espera nadie.
Guardar Configuración cancela la búsqueda en curso y arranca otra. `ConfigService.Save` es con lock:
las descargas de fondo guardan desde otro hilo.

### JUGAR

0. Si la búsqueda de novedades sigue, espera a que la base esté (no al catálogo).
1. `NeedsApply(baseId)` → true si falta el `database.xml`, si cambió la base elegida o si cambió la
   versión de Comunidad.
2. Comunidad: `EnsureCommunityDownloadedAsync` (descarga + hash) y, si el archivo es `.zip`,
   `ZipUtil.ExtractSingleXml` → `cache/community.applied.xml`. Original: `backups\database.original.xml`.
3. `DatabaseService.ApplyDatabase`: backup con timestamp (`database.yyyyMMdd-HHmmss-fff.xml`, con
   contador de unicidad) y recién después `File.Copy` sobre `database.xml`.
4. Se persiste `appliedBaseId` / `appliedBaseVersion` / `selectedBaseId`.
5. `GameLauncher.Launch`.

### Packs de imágenes (overlay "Packs de imágenes…")

Lista **todos** los `type == "images"` del pool ([ADR-055](../DECISIONS.md#adr-055)). No hay familias:
un pack nuevo —de un arco o de la comunidad— aparece solo, sin tocar el launcher ni el manifest del
bucket. La sección `images` del bucket ya no significa "el pack de la comunidad" sino **"este es el
pack recomendado"**: se lo reconoce por **sha256** (`IsBundledPack`, la parte que sigue vigente de
[ADR-048](../DECISIONS.md#adr-048)) y en el listado sale marcado "Recomendado". **JUGAR ya no lo
aplica** ([ADR-064](../DECISIONS.md#adr-064)): se baja a mano como cualquier otro. Las versiones
anteriores sí lo aplicaban al jugar y dejaban su propio registro (`appliedImagePackVersion/Sha256`),
así que `PackIsCurrent` sigue consultando los dos y bajarlo desde el listado sigue escribiendo los
dos; `SyncImagesAsync` queda en el motor, sin uso desde la UI.

**Qué se considera pendiente** ([ADR-054](../DECISIONS.md#adr-054)). Un pack en su **versión inicial**
(`LauncherEngine.IsInitialPackVersion`: `1`, `1.0` y `1.0.0` son la misma) **no** genera pendiente —es
catálogo opcional, no una novedad— y en el listado queda apagado en "No descargado". **Salvo que sea
nuevo**: un pack que aparece después de la siembra de `knownImagePacks` avisa igual, porque una expa
de la comunidad sale justamente en 1.0 (`PackIsNew`, [ADR-056](../DECISIONS.md#adr-056)); en el
listado dice "Pack nuevo · falta descargar". Con cualquier otra versión también: sin descargar aparece
como **"Novedad · falta descargar"** en oro. Y el **recomendado** avisa siempre que no esté al día,
aunque esté en 1.0 —desde que JUGAR no lo aplica, el aviso es lo único que le dice a quien no lo tiene
que le falta—, con "Falta descargar". Todos esos casos cuentan en
`GetPendingSummary().ImagePacks`, que alimenta el aviso del header (base Comunidad + packs; el update
del launcher va aparte, en su banner) y el resumen de la fila de imágenes (cuántos descargados sobre
el total). Si el manifest del pool no cargó, los dos lo dicen en vez de afirmar que está todo al día
(`PoolManifestLoaded`); ahí el único pack del que se sabe algo es el incluido, que viene por el
manifest del bucket.

Por pack: `ResolveSameOriginUrl` → descarga verificada a
`cache/packs/<id>-<version>.zip` → `ImagePackService.ApplyImagePack` sobre `<SunAndMoon>\images\` →
**se borra el zip** (pesan hasta ~280 MB) → se registra en `config.appliedImagePacks[id]` por hash.
Ids nuevos aparecen solos; el nombre amigable sale de `Pack_<id>` en el resx (fallback: el id).
Los packs sirven al juego **y** al Buscador (misma carpeta `images`).

---

## Estado que se persiste (`config.json`)

`sunAndMoonPath`, `manifestUrl`, `selectedBaseId`, `appliedBaseId`, `appliedBaseVersion`,
`cachedDatabases{}`, `cachedCatalogFiles{}`, `appliedImagePacks{}`, `appliedImagePackVersion/Sha256`,
`knownImagePacks[]` (packs ya vistos, para distinguir un pack **nuevo**,
[ADR-056](../DECISIONS.md#adr-056)), `originalBackupCaptured`. Detalle: [../design/persistencia.md](../design/persistencia.md).

---

## Decisiones que aplican

[ADR-001](../DECISIONS.md#adr-001) solo datos ·
[ADR-005](../DECISIONS.md#adr-005) la Original es local ·
[ADR-006](../DECISIONS.md#adr-006) SHA-256 + `.part` ·
[ADR-007](../DECISIONS.md#adr-007) confinamiento al origen ·
[ADR-008](../DECISIONS.md#adr-008) URL configurable ·
[ADR-009](../DECISIONS.md#adr-009) distribución del exe ·
[ADR-014](../DECISIONS.md#adr-014) TLS 1.2 / un solo `.xml` ·
[ADR-015](../DECISIONS.md#adr-015) firma del manifest diferida ·
[ADR-021](../DECISIONS.md#adr-021) el pool como fuente primaria.

## Puntos abiertos

- El manifest **no está firmado** (SEC-1, diferido) — ver [../ROADMAP.md](../ROADMAP.md).
- Sin topes anti zip-bomb al extraer (SEC-3, diferido).
- `news[]` modelado pero sin implementar ([../functional/noticias-v2-borrador.md](../functional/noticias-v2-borrador.md)).
