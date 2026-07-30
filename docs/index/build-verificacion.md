# Índice — build, verificación, CI y release

Estado actual del toolchain. Dominio: `src/L5ArgentinaLauncher.csproj`, `src/SelfTest.cs`,
`nuget.config`, `.github/workflows/`.

---

## Build

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Debug
dotnet run  --project src/L5ArgentinaLauncher.csproj
dotnet build src/L5ArgentinaLauncher.csproj -c Release    # exe único en src/bin/Release/net48/
```

- **Target `net48`** con csproj SDK-style: el runtime viene preinstalado en Windows 10/11, así que
  los usuarios no instalan nada ([ADR-002](../DECISIONS.md#adr-002)).
- `Microsoft.NETFramework.ReferenceAssemblies` evita depender del Developer Pack del sistema.
- ⚠️ **Fachadas del framework declaradas a mano.** El csproj referencia explícitamente
  `System.Net.Http`, `System.IO.Compression` y `System.IO.Compression.FileSystem`. Los proyectos
  SDK-style sobre .NET Framework **no** las referencian por defecto: hasta `System.Text.Json` 8.x
  llegaban de arrastre por su cadena de dependencias, y al actualizar a 10.x dejaron de llegar. Sin
  ellas no compilan `ManifestService`/`DownloadService` (`HttpClient`) ni
  `ZipUtil`/`ImagePackService`/`CatalogStore` (`ZipArchive`, `ZipFile`). Si aparece un `CS1069` o un
  `CS0012` hablando de un ensamblado "reenviado", es esto: se agrega la fachada, no se cambia el
  código.
- **Costura.Fody** embebe los DLLs → un solo `.exe` (~7 MB). Se distribuye **solo el exe**; los
  `.dll`/`.pdb`/`.exe.config` que quedan al lado no hacen falta ([ADR-003](../DECISIONS.md#adr-003)).
- `nuget.config` (raíz) fija nuget.org explícitamente porque el config global de la máquina de dev
  estaba vacío.
- La versión se bumpea en **dos lugares a la vez**: `<Version>` del csproj y
  `AppConstants.LauncherVersion`.

**Sobre `dotnet` en la máquina de dev:** hay un SDK global (`C:\Program Files\dotnet`, hoy 10.0.301)
y también uno user-local en `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` (instalado cuando no había
SDK global). Cualquiera compila el proyecto; CI usa `8.0.x`.

---

## Self-test (solo DEBUG)

```sh
src/bin/Debug/net48/L5ArgentinaLauncher.exe --selftest C:\ruta\reporte.txt
```

> El exe es **WinExe**: desde PowerShell hay que esperarlo con `Start-Process -Wait` para leer el
> exit code (0 = todo verde). El reporte de texto queda en la ruta indicada.

`SelfTest.Run` setea `L5A_DATA_ROOT` a una carpeta temporal y monta un **bucket falso + instalación
falsa** en `%TEMP%`: ejercita el pipeline real sin tocar nada del usuario, y limpia al terminar.

**264 checks** ([ADR-011](../DECISIONS.md#adr-011)):

| Bloque | Cubre |
|---|---|
| Pipeline (~86 con los del Buscador no-golden) | `VersionUtil`; parseo del manifest (incl. `latest_version` snake_case); `ResolveFileUri` (relativa OK, otro origen rechazado, `../` rechazado); descarga con hash correcto / hash mal → aborta y **no deja archivo**; base `.zip` con exactamente un `.xml`; zip-slip rechazado; backup Original + backups con timestamp; manifest del pool y `ResolveSameOriginUrl`; catálogo y `FilterPresetStore`. |
| Golden Buscador | **53 casos** generados con la lógica real de la app Android (criteria serializado + ids esperados; `orderMatters` decide secuencia vs conjunto). |
| Golden deckbuilder | **25 casos** replicando la secuencia exacta de `DeckRunner.kt` (mismos mazos "Golden N", string exacto del export). |
| Persistencia de mazos | Round-trip del `.txt` de Sun and Moon, `DeckLibrary` (rename/delete/activo/duplicados), resolución tolerante de nombres. |
| Conjuntos de cartas (`RunCardSetTests`, **21**) | Listas como `.txt` de `Lists\` (nunca en `Decks\`, tope 1, `3x` recortado al cargar, borrado que no toca el mazo homónimo); `SetInList` vs `AddCard` (la semántica portada intacta, la de listas sin slot ni banlist); `usercards.json` v1→v2 (traducción de ids, respaldo, listas a `.txt`) y la prueba de fuego de [ADR-044](../DECISIONS.md#adr-044): se barajan los ids del catálogo y las favoritas siguen apuntando a la misma carta. |
| Recorte (`RunTrimTests`, **14**) | `quantity=0` conserva la entrada; no cuenta en total ni export; **no** se escribe en el `.txt` del juego pero sobrevive al reinicio vía `decksmeta.json`; el `+` rescata conservando `sortOrder`; "limpiar recortadas"; el `.txt` manda sobre la marca del sidecar; una lista nunca guarda recortadas ([ADR-046](../DECISIONS.md#adr-046)). |
| Estado de armado (`RunDeckStateTests`, **10**) | `IsDeckComplete`: vacío → pendiente; slots + stronghold sin cartas → pendiente; un solo lado en rango → pendiente; 40/40 → terminado; sacar el stronghold o pasarse de 50 lo devuelve a pendiente; un slot cubierto por una **recortada** no cuenta; una lista nunca está terminada ([ADR-047](../DECISIONS.md#adr-047)). |
| Exportación a PDF (`RunPdfExportTests`, **36**) | Geometría de la hoja (A4/Carta, carta de 179×249 pt = 63×88 mm reales, grilla 3×3 centrada, margen de 1 mm); nombre de archivo saneado; reparto en hojas; expansión de copias; lectura del SOF del JPEG (dimensiones, componentes, baseline). Del archivo emitido: cabecera y `%%EOF`, `/MediaBox`, **cada offset del `xref` cae exacto en su objeto**, el JPEG de una carta repetida se embebe una sola vez, la Y espejada ubica la primera carta arriba, y la carta sin imagen sale como placeholder ([ADR-049](../DECISIONS.md#adr-049)). No necesita fixtures: genera su propio JPEG. |
| Previsualización con Alt (`RunHoverPreviewTests`, **9**) | La geometría de `HoverPreviewPlacer.ComputePosition`, con un solo invariante: **el cursor nunca cae adentro del popup** (si cae, el popup tapa la celda, salta su `MouseLeave` y arranca un parpadeo infinito — el bug que tenía contra el borde derecho). Casos de borde derecho, borde inferior y esquina, más un **barrido de la pantalla entera con paso de 8 px** y el caso patológico de un área de trabajo más chica que el propio popup ([ADR-052](../DECISIONS.md#adr-052)). Geometría pura: no necesita UI ni fixtures. |

Los golden leen sus fixtures de `tests/fixtures/` resolviendo la ruta hacia
arriba desde el exe (`FindRepoRelative`). Si el exe corre fuera del repo, esos bloques se **omiten**
sin fallar (aparece `SKIP` en el reporte) — al validar un cambio, verificar que el reporte diga
**0 FAIL y ningún SKIP**. El total (264 hoy) sube o baja con la batería: no es una constante.

### Costo y cadencia

Medido en la máquina de dev: build Debug incremental ≈ **1 s**, selftest ≈ **3 s**, reporte de ~180
líneas. El costo no es el reloj sino el ida y vuelta repetido: por eso la verificación se agrupa al
cierre de un desarrollo en vez de correrse después de cada edición.

> **Las reglas operativas** —cuándo correrlo, qué checks se agregan, cuándo se da de baja uno— viven
> en **[`CLAUDE.md` §5](../../CLAUDE.md)**, con el razonamiento en
> [ADR-032](../DECISIONS.md#adr-032) y [ADR-033](../DECISIONS.md#adr-033). Acá va solo el estado del
> harness: si las dos versiones divergen, manda `CLAUDE.md`.

En una línea: los 78 golden **nunca** se escriben a mano (se regeneran con el harness de
`tests/fixtures/*/generator/`); los checks propios crecen solo para escritura sobre la instalación o
el estado del usuario, validación de datos que vienen de la red y lógica de semántica no obvia.

La red de seguridad es CI: `build.yml` corre el selftest en cada push y PR a `main`, y **falla si
el reporte trae algún `SKIP:`** (señal de que no encontró las fixtures y se salteó los golden).

---

## Flags de debug

| Flag | Efecto |
|---|---|
| `--selftest [ruta]` | Corre el harness y termina con exit code 0/1. |
| `--buscador` | Abre el Buscador directo, sin `MainWindow` ni first-run. |
| `--mazos` | Abre el editor de Mazos directo. |
| `L5A_DATA_ROOT` (env var) | Redirige el estado **propio** (config, caché, backups, stores) a otra carpeta. También en Release: es el hook de portabilidad ([ADR-010](../DECISIONS.md#adr-010)). **No** redirige los archivos del juego, mazos incluidos — ver [../design/persistencia.md](../design/persistencia.md). |

Los tres flags están bajo `#if DEBUG`. En Release no existen.

También bajo `#if DEBUG`: el escape hatch de esquemas de `ManifestService.EnsureAllowedScheme`
(`file://` y `http://localhost`), para testear contra un bucket local.

---

## CI

| Workflow | Cuándo | Qué hace |
|---|---|---|
| `.github/workflows/build.yml` | push/PR a `main` | Build Release + build Debug + `--selftest` (falla el job si el exit code no es 0). |
| `.github/workflows/release.yml` | push de un tag `vX.Y.Z` | Build Release → (firma con SignPath **si** el secret está cargado) → SHA-256 del exe → publica exe + `.sha256.txt` en GitHub Releases. |

`.github/dependabot.yml`: NuGet + GitHub Actions, semanal. **Con los PR deshabilitados en el repo, el
bot no puede abrir ninguno**: solo empuja ramas `dependabot/*`, que no notifican nada. Para ver qué
está proponiendo: `git ls-remote origin 'refs/heads/dependabot/*'` (es también el chequeo previo a
cada push, [ADR-034](../DECISIONS.md#adr-034)). Esas ramas **no se cierran solas** cuando el bump se
aplica a mano: quedan hasta que se borren.

### Dependencias al día (2026-07-26)

| Paquete / action | Versión |
|---|---|
| `System.Text.Json` | 10.0.10 |
| `Costura.Fody` | 6.2.0 |
| `Fody` | 6.9.3 |
| `Microsoft.NETFramework.ReferenceAssemblies` | 1.0.3 |
| `actions/checkout` | v6 |
| `actions/setup-dotnet` | v5 (SDK `8.0.x`) |
| `actions/upload-artifact` | v7 |
| `signpath/github-action-submit-signing-request` | v2 |
| `softprops/action-gh-release` | v3 |

Las tres últimas viven **solo** en `release.yml`, que corre al pushear un tag: no las ejercita el CI
de cada push, así que su primera prueba real será el próximo release. Se verificó contra el
`action.yml` de cada una que los inputs y outputs que usamos siguen existiendo (en particular el
output `artifact-id` de `upload-artifact`, que alimenta a SignPath).

---

## Distribución y firma

- El exe se publica **solo en GitHub Releases**, nunca en el bucket R2
  ([ADR-009](../DECISIONS.md#adr-009)).
- El **SHA-256 del exe** se publica como segundo canal (web/Discord) — paso recurrente de cada release.
- **Firma de código:** SignPath.io (gratis para open source, certificado de la SignPath Foundation).
  El workflow ya está cableado condicionalmente; falta la cuenta aprobada, los secrets
  `SIGNPATH_API_TOKEN`/`SIGNPATH_ORGANIZATION_ID` y ajustar los 3 slugs `TODO(SignPath)` de
  `release.yml`. Hasta entonces el exe sale sin firmar y aparece SmartScreen
  (ver [../USUARIOS.md](../USUARIOS.md)).

---

## Decisiones que aplican

[ADR-002](../DECISIONS.md#adr-002) net48 + SDK-style ·
[ADR-003](../DECISIONS.md#adr-003) Costura ·
[ADR-004](../DECISIONS.md#adr-004) System.Text.Json ·
[ADR-009](../DECISIONS.md#adr-009) distribución del exe ·
[ADR-010](../DECISIONS.md#adr-010) `L5A_DATA_ROOT` ·
[ADR-011](../DECISIONS.md#adr-011) self-test en vez de proyecto de tests.
