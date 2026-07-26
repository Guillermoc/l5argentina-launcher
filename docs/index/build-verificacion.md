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

**177 checks** ([ADR-011](../DECISIONS.md#adr-011)):

| Bloque | Cubre |
|---|---|
| Pipeline (~91 con los del Buscador no-golden) | `VersionUtil`; parseo del manifest (incl. `latest_version` snake_case); `ResolveFileUri` (relativa OK, otro origen rechazado, `../` rechazado); descarga con hash correcto / hash mal → aborta y **no deja archivo**; base `.zip` con exactamente un `.xml`; zip-slip rechazado; backup Original + backups con timestamp; manifest del pool y `ResolveSameOriginUrl`; catálogo y stores (`UserCardDataStore`, `FilterPresetStore`). |
| Golden Buscador | **53 casos** generados con la lógica real de la app Android (criteria serializado + ids esperados; `orderMatters` decide secuencia vs conjunto). |
| Golden deckbuilder | **25 casos** replicando la secuencia exacta de `DeckRunner.kt` (mismos mazos "Golden N", string exacto del export). |
| Persistencia de mazos | Round-trip del `.txt` de Sun and Moon, `DeckLibrary` (rename/delete/activo/duplicados), resolución tolerante de nombres. |

Los golden leen sus fixtures de `tests/fixtures/` resolviendo la ruta hacia
arriba desde el exe (`FindRepoRelative`). Si el exe corre fuera del repo, esos bloques se **omiten**
sin fallar (aparece `SKIP` en el reporte) — al validar un cambio, verificar que el reporte diga
**0 FAIL y ningún SKIP**. El total (177 hoy) sube o baja con la batería: no es una constante.

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
| `L5A_DATA_ROOT` (env var) | Redirige config/caché/backups/`Decks` a otra carpeta. También en Release: es el hook de portabilidad ([ADR-010](../DECISIONS.md#adr-010)). |

Los tres flags están bajo `#if DEBUG`. En Release no existen.

También bajo `#if DEBUG`: el escape hatch de esquemas de `ManifestService.EnsureAllowedScheme`
(`file://` y `http://localhost`), para testear contra un bucket local.

---

## CI

| Workflow | Cuándo | Qué hace |
|---|---|---|
| `.github/workflows/build.yml` | push/PR a `main` | Build Release + build Debug + `--selftest` (falla el job si el exit code no es 0). |
| `.github/workflows/release.yml` | push de un tag `vX.Y.Z` | Build Release → (firma con SignPath **si** el secret está cargado) → SHA-256 del exe → publica exe + `.sha256.txt` en GitHub Releases. |

`.github/dependabot.yml`: NuGet + GitHub Actions, semanal.

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
