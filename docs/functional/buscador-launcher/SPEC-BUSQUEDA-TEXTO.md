# SPEC — Búsqueda de texto libre

Cubre los dos caminos de texto libre: la **barra de búsqueda** (campo `name` + operadores) y el
**filtro "Texto"** del modal (campo `text`). Fuentes: `src/SearchViewModel.kt`,
`src/CardQueryCompiler.kt`, `src/CardRepository.kt`. Commit `a9a48f1`.

## 1. La barra de búsqueda y sus modos

La barra tiene 5 modos según el **prefijo** de lo tipeado (`parseBarText`,
`src/SearchViewModel.kt:571-577` — el orden del `when` importa: `tx.` se evalúa antes que `t.`):

| Prefijo | Modo | Efecto sobre el criteria | Sugerencias |
|---|---|---|---|
| (ninguno) | NAME | `criteria.name = texto` **en vivo** con cada tecla | nombres de carta (ver §3) |
| `k.` | KEYWORD | nada hasta elegir sugerencia → agrega a `criteria.keywords` | códigos de la tabla `keywords` que **contengan** el fragmento |
| `c.` | CLAN | ídem → `criteria.clans` | claves del enum `Clan` |
| `t.` | TYPE | ídem → `criteria.types` | claves del enum `CardType` **sin `wind`** |
| `tx.` | TEXT | `criteria.text = resto` **en vivo** | ninguna |

- Al entrar en un modo operador (`k.`/`c.`/`t.`), `criteria.name` se limpia
  (`onSearchBarChange`, `:294-303`).
- Elegir una sugerencia de operador agrega el valor (dedup con `distinct()`) y **vacía la barra**
  (`commitOperatorSuggestion`, `:309-336`).
- Elegir una sugerencia de nombre pone el nombre completo en barra y criteria
  (`selectSuggestion`, `:305-307`).

## 2. Matching del campo `name`

`src/CardQueryCompiler.kt:14-17`:

```sql
-- name = "hida kisada"  →
AND name LIKE '%hida%' AND name LIKE '%kisada%'
```

- **Tokenización:** split por whitespace (`Regex("\\s+")`), tokens vacíos descartados.
- **Matching: substring por token**, AND entre tokens. Sin word-boundary, sin prefijo, sin fuzzy.
  - `isad` → matchea "Kisada" (caso `nombre-substring-interno` del fixture).
  - `hida kisada` → matchea "Hida Kisada" y "Kisada's Block… " no (necesita ambos).
- **Mayúsculas:** `LIKE` de SQLite es case-insensitive **solo ASCII**. `KISADA` ≡ `kisada`
  (caso `nombre-mayusculas`).
- **Acentos/diacríticos: NO se normalizan.** No hay `unaccent` ni collation especial. En los datos
  2.3.1 no hay acentos en `name` (verificado sobre el dataset), así que hoy es indistinguible;
  si el launcher usa datos con acentos (p. ej. `Á` vs `á`), LIKE de SQLite NO los iguala.
- **Puntuación:** literal. `kisada's` solo matchea si el nombre contiene exactamente `'s`.
  El apóstrofo va como argumento bindeado (sin riesgo de inyección).
- **Sin mínimo de caracteres** para filtrar (1 letra ya filtra); el mínimo de 2 chars aplica solo
  a las *sugerencias*.

## 3. Sugerencias de nombre (autocomplete)

`src/CardRepository.kt:36-42` + `src/SearchViewModel.kt:247-255`:

```sql
SELECT * FROM cards WHERE name LIKE '%<texto crudo>%' ORDER BY name ASC LIMIT 8
```

- **Debounce 200 ms**, mínimo **2 caracteres**, máximo **8 resultados**, orden alfabético.
- ⚠️ El texto va **crudo** (sin split por palabras): `hida kisada` como sugerencia busca la
  substring exacta `"hida kisada"` — distinto del filtro principal que hace AND por palabra.
  Con espacios en el medio del nombre real funciona; con palabras desordenadas no sugiere nada
  aunque el filtro principal sí encontraría resultados.
- ⚠️ Las sugerencias **ignoran los filtros activos y el estado de la carta**: la query no filtra
  `banned` ni ocultas — una carta baneada u oculta puede aparecer como sugerencia. La UI marca
  con "Bloqueado por filtros" las sugerencias cuyo id no está en el resultado actual
  (`isBlocked = card.id !in resultIds && !criteria.isEmpty`, SearchScreen).
- Sugerencias de operador: **debounce 150 ms**, sin mínimo de caracteres, máximo 8, matching
  `contains` case-insensitive sobre el código (`src/SearchViewModel.kt:257-276`).

## 4. Filtro "Texto" (`criteria.text`) — texto de reglas de la carta

`src/CardQueryCompiler.kt:72-88`. Dos modos según comillas:

### 4a. Frase exacta (entre comillas dobles)

Si el texto (trim) tiene length > 2 y empieza **y** termina con `"`:

```sql
AND text LIKE '%Ranged 3 Attack%'   -- para  "Ranged 3 Attack"
```

Substring **sin normalizar** sobre el HTML crudo — las comillas se quitan, el interior se busca
literal (espacios incluidos). Ojo: si la frase real cruza un tag (`Ranged <b>3</b> Attack`) no
matchea; se busca contra el texto con tags.

### 4b. Palabras sueltas (default)

Split por whitespace; cada palabra genera:

```sql
AND (' ' || replace(replace(...text...)...) || ' ') LIKE '% palabra %'
```

La cadena de `replace` anidados (`:82-83`) reemplaza por **espacio** estos 15 caracteres, en este
orden: `'` `.` `,` `:` `;` `!` `?` `(` `)` `<` `>` `[` `]` `/` `"` — y concatena un espacio al
inicio y al final. Después busca `'% palabra %'` (espacios incluidos) → **palabra completa**.

Consecuencias verificadas:

- `honor` **no** matchea "honorable"/"dishonor" (word-boundary; caso `texto-word-boundary`).
- Los tags HTML se desarman: `<b>Battle:</b>` → ` b Battle  /b ` — por eso `battle` matchea texto
  dentro de bold, y también por eso las letras sueltas `b` o `i` matchean casi todo (restos de
  tags). ⚠️ No portar "ingenuo": si el launcher normaliza HTML de otra forma (p. ej. strippeando
  tags), los resultados de palabras como `b`, `br`, `i` van a divergir de los golden.
- El guión `-` **no** se reemplaza: `two-handed` queda como una sola palabra.
- Los bullets `•` (U+2022, literal en los datos 2.3.1) **no** se reemplazan, pero siempre están
  rodeados de espacios en los datos, así que no rompen el word-boundary.
- Case-insensitive ASCII, igual que name. Sin normalización de acentos.
- AND entre palabras; el orden no importa; sin fuzzy ni stemming.

Cartas sin `text` (NULL): cualquier cláusula de texto/designador da NULL → **quedan excluidas**.

## 5. Orden de resultados y desempate

- El resultado del buscador usa el `ORDER BY` del criteria (default `name ASC`); no hay score de
  relevancia. Detalle completo en SPEC-FILTROS.md §15.
- Desempate entre nombres idénticos: ninguno explícito → orden de rowid (= orden del cards.json).
  Los golden con `orderMatters=true` documentan esto en `notes`.

## 6. Comportamiento dinámico

- **La query principal no tiene debounce**: cada cambio de criteria re-ejecuta la búsqueda
  (Room re-emite; `flatMapLatest` cancela la anterior — `src/SearchViewModel.kt:93-102`).
  El launcher puede necesitar debounce si su motor es más lento; documentarlo como divergencia.
- Cache por tab: al cambiar de tab se emite inmediatamente el último resultado conocido del tab
  destino mientras se resuelve la query real (`tabResultsCache`, `:88-101`).
- Al aplicar filtros desde el modal, el resultado se precarga **antes** de cerrar el modal para
  que no haya flash de contenido viejo (`applyFilters`, `:408-414`).
- El contador "N resultados" refleja `results` (post filtro de lista en memoria), no
  `displayCards` (que puede tener más entradas con `expandPrintings`).
