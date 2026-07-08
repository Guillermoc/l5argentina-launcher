# Generador de golden-tests.json

Harness JVM que ejecuta la lógica **real** de la app (sin modificar) contra SQLite y escribe
`../golden-tests.json`. Ya fue ejecutado — este directorio existe para poder **regenerar** los
expected (p. ej. si el launcher decide arreglar un ⚠️ en vez de portarlo, o si cambian los datos).

## Qué compila

- **Reales (de `../../src/`, verbatim):** `CardSearchCriteria.kt`, `CardType.kt`,
  `CardQueryCompiler.kt`, `CardEntity.kt`, `UserCardDataEntity.kt`, `CardDao.kt`, `CardJson.kt`,
  `RulesJson.kt`, `FiltersJson.kt`, `CardImporter.kt`, `RulesImporter.kt`.
  (No compilar los demás .kt de src/ — SearchViewModel/CardRepository/etc. dependen de Android.)
- **Stubs (`stub/`):** anotaciones de `androidx.room` con retención RUNTIME (para leer el SQL de
  las `@Query` reales por reflexión), `RoomDatabase`+`withTransaction` no-op,
  `SimpleSQLiteQuery`/`SupportSQLiteQuery`, y un `AppDatabase` vacío.
- **Driver (`driver/Runner.kt`):** schema (columnas exactas de CardEntity), DAO JDBC, seed de
  datos de usuario, definición de los 53 casos, ejecución y serialización.

## Dependencias

- kotlin-compiler **2.0.21** (incluye `lib/kotlinx-serialization-compiler-plugin.jar`)
- `kotlinx-serialization-json-jvm` + `-core-jvm` **1.7.3**, `kotlinx-coroutines-core-jvm` **1.9.0**
  (mismas versiones que la app), `sqlite-jdbc` 3.46.x, `slf4j-api` (runtime).

## Comandos (Windows; ajustar separador `;`→`:` en Unix)

```bash
# 1. preparar src: copiar los .kt "reales" listados arriba a un dir real/
# 2. compilar
kotlinc real stub driver \
  -cp "kotlinx-serialization-json-jvm-1.7.3.jar;kotlinx-serialization-core-jvm-1.7.3.jar;kotlinx-coroutines-core-jvm-1.9.0.jar;sqlite-jdbc-3.46.1.3.jar" \
  -Xplugin=<kotlinc>/lib/kotlinx-serialization-compiler-plugin.jar -d out
# 3. correr: dataDir debe contener cards-2.3.1.json (descomprimir ../data/cards_db-2.3.1.zip),
#    rules.json y filters.json (renombrar los de ../data/ quitando la versión)
java -cp "out;<jars>;<kotlinc>/lib/kotlin-stdlib.jar" harness.RunnerKt \
  <dataDir> <salida.json> <trabajo.db> <commit-hash>
```

El `.db` de trabajo queda en disco para inspeccionar con sqlite3 (misma DB que "ve" el motor).

## Fidelidad

- Import → `CardImporter.import(cards, rulesMap, initialHoldingByCard)` seguido de
  `RulesImporter.applyRules(rules)` — mismo orden que `CatalogSyncService` (database → rules),
  estado estable con rules cacheadas (afecta solo la imagen de `Border Keep - exp`).
- El SQL de banlist se lee por reflexión de las `@Query` de `CardDao.kt` — cero transcripción.
- Motor: sqlite-jdbc en vez del SQLite de Android — mismas semánticas relevantes
  (`LIKE` case-insensitive ASCII, `IN`/`=` case-sensitive, collation BINARY, booleans 0/1).
