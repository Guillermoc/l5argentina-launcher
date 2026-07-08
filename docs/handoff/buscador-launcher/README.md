# Handoff — Buscador de cartas para el L5Argentina Launcher

Paquete autocontenido para portar el Buscador de la app Android **L5Argentina** al
**L5Argentina Launcher** (WPF, .NET Framework 4.8). **Alcance: Buscador y filtros; Mazos afuera.**

- **Repo origen:** L5Argentina (Android), commit **`a9a48f120769432b2763b463dd643294e32ff81c`**
  (branch `feature/goldfish`; los archivos del buscador no tenían modificaciones locales).
- **Datos contra los que se generó todo:** canal `production` del CDN —
  cards_db **2.3.1**, rules **2.3.1**, filters **2.3.0** (copias en `fixtures/data/`).
- Fecha de generación: 2026-07-06.

## Índice

| Archivo | Contenido |
|---|---|
| [SPEC-FILTROS.md](SPEC-FILTROS.md) | Cada filtro: campos, semántica exacta (SQL), valores posibles y su origen, defaults, combinación AND/OR, ordenamiento (incl. orden por Set y "Ver todas las impresiones"). Con ⚠️ en comportamientos accidentales. |
| [SPEC-BUSQUEDA-TEXTO.md](SPEC-BUSQUEDA-TEXTO.md) | Barra de búsqueda (modos/prefijos), matching de `name`, filtro de texto (normalización + word-boundary + frase entre comillas), sugerencias (debounce, mínimos, límites), orden de resultados. |
| [SPEC-LEGALIDAD.md](SPEC-LEGALIDAD.md) | rules.json (banlist): estructura, aplicación (reset + apply por título), qué hace el buscador con baneadas, legalidad vs edición, mismatch `20F`/`TwentyFestivals`, SExMRP vs `s_extended`. |
| [MODELO-DATOS.md](MODELO-DATOS.md) | Modelo de carta, parsing (normalizaciones, stats especiales `*`/`+*`/`-`), selección de printing/imagen, mapeo columna ← JSON ← XML kamisasori, tags HTML exhaustivos (`<b>`, `<br>`, `<i>`), campos derivados (keywords, subtitle, action_keywords). |
| [UI-INVENTARIO.md](UI-INVENTARIO.md) | Pantalla: controles, tabs, carousel/grilla, visor, orden default, persistencia (ninguna), comportamientos no obvios. |
| [src/](src/) + [src/INDEX.md](src/INDEX.md) | Los .kt reales copiados verbatim (motor, importers, modelo, y SearchViewModel porque parte de la semántica vive ahí — marcado). |
| [fixtures/golden-tests.json](fixtures/golden-tests.json) | **53 casos golden** con `expectedIds` producidos ejecutando la lógica real (ver abajo). Cada caso incluye el SQL generado. |
| [fixtures/data/](fixtures/data/) | Los datos exactos usados: `cards_db-2.3.1.zip` (contiene `cards-2.3.1.json`), `rules-2.3.1.json`, `filters-2.3.0.json`, manifest del canal production. |
| [fixtures/generator/](fixtures/generator/) | El harness que generó los golden (driver JDBC + stubs mínimos de Room/SQLite). Reproducible: compila los .kt de `src/` **sin modificar** con kotlinc 2.0.21 (`-Xplugin=kotlinx-serialization-compiler-plugin`) + jars de kotlinx-serialization 1.7.3, coroutines 1.9.0 y sqlite-jdbc, y corre `harness.RunnerKt <dataDir> <out.json> <db> <commit>`. |

## Cómo se generaron los golden tests (garantía de "lógica real")

No están armados a mano. El harness de `fixtures/generator/`:

1. Compila **copias sin modificar** de `CardImporter.kt`, `RulesImporter.kt`,
   `CardQueryCompiler.kt`, `CardSearchCriteria.kt`, `CardEntity.kt`, `CardDao.kt` (+ modelos JSON)
   con stubs mínimos de `androidx.room`/`androidx.sqlite` (solo anotaciones y 2 clases).
2. Importa `cards-2.3.1.json` con el `CardImporter` real (mapa de rules + elegibles de holdings,
   igual que `CatalogSyncService`), aplica `RulesImporter.applyRules` (el SQL de
   `resetAllRules`/`applyCardRule` se lee **por reflexión de las `@Query` reales** de CardDao.kt).
3. Siembra datos de usuario documentados en `userDataSeed` (favoritas/ocultas/lista) para los
   casos que los necesitan.
4. Por cada caso, compila el criteria con el `CardQueryCompiler` real y ejecuta el SQL en SQLite
   (sqlite-jdbc) → `expectedIds` en el orden devuelto.

Única diferencia con el runtime Android: el motor SQLite es sqlite-jdbc en vez del de Android
(mismas semánticas de `LIKE` ASCII case-insensitive, `IN` case-sensitive y collation BINARY).

### Cómo usarlos en el launcher

- Mapear el `database.xml` al modelo según MODELO-DATOS.md §5 (o importar directamente el
  `cards-2.3.1.json` de `fixtures/data/` para la suite de paridad, que es lo recomendado: aísla
  el motor de filtros del mapeo XML).
- Ejecutar cada caso: `query` = texto de barra (campo name), `filters` = resto del criteria
  (campos ausentes = default; ver `src/CardSearchCriteria.kt`). Comparar ids con `expectedIds`
  (secuencia si `orderMatters`, conjunto si no — detalle en `notes` del JSON).
- Los ⚠️ de las specs marcan bugs/accidentes reproducidos en los golden (rareza case-sensitive,
  formato `20F`, subtitle substring, etc.): si el launcher decide **arreglarlos** en vez de
  portarlos, regenerar los expected de esos casos y anotarlo.

## Advertencias globales

- El launcher usa `database.xml` kamisasori; esta app consume un JSON derivado. El Buscador opera
  sobre campos que **no existen en el XML** (`keywords` editorial, legalidades por printing,
  flag `current`) — ver MODELO-DATOS.md §5 antes de decidir la fuente de datos del launcher.
- Ningún filtro se persiste entre sesiones; el único estado persistente es el preset manual.
- El orden por Set y "Ver todas las impresiones" NO viven en el SQL: son post-procesamiento en
  memoria (`SearchViewModel.displayCards`) — están especificados en SPEC-FILTROS.md §15.
