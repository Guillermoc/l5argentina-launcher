# Índice — Buscador de cartas

Estado actual del Buscador. Dominio: `src/Services/Search/`, `src/Models/CardCatalog.cs`,
`src/Views/SearchWindow.xaml(.cs)`, `CardDetailPanel`, `CardTextRenderer`.

El Buscador replica la **semántica de filtros de la app Android L5Argentina**. La spec funcional con
la que se construyó es el paquete de handoff
[../functional/buscador-launcher/](../functional/buscador-launcher/)
(`SPEC-BUSQUEDA-TEXTO.md`, `SPEC-FILTROS.md`, `SPEC-LEGALIDAD.md`, `MODELO-DATOS.md` y las fuentes
Kotlin) — que además sigue siendo la referencia viva de paridad: sus golden tests, en
[../../tests/fixtures/buscador/](../../tests/fixtures/buscador/), corren en cada verificación.

---

## Piezas

| Pieza | Archivo | Qué hace |
|---|---|---|
| Modelo | `Models/CardCatalog.cs` | `Card`, espejo de la tabla `cards` de la app (incluidas las columnas pipe-delimited) + catálogo + `InitialHoldingSlots`. |
| Importer | `Services/Search/CatalogImporter.cs` | Port 1:1 de `CardImporter` + `RulesImporter.applyRules` + ingestión de `filters.json`. Mismo estado final que un sync completo de la app. |
| Motor | `Services/Search/CardSearchEngine.cs` | Port del SQL de `CardQueryCompiler` a predicados en memoria. LIKE = contains case-insensitive **solo-ASCII**; `IN`/`=` = Ordinal (BINARY); NULLs de stats al final; empates = orden de inserción (sort estable). Orden por Set con `presortSets` + rank por printing. `ExpandForDisplay` = port de `displayCards`/`expandCard`. |
| Criterios | `Services/Search/CardSearchCriteria.cs` | Port de `CardSearchCriteria.kt`; nombres JSON compatibles con los golden. |
| Carga | `Services/Search/CatalogStore.cs` | Encuentra el trío de versión más alta en `cache/catalog/` (`cards_db-X.Y.Z.zip` o `cards-X.Y.Z.json`, `rules-X.Y.Z.json`, `filters-X.Y.Z.json`). `rules`/`filters` son opcionales (sin rules no hay banlist; sin filters no hay orden canónico de sets ni autocomplete). |
| Sync | `LauncherEngine.SyncCatalogFromPoolAsync` (primaria) / `SyncCatalogAsync` (fallback) | Ver [sincronizacion.md](sincronizacion.md). |
| Datos de usuario | `UserCardDataStore.cs`, `FilterPresetStore.cs` | Favoritas/ocultas (`usercards.json` v2, **por nombre de carta**, [ADR-044](../DECISIONS.md#adr-044)) y presets de filtros (`presets.json`). Las listas ya **no** viven acá: son `.txt` de `Lists\` ([mazos.md](mazos.md)). |
| UI | `Views/SearchWindow.xaml(.cs)` | Ventana redimensionable, filtros en vivo, tabla/grilla, panel derecho Carta \| Mazo. |
| Render | `Views/CardTextRenderer.cs` | `<b>/<i>/<br>` + bullets → `Inline`s de WPF (sin WebView). |
| Paridad | `SelfTest.RunGoldenSearchTests` | **53 golden tests** generados con la lógica real de la app. |

Catálogo de referencia: 6.394 cartas, 48 sets, 838 códigos de keyword (números que verifica el selftest).

---

## Fuente de datos

`cards.json` + `rules.json` + `filters.json` (los mismos artefactos que consume la app Android),
**no** el `database.xml`: el Buscador usa campos que el XML no tiene (keywords editoriales,
legalidades por printing, flag `current`). El `database.xml` sigue siendo un blob opaco para el
launcher ([ADR-016](../DECISIONS.md#adr-016)).

Los archivos llegan a `cache/catalog/` verificados por SHA-256, o se copian a mano ahí para
desarrollo (respeta `L5A_DATA_ROOT`). Sin catálogo, el botón del Buscador avisa y no abre.

---

## Funcionalidad hoy

**Búsqueda y filtros** — nombre y texto de carta; keywords incluir/excluir con autocomplete
("contiene", OrdinalIgnoreCase, sobre los 838 códigos); tipo, clan, designador, stats, edición
(agrupada por arcos), rareza, baneadas; quick filters (solo favoritas / ver ocultas / solo ocultas /
solo lista). Los filtros **aplican en vivo**, sin modal ni botón Aplicar.

**Operadores de barra** (port de `parseBarText`): `tx.` filtra texto de carta en vivo; `k.` / `c.` /
`t.` + Enter agregan keyword o activan chip de clan/tipo (match exacto o prefijo único).

**Vistas** — grilla de miniaturas por defecto (celdas 226×322, filas virtualizadas por chunking
manual, thumbnails con `DecodePixelWidth` + `IsAsync` + retención débil) y tabla virtualizada con
orden por click en cabecera. Chip "Impresiones" = ver todas las impresiones de cada carta; con
filtro de Edición activo muestra solo las de esos sets, y si ninguna coincide conserva la preferida
para no perder la carta. Navegación por teclado en la grilla; `Ctrl+F` enfoca la búsqueda; `Ctrl+N`
abre otra ventana del Buscador sobre el mismo catálogo (estado de filtros independiente por ventana).

**Paneles colapsables** — **rieles verticales** de altura completa entre cada panel y el centro
(`PaneRail`, [ADR-035](../DECISIONS.md#adr-035)), con un chevron que apunta adónde se va el borde.
Su columna (18 DIP) **nunca colapsa**: es la única forma de reabrir el panel. Cada colapso libera
270 (Filtros) / 405 (Panel) DIP contra una celda de 226 → sigue sumando una columna de cartas.
Anchos calibrados para 1080p a 125 % (~1530 DIP útiles).

La barra superior va en dos filas: **caja de búsqueda + contador de resultados** pegados a la
izquierda y vista/orden a la derecha arriba (sin título: ya lo muestra la barra de título propia);
Limpiar filtros, toggles rápidos, **Solo conjunto + selector de conjunto + `＋`/`⎘`/`🗑`** y **mazo
activo + Mazos…** abajo. Crear, copiar y borrar listas salió del panel de filtros a esos íconos
pegados al combo (el nombre se pide con `Dialogs.Prompt`, y borrar confirma): elegir conjunto es
navegación, no un filtro más. El selector de mazo
activo vive acá y no en el panel derecho: colapsar el panel no debe esconder el destino de los `+`/`−`.

**Barra de acciones por celda** — `−`/`+` contra el mazo activo (con feedback del `AddCardResult`),
`★` favorita, `⊘` ocultar, `≡` lista activa; badge con la cantidad en el mazo ("2/3").
Doble click = agregar al mazo activo.

**Ctrl+click = agregar**, sobre cualquier parte de la carta y sin apuntar al `+` chiquito. Con Ctrl
apretado se prenden los bordes de **todas** las celdas (hay un modo activo) y la de abajo del mouse
se cubre con un overlay "＋ Agregar al mazo", que además es el blanco del click. El estado vive en
`SearchWindow.IsAddModifierDown`, una DP de la ventana: un solo cambio y todas las celdas realizadas
reaccionan por binding, en vez de recorrer miles de VMs por tecla. Se limpia en `Deactivated` para
que el modo no quede pegado si se suelta Ctrl fuera de la ventana.

**Panel derecho** — **solo el detalle de carta** (arte, texto renderizado, etiquetas UNIQUE/BANNED
data-driven de `rules.json`) más la fila de acciones sobre la carta seleccionada. Ya no hay pestañas
ni contenido del mazo: eso vive en la ventana de Mazos ([ADR-042](../DECISIONS.md#adr-042)).

**Conjunto activo** ([ADR-045](../DECISIONS.md#adr-045)) — el selector de la barra elige **un
conjunto de cartas**, que puede ser una lista, un mazo, las favoritas o las ocultas; "Solo conjunto"
filtra los resultados a sus cartas. El desplegable tiene **dos niveles**: arriba las listas +
`★ Favoritas` + `🚫 Ocultas` + una entrada `Usar mazo…`, que recién ahí baja a la lista de mazos
(con `← Volver a listas` para salir). Los mazos suelen ser muchos y no son lo que se elige
habitualmente.

Cómo llega al motor: la UI **materializa** las cartas del conjunto en `UserCardData.Lists` al momento
de buscar (`UserCardData.WithList`), así que para `CardSearchEngine` sigue siendo el `listId` de
siempre — el que verifican los golden. Sumar un tipo de conjunto nuevo no toca el motor.

- **Listas**: crear con `＋` y borrar con `🗑` desde la barra. Cargarlas es el `≡` de la celda o
  `≡ A la lista` del detalle. Son `.txt` de `<juego>\Lists\`, 1 copia por carta, y se editan en la
  ventana de Mazos (solapa **Listas**).
- **Favoritas / ocultas**: se togglean con `★` / `⊘` como siempre. Como conjunto solo se filtran; para
  editarlas como una lista está `⎘ Guardar como lista`, que las copia a una lista nueva.
- **Mazos**: solo como filtro. El `≡` se apaga (para el mazo activo están los `+`/`−`). `⎘` también
  sirve acá para partir de un mazo sin tocarlo.

Un preset guarda `listKind` + nombre del conjunto; si el conjunto ya no existe, **lo avisa** y apaga
el filtro en vez de devolver 0 resultados en silencio.

**Presets de filtros** — guardar/cargar/borrar el criteria completo con nombre (superset del slot
único de la app), en `presets.json` con los mismos nombres JSON que los golden.

**Imágenes** — se leen de `<SunAndMoon>\images\...` con la cadena de fallback de la app
(`imagePath` → `printing_images` → placeholder de texto) más la carpeta del propio exe como raíz
alternativa (caso: launcher copiado dentro de la carpeta del juego). Si una imagen no aparece, el
detalle muestra la ruta donde se buscó. El detalle tiene **Copiar imagen** (arte al portapapeles). El arte se dibuja con esquinas
redondeadas y sin marco ([ADR-037](../DECISIONS.md#adr-037)). Alias de carpeta: la DB referencia las promos como
`images/cards/P/…` y el juego las guarda en `images/cards/Promo/…` — se prueban ambas (solo afecta
archivos; el filtro/orden por set sigue usando el código `P`). El Buscador es **100 % lectura**: no
toca red ni la instalación.

---

## Paridad y verificación

Los 53 golden tests pasan sin ajustes. **Si se toca el motor o el importer, correr `--selftest`.**
Si se decide *cambiar* una semántica, hay que regenerar los expected afectados con el harness del
handoff (`../../tests/fixtures/buscador/generator/`) y anotarlo.

Bugs de la app portados a propósito, con la UI compensando ([ADR-018](../DECISIONS.md#adr-018)):

| Bug (spec del handoff) | En el motor | En la UI |
|---|---|---|
| Rareza case-sensitive (⚠️ SPEC-FILTROS §7) | se porta | el chip manda `P` **y** `p` → se ven las 495 promos |
| Subtitle-substring en keywords (⚠️ §2) | se porta | toggle "Incluir subtítulos" |
| Formato/legalidad con mismatch `20F`/`TwentyFestivals` | se porta | sin UI (la app tampoco la tiene) |

Divergencias deliberadas: activar "Solo ocultas" no resetea los demás filtros (en la app sí);
`SortField` tiene extras del launcher (TYPE/CLAN/RARITY/HONOR_REQ/FOCUS) que no existen en la app y
no están cubiertos por goldens ([ADR-030](../DECISIONS.md#adr-030)).

---

## Decisiones que aplican

[ADR-016](../DECISIONS.md#adr-016) fuente de datos ·
[ADR-017](../DECISIONS.md#adr-017) motor en memoria ·
[ADR-018](../DECISIONS.md#adr-018) bugs portados ·
[ADR-019](../DECISIONS.md#adr-019) UX desktop ·
[ADR-020](../DECISIONS.md#adr-020) imágenes de la instalación ·
[ADR-021](../DECISIONS.md#adr-021) catálogo desde el pool ·
[ADR-044](../DECISIONS.md#adr-044) identidad de carta por nombre ·
[ADR-045](../DECISIONS.md#adr-045) listas y mazos son el mismo conjunto (sustituye a
[ADR-028](../DECISIONS.md#adr-028)).

## Puntos abiertos

- Contador de cartas en el selector de conjunto (hoy solo se ve en la solapa Listas del editor).
- Renombrar una lista solo se puede desde la ventana de Mazos, no desde la barra del Buscador.
