# src/ — Código Kotlin de referencia (verbatim, commit a9a48f1)

Rutas originales bajo `app/src/main/java/com/guillermoc/l5argentina/`.

| Archivo | Original | Qué contiene / rol |
|---|---|---|
| `DeckRepository.kt` | `data/repository/` | **El núcleo.** Única capa de mutación de mazos: `addCard` (validación banned/slot/maxCopies), `removeCard`/`setCardQuantity` (recorte vs borrado, `keepRowAtZero`), `importCards`, `exportDeck`, CRUD de mazos y grupos. Es el archivo a portar con más fidelidad. |
| `AddCardResult.kt` | `data/repository/` | Enum de resultado de `addCard`: Success, Banned, NoActiveDeck, LimitReached(max), SlotAlreadyCovered. |
| `ImportResult.kt` | `data/repository/` | `(imported, notFound, notFoundNames)`. |
| `DeckEntity.kt` | `data/db/entity/` | Tabla `decks`: columnas y nombres snake_case. |
| `DeckCardEntity.kt` | `data/db/entity/` | Tabla `deck_cards`: PK compuesta, CASCADE, quantity=0 = recortada. |
| `DeckGroupEntity.kt` | `data/db/entity/` | Grupos de usuario (carpetas para organizar mazos). |
| `DeckCardGroupEntity.kt` | `data/db/entity/` | Subgrupos dentro de un mazo (⚠️ en desuso). |
| `DeckCardWithCard.kt` | `data/db/relation/` | Relación de lectura mazo↔carta; `card` nullable (huérfanas) → filtrada en el repo. |
| `DeckDao.kt` | `data/db/dao/` | SQL de todas las queries de mazo (útil como referencia del SQL exacto). |
| `DeckGroupDao.kt`, `DeckCardGroupDao.kt` | `data/db/dao/` | SQL de grupos/subgrupos. |
| `FormatInitialHoldingDao.kt` | `data/db/dao/` | `observeRequiredSlots` (slots required) + `getAllEligibleCardSlots` (name→slot). |
| `DeckGroup.kt` | `data/model/` | Enum starter/dynasty/fate → tipos de carta de cada grupo. Base de los conteos y del export. |
| `DeckType.kt` | `data/model/` | Enum deck/list. |
| `DeckDetailViewModel.kt` | `ui/screens/deckdetail/` | ⚠️ **Capa UI con semántica**: conteos Dynasty/Fate (validez blanda), `uncoveredRequiredSlots`, recorte/borrado con gracia 2.5s, orden en grilla. Los indicadores de validez viven acá, no en el repo. |
| `DecksViewModel.kt` | `ui/screens/decks/` | Lista de mazos: crear (auto-activa), secciones Mazos/Listas, grupos, filtro por clan, sugerencias. Muestra que **no hay duplicar**. |
| `MainViewModel.kt` | `ui/` | `activeDeckInfo` (clan derivado del primer stronghold, conteos) + `recentDecks`. Define para qué sirve el mazo activo. |
| `GoldfishViewModel.kt` | `ui/screens/goldfish/` | ⚠️ **Única fuente del honor inicial**: `honorCounter = stronghold.startingHonor` en `buildInitialState`. Sandbox de juego, no persiste. |
| `GoldfishModels.kt` | `ui/screens/goldfish/` | Constantes `PROVINCE_COUNT=4`, `INITIAL_HAND_SIZE=5`. |

**No copiado** (UI Compose, comportamiento descripto en `UI-INVENTARIO.md` / specs):
`DeckDetailScreen.kt` (el `StatsModal` está transcripto con líneas en `SPEC-STATS-INICIALES.md`;
`SIDE_TARGET=40` y `LegalityCounter` en `SPEC-VALIDACION.md`), `DecksScreen.kt`, `HalfCardCell.kt`,
`DeckGridActionRails.kt`.

Para recompilar el generador de golden tests hacen falta también `CardEntity`, `CardImporter`,
`RulesImporter`, `CardDao`, modelos JSON — están en el handoff del Buscador (`src/` de
`buscador-launcher`), no se duplican acá. Ver `fixtures/generator/`.
