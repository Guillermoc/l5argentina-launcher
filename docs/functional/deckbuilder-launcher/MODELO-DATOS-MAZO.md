# MODELO-DATOS-MAZO — Entidades de mazo e import/export

Fuentes: `src/DeckEntity.kt`, `src/DeckCardEntity.kt`, `src/DeckGroupEntity.kt`,
`src/DeckCardGroupEntity.kt`, `src/DeckCardWithCard.kt`, `src/DeckRepository.kt`. Commit `a9a48f1`.
No repite el modelo de carta (ver el handoff del Buscador `MODELO-DATOS.md`); solo lo nuevo.

## 1. Entidades Room

### `decks` — `DeckEntity`

```kotlin
@Entity(tableName = "decks")
data class DeckEntity(
    @PrimaryKey val id: String,                                   // UUID
    val name: String,
    @ColumnInfo(name = "created_at") val createdAt: Long,         // System.currentTimeMillis()
    val notes: String? = null,
    @ColumnInfo(name = "type", defaultValue = "deck") val type: String = "deck",   // "deck" | "list"
    @ColumnInfo(name = "list_name") val listName: String? = null,
    @ColumnInfo(name = "list_description") val listDescription: String? = null,
    @ColumnInfo(name = "group_id") val groupId: String? = null,   // FK lógica → deck_groups.id
    @ColumnInfo(name = "sort_order", defaultValue = "0") val sortOrder: Int = 0,
    @ColumnInfo(name = "sort_mode") val sortMode: String? = null  // null|"default"|"by_type"|"custom"
)
```

⚠️ Nombres de columna **snake_case** vía `@ColumnInfo` (`created_at`, `list_name`, etc.) — no
asumas camelCase. No hay columna de formato objetivo, clan, ni validez.

### `deck_cards` — `DeckCardEntity`  (la relación mazo↔carta)

```kotlin
@Entity(
    tableName = "deck_cards",
    primaryKeys = ["deck_id", "card_id"],
    foreignKeys = [ForeignKey(entity = DeckEntity::class, parentColumns = ["id"],
        childColumns = ["deck_id"], onDelete = ForeignKey.CASCADE)]
)
data class DeckCardEntity(
    @ColumnInfo(name = "deck_id") val deckId: String,
    @ColumnInfo(name = "card_id") val cardId: String,
    val quantity: Int,                                            // 0 = "recortada" (fila persiste)
    @ColumnInfo(name = "card_group_id") val cardGroupId: String? = null,
    @ColumnInfo(name = "sort_order", defaultValue = "0") val sortOrder: Int = 0
)
```

- PK compuesta `(deck_id, card_id)` → **una fila por carta distinta**, la multiplicidad va en
  `quantity` (no N filas). Distinto de algunos modelos que guardan una fila por copia.
- **ON DELETE CASCADE** desde `decks`: borrar un mazo borra sus `deck_cards`.
- ⚠️ **No hay FK a `cards`**: `card_id` puede quedar huérfano si el catálogo cambia. La app lo
  maneja con relación nullable + filtrado (ADR-013, ver §4). El launcher, si su catálogo es
  estable, puede poner FK real — pero perdería la resiliencia a re-catálogos.
- `quantity=0` = carta recortada (ver SPEC-DECK-CRUD.md §4). El export las filtra.

### `deck_groups` — `DeckGroupEntity`  (organización, opcional)

```kotlin
@Entity(tableName = "deck_groups")
data class DeckGroupEntity(
    @PrimaryKey val id: String,
    val name: String,
    @ColumnInfo(name = "section_type") val sectionType: String   // "deck" | "list"
)
```

Grupos de usuario para ordenar la lista de mazos (carpetas). No afectan composición ni validez.

### `deck_card_groups` — `DeckCardGroupEntity`  (subgrupos dentro de un mazo)

```kotlin
@Entity(tableName = "deck_card_groups", foreignKeys = [ForeignKey(... onDelete = CASCADE)])
data class DeckCardGroupEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "deck_id") val deckId: String,
    val name: String,
    @ColumnInfo(name = "parent_group") val parentGroup: String   // "starter"|"dynasty"|"fate"
)
```

⚠️ Existe en el schema pero **está en desuso**: `DeckCardEntity.cardGroupId` los referencia, pero
ninguna pantalla actual crea/usa subgrupos (la feature se sacó en el rediseño V2.4). Se puede
ignorar en el launcher.

### `format_initial_holding_slots` / `_eligible_cards` (catálogo, ya en tu Buscador)

Ya las tenés del handoff anterior. Relevancia para mazos: `observeRequiredSlots()` (required=1)
alimenta el aviso de slots sin cubrir (SPEC-VALIDACION.md §5b), y `eligibleCards` (name→slotId)
es lo que hace que una carta tenga `initial_holding_slot` seteado, disparando la exclusividad de
slot en `addCard`.

## 2. Relación de lectura: `DeckCardWithCard`

`src/DeckCardWithCard.kt`:

```kotlin
// Fila cruda del DAO — card NULLABLE (deck_cards puede referenciar cartas ausentes del catálogo)
data class DeckCardWithCardRow(
    @Embedded val deckCard: DeckCardEntity,
    @Relation(parentColumn = "card_id", entityColumn = "id") val card: CardEntity?
)
// Tipo expuesto por DeckRepository (huérfanas ya filtradas) — card non-null
data class DeckCardWithCard(val deckCard: DeckCardEntity, val card: CardEntity)
```

`DeckRepository.getDeckCardsWithCards(deckId)` hace el JOIN y **filtra las filas con `card == null`**
(`:114-117`). Los ViewModels solo ven `DeckCardWithCard` (card garantizada).

## 3. Import / Export — SÍ existe, es un formato de texto propio

Formato de texto plano, sin estándar de comunidad externo — es propio de esta app. Se usa por
portapapeles (Toast "copiado"). No es un `.txt` con estructura especial ni un formato de terceros.

### Export (`DeckRepository.exportDeck`, `src/DeckRepository.kt:289-322`)

Ejemplo **real** (golden `export-roundtrip`, deck con stronghold + dynasty + fate):

```
# Golden 18 - (8) [4]
# Starter - (1) [1]
# Stronghold - (1) [1]
The Seventh Tower
# Dynasty - (5) [2]
# Holding - (2) [1]
2 Jade Works
# Personality - (3) [1]
3 Hida Akeno
# Fate - (2) [1]
# Strategy - (2) [1]
2 A Brave New World
```

Reglas de generación:
- Notas del mazo primero, cada línea prefijada `# ` (si hay notas).
- `# {nombre} - ({total}) [{distintas}]` — header del mazo.
- Por cada `DeckGroup` (starter → dynasty → fate) con cartas: `# {Grupo} - (tot) [dist]`, luego
  por tipo `# {Tipo} - (tot) [dist]`, luego las cartas.
- Línea de carta: `{qty} {nombre}` si qty>1, o solo `{nombre}` si qty==1.
- **Excluye recortadas** (`quantity > 0`). Orden dentro de tipo: alfabético por nombre.
- Si `sortMode != null`, se antepone `# sort_mode: {modo}` (no aparece en el ejemplo porque el
  deck de prueba no tenía modo seteado; lo agrega `exportDeck` cuando corresponde — ver el
  formato de import abajo).

### Import (`DeckRepository.importCards`, `src/DeckRepository.kt:219-283`)

Parsea el mismo formato (y tolera variantes):
- `# sort_mode: X` → aplica el modo, no va a notas.
- `# {texto} - (N) [N]` → header auto-generado, **se descarta** (regex `^.*\s*-\s*\(\d+\)\s*\[\d+\]$`).
- Cualquier otra línea `#` → se acumula como nota del mazo.
- `{n} {nombre}` → agrega n copias de la carta (busca por **nombre exacto**, case-sensitive en
  SQLite `=`); clampa a maxCopies; baneadas/inexistentes/slot-ocupado → `notFound`.
- Línea sin número → cantidad 1.
- Líneas en blanco → ignoradas.

Round-trip verificado (golden `export-roundtrip`): exportar e importar en un mazo nuevo da el
mismo total de cartas (8 → 8). ⚠️ El round-trip **no** preserva cartas cuyo nombre no matchee
exactamente el catálogo del importador (p. ej. si cambió el catálogo), y las recortadas se pierden
(no se exportan).

⚠️ El match por nombre es frágil: nombres con sufijos de experiencia ("- exp") o comas deben
coincidir carácter por carácter. Es el mismo mecanismo de import de mazos que ya está en producción.

## 4. Integridad: cartas huérfanas (ADR-013)

Si `card_id` en `deck_cards` no existe en `cards` (catálogo re-descargado, carta eliminada), la
fila se conserva y la carta simplemente no aparece en UI/export; reaparece si el catálogo se
repuebla. Hay queries de detección con guard "catálogo no vacío" (`getOrphanDeckCards`,
`countOrphanDeckCards` en `src/DeckDao.kt`). Relevante para el launcher solo si su catálogo puede
cambiar bajo los pies de mazos existentes.
