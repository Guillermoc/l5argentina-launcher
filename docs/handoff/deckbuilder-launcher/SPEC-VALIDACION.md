# SPEC — Validación de mazo

El corazón del handoff. Fuentes: `src/DeckRepository.kt` (única capa de mutación),
`src/AddCardResult.kt`, `src/DeckDetailViewModel.kt` (indicadores de UI). Commit `a9a48f1`.

## TL;DR — qué es "válido" en esta app

> **No existe un motor de validación de mazo.** No hay `DeckValidator`, no hay un booleano
> `isLegal`, no hay ✅/❌. La app **nunca impide guardar ni activar un mazo inválido.**

Lo único que la app hace es:

1. **Al agregar una carta** (`DeckRepository.addCard`), rechaza 3 cosas: baneada, slot de holding
   inicial ya cubierto, y superar `maxCopies`. Devuelve un `AddCardResult`.
2. **Muestra indicadores blandos** en la pantalla de mazo: contadores Dynasty/Fate en rojo si
   `< 40`, y una fila "pendiente" por cada slot de holding requerido sin cubrir. **Ninguno
   bloquea nada.**

Eso es **toda** la diferencia con una "lista de cartas simple": límites de copia por carta,
rechazo de baneadas, exclusividad de slots de holding inicial, y partición por
`deck_group`/tipo para conteos y export. Todo lo demás (cantidad de strongholds, tamaño mínimo,
número de provincias, legalidad por formato) **no se valida en absoluto.**

---

## 1. `AddCardResult` — el enum completo

`src/AddCardResult.kt`:

```kotlin
sealed class AddCardResult {
    data object Success : AddCardResult()
    data object Banned : AddCardResult()
    data object NoActiveDeck : AddCardResult()
    data class LimitReached(val max: Int) : AddCardResult()
    data object SlotAlreadyCovered : AddCardResult()
}
```

Orden de chequeos en `DeckRepository.addCard(deckId, cardId)` (`src/DeckRepository.kt:138-157`):

| # | Condición | Resultado | Nota |
|---|---|---|---|
| 1 | `cardDao.getById(cardId) == null` | `NoActiveDeck` | ⚠️ **Nombre engañoso**: significa "carta no encontrada en el catálogo", no "no hay mazo activo". Golden `add-card-inexistente`. |
| 2 | `card.banned == true` | `Banned` | Se chequea **antes** que maxCopies. Golden `add-banned-rechazado`. |
| 3 | `deckDao.getDeckById(deckId) == null` | `NoActiveDeck` | El mazo no existe. Golden `add-deck-inexistente`. |
| 4 | `card.initialHoldingSlot != null` **y** `countCardsInSlot(deckId, slot) > 0` | `SlotAlreadyCovered` | Ese slot ya tiene una carta con quantity>0. Golden `add-holding-slot-ocupado`. |
| 5 | `current >= maxCopies` | `LimitReached(maxCopies)` | `maxCopies = if (deck.type == "list") 1 else card.maxCopies`. Golden `add-max-copias-deck`, `add-unique-max1`, `add-lista-tope-1`. |
| — | si nada de lo anterior | `Success` (quantity += 1) | — |

`maxCopies` **es el de `CardEntity.maxCopies`**, ya con rules.json aplicado (banned→0, Unique→1,
stronghold/sensei→1, override explícito, o default 3 — ver §3 y SPEC del Buscador
`SPEC-LEGALIDAD.md` si necesitás el detalle del import). El launcher ya tiene esta columna.

⚠️ **Cartas baneadas quedan con `maxCopies=3`** tras el sync completo (el reset de reglas no
propaga maxCopies=0 a las banned). Es invisible acá porque el chequeo `banned` gana primero,
pero **no derives "baneada" de `maxCopies==0`** en el launcher — usá el flag `banned`.

## 2. Lo que NO se valida (⚠️ diferencias clave con las reglas del juego)

Ninguna de estas reglas del reglamento L5R está implementada:

| Regla del juego | En esta app | Golden |
|---|---|---|
| **Exactamente 1 stronghold por mazo** | ❌ No se controla. Podés agregar 2+ strongholds distintos, ambos `Success`. | `add-dos-strongholds` |
| **Mínimo 40 cartas Dynasty / 40 Fate** | ❌ Solo un contador que se pinta rojo si `<40`. No hay tope superior. No bloquea. | `conteos-dynasty-fate` |
| **Máximo de cartas por provincia / nº de provincias** | ❌ No existe el concepto de provincia en el mazo (solo en el sandbox Goldfish, en runtime). | — |
| **Legalidad por formato** (carta legal en el formato del mazo) | ❌ **El mazo no declara formato objetivo.** La columna `legality` de las cartas nunca se cruza contra el mazo. Podés mezclar cartas de cualquier era. | — |
| **Holdings iniciales requeridos cubiertos** | ⚠️ Solo se **avisa** (fila "pendiente"), no bloquea guardar/activar. | `slots-requeridos-sin-cubrir` |
| **1 sensei máx / sensei excluye stronghold** | ❌ No se controla (sensei tiene maxCopies=1 por sí solo, pero nada impide stronghold+sensei juntos). | — |

**El mazo no tiene "formato objetivo" en ningún lado del modelo** (`DeckEntity` no tiene columna
de formato — ver MODELO-DATOS-MAZO.md). La única noción de formato en la app es global: la tabla
`format_initial_holding_slots` (hoy solo `samurai`) define slots requeridos que aplican a
**todos** los mazos por igual, no por elección del usuario.

## 3. Stronghold y Sensei: son cartas del mazo, no selección aparte

⚠️ Diferencia importante con muchos deckbuilders de L5R: **no hay un selector de stronghold/sensei
separado.** El stronghold es una carta más con `type='stronghold'` y `deck_group='starter'`; se
agrega con el mismo `addCard` que cualquier otra. Consecuencias:

- Podés tener 0, 1 o N strongholds en un mazo (`add-dos-strongholds`).
- El "clan del mazo" se deriva del **primer** stronghold encontrado
  (`MainViewModel.kt:65-68`, `DecksViewModel.kt:122-125`):
  `deckCards.firstOrNull { type == "stronghold" }?.clan ?: "No clan" / "—"`.
  Si hay dos strongholds de clanes distintos, gana el primero del orden de la query (no
  determinista de cara al usuario).
- `maxCopies` de stronghold y sensei = 1 (asignado en import), así que no podés tener 2 copias
  del **mismo** stronghold, pero sí dos strongholds distintos.
- El honor/oro/provincia del stronghold: ver `SPEC-STATS-INICIALES.md`.

El grupo `starter` (`src/DeckGroup.kt`) contiene: `stronghold`, `sensei`, `wind`. Es lo que en
la UI aparece bajo la sección "Starter".

## 4. Import: mismas reglas que addCard, aplicadas por lote

`DeckRepository.importCards(deckId, text)` (`src/DeckRepository.kt:219-283`) aplica las mismas
validaciones pero **acumulando en `ImportResult`** en vez de cortar:

- Carta inexistente **o baneada** → cuenta como `notFound` + su nombre en `notFoundNames`
  (`:257`). ⚠️ Baneada y "no existe" se mezclan en el mismo contador. Golden `import-baneada`,
  `import-desconocida`.
- Slot de holding ya cubierto **(en el mazo o ya visto en este import)** → `notFound` (`:259-264`).
  Golden `import-holding-conflicto`.
- Cantidad pedida se **clampa** a `maxCopies - current` (`:270`): pedir 5 de una carta max 3 →
  importa 3, sin error. Golden `import-clamp-max`.
- `type == "list"` → maxCopies forzado a 1. Golden `import-lista-tope-1`.
- Líneas `#`: `# sort_mode: X` aplica el modo y no va a notas; `# X - (N) [N]` (headers
  auto-generados del export) se descartan; cualquier otra `#` se acumula como nota del mazo.
  Golden `import-notas-headers-sortmode`.
- Línea sin número → cantidad implícita 1. Golden `import-qty-implicita`.

`ImportResult(imported, notFound, notFoundNames)` (`src/ImportResult.kt`).

## 5. Indicadores de validez en la UI (todos blandos)

Lo que el usuario ve para juzgar "¿este mazo sirve?" — **ninguno impide nada**:

### a) Contadores Dynasty / Fate (`DeckDetailViewModel.kt:125-133`, `DeckDetailScreen.kt:852-860`)

```kotlin
private const val SIDE_TARGET = 40   // "Objetivo de legalidad por lado (formato L5R estándar)"
// LegalityCounter: "$label $count/$SIDE_TARGET", color = if (count < SIDE_TARGET) AccentRed else TextPrimary
```

- `dynastyCount` = suma de `quantity` de cartas con `deck_group == "dynasty"` y `quantity > 0`.
- `fateCount` = ídem para `"fate"`.
- Se muestran como `Dynasty 37/40` en rojo si `< 40`, en color normal si `>= 40`.
- **No hay límite superior**, no hay mensaje, no bloquea guardar/activar/exportar.
- Para mazos `type == "list"` los contadores son 0 y se muestra solo el total.
- Golden `conteos-dynasty-fate`.

`SIDE_TARGET = 40` es una **constante hardcodeada** (`DeckDetailScreen.kt:148`), no viene de
datos ni del reglamento en runtime. El launcher puede portarla o parametrizarla por formato.

### b) Slots de holding inicial sin cubrir (`DeckDetailViewModel.kt:145-153`)

```kotlin
uncoveredRequiredSlots = required.filter { it.slotId !in coveredSlotIds }.map { it.slotId to it.label }
// coveredSlotIds = slots de las cartas del mazo con quantity>0 e initialHoldingSlot != null
// required = format_initial_holding_slots con required=1  (hoy: bamboo, border — formato samurai)
```

- Por cada slot requerido sin una carta elegible en el mazo, aparece una fila `PendingSlotRow`
  ("Falta: {label}") bajo la sección Starter (`DeckDetailScreen.kt:562-564,224-226`).
- Solo un **aviso visual**. No bloquea. Para `type == "list"` siempre vacío.
- Golden `slots-requeridos-sin-cubrir`: sin holdings → `[bamboo, border]`; tras agregar
  Border Keep → `[bamboo]`.

### c) Cartas huérfanas (no es validez de deckbuilding, es integridad de datos)

Si el catálogo se re-descarga y una carta del mazo desaparece, la fila `deck_cards` queda
huérfana (card_id sin carta). Se muestra un badge rojo + popup con `card_id ×qty`
(`DeckDetailViewModel.orphanCards`, ADR-013). No afecta la validez de juego; es para que el
usuario no pierda datos silenciosamente. El launcher puede ignorarlo si su catálogo es estable.

## 6. Resumen: lista exhaustiva de "condiciones que marcan algo"

Para el launcher, estas son **todas** las señales que la app produce (con su mensaje/efecto real):

| Señal | Dónde | Efecto | Bloquea |
|---|---|---|---|
| `AddCardResult.Banned` | addCard | La carta no se agrega | Sí (esa carta) |
| `AddCardResult.LimitReached(max)` | addCard | La carta no se agrega | Sí (esa carta) |
| `AddCardResult.SlotAlreadyCovered` | addCard | La carta no se agrega | Sí (esa carta) |
| `AddCardResult.NoActiveDeck` | addCard | Nada se agrega (carta o mazo inexistente) | Sí (esa carta) |
| `ImportResult.notFound` + nombres | importCards | Se listan las no importadas (baneadas/inexistentes/slot ocupado) | No (informativo) |
| Contador Dynasty/Fate rojo | DeckDetail | Texto rojo `N/40` | **No** |
| `PendingSlotRow` | DeckDetail | Fila "Falta: {label}" | **No** |
| Badge huérfanas | DeckDetail | Badge + popup | **No** |

⚠️ Ninguna `AddCardResult` distinta de `Success` produce un **mensaje** en la UI del Buscador ni
del deckbuilder: el `addCard` se llama desde los botones +/− y el resultado **se ignora**
(`SearchViewModel.addCard`/`DeckDetailViewModel.addCard` lanzan la corrutina sin leer el retorno).
El usuario simplemente ve que la carta no incrementa. Es una decisión de producto a revisar en el
launcher (mostrar un toast "baneada"/"límite alcanzado" sería una mejora, no una paridad).
