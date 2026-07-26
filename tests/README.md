# tests — verificación del launcher

Todo lo relacionado con tests que **no** es código de la app. El harness en sí vive en
[`../src/SelfTest.cs`](../src/SelfTest.cs) porque se compila dentro del exe (solo en DEBUG) para
poder ejercitar el pipeline real; acá están sus **datos**.

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Debug
src/bin/Debug/net48/L5ArgentinaLauncher.exe --selftest out.txt   # pipeline + golden tests
```

> El exe es WinExe: desde PowerShell esperarlo con `Start-Process -Wait` para leer el exit code
> (0 = todo verde). Un resultado válido dice **0 FAIL** y **ningún SKIP**; el total sube o baja a
> medida que la batería crece ([ADR-033](../docs/DECISIONS.md#adr-033)).

**Cuándo correrlo y qué checks se agregan o se dan de baja:** [`CLAUDE.md` §5](../CLAUDE.md), con el
razonamiento en [ADR-032](../docs/DECISIONS.md#adr-032) y [ADR-033](../docs/DECISIONS.md#adr-033).
Qué cubre cada bloque: [`../docs/index/build-verificacion.md`](../docs/index/build-verificacion.md).

---

## Contenido

| Carpeta | Qué es |
|---|---|
| [`fixtures/buscador/`](fixtures/buscador/) | **53 golden tests** del motor de búsqueda: `golden-tests.json` (criteria + ids esperados + el SQL que generó la app), `data/` (los artefactos exactos con los que se generaron) y `generator/` (el harness JVM para regenerarlos). |
| [`fixtures/deckbuilder/`](fixtures/deckbuilder/) | **25 golden tests** del `DeckRepository`, misma estructura. |

`data/` contiene `cards_db-2.3.1.zip`, `rules-2.3.1.json` y `filters-2.3.0.json` del canal
`production` del CDN de la app Android — los mismos que consume el Buscador en producción. El
`SelfTest` los carga con el `CatalogStore` real, así que también verifican el importer.

## Por qué existen los golden

El Buscador y el deckbuilder son ports 1:1 de la app Android: los golden se generaron ejecutando la
lógica **real** de la app sobre SQLite, y son lo que garantiza la paridad. La regla completa —qué se
puede cambiar, qué no, y cómo se regeneran— está en
[`../docs/design/paridad-app-android.md`](../docs/design/paridad-app-android.md).

Las specs de las que salió cada port (y los `.kt` originales que compila el generador) están en los
paquetes de handoff: [`../docs/functional/buscador-launcher/`](../docs/functional/buscador-launcher/)
y [`../docs/functional/deckbuilder-launcher/`](../docs/functional/deckbuilder-launcher/).

## ⚠️ Rutas

`SelfTest.cs` resuelve estas carpetas por ruta relativa al repo
(`tests/fixtures/buscador` y `tests/fixtures/deckbuilder`, subiendo desde el directorio del exe).
Moverlas o renombrarlas **rompe los golden tests**: hay que actualizar `SelfTest.cs` en el mismo
cambio y volver a correr el selftest. Si no las encuentra no falla: reporta `SKIP` — por eso un
reporte válido no tiene SKIPs.
