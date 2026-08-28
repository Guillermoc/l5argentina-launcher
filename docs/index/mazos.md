# Índice — Mazos / deckbuilder

Estado actual del editor de mazos. Dominio: `src/Services/Deck/`, `src/Models/DeckModels.cs`,
`src/Views/DeckWindow.xaml(.cs)` y el panel derecho de `SearchWindow`.

Port del deckbuilder de la app Android. La spec funcional con la que se construyó es el paquete de
handoff [../functional/deckbuilder-launcher/](../functional/deckbuilder-launcher/)
(`SPEC-DECK-CRUD.md`, `SPEC-VALIDACION.md`, `SPEC-STATS-INICIALES.md`, `MODELO-DATOS-MAZO.md` y las
fuentes Kotlin), que además sigue siendo la referencia viva de paridad vía sus golden tests, en
[../../tests/fixtures/deckbuilder/](../../tests/fixtures/deckbuilder/).

---

## Piezas

| Pieza | Archivo | Qué hace |
|---|---|---|
| Modelo | `Models/DeckModels.cs` | `Deck`/`DeckCardEntry` (espejo de `decks`/`deck_cards`), `AddCardResult`, `ImportResult`, `DeckGroups` (starter/dynasty/fate + orden de tipos). |
| Motor | `Services/Deck/DeckRepository.cs` | **Port 1:1 de `DeckRepository.kt`**: `AddCard` (banned → slot → maxCopies, **en ese orden**), `RemoveCard`/`SetCardQuantity` (con `keepRowAtZero`), `ImportCards`/`ExportDeck` (formato de texto de la app), CRUD, y los espejos de UI de las specs (conteos Dynasty/Fate, slots sin cubrir, honor inicial del Goldfish, clan del primer stronghold). **Extra fuera del port:** `SetInList`/`ToggleInList` para las listas (ver abajo). |
| Formato txt | `Services/Deck/SunAndMoonDeckFile.cs` | Lee/escribe el formato de la carpeta `Decks` del juego (`Legality: X`, starter sin cantidad, `Nx Nombre`), **incluidos el prefijo `Proxy <tipo>: ` y la edición entre paréntesis**, que se leen y se devuelven iguales ([ADR-060](../DECISIONS.md#adr-060)). El parser tolera también el formato de export de la app (`N Nombre`, líneas `#`). Lo usan mazos **y** listas. El contrato entero, leído del `Assembly-CSharp.dll` del juego: [contratos/formato-txt-sun-and-moon.md](../technical/contratos/formato-txt-sun-and-moon.md). |
| Biblioteca | `Services/Deck/DeckLibrary.cs` | Un `.txt` por conjunto: mazos en `<SunAndMoon>\Decks\`, listas en `<SunAndMoon>\Lists\`, + sidecar `decksmeta.json`. Un solo `DeckRepository` para los dos (`Decks`/`Lists` son vistas filtradas por `Type`). Instancia compartida por proceso (`GetOrLoad`): Buscador y editor operan sobre los mismos archivos, el mismo mazo activo y la misma lista activa. `SaveMeta()` guarda **solo** el sidecar (etiquetas: el juego no las ve). |
| Etiquetas | `Services/Deck/DeckTags.cs` | Vocabulario y asignaciones de un mazo, la paleta de colores, el filtro (unión / cruce) y `Sections<T>` — las pilas, con la carta compartida en todas las suyas y el conteo de solapamiento ([ADR-057](../DECISIONS.md#adr-057)). Todo por CardId; la traducción a nombre la hace `DeckLibrary`. |
| Ajuste de oro | `Services/Deck/DeckGold.cs` | El coste que se **paga** vs. el impreso ([ADR-059](../DECISIONS.md#adr-059)): la regla del clan del stronghold (−2 propio / +2 otro clan grande), los ajustes manuales de −3 a +3 y el coste efectivo. Regla **viva**: se calcula contra el stronghold del momento, al sidecar van solo los overrides. |
| Filtros de carta | `Views/CardFilterPanel.xaml(.cs)` | El **mismo panel que el Buscador** ([ADR-058](../DECISIONS.md#adr-058)), aplicado a las cartas del mazo: llena un `CardSearchCriteria`, el editor lo corre con `CardSearchEngine` contra el catálogo y se queda con los ids que están en el mazo. |
| Slots | `CatalogImporter` → `CardCatalog.InitialHoldingSlots` | `filters.json → formats[].initialHoldingSlots` (id/label/required) — alimenta el aviso "Falta: {slot}". |
| UI Buscador | `Views/SearchWindow.xaml(.cs)` | Label del mazo activo + conteos en la barra; `+`/`−` por celda con feedback del `AddCardResult`. Doble click o **Ctrl+click** = agregar. No elige mazo ni muestra su contenido. El selector de **conjunto activo** (listas/mazos/favoritas) vive acá — ver [buscador.md](buscador.md). |
| UI editor | `Views/DeckWindow.xaml(.cs)` | Ventana única. Paneles agrupados **por objeto** ([ADR-051](../DECISIONS.md#adr-051)): izquierda el **mazo abierto** (pestañas **Filtros \| Stats**, con los bloques de **Etiquetas** y **Filtros de carta**), centro sus cartas agrupadas por **tipo o por etiqueta** (vistas Lista/Grilla), derecha la **carta seleccionada** con sus etiquetas al pie. El selector de mazo (segmentado Mazos \| Listas, buscador, orden/clan/estado) cuelga del **título**, no de un panel. Barra: nombre + **Buscador** + menú `⋯` ([ADR-043](../DECISIONS.md#adr-043)); vista/zoom/orden en la segunda fila ([ADR-050](../DECISIONS.md#adr-050)). Notas con debounce de 1 s. Confirmaciones y avisos por `Dialogs`, no por `MessageBox`. |
| Export PDF | `Services/Deck/PdfExporter.cs` + `Views/PdfExportWindow.xaml(.cs)` | Arma un PDF para imprimir proxies a tamaño real (63×88 mm), margen opcional de 1 mm: grilla 3×3 en A4 o Carta, **6×3 apaisada en A3** — el doble por hoja, capacidad propia del launcher que la app no tiene ([ADR-061](../DECISIONS.md#adr-061)). La ventana previa lista las cartas del mazo con un contador de copias **independiente** del mazo. El PDF se escribe a mano y embebe los JPEG sin recomprimir ([ADR-049](../DECISIONS.md#adr-049)). Solo mazos: una lista no se imprime. |
| Paridad | `SelfTest.RunGoldenDeckTests` + `RunDeckStoreTests` + `RunDeckProxyTests` + `RunCardSetTests` + `RunTrimTests` + `RunDeckStateTests` + `RunDeckTagTests` + `RunDeckGoldTests` + `RunPdfExportTests` | **25 golden tests** replicando la secuencia exacta del harness (mismos mazos "Golden N", string exacto del export) + checks propios de persistencia txt (incluidos proxies y ediciones), listas, identidad por nombre, recorte, estado de armado, etiquetas, ajuste de oro y la geometría/estructura del PDF. |

---

## Mazos y listas

Son **el mismo conjunto de cartas** ([ADR-045](../DECISIONS.md#adr-045)): un `Models.Deck` con
`Type` `"deck"` o `"list"`, el modelo que la app Android ya tenía. Difieren en cuatro cosas:

| | Mazo | Lista |
|---|---|---|
| Archivo | `<SunAndMoon>\Decks\<Nombre>.txt` | `<SunAndMoon>\Lists\<Nombre>.txt` (**hermana**, no adentro) |
| Lo lee el juego | sí | no |
| Tope por carta | `MaxCopies` | **1** |
| Validación | conteos Dynasty/Fate, slots requeridos | ninguna (`SideCount`/`UncoveredRequiredSlots` devuelven vacío) |

Un mazo y una lista **pueden llamarse igual**: son carpetas distintas. En el editor, la solapa de la
izquierda elige cuál de los dos se está viendo; en una lista desaparecen los `−`/qty/`+` (la carta
está o no está) y queda solo el `✕`.

**Las listas no pasan por `AddCard`.** La UI las muta con `SetInList`/`ToggleInList`, que **no**
aplican el rechazo de baneadas ni el de slot inicial ya cubierto: una lista es material de armado,
no un mazo legal, y querer juntar las cuatro versiones de Bamboo Harvesters es el caso normal.
`AddCard` conserva intacta la semántica portada (incluida su rama `type == "list"`, que sigue
existiendo por los golden) y el selftest verifica **las dos** por separado.

---

## Persistencia

**Los conjuntos son archivos.** Cada uno es un `.txt` — el mazo que se arma acá se juega directo, sin
exportar nada. Los `.txt` que ya estaban (los del juego o hechos a mano) se cargan tal cual; en una
lista, las cantidades `Nx` se recortan a 1 al cargar en vez de rechazar el archivo. Sin instalación
configurada, las carpetas caen en `%APPDATA%\...\Decks` y `%APPDATA%\...\Lists`.

**La identidad de cada carta en el `.txt` es su NOMBRE** ([ADR-044](../DECISIONS.md#adr-044)), no su
id de catálogo — por eso los mazos sobreviven a un cambio de base, y por eso las listas se mudaron
acá desde `usercards.json`.

Lo que el `.txt` no puede representar (notas, `sortMode`, fecha de creación, orden, mazo activo,
lista activa, **las recortadas y las etiquetas**) va al sidecar `%APPDATA%\...\decksmeta.json`,
indexado por **nombre de archivo** — las listas con prefijo `list:` para que no colisionen con un mazo
homónimo. El sidecar manda sobre lo
parseado del txt; si está corrupto, se regenera en el próximo guardado.

Cada mutación guarda inmediatamente (`Repo.DeckChanged` → `SaveDeck`): renombrar el mazo renombra el
archivo, borrarlo lo borra, y los nombres duplicados se resuelven con `" (2)"`, `" (3)"`…
Los caracteres inválidos de nombre de archivo se reemplazan por `_` (el nombre del mazo **es** el
nombre del archivo).

**Proxies y ediciones** ([ADR-060](../DECISIONS.md#adr-060)). El `.txt` admite dos cosas más que el
launcher **lee y devuelve iguales, sin hacer nada con el dato**: el prefijo `Proxy <tipo>: ` (que en
el juego apoya la carta en la **mesa** en vez de meterla en el mazo) y la edición entre paréntesis
(`Productive Mine (GoC)`). Van a `DeckCardEntry.ProxyType` / `.Edition`; los conteos, la validación,
el PDF y el export de la app siguen viendo una carta común. La misma carta con y sin prefijo **no se
fusiona** —son dos destinos distintos en el juego—: la segunda línea se conserva cruda.
Al escribir, los **starter holdings** de un mazo salen siempre con `Proxy holding: ` (en minúscula,
que es lo único que el juego reconoce después); en las listas no, porque `Lists\` no lo lee.
El **portapapeles** también los lleva, pero como opción: `ExportDeck` sin marcadores es el que fija
el golden y el que usa `DuplicateDeck`, así que la marca la prende solo el botón de copiar y al
pegar el texto se adapta en el borde (`NormalizeForImport`: saca el prefijo y traduce `Nx` a `N`,
la única forma de cantidad que el import portado entiende).

**Nombres del juego ≠ nombres del catálogo.** Sun and Moon escribe los strongholds sin el sufijo de
versión (`The Impregnable Fortress of the Crab` vs `… (1)/(2)`), a veces con case distinto o sin
`- exp`. `GetCardByNameRelaxed` resuelve esa cadena (exacto → trim → sufijo → case-insensitive →
`- exp`) **solo en el parser de txt**; `ImportCards` mantiene el match exacto de la app
([ADR-023](../DECISIONS.md#adr-023)). Al reescribir, cada entrada conserva el nombre original del
archivo (`SourceName`) y las líneas no reconocidas se reescriben verbatim: el launcher **nunca** le
rompe el mazo al juego ([ADR-024](../DECISIONS.md#adr-024)).

---

## Recorte (solo mazos)

Bajar una carta a **0** con el `−` no la saca del mazo: la deja **recortada**
([ADR-046](../DECISIONS.md#adr-046)). Sigue ahí, grisada y con su `sortOrder`, pero no se juega. El
`+` la devuelve; el `✕` —acción aparte— la saca del todo; "Limpiar recortadas" del menú `⋯` las saca
todas de una. El toggle **Recortadas** del panel izquierdo las muestra u oculta, y arranca prendido.

El motor ya lo tenía entero (`keepRowAtZero`, `DeleteCardEntry`, `RemoveAllTrimmed`, con sus
goldens): lo que faltaba era la UI y **dónde se guardan**. Van al sidecar `decksmeta.json`, por
nombre de carta, y nunca al `.txt` — ese archivo lo lee el juego y no tiene forma de expresar "está
pero no cuenta". `SunAndMoonDeckFile.Write` ya filtraba `Quantity > 0`, así que no hubo que tocarlo.
Al cargar, **el `.txt` manda**: si el nombre aparece ahí con cantidad, la marca de recorte se ignora.

Una recortada no suma en conteos, stats ni export, y tampoco cuenta como carta distinta en los
`[N]` de las cabeceras. Las **listas no tienen recorte**: ahí la carta está o no está.

---

## Filtros de carta (el panel del Buscador, adentro del mazo)

El panel `CardFilterPanel` es **el mismo control** que usa el Buscador ([ADR-058](../DECISIONS.md#adr-058)):
keywords con autocomplete, texto de carta, tipo, clan, designador, stats, edición y rareza. Acá filtra
las cartas del mazo abierto. Arranca **plegado** —comparte panel con los filtros del mazo y las
etiquetas— y trae **Limpiar filtros**, que aparece solo cuando hay algo que limpiar.

Cómo se aplica: el editor corre `CardSearchEngine` con ese criteria sobre el catálogo entero y se
queda con los ids que además están en el mazo. Es un `.Where()` más en el mismo pipeline, así que
agrupar (por tipo o por etiqueta), ordenar, las recortadas y los chips Starter/Dynasty/Fate siguen
valiendo. Los `(N) [M]` de las cabeceras cuentan lo que quedó; los totales de la barra, el mazo entero.

**Dos filtros se fuerzan: las baneadas y las ocultas siempre entran**, y la sección "Otros" no se
muestra. Son filtros para navegar el catálogo: adentro de un mazo lo único que podrían hacer es
esconder cartas que el mazo tiene de verdad (una baneada que quedó en el `.txt` hay que **verla** para
poder sacarla).

**La caja de texto acepta los prefijos** de la barra del Buscador: `tx.` busca en el texto de la
carta en vivo, y `k.` / `c.` / `t.` + Enter agregan keyword o prenden el chip de clan/tipo del panel.
En modo operador la caja **no** filtra por nombre, o `k.bushi` no matchearía ningún nombre.

---

## Etiquetas (las pilas de la mesa)

Extra del launcher, no existe en la app ([ADR-057](../DECISIONS.md#adr-057)). Cada mazo —y cada
lista— tiene su propio vocabulario de etiquetas (nombre + color de una paleta fija) y sus
asignaciones. **Son del mazo**: la misma carta puede ser "deshonor" en uno y "defensa" en otro.

**Agrupar por etiqueta arma una sección por etiqueta**, en el orden en que se crearon y con
`Sin etiquetar` al final. **Una carta aparece en todas sus pilas**: la que quita honor y pide
magistrados está en las dos, porque la pregunta al armar es "cuánto deshonor tengo" y una pila que se
saltea las compartidas contesta de menos. Los `(N) [M]` de las cabeceras, entonces, **no** suman el
mazo —son conjuntos que se solapan— y por eso la cabecera agrega `· N también en otra`.

> Antes de esto la agrupación era una **partición** por combinación exacta (el cruce como sección
> aparte, cada carta una sola vez). Contestaba bien "¿en qué caja va?" y mal la pregunta real de armar.
> El razonamiento completo está en [ADR-057](../DECISIONS.md#adr-057).

- **Crear/editar**: `＋` en el panel Filtros, o `＋ Etiqueta nueva…` desde la carta. Click derecho
  sobre el chip de una etiqueta —en el panel de filtros o en el de la carta— abre su menú: **elegir
  el color** entre las diez muestras de la paleta, renombrarla (arrastra las asignaciones) o
  borrarla. El color se asigna solo al crearla, por orden de la paleta, para no frenar el armado con
  una decisión de color; cambiarlo después se ve al toque en el chip y en los puntitos.
- **Asignar**: click derecho sobre la carta (menú con sus etiquetas), o los chips del panel derecho.
  El menú de la carta se lleva también **Copiar imagen**, que era lo que hacía el click derecho antes.
- **Filtrar**: los chips del panel Filtros, con el conteo al lado. Con dos o más marcadas aparece
  **Alguna \| Todas** (unión o cruce).
- **Ver**: cada carta lleva los puntitos de sus etiquetas **también** en la vista por tipo — es lo que
  hace visible el solapamiento sin cambiar de agrupación.

Los chips Starter/Dynasty/Fate y el filtro de texto siguen valiendo en las dos agrupaciones: filtran,
no son el eje. Sin ninguna etiqueta creada, el segmentado **Por etiqueta** queda deshabilitado.

**La sección arranca plegada**, como las de Filtros de carta. Su cabecera lleva el estado, porque
plegada es lo único que se ve: `Etiquetas` sin ninguna, `Etiquetas (N)` con vocabulario, y
`Etiquetas · filtrando (N)` cuando hay chips marcados — filtrar sin señal a la vista es cómo se llega
a "no entiendo por qué faltan cartas".

**La etiqueta acompaña a la carta mientras esté en el mazo**: sacarla con `✕` se lleva su
clasificación (`DeckTags.Prune` en cada guardado); recortarla no. Duplicar el mazo copia vocabulario y
asignaciones.

---

## Ajuste de oro (el coste que se paga)

Extra del launcher, no existe en la app ([ADR-059](../DECISIONS.md#adr-059)). El chip **Ajuste de
oro** del panel Filtros prende el modo **en ese mazo**: el orden por Coste, los mini-stats de la
vista Lista y la curva de las stats pasan a usar el coste **efectivo** en vez del impreso.

**La regla es viva, no un número escrito en cada carta.** Al prender el modo no se materializa nada:
el ajuste se calcula contra el clan del stronghold del momento. Cambiar el stronghold re-ajusta el
mazo entero solo y una carta agregada después ya entra ajustada. Al sidecar van **solo los ajustes
manuales**.

- **La regla:** personalidad del clan de tu stronghold → **−2**; personalidad de **otro de los nueve
  clanes grandes** → **+2**; sin clan, o de una facción menor (shadowlands, ratling, naga, ronin,
  hare, spirit, monk, ninja) → **0**. Sin stronghold no hay contra qué comparar y no ajusta nada.
  El descuento sí es literal: si la carta lleva el clan del stronghold, sea cual sea, es tuya.
- **A mano:** click derecho sobre la carta → tira **−3 … +3**. El encabezado del menú dice si el
  número lo pone la regla o el usuario, y **Volver a la regla** aparece solo si es manual. Elegir a
  mano el mismo número que la regla **sigue siendo** manual: es cómo se fija una carta.
- **Volver a la regla (N)**, bajo el chip, borra los N ajustes manuales del mazo (con confirmación).
- **Dónde se ve:** en la Grilla, una pastilla discreta contra el borde derecho y a media altura, solo
  si el ajuste no es cero. En la Lista, al lado del coste impreso: `©8 +2` — el impreso no se
  reemplaza, es lo que dice la carta.
- **Piso en 0:** una personalidad de 2 con −2 sale gratis, no −1. El ajuste igual se muestra `−2`.
- **Escalones de coste:** con el modo prendido, el bloque **Personality** se parte en una sección por
  coste **efectivo** (`Costo 6 — (N) [M]`), que es lo que vuelve legible el ajuste: "las de 6" es lo
  que sale una compra, no un tramo de una lista ordenada. Es una **partición** —cada carta cae en una
  sola sección y los conteos suman el bloque—, al revés que las pilas de etiquetas. Si el orden
  elegido es por Coste, los escalones siguen su dirección; con cualquier otro van de menor a mayor.
  Las de coste variable (★) quedan al final siempre. Solo Personality: es donde la regla ajusta.

El coste efectivo es **de la UI**: `DeckRepository`, el export y el PDF siguen viendo el coste del
catálogo, así que nada de lo cubierto por los golden cambia de semántica.

---

## Validación

No hay motor de validación: paridad con la app. Solo existen los rechazos de `AddCard`
(baneada → slot cubierto → tope de copias) y los indicadores blandos (Dynasty/Fate en rojo si < 40,
"Falta: {slot}"). Dos strongholds siguen permitidos (⚠️ paridad).

⚠️ `DeckClan` toma el primer stronghold **sin mirar la cantidad**, igual que la app: recortar el
stronghold deja el mazo etiquetado con su clan. Semántica portada, se deja ([ADR-046](../DECISIONS.md#adr-046)).

**Estado de armado** (`IsDeckComplete`, extra del launcher — [ADR-047](../DECISIONS.md#adr-047)):
un mazo está **terminado** si cubre los dos slots requeridos (Border Keep y Bamboo Harvesters, vía
`UncoveredRequiredSlots`, o sea leídos de `filters.json` y no hardcodeados), tiene stronghold y cada
lado queda entre 40 y `SideMax` (50). Es blando como todo lo demás: no bloquea nada, solo alimenta
el filtro y el `✓` del subtítulo. Una carta recortada no cubre slot ni suma al lado.

**Divergencia de UX deliberada**: acá los `AddCardResult` distintos de `Success` **se informan**
(feedback en el panel / status del editor); la app los ignora en silencio
([ADR-026](../DECISIONS.md#adr-026)).

---

## Layout

- **Buscador**: filtros a la izquierda y panel Carta|Mazo a la derecha, ambos colapsables desde los
  rieles verticales ([ADR-035](../DECISIONS.md#adr-035)). El mazo activo y la lista activa se eligen
  en la barra superior (el selector de mazo salió del panel derecho para no perderse al colapsarlo). Los `+`/`−` van
  siempre contra el mazo activo.
- **Editor, selector de mazo**: cuelga del **título** ([ADR-051](../DECISIONS.md#adr-051)), no de un
  panel. Trae segmentado Mazos \| Listas, `＋`, buscador (por nombre del conjunto **o** por una carta
  que contenga), y tres selectores — **orden** (Recientes, default / Alfabético), **clan** (solo los
  que existen entre los conjuntos visibles) y **estado** (Todos / Terminados / Pendientes, oculto en
  Listas). "Recientes" ordena por la última escritura del `.txt`, que `DeckLibrary` captura al cargar
  y al guardar: es "lo último que tocaste", no la fecha de creación. Se cierra al elegir. Sin ningún mazo se abre solo, porque el `＋` vive adentro y crear es lo único
  que se puede hacer ([ADR-052](../DECISIONS.md#adr-052)).
- **Editor, panel izquierdo**: dos pestañas sobre **el mismo objeto**, el mazo abierto —
  **Filtros** (texto con prefijos sobre sus cartas, chips Starter/Dynasty/Fate, toggle
  **Recortadas (N)**, y dos secciones plegadas: **Etiquetas** y los **Filtros de carta** compartidos
  con el Buscador: `＋`, segmentado Por tipo \| Por etiqueta, y los chips con su conteo) y
  **Stats**. Colapsarlo esconde una sola cosa; antes se llevaba el selector de mazo también.
- **Editor, panel derecho**: la **carta seleccionada**, sin pestañas, con sus **etiquetas** al pie
  (fuera del scroll del detalle, que es un control compartido con el Buscador y no sabe de mazos).
- **Lado a lado** (botón de los dos paneles en la barra; estuvo brevemente en el menú `⋯`, ver
  [ADR-052](../DECISIONS.md#adr-052)): acomoda Buscador y editor sobre el área de trabajo, 70 % / 30 %,
  con el editor en vista Lista y todo colapsado. Es el layout de armar — ver
  [ui.md](ui.md).
- **Vista Grilla**: mismos cuatro tamaños de celda que el Buscador (`－`/`＋` o **Ctrl + rueda**) y la
  misma previsualización con **Alt**. Click derecho abre el menú de la carta (etiquetas + copiar el
  arte, [ADR-057](../DECISIONS.md#adr-057)). Las miniaturas salen del mismo
  `GridCell` que el Buscador, así que comparte su caché y su cola de decodificación
  ([ADR-053](../DECISIONS.md#adr-053)) — y compartía también su defecto. En el tamaño más chico los
  `−`/`+`/`✕` no entran y queda solo la cantidad, que es lo único que no se deduce mirando la carta;
  para editar a ese tamaño está la vista Lista.
- **`?`** al final de la barra: el listado de atajos de esta ventana.
- **Editor**: **seleccionar un mazo lo activa** — no hay botón "Activar" ni badge "● Activo". Con la
  solapa **Listas** abierta, lo que se activa es la **lista activa** (destino del `≡` del Buscador),
  sin tocar el mazo activo: son dos punteros distintos, ambos en el sidecar.
  La barra superior tiene solo el nombre del mazo, **Buscador** (abre otra ventana del Buscador sobre
  el mismo catálogo, como el `Ctrl+N`) y el menú `⋯` con renombrar / duplicar / importar / exportar /
  carpeta / eliminar ([ADR-043](../DECISIONS.md#adr-043)).
  Layout: mazos+filtros | cartas agrupadas | detalle/stats, con los mismos rieles de colapso y
  las mismas pestañas adosadas (Carta | Stats) que el Buscador. El centro tiene dos vistas
  (segmentado Lista/Grilla): filas compactas o cartas en imagen (~3 columnas, default como la app),
  ambas con las secciones Starter/Dynasty/Fate → tipo y los controles `−`/qty/`+`/`✕`.

---

## Verificación

Del total de la batería, **153 checks** son del deckbuilder: los 25 golden + persistencia txt /
`DeckLibrary` + resolución tolerante de nombres + los 21 de `RunDeckProxyTests` (prefijo de proxy y
edición: se leen, se reescriben igual, un tipo fuera de los doce no es prefijo, la misma carta con y
sin prefijo no se fusiona, los starter holdings salen marcados y las listas no, y el portapapeles los lleva sin romper duplicar mazo, más la traducción de `Nx` a `N` que hace entrar un `.txt` del juego pegado) + los 21 de
`RunCardSetTests` (listas como `.txt`,
`SetInList` vs `AddCard`, migración e identidad por nombre de `usercards.json`) + los 14 de
`RunTrimTests` (recorte) + los 10 de `RunDeckStateTests` (mazo terminado vs pendiente) + los 27 de
`RunDeckTagTests` (etiquetas: sidecar por nombre, pilas con la carta compartida en todas, filtro,
renombrar/borrar) + los 27 de `RunDeckGoldTests` (ajuste de oro: las tres ramas de la regla sobre
el ejemplo 8/8/8 → 6/8/10, el override que gana y vuelve, los topes, el round-trip por el sidecar y
que cambiar el stronghold re-ajuste el mazo solo, más los escalones de coste como partición). Si se toca
`DeckRepository` o el importer, correr `--selftest`; si se *cambia* una semántica golden, regenerar
los expected con el harness del handoff (`../../tests/fixtures/deckbuilder/generator/`) y anotarlo.

---

## Decisiones que aplican

[ADR-022](../DECISIONS.md#adr-022) persistencia en los txt del juego ·
[ADR-023](../DECISIONS.md#adr-023) resolución tolerante solo en el parser ·
[ADR-024](../DECISIONS.md#adr-024) líneas no reconocidas verbatim ·
[ADR-046](../DECISIONS.md#adr-046) recorte, en el sidecar (sustituye a
[ADR-025](../DECISIONS.md#adr-025)) ·
[ADR-026](../DECISIONS.md#adr-026) validación de la app + feedback ·
[ADR-027](../DECISIONS.md#adr-027) `NoActiveDeck` conserva su nombre ·
[ADR-028](../DECISIONS.md#adr-028) listas ≠ mazos ·
[ADR-029](../DECISIONS.md#adr-029) duplicar mazo ·
[ADR-057](../DECISIONS.md#adr-057) etiquetas: pilas que se solapan, en el sidecar ·
[ADR-059](../DECISIONS.md#adr-059) ajuste de oro: regla viva, overrides en el sidecar ·
[ADR-060](../DECISIONS.md#adr-060) proxies y ediciones del txt: se leen y se devuelven iguales; los
starter holdings salen marcados.

## Puntos abiertos

- Goldfish (sandbox de testeo) — fuera de alcance; la spec del handoff lo cubre si algún día se quiere.
- Grupos de usuario (carpetas de mazos) — no portados. `deck_card_groups` (subgrupos dentro del mazo,
  en desuso en la app) tampoco: las **etiquetas** cubren el caso con otro modelo, una carta en varias
  pilas en vez de un grupo excluyente ([ADR-057](../DECISIONS.md#adr-057)).
- Etiquetar de a varias cartas (selección múltiple en el centro) — el centro no tiene modelo de
  selección; hoy se etiqueta carta por carta.
- Buscador embebido en el editor: hoy el flujo es "Buscador `+`/`−` sobre el mazo activo" (como la app).
