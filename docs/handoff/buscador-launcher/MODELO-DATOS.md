# MODELO-DATOS — Carta, parsing y mapeo de fuentes

Fuentes: `src/CardEntity.kt` (modelo persistido), `src/CardJson.kt` (modelo de entrada),
`src/CardImporter.kt` (transformación), `src/Converters.kt` (formato pipe). Datos reales en
`fixtures/data/`. Commit `a9a48f1`.

## 1. Qué parsea esta app

**JSON, no XML.** La app consume `cards-X.Y.Z.json` (dentro de `cards_db.zip` del CDN), que es una
**derivación editorial del `database.xml` kamisasori** (mismo universo de datos, campos renombrados
y saneados). El launcher parsea el XML directamente, así que la tabla de §5 mapea las tres capas:
columna Room ← campo JSON ← elemento XML.

Estructura real de cards 2.3.1 (6394 cartas, root = array):

```json
{
  "id": "Emperor028", "type": "holding", "name": "Border Keep", "rarity": "F",
  "text": "<b>Border Keep</b>…", "cost": null, "focus": "…", "artist": "…",
  "keywords": ["…"],
  "printings": [
    { "id": "Celestial023", "edition": "Celestial",
      "image": "images/cards/Celestial/Celestial023.jpg",
      "legalities": ["celestial"], "current": false },
    { "id": "Emperor028", "edition": "Emperor",
      "image": "images/cards/Emperor/Emperor028.jpg",
      "legalities": ["emperor"], "current": true },
    { "id": "WoH011", "edition": "WoH", "image": "images/cards/WoH/WoH011.jpg",
      "legalities": [], "current": false }
  ]
}
```

- En 2.3.1 **toda carta tiene `printings`** y exactamente **un** printing con `current: true`.
- Los campos legados top-level (`edition`, `image`, `legal`, `editions`, `legalities`) ya no
  vienen, pero el importer los soporta como fallback (formatos viejos).
- Los valores "numéricos" son **strings** (`"cost": "4"`), con valores especiales `*`, `+*`, `-`,
  `+0`… (ver §6).

### Normalizaciones previas al parseo (`CardImporter`, `src/CardImporter.kt:68-114`)

1. Strip de BOM UTF-8 (`raw.removePrefix("﻿")`).
2. Root: acepta array `[…]` o wrapper `{"cards": […]}`.
3. `printings` objeto suelto → array de 1.
4. `legalities` string → array de 1 (dentro de cada printing y top-level).
5. `keywords` no-array (string, string vacío) → array vacío.
6. Parser JSON laxo: `ignoreUnknownKeys + isLenient + coerceInputValues`
   (config de `L5ArgentinaApp.jsonParser`).
7. Carta sin `type` → **descartada** (`toEntity` devuelve null, `:117`). Cualquier otra excepción
   por carta también la descarta silenciosamente (`:180-182`).

## 2. Modelo de carta persistido (`CardEntity` → tabla `cards`)

DDL equivalente completo en `fixtures/generator/driver/Runner.kt` (constante `DDL`, columnas
idénticas a las de Room). Tipos: TEXT / INTEGER; booleans como 0/1.

## 3. Formato pipe para listas (`src/Converters.kt`)

`List<String>` ↔ `"|val1|val2|"` (vacía ↔ `""`). Usado en `sets`, `legality`, `keywords`,
`action_keywords`, `printing_images`. La razón: `LIKE '%|val|%'` da match exacto de elemento sin
tabla de relación (ADR-005). **El launcher debe conservar delimitadores equivalentes o cambiar la
semántica de los LIKE en conjunto.**

## 4. Selección de printing e imagen

- **Printing actual** (`src/CardImporter.kt:126`):
  `printings.firstOrNull { it.current } ?: printings.lastOrNull()` — el flag `current` manda;
  fallback: **el último del array** (la convención del pipeline, heredada del XML kamisasori, es
  "el printing más reciente va último").
- **imagePath** (`:127-131`): si hay regla `preferredPrinting` → imagen del printing con esa
  edición (o path sintetizado `images/cards/{pref}/{id}.jpg`); si no → imagen del printing actual;
  fallback final → campo `image` top-level.
- **printingImages** (`:132`): todas las `printings[].image` (no-null, dedup, en orden) —
  pipe-delimited. La UI la usa como cadena de fallback: intenta `imagePath` → cada path de
  `printing_images` → placeholder (primer archivo que exista en disco).
- **Resolución a archivo**: los paths son relativos a `filesDir` de la app:
  `File(filesDir, "images/cards/{edición}/{printingId}.jpg")`. Es el mismo layout que usa el
  launcher (`images/cards/<EDICION>/<ID>.jpg`) — **el id del archivo es el id del printing, no el
  id de la carta** (Border Keep, carta `Emperor028`, tiene la imagen Celestial en
  `images/cards/Celestial/Celestial023.jpg`).
- **Placeholder obligatorio** cuando no hay imagen o el archivo no existe (regla de UI global).
- "Ver todas las impresiones" deriva la edición mostrada **parseando el path**:
  `edición = segmento entre "images/cards/" y "/"`; `printingId = nombre de archivo sin .jpg`
  (`src/SearchViewModel.kt:546-551`).

## 5. Mapeo columna ← JSON ← XML kamisasori

Convención XML (del lado launcher): raíz `<cards>`, hijos `<card id="…" type="…">` con `<name>`,
`<rarity>`, pares `<edition>`+`<image>` repetidos (el más reciente último), `<legal>` (repetible),
`<clan>`, `<text>` CDATA con HTML, stats según tipo, `<flavor>`, `<artist>`.

| Columna Room | ← campo JSON | Transformación | ← XML kamisasori (equivalencia) |
|---|---|---|---|
| `id` | `id` | directo | atributo `id` de `<card>` |
| `name` | `name` | directo | `<name>` |
| `type` | `type` | directo (lowercase en datos) | atributo `type` |
| `clan` | `clan` | directo; ausente → NULL | `<clan>`; ausente → NULL |
| `deck_group` | — | derivado de `type` (§7) | derivar igual |
| `rarity` | `rarity` | `?: ""` — **sin normalizar case** | `<rarity>` |
| `sets` | `printings[].edition` | dedup en orden → pipe. Fallbacks legados: `editions`, `[edition]` | los `<edition>` de la carta, en orden |
| `legality` | `printings[].legalities` | aplanado+dedup → pipe. Fallbacks: `legalities`, `[legal]` | los `<legal>` de la carta ⚠️ el XML no tiene legalidad *por printing*; el JSON la derivó editorialmente — para el Buscador solo importa la unión |
| `keywords` | `keywords` (array) | pipe; ausente → `""` | ⚠️ **no existe en el XML**: derivado editorial del encabezado `<b>…</b>` del text (separado por `•`/`&#8226;`, cortado antes de la primera habilidad, con correcciones manuales). Si el launcher parsea el XML, tiene que replicar esa derivación o consumir el JSON para keywords |
| `text` | `text` | directo (HTML inline) | `<text>` CDATA — ⚠️ el JSON ya viene con entidades resueltas (bullets literales `•`); el XML puede traer `&#8226;` (ver §8) |
| `flavor` | `flavor` | directo | `<flavor>` |
| `force` / `force_special` | `force` | §6 | stat del XML |
| `chi` | `chi` | `toIntOrNull()` (sin flag) | stat |
| `cost` / `cost_special` | `cost` | §6 | stat |
| `honor` / `honor_req_negative` | `honor_req` | §6 (`-`) | stat |
| `personal_honor` / `personal_honor_negative` | `personal_honor` | §6 (`-`) | stat |
| `focus` | `focus` | `toIntOrNull()` | stat |
| `gold_production` / `_special` | `gold_production` | §6 | stat |
| `province_strength` | `province_strength` | `toIntOrNull()` | stat |
| `starting_honor` | `starting_honor` | `toIntOrNull()` (admite negativos) | stat |
| `artist` | `artist` | directo | `<artist>` |
| `image_path` | `printings[].image` | §4 | los `<image>` |
| `printing_images` | `printings[].image` | todos, dedup → pipe | todos los `<image>` |
| `max_copies`, `banned`, `note`, `label_text`, `label_color`, `highlight_color` | — | de rules.json (SPEC-LEGALIDAD.md) | — |
| `initial_holding_slot` | — | de filters.json: si `name` ∈ eligibleCards de algún slot → slotId; fuerza `deck_group="starter"` | — |
| `subtitle` | — | derivado de `name` (§9) | derivar igual |
| `action_keywords` | — | derivado de `text` + legalidades (§10) | derivar igual |

⚠️ Los nombres exactos de los elementos de stats del XML kamisasori no se verificaron desde este
repo (esta app nunca ve el XML); confirmarlos contra el `database.xml` del launcher. Los nombres
JSON de la tabla sí son exactos.

## 6. Parsing de stats (reglas exactas, `src/CardImporter.kt:185-201`)

| Regla | Función | Ejemplos reales (dataset 2.3.1) |
|---|---|---|
| Contiene `*` → valor NULL + flag `_special=1` | `parseStatInt`/`isSpecialStat` | force `+*` (2 cartas), cost `*` (9 strategies), gold `*`, `1*` |
| `-` (guión solo) → NULL + flag `_negative=1` (solo honor_req y personal_honor) | `parseHonorInt`/`isDashStat` | honor_req `-` (992), personal_honor `-` (236) |
| Resto → `toIntOrNull()` | — | `"4"` → 4; no parseable → NULL sin flag |

⚠️ **Quirk de los modificadores `+N`**: `"+2".toIntOrNull()` en Kotlin devuelve `2` — los stats
modificador (followers/items con force `+2`, chi `+0`, etc.) se guardan como el número **sin
distinción del valor absoluto**: un follower force `+2` y una personality force `2` son iguales
para el filtro de rango. En C#, `int.TryParse("+2")` también da 2 con estilos default — verificar.
Inventario de valores no-numéricos del dataset: force `*`,`+0..+6`,`+*`; chi `+0..+3`;
cost `*`; honor_req `-`; personal_honor `-`; gold `+0`,`+1`,`1*`,`*`; province `+0..+2`;
starting_honor `+0..+2`.

## 7. deckGroup (derivado de type, `src/CardImporter.kt:245-250`)

`starter`: stronghold, sensei, wind · `dynasty`: celestial, event, holding, region, personality ·
`fate`: ancestor, follower, ring, item, spell, strategy · desconocido → `""`.
Excepción: carta elegible de holding inicial → forzada a `starter` (`:146`).
(El Buscador no usa deckGroup para filtrar; se documenta por completitud del modelo.)

## 8. HTML dentro de `text` — inventario exhaustivo y renderizado

Inventario sobre **todo** el dataset 2.3.1 (`text` + `flavor`, 6394 cartas):

| Tag | Ocurrencias | Uso |
|---|---|---|
| `<b>` | 21.936 | keywords de encabezado, designadores de habilidad (`<b>Battle:</b>`), nombres propios |
| `<br>` | 7.510 | saltos de línea (sin cierre, estilo HTML suelto) |
| `<i>` | 2.356 | cursivas (flavor, aclaraciones) |

**No hay ningún otro tag.** Entidades HTML: **ninguna** en 2.3.1 — los bullets son caracteres
literales `•` (U+2022, 9.624 veces) y `●` (U+25CF, 3 veces). El formato histórico usaba
`&#8226;`; el importer todavía divide por ambos (`split("&#8226;", "•")`,
`src/CardImporter.kt:214,242`) — si el XML del launcher trae la entidad, decodificarla antes o
dividir por ambas, igual que acá.

**Renderizado en esta app:** `HtmlCompat.fromHtml(text, FROM_HTML_MODE_COMPACT)` de Android
(CardListItem para texto rico; CardViewerModal lo convierte a plano con `.toString()` para el
placeholder sin imagen). O sea: `<b>`→negrita, `<i>`→cursiva, `<br>`→salto; cualquier otro tag lo
resolvería el parser de Android, pero no aparece ninguno. En WPF: 3 tags + bullets literales es
todo lo que hay que soportar.

⚠️ Para el **filtro de texto**, el matching es contra el HTML **crudo** con `<`/`>`/`/` convertidos
a espacio (no contra el texto renderizado) — ver SPEC-BUSQUEDA-TEXTO.md §4b.

## 9. subtitle (derivado, `src/CardImporter.kt:204-207`)

Todo lo que sigue a la **primera coma** del `name`, con trim; sin coma → NULL. 85 cartas.
Ejemplos reales: `"Rise, Brother"` → `"Brother"`; `"Loyalty, Unto Death"` → `"Unto Death"`.
⚠️ Quirk: `"1,000 Cuts Technique"` → subtitle `"000 Cuts Technique"` (la coma del número dispara
la regla). Se usa solo para el toggle "Subtítulos" del filtro de keywords.

## 10. action_keywords (derivado, `src/CardImporter.kt:209-228`)

Solo si la carta tiene alguna legalidad en `{ivory, TwentyFestivals, samurai}` (`ACTION_KW_LEGAL`).
Algoritmo sobre el `text`:

1. Buscar cada bloque `<b>X:</b>` o `<b>X</b>:` (regex `BOLD_COLON_RE`).
2. Dividir X por bullets (`&#8226;` y `•`).
3. Cada parte: split por espacios; **la última palabra debe ser un designador**
   (`Open, Limited, Engage, Battle, Dynasty, Reaction, Interrupt`) — si no, se descarta la parte.
4. Las palabras restantes (antes del designador) son las action keywords; los compuestos
   `Bushido Virtue` y `Dark Virtue` se toman como unidad; el resto palabra por palabra.
5. Dedup preservando orden → pipe-delimited.

Ejemplos reales: `"<b>Recon Battle:</b>…"` → `|Recon|`; `"<b>Fear Battle:</b>"` → `|Fear|`.
728 cartas con action_keywords. Nota: el designador en sí **no** queda en la columna (el filtro
de Designador va contra `text` directamente, ver SPEC-FILTROS.md §3).

## 11. Tablas de catálogo auxiliares (de filters.json)

- `sets(code, name)` — 48 filas, **el orden de inserción define el orden canónico** del selector
  de Edición y del orden por Set (con la corrección `presortSets`, SPEC-FILTROS.md §15).
- `formats(code, name)` — 5 filas (ver SPEC-LEGALIDAD.md §4).
- `keywords(code, name)` — 838 filas; alimenta sugerencias/autocomplete de keywords.
- `format_initial_holding_slots` / `_eligible_cards` — slots requeridos por formato y cartas
  elegibles (hoy solo formato samurai: slots `bamboo` y `border`).
Todas destructivas en cada sync (`deleteAll + insertAll`).
