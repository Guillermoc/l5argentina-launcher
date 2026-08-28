# Diseño transversal — paridad con la app Android

Aplica a **dos dominios** (Buscador y Mazos) y a cualquier port futuro desde la app L5Argentina.
Es la regla que explica por qué hay código que se ve "mal" a propósito.

Specs de origen (paquetes de handoff): [../functional/buscador-launcher/](../functional/buscador-launcher/)
y [../functional/deckbuilder-launcher/](../functional/deckbuilder-launcher/).

---

## La regla

**El motor se porta 1:1; la experiencia se adapta al escritorio.**

- **Semántica** (qué cartas devuelve una consulta, qué acepta o rechaza `AddCard`, qué string produce
  un export): idéntica a la app, verificada por **golden tests**. Incluye los bugs.
- **Interacción** (dónde va un panel, cuándo se muestra un mensaje, qué atajos hay): libre, pensada
  para monitor y teclado.

Un cambio de interacción que altere qué cartas se devuelven **no es** un cambio de interacción.

---

## Golden tests: qué son y qué los rompe

| | Buscador | Deckbuilder |
|---|---|---|
| Casos | 53 | 25 |
| Generados con | `CardQueryCompiler` real de la app sobre SQLite | `DeckRepository` real de la app sobre SQLite |
| Fixtures | [`tests/fixtures/buscador/`](../../tests/fixtures/buscador/) | [`tests/fixtures/deckbuilder/`](../../tests/fixtures/deckbuilder/) |
| Forma del caso | criteria serializado + ids esperados (`orderMatters` decide secuencia vs conjunto) | secuencia exacta del driver (mismos mazos "Golden N", string exacto del export) |
| Corre en | `SelfTest.RunGoldenSearchTests` | `SelfTest.RunGoldenDeckTests` |

**Si tocás `CardSearchEngine`, `CatalogImporter`, `CardSearchCriteria` o `DeckRepository`: corré
`--selftest` al terminar ese cambio y dejalo en verde** (una corrida al final, no una por edición —
[ADR-032](../DECISIONS.md#adr-032)). Si el cambio *pretende* modificar una semántica, no se ajusta el
código hasta que pase: se **regeneran los expected afectados** con el harness del handoff
([`tests/fixtures/<dominio>/generator/`](../../tests/fixtures/)), se anota qué cambió y por qué, y
recién ahí vale el verde nuevo.

Los golden se saltean (SKIP) si el exe corre fuera del repo: un reporte válido no trae FAIL ni
ningún SKIP.

---

## Semánticas de SQLite replicadas a mano

El motor del Buscador corre en memoria ([ADR-017](../DECISIONS.md#adr-017)), así que las reglas del
SQL original están reimplementadas explícitamente. No son detalles de estilo:

- `LIKE` → contains **case-insensitive solo-ASCII** (una `Á` no matchea una `á`).
- `IN` / `=` → comparación **Ordinal** (BINARY), case-sensitive.
- Stats `NULL` → siempre al final del orden.
- Empates → **orden de inserción** (los sorts tienen que ser estables).
- Orden por Set → `presortSets` + rank por printing, no alfabético.

---

## Bugs portados a propósito

Los ⚠️ de las specs del handoff se portan tal cual; la corrección de experiencia va **en la capa
UI** ([ADR-018](../DECISIONS.md#adr-018)):

| Bug | Motor | UI |
|---|---|---|
| Rareza case-sensitive | se porta | el chip manda `P` y `p` |
| Subtitle-substring en keywords | se porta | toggle "Incluir subtítulos" |
| Mismatch `20F` / `TwentyFestivals` en formatos | se porta | sin UI de formato (la app tampoco) |
| Dos strongholds permitidos en un mazo | se porta | indicador blando, no bloqueo |

Los nombres del port también siguen al original aunque mientan: `NoActiveDeck` significa "carta o
mazo inexistente" ([ADR-027](../DECISIONS.md#adr-027)).

---

## Divergencias deliberadas (todas anotadas)

| Divergencia | Dónde | Por qué |
|---|---|---|
| Se **muestra** el `AddCardResult`; la app lo ignora | Buscador y editor | Un `+` que no hace nada sin explicar es un bug de UX ([ADR-026](../DECISIONS.md#adr-026)) |
| "Solo ocultas" no resetea los demás filtros | Buscador | La app sí lo hace; acá sorprende |
| `SortField` con TYPE/CLAN/RARITY/HONOR_REQ/FOCUS | Buscador | Orden por cabecera de tabla; sin goldens ([ADR-030](../DECISIONS.md#adr-030)) |
| Duplicar mazo | Editor | Pedido frecuente, sale gratis ([ADR-029](../DECISIONS.md#adr-029)) |
| Sin gracia de 2,5 s al llegar a 0 | Editor | Patrón táctil para no re-tocar un botón chico; el recorte sí está ([ADR-046](../DECISIONS.md#adr-046)) |
| "Recortadas" arranca visible; en la app se ocultan | Editor | Sin la gracia de 2,5 s, esconder la carta en el instante en que la recortás deja al usuario sin ver lo que hizo ([ADR-046](../DECISIONS.md#adr-046)) |
| Presets de filtros con nombre | Buscador | Superset del slot único de la app |
| Resolución tolerante de nombres al leer `.txt` | Mazos | Los nombres del juego difieren del catálogo ([ADR-023](../DECISIONS.md#adr-023)) |
| Sin marco del color del clan alrededor del arte | Detalle de carta | El marco del `CardViewerModal` de la app hacía resaltar las puntitas blancas del escaneo; acá el arte va redondeado y el marco quedó solo como placeholder ([ADR-037](../DECISIONS.md#adr-037)) |
| **Sin gracia de 2 s al ocultar una carta** | Buscador | En la app, ocultar marca el ícono y recién a los 2 s saca la carta; tocar de nuevo cancela. Es el mismo patrón táctil que el recorte: existe para no re-tocar un botón chico en un celular. Con mouse, el `⊘` es preciso y el deshacer es volver a clickear — la demora solo agregaría un estado intermedio que explicar. Misma lógica que [ADR-025](../DECISIONS.md#adr-025) |
| **Sin sugerencias de nombre en la barra** | Buscador | La app las necesita porque el resultado vive detrás de un modal de filtros; acá la búsqueda **filtra en vivo** y la grilla ya *es* la lista de sugerencias, con la ventaja de respetar los filtros activos (las de la app los ignoran y por eso tienen que marcar "Bloqueado por filtros") |
| **Click derecho = copiar la imagen** | Grillas del Buscador y del editor | Extra del launcher: en la app el copiar vive en la barra del visor. Con mouse, el click derecho sobre la carta es el gesto esperado y evita abrir el detalle para una acción de un paso |
| **Estado de armado del mazo (terminado / pendiente)** | Editor | Extra del launcher; la app no tiene la noción ([ADR-047](../DECISIONS.md#adr-047)) |
| **El PDF se guarda con un diálogo de archivo; la app lo comparte por `Intent`** | Export PDF | Android no tiene "guardar en tal carpeta": la pantalla genera el PDF en `filesDir` y abre el share sheet. En escritorio el gesto equivalente es elegir dónde va el archivo, y después se ofrece abrirlo ([ADR-049](../DECISIONS.md#adr-049)) |
| **Botón "Volver al mazo" en el export PDF** | Export PDF | Devuelve todos los contadores de copias a la cantidad del mazo. La app no lo tiene: allá los contadores se pierden al salir de la pantalla, así que no hay nada que revertir |
| **El export PDF no se ofrece sobre una lista** | Export PDF | En la app toda entidad de mazo tiene el menú; acá una lista es una selección para filtrar, no algo que se imprima ([ADR-045](../DECISIONS.md#adr-045)) |
| **Tamaño de hoja A3, apaisado, el doble de cartas por hoja** | Export PDF | Extra del launcher: la app solo tiene A4 y Carta. A3 sale 6×3 = 18 en vez de 3×3 = 9 ([ADR-061](../DECISIONS.md#adr-061)) |

**Al agregar una divergencia nueva:** anotarla acá y en el índice del dominio, y verificar que no
toque ninguna semántica cubierta por goldens.
