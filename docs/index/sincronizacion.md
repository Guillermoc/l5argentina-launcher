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
- `images` → pack de imágenes propio de la comunidad (~30 cartas fuera de los packs clásicos).
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

### Buscar actualizaciones (MainWindow)

1. `LoadManifestAsync` (launcher) y `LoadPoolManifestAsync` (pool).
2. `LauncherUpdateAvailable()` → banner con link a `ReleasesPageUrl` (hardcodeado).
3. `CommunityNeedsDownload()` / `ImagesNeedSync()` / `CatalogNeedsSyncFromPool()` comparan el hash del
   manifest contra el guardado en `config.json` (no contra el archivo: no se re-hashea la caché).
4. `SyncCatalogFromPoolAsync` baja `cards_db-X.Y.Z.zip` / `rules-X.Y.Z.json` / `filters-X.Y.Z.json` a
   `cache/catalog/` con **nombre versionado** (la URL del pool no lleva versión) y borra las versiones
   viejas. Si el pool no responde, `SyncCatalogAsync` hace lo mismo desde la sección `catalog`.

### JUGAR

1. `NeedsApply(baseId)` → true si falta el `database.xml`, si cambió la base elegida o si cambió la
   versión de Comunidad.
2. Comunidad: `EnsureCommunityDownloadedAsync` (descarga + hash) y, si el archivo es `.zip`,
   `ZipUtil.ExtractSingleXml` → `cache/community.applied.xml`. Original: `backups\database.original.xml`.
3. `DatabaseService.ApplyDatabase`: backup con timestamp (`database.yyyyMMdd-HHmmss-fff.xml`, con
   contador de unicidad) y recién después `File.Copy` sobre `database.xml`.
4. Se persiste `appliedBaseId` / `appliedBaseVersion` / `selectedBaseId`.
5. `GameLauncher.Launch`.

### Packs de imágenes (overlay "Packs de imágenes…")

Lista los `type == "images"` del pool. Por pack: `ResolveSameOriginUrl` → descarga verificada a
`cache/packs/<id>-<version>.zip` → `ImagePackService.ApplyImagePack` sobre `<SunAndMoon>\images\` →
**se borra el zip** (pesan hasta ~280 MB) → se registra en `config.appliedImagePacks[id]` por hash.
Ids nuevos aparecen solos; el nombre amigable sale de `Pack_<id>` en el resx (fallback: el id).
Los packs sirven al juego **y** al Buscador (misma carpeta `images`).

---

## Estado que se persiste (`config.json`)

`sunAndMoonPath`, `manifestUrl`, `selectedBaseId`, `appliedBaseId`, `appliedBaseVersion`,
`cachedDatabases{}`, `cachedCatalogFiles{}`, `appliedImagePacks{}`, `appliedImagePackVersion/Sha256`,
`originalBackupCaptured`. Detalle: [../design/persistencia.md](../design/persistencia.md).

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
