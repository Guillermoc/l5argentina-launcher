# SPEC — Legalidad, banlist y rules.json

Fuentes: `src/RulesImporter.kt`, `src/RulesJson.kt`, `src/CardDao.kt` (SQL de reset/aplicación),
`src/CardImporter.kt`, `src/CardQueryCompiler.kt`. Datos reales: `fixtures/data/rules-2.3.1.json`.
Commit `a9a48f1`.

## 1. Qué es rules.json

Overrides editoriales **por título de carta** (no por id). Estructura
(`src/RulesJson.kt`; ejemplo real abajo):

```json
{ "rules": [
  { "title": "A Kensai's Art", "banned": true,
    "label": { "text": "BANNED", "color": "#FF0000" },
    "highlight": { "color": "#FF0000" } },
  { "title": "A Common Goal", "maxCopies": 1,
    "label": { "text": "UNIQUE", "color": "#000000" } },
  { "title": "Border Keep - exp", "preferredPrinting": "FL",
    "label": { "text": "Border Keep", "color": "#8B4513" } },
  { "title": "Bandit's Sanctuary", "maxCopies": 5, "note": "5 copias max" }
] }
```

Campos por regla: `title` (obligatorio, match exacto con `cards.name`), `maxCopies`
(999=ilimitado, 0=baneada), `banned`, `preferredPrinting` (código de edición para la imagen),
`note` (texto informativo en el visor), `label` (pastilla sobre la imagen), `highlight`
(marco de color). En rules 2.3.1: 174 reglas — 60 baneadas, 103 con maxCopies, 1 preferredPrinting,
10 notes.

## 2. Cómo se aplica (dos momentos)

### 2a. Al importar la DB (`CardImporter.toEntity`, `src/CardImporter.kt:116-183`)

Con el mapa `title → CardRule` parseado (`RulesImporter.parse`):

- `banned = (rule.banned == true) || rule.maxCopies == 0` (`:134`)
- `maxCopies`: si banned → 0; si la regla trae `maxCopies` → ese valor; si no → base por carta
  (stronghold/sensei → 1; "Unique" → 1; default 3) (`:135-139` y `calculateMaxCopies`, `:230-243`)
- `imagePath`: si la regla trae `preferredPrinting` → imagen del printing con esa edición
  (o path construido `images/cards/{pref}/{id}.jpg` si no existe el printing) (`:127-131`)
- `note`, `labelText`, `labelColor`, `highlightColor` directos de la regla (`:172-175`)

### 2b. Post-import / re-sync sin re-importar (`RulesImporter.applyRules`, `src/RulesImporter.kt:22-47`)

1. `CardDao.resetAllRules()` (SQL en `src/CardDao.kt:42-55`): resetea **todas** las cartas a
   base — `banned=0`, `max_copies` recalculado con `CASE WHEN type IN ('stronghold','sensei')
   THEN 1 WHEN keywords LIKE '%|Unique|%' THEN 1 ELSE 3 END`, `note/label/highlight = NULL`.
2. Por cada regla, UPDATE por `name`:
   - con `maxCopies` explícito → `applyCardRule` (setea banned+maxCopies+extras)
   - sin `maxCopies` → `applyCardRuleKeepMaxCopies` (setea banned+extras, **conserva** el
     max_copies base)
3. Títulos que no matchean ninguna carta → se loggean en `sync_log.txt` (con rules 2.3.1 + cards
   2.3.1: **0 sin match**, verificado al generar los fixtures).

**Orden real del sync** (CatalogSyncService): `filters → database → rules`. Tras un sync completo,
el estado final de banned/maxCopies/label es siempre el de `applyRules` (2b).

### ⚠️ Divergencias conocidas entre 2a y 2b (comportamiento accidental)

- **Unique por texto:** al importar, `isUnique` también detecta "Unique" en el primer bloque
  `<b>…</b>` del `text` cuando la carta no tiene array `keywords` (`src/CardImporter.kt:237-243`).
  El `resetAllRules` de 2b solo mira la **columna** `keywords`. Una carta Unique-solo-en-texto
  queda con max_copies=1 tras import y **max_copies=3 tras applyRules**. Como el sync siempre
  corre applyRules después del import, gana el 3. (No afecta al Buscador — maxCopies no filtra —
  pero sí al límite de copias del mazo.)
- **`banned` sin `maxCopies` deja max_copies=3:** las 60 reglas banned de 2.3.1 no traen
  maxCopies → tras applyRules quedan `banned=1, max_copies=3` (verificado:
  `A Kensai's Art → banned=1, max_copies=3`). El chequeo de mazo mira `banned` primero, así que
  no importa, pero si el launcher deriva "baneada" de `maxCopies==0` va a divergir. **Baneada =
  flag `banned`, no maxCopies.**
- **preferredPrinting solo se aplica al re-importar la DB** (está baked en el import); en una
  instalación fresca el primer sync importa la DB *antes* de tener rules.json cacheado, así que
  `Border Keep - exp` muestra la imagen del printing `current` hasta el próximo re-import de DB.
  Los fixtures se generaron en el estado estable (rules cacheadas presentes).

## 3. Qué hace el Buscador con las baneadas

- **Default: las oculta** — `AND banned = 0` (`src/CardQueryCompiler.kt:194`).
- Con el chip "Incluir baneadas" (`includeBanned=true`): aparecen, y el visor/lista las marca con
  la pastilla `labelText` en `labelColor` (BANNED rojo) + marco `highlightColor` + `note` si hay.
  La marca visual es **data-driven** (viene de rules.json), no hardcodeada.
- Las **sugerencias de nombre ignoran `banned`** (pueden sugerir una baneada; al elegirla, el
  resultado queda vacío salvo includeBanned). Ver SPEC-BUSQUEDA-TEXTO.md §3.
- Casos golden: `baneada-excluida-default` (name='kensai' → 1 resultado: "Kensai's Blade"),
  `baneada-incluida` (4: + A Kensai's Art/Victory/Will).

## 4. Legalidad (columna `legality`) y formatos

- La columna `legality` es la **unión histórica** de legalidades de todos los printings:
  `card.legalities` si viene; si no, `printings[].legalities` aplanado+dedup; si no, `[card.legal]`
  (`src/CardImporter.kt:122-124`). Pipe-delimited: `"|celestial|emperor|"`.
- Valores reales en cards 2.3.1: `samurai`, `celestial`, `emperor`, `ivory`, `TwentyFestivals`
  (inventario completo del dataset; ninguno más).
- La tabla `formats` (de `filters.json`) define los formatos "oficiales" con código+nombre:
  `samurai, celestial, emperor, ivory, 20F`. Se importa pero hoy solo se usa para la
  normalización vestigial de ADR-011 (ver SPEC-FILTROS.md §9).
- ⚠️ **Mismatch `20F` vs `TwentyFestivals`**: el código de formato es `20F`, la legalidad en las
  cartas es `TwentyFestivals`. `legality LIKE '%|20F|%'` = 0 resultados (golden
  `formato-20F-mismatch`). Cualquier UI de formato en el launcher necesita una tabla de mapeo
  código→valor-de-legalidad o usar los valores de legalidad directamente.
- ⚠️ **El filtro de formato no tiene UI hoy** en esta app (el modal no lo muestra). El motor lo
  soporta (golden `formato-samurai`, `formato-TwentyFestivals`).

## 5. "Samurai Extended" / SExMRP — lección aprendida

- **`SExMRP` es una EDICIÓN (set), no una legalidad.** Aparece en `printings[].edition` y por lo
  tanto en la columna `sets` y como chip del filtro de Edición (arco "Samurai Extended" del
  selector); su nombre en filters.json es "Samurai Extended MRP".
- No existe ninguna legalidad `s_extended` en los datos de esta app: los printings SExMRP llevan
  legalidad `samurai`. Si el `database.xml` del launcher trae `<legal>s_extended</legal>` u otro
  valor propio, es **un vocabulario distinto** al de este pipeline — no asumir equivalencia:
  en esta app "jugable en Samurai Extended" se modela como *set* SExMRP + legalidad samurai.
- Regla práctica para el port: legalidad = para qué formatos fue legal la carta alguna vez
  (unión histórica, no "formato vigente"); edición = en qué producto salió físicamente. El
  buscador filtra por cualquiera de las dos de forma independiente.

## 6. Banlist y mazos (fuera de alcance, para contexto)

El launcher no porta Mazos, pero para entender los datos: `DeckRepository.addCard()` rechaza
cartas con `banned=true` (`AddCardResult.Banned`) y aplica `maxCopies` como tope de copias.
El Buscador es solo visualización: banned filtra, maxCopies no.

## 7. Resumen de decisiones a tomar en el launcher

| Tema | Comportamiento de esta app | ¿Portar? |
|---|---|---|
| Baneadas ocultas por default | sí, `banned=0` | recomendado sí |
| Baneada = flag, no maxCopies | sí | sí, obligatorio para paridad |
| Match de reglas por título exacto | sí (case-sensitive en el `WHERE name = :name`… ojo: SQLite `=` es case-sensitive; los títulos del rules.json están escritos idénticos a los nombres) | sí |
| Reset completo antes de re-aplicar | sí (`resetAllRules`) | sí — hace las reglas idempotentes |
| Unique-por-texto vs columna | divergencia conocida (gana la columna) | decidir; afecta solo mazos |
| Formato en UI | no expuesto | libre |
