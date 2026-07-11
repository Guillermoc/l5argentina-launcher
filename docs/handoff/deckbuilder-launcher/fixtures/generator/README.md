# Generador de golden-tests.json (deckbuilder)

Harness JVM que ejecuta la lógica **real** (`DeckRepository` + `CardImporter` + `RulesImporter`,
compilados sin modificar) contra SQLite, y escribe `../golden-tests.json`. Ya fue ejecutado; esto
existe para **regenerar** si corregís un ⚠️ o cambian los datos.

## Qué compila

- **Reales (verbatim):** de `../../src/` los archivos de mazo (`DeckRepository`, entidades, DAOs,
  `DeckGroup`, `AddCardResult`, `ImportResult`, `DeckCardWithCard`) **más** los del catálogo que
  viven en el handoff del Buscador (`buscador-launcher/src/`): `CardEntity`, `UserCardDataEntity`,
  `CardType`, `CardJson`, `RulesJson`, `FiltersJson`, `CardImporter`, `RulesImporter`, `CardDao`.
  Copiá todos esos `.kt` a un directorio `real/` antes de compilar.
- **Stubs (`stub/`):** `androidx.room` (anotaciones con retención RUNTIME para leer `@Query` por
  reflexión; incluye `@ForeignKey`, `@Transaction`, `@Embedded`, `@Relation`), `SimpleSQLiteQuery`,
  y `AppDatabase` vacío.
- **Driver (`driver/DeckRunner.kt`):** schema (columnas exactas de las entidades), **DAOs JDBC**
  que implementan cada interfaz replicando el SQL de sus `@Query` (el de `resetAllRules`/
  `applyCardRule*` se lee por reflexión), definición de los 25 casos, ejecución vía el
  `DeckRepository` real, y serialización.

## Dependencias

kotlin-compiler **2.0.21** (+ `kotlinx-serialization-compiler-plugin.jar`),
`kotlinx-serialization-json/core-jvm` **1.7.3**, `kotlinx-coroutines-core-jvm` **1.9.0**,
`sqlite-jdbc` 3.46.x, `slf4j-api` (runtime).

## Comandos (Windows; `;`→`:` en Unix)

```bash
# 1. real/ = .kt de mazo (../../src) + .kt de catálogo (../../../buscador-launcher/src listados arriba)
# 2. compilar
kotlinc real stub driver \
  -cp "kotlinx-serialization-json-jvm-1.7.3.jar;kotlinx-serialization-core-jvm-1.7.3.jar;kotlinx-coroutines-core-jvm-1.9.0.jar;sqlite-jdbc-3.46.1.3.jar" \
  -Xplugin=<kotlinc>/lib/kotlinx-serialization-compiler-plugin.jar -d out
# 3. dataDir debe tener cards-2.3.1.json (de ../data/cards_db-2.3.1.zip), rules.json, filters.json
java -cp "out;<jars>;<kotlinc>/lib/kotlin-stdlib.jar" harness.DeckRunnerKt \
  <dataDir> <salida.json> <trabajo.db> <commit-hash>
```

## Fidelidad

- El estado del catálogo replica el sync real: `CardImporter.import(...)` con rulesMap +
  holdings, seguido de `RulesImporter.applyRules(...)` (orden database → rules).
- Los `AddCardResult`/`ImportResult`/export son los que devuelve el **`DeckRepository` real**.
- Los conteos de validez, slots sin cubrir y honor inicial son **espejos** de expresiones de
  `DeckDetailViewModel`/`GoldfishViewModel` (citadas en las specs), computados con los mismos
  predicados en el driver — no salen del repo, porque en la app viven en la capa UI.
- Motor: sqlite-jdbc en vez del SQLite de Android (mismas semánticas de `=`/`LIKE`/booleans 0-1).
