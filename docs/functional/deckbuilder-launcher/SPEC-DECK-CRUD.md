# SPEC — CRUD de mazos y cartas

Fuentes: `src/DeckRepository.kt`, `src/DecksViewModel.kt`, `src/DeckDetailViewModel.kt`,
`src/MainViewModel.kt`, `src/DeckEntity.kt`, `src/DeckCardEntity.kt`. Commit `a9a48f1`.

## 1. Modelo de un mazo

`DeckEntity` (`src/DeckEntity.kt`) → tabla `decks`:

| Campo | Columna | Tipo | Nota |
|---|---|---|---|
| id | `id` | TEXT PK | UUID string (`UUID.randomUUID().toString()`) |
| name | `name` | TEXT | nombre visible |
| createdAt | `created_at` | INTEGER | `System.currentTimeMillis()` al crear |
| notes | `notes` | TEXT? | notas libres del usuario |
| type | `type` | TEXT | `"deck"` o `"list"` — default `"deck"` |
| listName | `list_name` | TEXT? | solo listas: nombre corto (máx 10) |
| listDescription | `list_description` | TEXT? | solo listas |
| groupId | `group_id` | TEXT? | FK a `deck_groups` (grupo de usuario, opcional) |
| sortOrder | `sort_order` | INT | orden dentro del grupo |
| sortMode | `sort_mode` | TEXT? | `null`/`"default"`/`"by_type"`/`"custom"` (orden en pantalla) |

⚠️ **No hay columna de "formato objetivo"**, ni de clan, ni de validez. El clan y los conteos se
**derivan** de las cartas en runtime (ver SPEC-VALIDACION.md §3). `type='list'` vs `'deck'` es la
única distinción estructural — y es exactamente la diferencia entre "lista simple" (tope 1 por
carta, sin conteos de legalidad, sin holdings) y "mazo" (ver más abajo).

Relación mazo ↔ cartas: `DeckCardEntity` (`src/DeckCardEntity.kt`) → tabla `deck_cards`, PK
compuesta `(deck_id, card_id)`:

| Campo | Columna | Nota |
|---|---|---|
| deckId | `deck_id` | FK → decks.id, **ON DELETE CASCADE** |
| cardId | `card_id` | id de carta (sin FK formal a cards) |
| quantity | `quantity` | ≥ 0; **quantity=0 = carta "recortada"** (la fila persiste, ver §4) |
| cardGroupId | `card_group_id` | subgrupo opcional dentro del mazo |
| sortOrder | `sort_order` | posición manual en modo CUSTOM |

Detalle completo de columnas/índices/relaciones en `MODELO-DATOS-MAZO.md`.

## 2. Múltiples mazos: crear / renombrar / borrar / duplicar

Todas las mutaciones pasan por `DeckRepository` (nunca desde la UI directo — es una regla de
arquitectura del proyecto).

- **Crear** (`createDeck`, `src/DeckRepository.kt:36-59`): genera UUID, calcula `sortOrder` =
  max del tipo + 1, inserta. Firma:
  `createDeck(name, type="deck", listName?, listDescription?, groupId?) → String (id)`.
  En la UI (`DecksViewModel.createDeck`, `:251-267`) el mazo recién creado **se activa
  automáticamente** (`setActiveDeckId(newId)`).
- **Renombrar** (`renameDeck(id, name)`).
- **Borrar** (`deleteDeck(id)`): borra la fila; `deck_cards` cae por CASCADE. Si el mazo borrado
  era el activo, se limpia el activo (`DecksViewModel.confirmDelete:280-286`,
  `DeckDetailViewModel.confirmDelete:418-425`).
- **Notas** (`updateNotes`), **grupo** (`assignDeckToGroup`), **orden** (`reorderDecks`),
  **modo de orden** (`updateSortMode`).
- ⚠️ **Duplicar NO existe.** No hay `duplicateDeck`/`copyDeck`/`cloneDeck` en ningún lado
  (verificado por búsqueda global). La única forma de "copiar" un mazo es exportar → crear otro →
  importar el texto. Es una decisión de producto para el launcher si querés agregarlo.

Grupos de usuario (`deck_groups`, opcional, para organizar la lista de mazos): CRUD en
`DeckRepository:87-105` (`createGroup`, `renameGroup`, `deleteGroup` que primero desvincula los
mazos). No afectan validez ni composición; son puramente organizativos. Un mazo y una lista viven
en secciones separadas (`section_type`).

## 3. Mazo activo (`activeDeckId`)

- Es un **único id** persistido en DataStore (`AppPreferences.activeDeckId`, no en la tabla
  `decks`). Un solo mazo activo global a la vez.
- **Qué lo setea:** `createDeck` en la UI (auto-activa el nuevo); el ítem "Activar" del menú ⋮
  (`DeckDetailViewModel.setAsActive`); tap en una tarjeta de la lista de mazos
  (`DecksViewModel.activateDeck`); selección desde el popup de "mazos recientes".
- `setActiveDeckId(id)` también registra la apertura en `recentDeckIds` (lista de máx 20,
  más reciente primero) — de ahí sale el popup de recientes.
- **Para qué se usa:**
  1. Los botones +/− del **Buscador** operan sobre el mazo activo
     (`SearchViewModel.addCard/removeCard` usan `activeDeckId.value`); sin mazo activo, los
     botones no hacen nada.
  2. La barra inferior de navegación muestra el mazo activo con su clan y conteos
     (`MainViewModel.activeDeckInfo`).
  3. El popup de mazos recientes (long-press en el tab del mazo activo).
- La **pantalla de detalle de mazo** (`DeckDetailScreen`/`DeckDetailViewModel`) NO usa el activo:
  opera siempre sobre el `deckId` de la ruta. O sea, podés editar cualquier mazo sin activarlo;
  el activo solo importa para el flujo "buscar carta → +" del Buscador.

## 4. Agregar / quitar cartas desde la pantalla de mazo

Dos superficies agregan cartas: el **Buscador** (botones +/− sobre el mazo activo, ya portado en
tu Buscador) y la **pantalla de mazo** (`DeckDetailScreen`). Acá va la de la pantalla de mazo
(`DeckDetailViewModel`):

- **Agregar** (`addCard`, `:232-241`): llama `DeckRepository.addCard`. Si la carta estaba en
  período de gracia de recorte o de borrado, primero la rescata. El `AddCardResult` **se ignora**
  (no hay toast de "baneada"/"límite").
- **Quitar 1** (`removeCard`, `:251-261`): si quantity>1, decrementa; si quantity==1, hace
  **recorte** (setea quantity=0, la fila persiste) con período de gracia visual de 2.5s.
- **Recortar** (`trimCard`, drag-to-action): quantity→0 directo desde cualquier cantidad.
- **Fijar cantidad exacta** (`setCardQuantity`, drag-to-action 1/2/3): clampa a `[0, maxCopies]`;
  `maxCopies` = 1 para listas, `card.maxCopies` para mazos. 0 → recorta.
- **Eliminar del mazo** (`deleteCard`): borra la fila entera (no recorte) con gracia de 2.5s con
  tinte rojo; el `[+]` durante la gracia la rescata.

**Comportamiento al llegar a 0 — dos conceptos distintos (⚠️ importante para el port):**

| Acción | Efecto en `deck_cards` | Reversible |
|---|---|---|
| **Recortar** (quantity→0) | La fila **persiste** con `quantity=0` ("carta recortada") | Sí, con `[+]` |
| **Eliminar** (`deleteCardEntry`) | La fila **se borra** | Solo durante gracia 2.5s |

`removeCard` en el **Buscador** usa `keepRowAtZero=false` (borra la fila al llegar a 0);
en la **pantalla de mazo** usa `keepRowAtZero=true` (recorta = conserva). Es el parámetro
`keepRowAtZero` de `DeckRepository.removeCard/setCardQuantity` (`:161-190`).

Las cartas recortadas (quantity=0):
- **no** cuentan en conteos, stats ni export;
- se ocultan en la grilla salvo que se active "Mostrar recortadas";
- se limpian en masa con "Limpiar recortadas" (`removeAllTrimmed` → `DELETE ... WHERE quantity=0`).

El diseño de "recorte" existe para que el usuario baje una carta a 0 sin perder su lugar/orden y
pueda re-subirla — algo que una lista simple (borrar = fuera) no tiene. Si el launcher no quiere
esta UX, puede tratar quantity=0 como borrado y no perder paridad de datos (los golden nunca
dependen de filas quantity=0: el export las filtra).
