# src/ — Código Kotlin de referencia (copiado verbatim del commit a9a48f1)

Rutas originales bajo `app/src/main/java/com/guillermoc/l5argentina/`.

| Archivo | Original | Qué contiene / rol |
|---|---|---|
| `CardQueryCompiler.kt` | `data/db/` | **El motor de filtros.** Compila `CardSearchCriteria` → SQL (`SELECT * FROM cards WHERE 1=1 AND …`). Es el archivo a portar con más fidelidad. |
| `CardSearchCriteria.kt` | `data/model/` | Modelo de criterios de búsqueda + enum `SortField` + `isEmpty`. Define los defaults de la pantalla. |
| `CardEntity.kt` | `data/db/entity/` | Modelo de carta persistido (tabla `cards`); nombres de columna via `@ColumnInfo`. |
| `CardDao.kt` | `data/db/dao/` | DAO Room: query dinámica + **SQL de resetAllRules/applyCardRule** (banlist). |
| `UserCardDataEntity.kt` | `data/db/entity/` | Tabla `user_card_data` (favoritas/ocultas) — la importa CardDao; necesaria para recompilar el generador. |
| `CardRepository.kt` | `data/repository/` | Fachada: `search()` = compile+dao; **query de sugerencias de nombre** (`searchSuggestions`, LIKE + LIMIT 8). |
| `Converters.kt` | `data/db/` | Formato pipe `"\|a\|b\|"` ↔ List — base de los `LIKE '%\|v\|%'`. |
| `CardType.kt`, `Clan.kt`, `Rarity.kt` | `data/model/` | Enums hardcodeados que alimentan los chips del modal y las sugerencias de operador. |
| `CardJson.kt` | `data/remote/` | Modelo de entrada del cards.json (incluye `PrintingJson` con `current`). |
| `RulesJson.kt` | `data/remote/` | Modelo de rules.json (banlist/labels/preferredPrinting). |
| `FiltersJson.kt` | `data/remote/` | Modelo de filters.json (sets/formats/keywords/holdings iniciales). |
| `CardImporter.kt` | `sync/importer/` | **Transformación JSON → carta**: normalizaciones, parsing de stats especiales, printings/imagen, subtitle, action_keywords, deckGroup, maxCopies base. |
| `RulesImporter.kt` | `sync/importer/` | Parse del rules.json + `applyRules` (reset + apply por título). |
| `FiltersImporter.kt` | `sync/importer/` | Población de sets/formats/keywords/slots (orden de inserción = orden canónico de sets). |
| `SetOrdering.kt` | `ui/screens/search/` | `presortSets`/`setOrderMap`: orden canónico de sets (compartido por el selector de Edición y el orden por Set). Lógica pura. |
| `SearchViewModel.kt` | `ui/screens/search/` | ⚠️ **Capa UI copiada a propósito porque parte de la semántica vive acá**: filtro "Solo lista" en memoria (`results`, líneas 131-133), **orden por Set + expansión por impresión** (`displayCards`, 155-201), parseo de prefijos de la barra (`parseBarText`, 571-577), sugerencias con debounce (247-276), normalización de formats en `applyFilters` (401-418), resumen de filtros activos (`summaryOf`). El resto (tabs, scroll, modales) es plomería Android que no hace falta portar. |

No copiado (irrelevante para la lógica): SearchScreen.kt, FilterModal.kt, CardCarouselView.kt y
demás composables — su comportamiento está descripto en UI-INVENTARIO.md y SPEC-FILTROS.md; los
valores hardcodeados que definen (chips de designadores, tipos omitiendo `wind`, separadores de
arcos `ARC_SEPARATOR_CODES`) están transcriptos en las specs.
