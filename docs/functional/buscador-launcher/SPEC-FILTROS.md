# SPEC — Filtros del Buscador

Fuente de verdad: `src/CardQueryCompiler.kt` (compilador de criterios → SQL), `src/CardSearchCriteria.kt`
(modelo de criterios) y `src/SearchViewModel.kt` (capa UI que decide cómo se cruzan).
Generado leyendo el código en el commit `a9a48f1` — cada afirmación cita archivo y línea.

## Arquitectura

```
CardSearchCriteria  →  CardQueryCompiler.compile()  →  SELECT * FROM cards WHERE 1=1 AND … →  SQLite
        ↑                                                                                        ↓
   FilterModal / barra de búsqueda                                             lista ordenada de CardEntity
```

- Todo el filtrado es **una sola query SQL** sobre la tabla `cards` (`src/CardQueryCompiler.kt:10`).
  No hay post-filtrado en memoria, con dos excepciones de capa UI:
  1. **"Solo lista"** (`soloListActive`): filtra el resultado en memoria por los ids de la lista
     seleccionada (`src/SearchViewModel.kt:131-133`) — ver §Solo lista.
  2. **Orden por Set** y **"Ver todas las impresiones"**: se aplican en memoria en
     `SearchViewModel.displayCards` (`src/SearchViewModel.kt:155-201`) — ver §Ordenamiento.
- El SQL exacto generado para cada combinación está en `fixtures/golden-tests.json` (campo `sql`
  de cada caso).

## Reglas globales de combinación

| Regla | Semántica | Fuente |
|---|---|---|
| Filtros distintos entre sí | **AND** (cada bloque se agrega con ` AND …`) | todo `compile()` |
| Multiselección dentro de un filtro (types, clans, rarities, sets, formats, designators) | **OR** | p. ej. `src/CardQueryCompiler.kt:92,101-124,134-153` |
| Varias palabras en `name` o `text` | **AND** entre palabras | `:14-17`, `:79-87` |
| Varias keywords positivas | AND u OR según `keywordMatchAll` (default **AND**) | `:20-37` |
| Keywords negativas | cada una agrega un `AND NOT (…)` | `:40-51` |
| Filtro vacío (lista vacía / null) | **no agrega cláusula** → no restringe | guards `isNotEmpty()` en cada bloque |

Case-sensitivity global:

- Todo lo que usa `LIKE` es **case-insensitive solo para ASCII** (comportamiento default de SQLite,
  igual en Android). No hay normalización de acentos (los datos 2.3.1 no tienen acentos en `name`).
- Todo lo que usa `IN (…)` o `=` es **case-SENSITIVE** → ver ⚠️ en Rareza.

## Estado default al abrir la pantalla

`CardSearchCriteria()` con todos los defaults (`src/CardSearchCriteria.kt:16-65`):
listas vacías, rangos null, `keywordMatchAll=true`, `includeActionKeywords=true`,
`includeSubtitleKeywords=true`, `includeBanned=false`, `onlyFavorites=false`, `includeHidden=false`,
`onlyHidden=false`, `onlyInitialHoldings=false`, `listId=null`, `sortField=NAME`, `sortAscending=true`.

Con ese default la query es (caso `query-vacia-defaults` del fixture):

```sql
SELECT * FROM cards WHERE 1=1 AND banned = 0
  AND NOT EXISTS (SELECT 1 FROM user_card_data WHERE card_id = cards.id AND type = 'hidden')
ORDER BY name ASC
```

Es decir: **el default ya excluye baneadas y ocultas**. Los filtros **no** se persisten entre
sesiones (estado vive en el ViewModel; lo único persistente es el preset manual, ver §Preset).

`isEmpty` (`src/CardSearchCriteria.kt:68-79`) es true solo con todo en default; **excluye
`sortField`/`sortAscending`** (cambiar el orden no cuenta como "filtro activo"). La UI lo usa para
mostrar el botón de limpiar y el indicador "Bloqueado por filtros" en sugerencias.

---

## Filtro por filtro

### 1. Nombre (`name: String`)

- **Campo:** columna `name` (viene de `name` del cards.json, sin transformar).
- **Semántica:** se separa por whitespace; cada palabra genera `AND name LIKE '%palabra%'`
  (`src/CardQueryCompiler.kt:14-17`). Substring puro — **sin** word-boundary: `isad` matchea
  "K**isad**a". Case-insensitive ASCII. No es búsqueda de frase: `hida kisada` matchea cualquier
  nombre que contenga ambas palabras en cualquier posición.
- **Origen UI:** barra de búsqueda en modo NAME (sin prefijo de operador). Se aplica **en vivo**
  con cada tecla (`src/SearchViewModel.kt:294-303`), sin debounce sobre la query principal.
- **Default:** `""` (sin cláusula).
- Detalles finos en `SPEC-BUSQUEDA-TEXTO.md`.

### 2. Keywords (`keywords`, `negativeKeywords`, `keywordMatchAll`, `includeActionKeywords`, `includeSubtitleKeywords`)

- **Campos de la carta:**
  - `keywords`: columna pipe-delimited `"|Crab Clan|Samurai|Unique|"` (del array `keywords` del
    cards.json — campo editorial derivado del encabezado del `text`; ver MODELO-DATOS.md).
  - `action_keywords`: pipe-delimited, keywords que preceden a un designador dentro de un bloque
    `<b>…:</b>` del texto (p. ej. `|Recon|` de "`<b>Recon Battle:</b>`"). Solo populado para cartas
    con legalidad ivory/TwentyFestivals/samurai (ver MODELO-DATOS.md §action_keywords). 728 cartas.
  - `subtitle`: lo que sigue a la primera coma del `name` (85 cartas).
- **Semántica** (`src/CardQueryCompiler.kt:20-37`): cada keyword `kw` arma
  `(keywords LIKE '%|kw|%' [OR action_keywords LIKE '%|kw|%'] [OR subtitle LIKE '%kw%'])`
  según los toggles; entre keywords se une con AND (`keywordMatchAll=true`, default) u OR.
  - Los pipes en el patrón hacen **match exacto de valor completo** dentro de la lista:
    `|Kami|` no matchea `|Shiba Kami|`… ojo: `LIKE '%|Kami|%'` **sí requiere** los dos pipes, así
    que "Kami" ≠ "Kami's Guidance". Es match exacto del elemento.
  - ⚠️ **`subtitle` va SIN pipes** (`%kw%`, `:31-33`): es substring del subtítulo. `Kami` matchea
    subtítulos que contengan "Kami" en cualquier parte (caso `keyword-subtitle-substring` del
    fixture: 30 resultados). Asimetría deliberada-o-accidental respecto de las otras dos columnas —
    decidir si se porta.
- **Negativas** (`:40-51`): cada una agrega
  `AND NOT (keywords LIKE '%|kw|%' [OR action_keywords LIKE '%|kw|%'])`.
  - **No** consultan `subtitle` (evita falsos positivos por substring), pero **sí** heredan el
    toggle `includeActionKeywords`.
  - Siempre AND entre sí (no las afecta `keywordMatchAll`).
- **Valores posibles:** libres; la UI sugiere desde la tabla `keywords` (838 códigos importados de
  `filters.json`, ver `fixtures/data/filters-2.3.0.json`), pero el usuario puede tipear cualquiera.
  En el modal, confirmar el input lo **divide por espacios** (cada palabra = keyword separada);
  tocar una sugerencia agrega el código exacto (con espacios internos).
- **Sin el campo:** cartas con `keywords=""` simplemente no matchean (no hay tratamiento especial).
- **Default:** listas vacías; toggles `true/true/true`.

### 3. Designador (`designators: List<String>`)

- **Campo:** columna `text` (HTML crudo).
- **Semántica** (`src/CardQueryCompiler.kt:53-70`): OR entre designadores; cada uno prueba 3
  patrones **anclados al cierre del bold**:
  `text LIKE '%D:</b>%'` OR `text LIKE '%D</b>:%'` OR `text LIKE '%D</b>,%'`.
  El anclaje evita falsos positivos: "discard a Dynasty card … `<b>Limited</b>:`" **no** matchea
  el designador `Dynasty` (el comentario en `:53-59` documenta el bug que se arregló).
- **Valores posibles (hardcodeados en la UI):** `Open, Limited, Engage, Battle, Dynasty, Interrupt,
  Reaction` (`FilterModal.kt:287` en el repo original — chips fijos, no derivados de la DB).
- Solo produce matches útiles en cartas cuyos textos usan la convención `<b>Designador:</b>`
  (eras Ivory/20F principalmente).
- **Default:** vacío.

### 4. Texto de carta (`text: String`)

Ver `SPEC-BUSQUEDA-TEXTO.md` §Filtro de texto. Resumen: con comillas = substring exacto del HTML
crudo; sin comillas = AND de palabras completas sobre una versión normalizada (puntuación y
`<>[]()/'"` reemplazados por espacio) (`src/CardQueryCompiler.kt:72-88`).

### 5. Tipo (`types: List<String>`)

- **Campo:** columna `type` (lowercase, del `type` del JSON).
- **Semántica:** `AND type IN (…)` (`src/CardQueryCompiler.kt:91-94`). OR dentro del filtro.
  Case-sensitive, pero no importa en la práctica: datos y chips usan lowercase.
- **Valores posibles:** enum hardcodeado `CardType` (`src/CardType.kt`): personality, holding,
  event, strategy, item, spell, follower, ring, ancestor, stronghold, sensei, wind, region,
  celestial. La UI **omite `wind`** (chips del modal y sugerencias `t.`); el dataset 2.3.1 no tiene
  cartas wind.
- **Default:** vacío.

### 6. Clan (`clans: List<String>`)

- **Campo:** columna `clan` (lowercase; NULL si la carta no tiene clan) + rama especial sobre
  `keywords` para sensei.
- **Semántica** (`src/CardQueryCompiler.kt:96-125`): `AND ( … OR … )` con estas ramas:
  - Clanes nombrados: `clan IN ('crab', …)`.
  - **Sensei:** los sensei tienen `clan = NULL`; su compatibilidad vive en keywords. Si hay clanes
    nombrados se agrega `OR (type = 'sensei' AND (keywords LIKE '%|Crab|%' OR keywords LIKE
    '%|Crab Clan|%' OR … OR keywords LIKE '%|All Clans|%' OR keywords LIKE '%|Any|%' OR keywords
    LIKE '%|Any Clan|%'))`. El nombre del clan se capitaliza (`crab` → `Crab`, `:109`).
    Caso `clan-sensei-compat` del fixture: dragon+sensei = 15 cartas.
  - **`noclan`:** `OR clan IS NULL OR clan IN ('shadowlands','ratling','naga','hare','ronin',
    'ninja','monk','spirit')` (`:118-122`) — "Sin clan" incluye las **facciones menores**
    (lista hardcodeada en el compilador).
- **Valores posibles:** enum hardcodeado `Clan` (`src/Clan.kt`): crab, crane, dragon, lion, mantis,
  phoenix, scorpion, spider, unicorn, noclan. Los valores de facciones menores existen en la
  columna `clan` de la DB pero **no son seleccionables individualmente** — solo entran vía noclan.
- **Sin el campo:** `clan NULL` solo aparece con `noclan`.
- **Default:** vacío.

### 7. Rareza (`rarities: List<String>`)

- **Campo:** columna `rarity` (string cruda del JSON; **puede venir en minúscula**).
- **Semántica:** `AND rarity IN (…)` (`src/CardQueryCompiler.kt:128-131`).
- **Valores posibles:** enum hardcodeado `Rarity` (`src/Rarity.kt`): `P R U C F` (mayúsculas).
- ⚠️ **Bug/comportamiento accidental:** `IN` es case-sensitive en SQLite y el dataset 2.3.1 trae
  rarezas en minúscula: `p`×338, `u`×2, `f`×9, `r`×3, `c`×2. Filtrar `P` devuelve solo las 160
  cartas con `P` mayúscula y **deja afuera las 338 promos con `p`** (casos
  `rareza-P-case-sensitive` y `rareza-R` del fixture). En el launcher hay que decidir: portar el
  bug (para paridad byte a byte) o normalizar rareza al importar. Recomendación: normalizar y
  regenerar los golden esperados para esos 2 casos.
- **Sin el campo:** `rarity` es NOT NULL (el importer guarda `""` si falta); `""` nunca matchea.
- **Default:** vacío.

### 8. Edición / Set (`sets: List<String>`)

- **Campo:** columna `sets`, pipe-delimited con **todas** las ediciones de la carta
  (`"|Celestial|Emperor|WoH|"`), derivada de `printings[].edition` (ver MODELO-DATOS.md).
- **Semántica:** OR de `sets LIKE '%|Code|%'` (`src/CardQueryCompiler.kt:134-142`). Una carta
  multi-printing matchea por **cualquiera** de sus ediciones, no solo la del arte mostrado
  (caso `edicion-carta-multi-printing`: Border Keep aparece filtrando por `WoH` aunque muestra
  el arte de Emperor).
- **Valores posibles:** derivados de la DB — tabla `sets`, poblada desde `filters.json`
  (48 sets, código+nombre; `fixtures/data/filters-2.3.0.json`). La UI agrupa los chips por arco
  usando separadores hardcodeados (`ARC_SEPARATOR_CODES` en FilterModal: Samurai, Celestial,
  Emperor, Ivory, TwentyFestivals, SExMRP, P) y el orden canónico de `presortSets`
  (ver §Ordenamiento por Set).
- **Sin el campo:** toda carta tiene al menos una edición.
- **Default:** vacío.

### 9. Legalidad / Formato (`formats: List<String>`)

- **Semántica:** OR de `legality LIKE '%|fmt|%'` (`src/CardQueryCompiler.kt:145-153`), columna
  pipe-delimited con la **unión histórica** de legalidades de todos los printings.
- ⚠️ **Este filtro no tiene UI hoy**: el FilterModal no renderiza ninguna sección de formato
  (verificado en FilterModal.kt completo). El criterio, el compilador y la normalización en
  `applyFilters` (`src/SearchViewModel.kt:405-407`: si el draft trae *todos* los formatos se
  normaliza a lista vacía = sin restricción, ADR-011) quedaron vivos pero inalcanzables.
- ⚠️ **Mismatch de códigos**: la tabla `formats` (de filters.json) usa el código `20F`
  ("Twenty Festivals") pero las cartas guardan `TwentyFestivals` en `legality`. Filtrar por `20F`
  da **0 resultados** (caso `formato-20F-mismatch`); por `TwentyFestivals` da 1545
  (caso `formato-TwentyFestivals`). Si el launcher expone este filtro, tiene que mapear el código
  o usar los valores reales de legality: `samurai, celestial, emperor, ivory, TwentyFestivals`.
- Más contexto y relación con banlist en `SPEC-LEGALIDAD.md`.
- **Default:** vacío (sin restricción).

### 10. Stats numéricos

Todos comparten el patrón: rango `[min, max]` con inputs numéricos independientes; cualquiera de
los dos puede faltar. Sin rango ni flag → sin cláusula.

| Stat | Columnas | Flag especial | UI |
|---|---|---|---|
| Fuerza | `force` + `force_special` | `includeSpecialForce` ("Include +\*") | sí |
| Chi | `chi` | — | sí |
| Costo | `cost` + `cost_special` | `includeSpecialCost` ("Include \*") | sí |
| Honor req. | `honor` + `honor_req_negative` | `includeNegativeHonorReq` (Include "-") | sí |
| Honor personal | `personal_honor` + `personal_honor_negative` | `includeNegativePersonalHonor` | sí |
| Focus | `focus` | — | sí |
| Prod. de oro | `gold_production` + `gold_production_special` | `includeSpecialGoldProduction` | **no** (campo del criteria sin UI; el filtro se eliminó del modal) |

- **Semántica con flag** (`appendSpecialOrRange`, `src/CardQueryCompiler.kt:224-239`):
  `AND (col_special = 1 OR (col IS NOT NULL AND col >= ? AND col <= ?))` — el rango numérico
  **excluye NULLs explícitamente**; el flag reincorpora las cartas cuyo stat impreso es `*`/`+*`
  (guardadas como col=NULL + special=1). Con flag solo (sin rango): `AND (col_special = 1)`.
- **Honor negativo** (`appendNegativeOrRange`, `:242-257`): idéntico pero con
  `honor_req_negative`/`personal_honor_negative` = 1 para cartas con `-` impreso.
- **Chi y Focus** (`:162-163,184-185`): solo `AND chi >= ?` / `AND chi <= ?` — sin cláusula
  `IS NOT NULL` (innecesaria: NULL nunca satisface `>=`) y **sin flag especial**.
- Cómo se llenan esas columnas (qué es `*`, `+*`, `-`, `+2`): MODELO-DATOS.md §Stats.
- Casos del fixture: `fuerza-rango`, `fuerza-rango-con-especial`, `fuerza-rango-item-sin-especial`,
  `costo-cero-excluye-asterisco` (1240) vs `costo-incluye-asterisco` (1249 = +9 strategies con
  costo `*`), `honor-req-guion`, `honor-req-solo-rango`, `chi-min`, `focus-rango`.
- **Default:** todo null / false.

### 11. Baneadas (`includeBanned: Boolean`)

- `false` (default) → `AND banned = 0` (`src/CardQueryCompiler.kt:194`). Las baneadas se
  **ocultan**, no se marcan (en el buscador; el visor sí muestra la pastilla BANNED si la carta
  entra por `includeBanned=true`).
- `true` → sin cláusula (aparecen baneadas y no baneadas).
- Origen del flag `banned`: rules.json (ver SPEC-LEGALIDAD.md).
- Casos: `baneada-excluida-default` (1) vs `baneada-incluida` (4).

### 12. Favoritas / Ocultas (`onlyFavorites`, `includeHidden`, `onlyHidden`)

Datos de usuario en tabla `user_card_data (card_id, type)` con `type ∈ {'favorite','hidden'}`.

- `onlyFavorites=true` → `AND EXISTS (SELECT 1 FROM user_card_data WHERE card_id = cards.id AND
  type = 'favorite')` (`:197-199`).
- Ocultas (`:202-206`), en orden de prioridad:
  - `onlyHidden=true` → `AND EXISTS (… type = 'hidden')` — **ignora** `includeHidden`.
  - si no, `includeHidden=false` (default) → `AND NOT EXISTS (… type = 'hidden')`.
  - si no (`includeHidden=true`) → sin cláusula.
- En la UI, activar "Solo ocultas" en el modal **resetea todos los demás filtros**
  (`FilterModal.kt:435`: `onDraftChange(CardSearchCriteria(onlyHidden = true))`) y muestra una
  advertencia. Desactivarlo solo apaga el flag.
- Casos del fixture (con el seed documentado en `userDataSeed`): `solo-favoritas`,
  `ocultas-excluidas-default`, `ocultas-incluidas`, `solo-ocultas`.

### 13. Solo holdings iniciales (`onlyInitialHoldings: Boolean`)

- `true` → `AND initial_holding_slot IS NOT NULL` (`:209`).
- La columna se setea al importar si el `name` de la carta está en la lista de elegibles de
  `filters.json` (`formats[].initialHoldingSlots[].eligibleCards`); hoy son 7 cartas
  (Bamboo Harvesters ×3 variantes, Border Keep ×4 variantes).
- El chip solo se muestra si existe al menos una carta elegible (`hasInitialHoldings`).
- **Default:** false.

### 14. Solo lista (`listId` + filtrado en memoria)

Dos mecanismos distintos que conviven:

- `criteria.listId != null` → SQL `AND id IN (SELECT card_id FROM deck_cards WHERE deck_id = ?)`
  (`:188-191`). ⚠️ **La UI actual nunca setea `criteria.listId`** — es un campo soportado por el
  motor (caso `filtro-lista` del fixture) pero el flujo real usa el mecanismo 2.
- **Mecanismo real de la UI**: el toggle "Solo lista" guarda `soloListActive` + `selectedListId`
  en el estado del tab (fuera del criteria) y filtra el resultado SQL **en memoria**:
  `cards.filter { it.id in listCardIds }` (`src/SearchViewModel.kt:131-133`), donde `listCardIds`
  son los ids de la lista seleccionada. Al activarlo sin lista seleccionada se abre el selector.
- Consecuencia observable: ambos producen el mismo conjunto (la intersección), pero el mecanismo
  UI **no** aparece en `activeSearchSummary` ni en `isEmpty`.

### 15. Ordenamiento (`sortField`, `sortAscending`)

- **Campos:** enum `SortField` (`src/CardSearchCriteria.kt:6-13`): NAME (default), COST, FORCE,
  CHI, PERSONAL_HONOR, SET. Dirección default: ascendente. Cambiar el orden **no** cuenta como
  filtro activo.
- **SQL** (`src/CardQueryCompiler.kt:211-219`):
  - NAME y SET → `ORDER BY name ASC|DESC`.
  - resto → `ORDER BY CASE WHEN col IS NULL THEN 1 ELSE 0 END ASC, col ASC|DESC, name ASC` —
    **NULLs siempre al final** (en ambas direcciones) y desempate por `name ASC`.
- **SET es especial** (ADR-015): el SQL solo da orden base por name; el orden real se aplica en
  memoria en `displayCards` (`src/SearchViewModel.kt:155-201`):
  1. Rank de cada carta = posición de la **edición de la impresión mostrada** (carpeta de
     `image_path`: `images/cards/{edición}/{id}.jpg`) en el orden canónico de sets.
  2. Si esa carpeta no matchea ningún código de la tabla `sets`, fallback al set más temprano de
     la columna `sets` de la carta (`cardSetRank`, `:554-557`).
  3. Sets desconocidos → siempre al final, en ambas direcciones (`:168-175`).
  4. Desempate: `printingId` (nombre de archivo sin `.jpg`) ascendente.
  - **Orden canónico de sets** (`src/SetOrdering.kt`): el orden de la tabla `sets` (= orden del
    array en filters.json), con la excepción `FORCE_BEFORE_SAMURAI = {Filters, Tomorrow, TTT,
    CotM, KD}`: si alguno de esos códigos aparece *después* de "Samurai" se adelanta a justo antes.
    ⚠️ Con filters 2.3.0 ya vienen antes → `presortSets` es un no-op hoy; existe por resiliencia
    ante un filters.json mal ordenado ("Filters" en esa lista es un código legado que ya no existe).
- **"Ver todas las impresiones"** (`expandPrintings`, toggle en el SortModal — solo visualización):
  expande cada carta en una entrada por impresión (`imagePath` preferida + cada path de
  `printing_images`, sin duplicados; `expandCard`, `src/SearchViewModel.kt:561-569`). El `card.id`
  se repite entre entradas (cantidad/favorito/oculta aplican a todas). Con filtro de sets activo,
  solo se muestran las impresiones de esos sets; si ninguna carpeta matchea, se muestra la
  impresión preferida para no perder la carta (`:184-197`). No afecta la query SQL.
- Casos del fixture: `orden-costo-desc`, `orden-fuerza-asc-nulls-al-final`,
  `orden-por-set-sql-base` (membresía; el orden por set es capa UI).

### 16. Preset de filtros

- "Guardar como preset" serializa el `CardSearchCriteria` completo a JSON y lo persiste
  (DataStore); "Cargar preset" lo restaura al draft del modal. Uno solo, sin nombre.
- Es la **única** persistencia de filtros entre sesiones, y es manual.

## Flujo del modal (semántica de aplicar/limpiar)

- El modal edita un **draft** (copia del criteria del tab); nada se aplica hasta "Aplicar filtros"
  (`applyFilters`, `src/SearchViewModel.kt:401-418`). "Cerrar" descarta el draft.
- Al aplicar: se normaliza formats (ADR-011, hoy vestigial), se precarga el resultado antes de
  cerrar el modal (evita flash), se copia `criteria.name` a la barra, se resetea la posición del
  carousel y se incrementa `searchEpoch`.
- "Limpiar filtros" (dentro del modal) resetea el draft a `CardSearchCriteria()` sin aplicar.
- **Limpiar rápido** (ícono en la fila "N resultados"; `clearFiltersQuick`, `:427-436`): resetea
  todo el criteria **preservando `name`** si la barra está en modo NAME (si la barra tenía un
  operador `k.`/`c.`/`t.`/`tx.`, borra todo).
- 3 tabs de búsqueda independientes: cada tab tiene su propio criteria, barra, modal, scroll y
  visor (`SearchTabState`, `src/SearchViewModel.kt:51-69`).
