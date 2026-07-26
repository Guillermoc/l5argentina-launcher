# SPEC — Stats iniciales y derivados del mazo

Fuentes: `src/GoldfishViewModel.kt`, `src/GoldfishModels.kt`, `src/DeckDetailViewModel.kt`
(StatsModal vive en `DeckDetailScreen.kt`, transcripto abajo). Commit `a9a48f1`.

## ⚠️ Titular: casi nada de esto se calcula

El pedido menciona "honor inicial, oro de inicio y fuerza total de provincias". La realidad del
código:

- **Honor inicial**: se calcula **solo en el sandbox Goldfish**, y es simplemente
  `startingHonor` del stronghold — no una suma. No se muestra en la pantalla de mazo.
- **Oro de inicio**: **no se calcula en ningún lado.** La columna `gold_production` existe en
  `CardEntity` y se importa, pero ninguna pantalla la suma ni la muestra.
- **Fuerza total de provincias**: **no se calcula en ningún lado.** `province_strength` existe
  como columna pero nunca se agrega.

Verificado por búsqueda global: `startingHonor`/`provinceStrength`/`goldProduction` solo aparecen
en `CardImporter` (parseo), `CardEntity`/`CardJson` (modelo) y `GoldfishViewModel` (setup). **Cero
uso en `DeckDetailViewModel`, `DecksViewModel`, `MainViewModel` o el StatsModal.**

Si el launcher quiere mostrar oro de inicio / fuerza de provincias / honor como resumen del mazo,
es **funcionalidad nueva**, no un port. Las fórmulas correctas del juego las tendrías que definir
vos; abajo doy lo único que la app hace hoy.

## 1. Honor inicial (única fórmula existente — Goldfish)

`GoldfishViewModel.buildInitialState()` (`src/GoldfishViewModel.kt:80`):

```kotlin
honorCounter = starter.firstOrNull { it.card.type == "stronghold" }?.card?.startingHonor ?: 0
```

- Es el `startingHonor` del **primer** stronghold del área starter (tras ordenar por
  `starterRank` → slot → nombre, `:61-67,85-90`).
- Sin stronghold → 0. Stronghold con `startingHonor` nulo → 0.
- ⚠️ `startingHonor` **puede ser 0 legítimamente** (p. ej. "Barracks of the Damned"): 0-por-dato
  y 0-por-ausencia son indistinguibles en este cálculo (`?: 0`). Golden `stat-inicial-honor-cero`.
- ⚠️ Con **2+ strongholds** (permitido, ver SPEC-VALIDACION.md §3), gana el primero por orden
  alfabético de nombre → qué honor se usa es no determinista de cara al usuario. Golden
  `stat-inicial-honor-multi-stronghold`: con "The Seventh Tower" (3) + "The Hall of Ancestors"
  (7), gana "The Hall of Ancestors" (7) por orden de nombre.
- Golden `stat-inicial-honor-stronghold`: The Seventh Tower → honor 3 (y muestra que
  provinceStrength=8, goldProduction=4 existen pero **no** se agregan a nada).

La fuente de `startingHonor`/`provinceStrength`/`goldProduction` en `CardEntity`: parseo del JSON
en el import (strings numéricos → Int?; `startingHonor` admite negativos). El launcher ya tiene
estas columnas en su `CardEntity`.

## 2. Provincias (Goldfish, no es un stat del mazo)

`GoldfishModels.kt`: `PROVINCE_COUNT = 4`, `INITIAL_HAND_SIZE = 5`. En el setup se reparten 4
cartas Dynasty boca abajo como provincias y 5 Fate como mano inicial. Esto es **estado de juego en
runtime** del sandbox, no una propiedad del mazo. No hay "fuerza total de provincias" calculada.

## 3. Stats derivados que SÍ se muestran (StatsModal, pantalla de mazo)

El `BarChart` en el header de `DeckDetailScreen` abre `StatsModal`
(`DeckDetailScreen.kt:1354-1557`). Solo cuenta cartas con `quantity > 0`. Es lo único de "resumen"
que existe. Cálculos exactos:

### a) Curva de coste (`costDistribution`, `:1356-1360`)

```kotlin
cards.groupBy { it.card.cost }                       // agrupa por costo (Int? — null = coste variable "*")
     .mapValues { (_, items) -> items.sumOf { it.deckCard.quantity } }
     .entries.sortedWith(compareBy { it.key ?: Int.MAX_VALUE })  // nulls al final
```

Se dibuja como barras (una por coste) para: **personalities**, **holdings** (dynasty),
**followers**, **items**, **spells** (fate). El coste `null` se rotula "★" (coste variable).

### b) Mediana de coste (`medianCost`, `:1362-1370`) — solo personalities

Expande por cantidad, descarta costes null, ordena, toma la mediana (promedio de los 2 centrales
si el n es par, con **división entera**). Solo se muestra para personalities.

### c) Top 5 keywords (`StatsTopKeywords`, `:1538-1557`)

Cuenta keywords (pipe-delimited de `card.keywords`) ponderadas por cantidad, **excluye
`Unique` y `Experienced`**, ordena desc, toma 5. Se muestra para personalities, followers,
items, spells.

### d) Foco promedio Fate (`:1460-1469`)

`Σ(focus × qty) / Σ(qty)` sobre cartas Fate con `focus != null`. Un decimal.

### e) Conteos por tipo

Suma de `quantity` por tipo, agrupados en Starter / Dynasty / Fate. (Los mismos conteos que los
indicadores de validez, ver SPEC-VALIDACION.md §5a.)

**El StatsModal NO calcula:** honor, oro, fuerza de provincia, fuerza total de personalities,
curva de honor, ni distribución por clan. Si el launcher los quiere, son nuevos.

## 4. Tabla resumen para el launcher

| Stat | ¿Se calcula hoy? | Dónde | Fórmula |
|---|---|---|---|
| Honor inicial | Sí, solo en Goldfish | `GoldfishViewModel:80` | `stronghold.startingHonor ?: 0` (primer stronghold) |
| Oro de inicio | ❌ No | — | `gold_production` existe pero nunca se suma |
| Fuerza de provincias | ❌ No | — | `province_strength` existe pero nunca se suma |
| Curva de coste | Sí | StatsModal | groupBy cost, suma qty; nulls al final |
| Mediana de coste | Sí (personalities) | StatsModal | mediana con división entera |
| Top keywords | Sí | StatsModal | count×qty, excl. Unique/Experienced, top 5 |
| Foco promedio | Sí (fate) | StatsModal | Σ(focus·qty)/Σqty |
| Conteo Dynasty/Fate | Sí | DeckDetail | suma qty por deck_group |
| Distribución por clan | ❌ No | — | — |
