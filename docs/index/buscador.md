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
| Panel de filtros | `Views/CardFilterPanel.xaml(.cs)` | **Compartido con el editor de Mazos** ([ADR-058](../DECISIONS.md#adr-058)): keywords, texto, tipo, clan, designador, stats, edición, rareza y otros. No busca: llena un `CardSearchCriteria` (`Fill`) y avisa (`Changed`/`TextTyped`). También vive acá `ParseBar`, el parser de los prefijos de la barra. |
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
`t.` + Enter agregan keyword o activan chip de clan/tipo (match exacto o prefijo único). El parser
(`CardFilterPanel.ParseBar`) lo comparte el editor de Mazos y tiene checks propios: lo delicado es
que **`tx.` se evalúa antes que `t.`**.

**El panel de filtros no es de esta ventana.** Es `CardFilterPanel`, compartido con el editor de
Mazos ([ADR-058](../DECISIONS.md#adr-058)). Lo que sigue siendo del Buscador y se agrega **después**
de `Fill`: la barra con sus modos, los toggles rápidos (favoritas / holdings iniciales / ver ocultas
/ solo lista), el conjunto activo, el orden y los **presets** — que guardan justamente eso, y por eso
no se fueron al control.

**Vistas** — grilla de miniaturas por defecto (celda de 226×322 en el zoom del medio, filas
virtualizadas por chunking manual, thumbnails con `DecodePixelWidth` + cola propia de decodificación
+ caché de dos niveles, ver abajo) y tabla virtualizada con
orden por click en cabecera. Chip "Impresiones" = ver todas las impresiones de cada carta; con
filtro de Edición activo muestra solo las de esos sets, y si ninguna coincide conserva la preferida
para no perder la carta. Navegación por teclado en la grilla; `Ctrl+F` enfoca la búsqueda; `Ctrl+N`
abre otra ventana del Buscador sobre el mismo catálogo (estado de filtros independiente por ventana).

**Paneles colapsables** — **rieles verticales** de altura completa entre cada panel y el centro
(`PaneRail`, [ADR-035](../DECISIONS.md#adr-035)), con un chevron que apunta adónde se va el borde.
Su columna (18 DIP) **nunca colapsa**: es la única forma de reabrir el panel. Cada colapso libera
270 (Filtros) / 405 (Panel) DIP contra una celda de 226 → sigue sumando una columna de cartas.
Anchos calibrados para 1080p a 125 % (~1530 DIP útiles). **Bajo 1000 DIP** los dos paneles se
cierran solos y los rieles se apagan; **bajo 900** además se esconde lo accesorio de la barra (mazo
activo, resumen de filtros, zoom) porque el piso lo pone la barra de botones. Al volver a agrandar se
restaura lo que el usuario tenía. Detalle en [ui.md](ui.md).

La barra superior va en dos filas: **caja de búsqueda + contador de resultados** pegados a la
izquierda y vista/orden a la derecha arriba (sin título: ya lo muestra la barra de título propia);
Limpiar filtros (**solo si hay filtros puestos**), toggles rápidos, **Solo lista + selector de
conjunto** y el **mazo activo** abajo. Crear y borrar listas viven **adentro** del desplegable de
conjunto, como "Usar mazo…" (el nombre se pide con `Dialogs.Prompt`, y borrar confirma): son
operaciones sobre lo que ese desplegable lista, y elegir conjunto es navegación, no un filtro más
([ADR-050](../DECISIONS.md#adr-050)). El nombre del mazo activo es clickeable y abre la ventana de
Mazos (`Ctrl+D` hace lo mismo); vive acá y no en el panel derecho porque colapsar el panel no debe
esconder el destino de los `+`/`−`. La fila 1 es un `DockPanel`: ver [ui.md](ui.md).

**Contador + resumen de filtros activos** — al lado de "N resultados" va un compacto de lo que está
filtrando (`"hida" · personality/holding · Cangrejo · F≥5 · incl. baneadas`), y el click lo abre
completo. Es el `summaryOf` de la app, y acá pesa **más**: el panel de filtros colapsa
([ADR-035](../DECISIONS.md#adr-035)), así que sin esto se puede quedar filtrando sin ninguna señal
de por qué.

**Barra de acciones por celda** — `−`/`+` contra el mazo activo (con feedback del `AddCardResult`),
`★` favorita, `⊘` ocultar, `≡` lista activa; badge con la cantidad en el mazo ("2/3").
Doble click = agregar al mazo activo. **Click derecho = copiar el arte** al portapapeles (sin
tooltip en la celda: saltaba en cada carta y la acción está en el `?`); se
re-decodifica el archivo a tamaño completo (`CardImageClipboard`), no se copia la miniatura de 340 px
de la grilla.

**Zoom de la grilla** — cuatro tamaños de celda (124 / 168 / 226 / 302 de ancho, proporción de carta
escaneada), con el desplegable de tamaño que cierra el segmentado Lista \| Grilla o con
**Ctrl + rueda** ([ADR-050](../DECISIONS.md#adr-050)). En el más
chico **la barra de acciones de la celda no se dibuja**: mide ~160 con sus cinco botones y no entra
en 124. No se pierde nada — Ctrl+click agrega, click derecho copia, Alt previsualiza y el panel de
detalle tiene las mismas acciones.

Cambiar de zoom **solo reagrupa las filas** (cambia la cantidad de columnas): el tamaño viaja por
binding a las celdas ya realizadas y las miniaturas no dependen del nivel. Es deliberado que la
resolución de decodificación sea **una sola para los cuatro niveles**: variarla obligaba a vaciar el
caché en cada cambio y a redecodificar desde disco todo lo visible.

**Caché de miniaturas — dos niveles.** Con ~6400 cartas y ~0,87 MB por miniatura decodificada
(400 px), un LRU fuerte grande retendría cientos de MB para siempre. `ThumbnailCache` guarda las
últimas **120** con referencia fuerte —dos o tres pantallas en cualquier zoom, que es lo que hace
falta para scrollear en las dos direcciones sin tocar disco— y el resto con **`WeakReference`**:
siguen disponibles mientras el GC no las reclame, así que volver a una zona ya vista casi nunca
redecodifica, pero bajo presión de memoria el runtime se las lleva en vez de que el proceso siga
creciendo. Nunca es peor que un LRU puro del mismo tamaño: lo desalojado no se pierde, se degrada. El
tamaño de celda viaja por dos `DependencyProperty` de la ventana (`CellWidth`/`CellHeight`) a las que
se bindea la plantilla, así un solo cambio re-mide todas las celdas realizadas.

**Quién decodifica: `ThumbnailLoader`, no `IsAsync`** ([ADR-053](../DECISIONS.md#adr-053)). El binding
de `GridCell.Thumb` es **sincrónico** y devuelve lo que ya esté en caché (`ThumbnailCache.Peek`, sin
tocar disco); la primera vez encola el archivo y la celda se repinta por `PropertyChanged` cuando
llega. La cola tiene tres workers, se atiende **LIFO** —al scrollear rápido, lo que quedó en pantalla
es lo último pedido— y no descarta pedidos viejos. Reemplaza a los bindings `IsAsync`, que
**despachaban a cuentagotas**: medido con la caché caliente, la primera miniatura tardaba 23 s y el
resto 39 s, con la CPU plana. El editor de Mazos usa el mismo `GridCell` y se arregló con él.

**Previsualización con Alt** — mantener **Alt** y pasar sobre una carta la abre grande al lado del
cursor, con el arte **original** (no la miniatura). El modificador se lee en el `MouseMove` y no
escuchando la tecla: en WPF Alt llega como `Key.System` y compite con la activación de menús. El
popup se corre 26 px abajo-derecha para no quedar bajo el mouse — si tapara la celda, el `MouseLeave`
lo cerraría en loop— y no es clickeable. Se cierra al soltar Alt, al salir de la celda, al pasar a
vista Lista o al perder el foco la ventana.

**Chip "Solo holdings iniciales"** (`onlyInitialHoldings`) — las 7 cartas elegibles de
`filters.json` (Bamboo Harvesters y Border Keep con sus variantes). El chip se esconde si el
catálogo no trae ninguna, como el `hasInitialHoldings` de la app.

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
conjunto de cartas** —una lista, un mazo o las ocultas— y el chip **"Solo lista"**
filtra los resultados a sus cartas. (El chip dice "Solo lista" porque es el caso de todos los días;
el selector admite igual cualquier conjunto.) El desplegable tiene **dos niveles**: arriba las listas +
`🚫 Ocultas` + una entrada `Usar mazo…`, que recién ahí baja a la lista de mazos
(con `← Volver a listas` para salir). Los mazos suelen ser muchos y no son lo que se elige
habitualmente.

Cómo llega al motor: la UI **materializa** las cartas del conjunto en `UserCardData.Lists` al momento
de buscar (`UserCardData.WithList`), así que para `CardSearchEngine` sigue siendo el `listId` de
siempre — el que verifican los golden. Sumar un tipo de conjunto nuevo no toca el motor.

- **Listas**: crear y borrar desde el propio desplegable de conjunto. Cargarlas es el `≡` de la celda o
  `≡ A la lista` del detalle. Son `.txt` de `<juego>\Lists\`, 1 copia por carta, y se editan en la
  ventana de Mazos (solapa **Listas**).
- **Ocultas**: se togglean con el `⊘` de siempre; como conjunto solo se filtran. **Favoritas no está
  en el selector**: ya tiene su chip "Solo favoritas" y su `★` por celda, y como conjunto no aportaba
  nada que no estuviera a un click.
- **Mazos**: solo como filtro. El `≡` se apaga (para el mazo activo están los `+`/`−`).

Un preset guarda `listKind` + nombre del conjunto; si el conjunto ya no existe, **lo avisa** y apaga
el filtro en vez de devolver 0 resultados en silencio.

**Ayuda** — un `?` discreto al final de la fila de vista/orden abre el listado completo de atajos
de teclado y mouse (`Dialogs.Info`, texto en `Help_ShortcutsSearch`).

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
