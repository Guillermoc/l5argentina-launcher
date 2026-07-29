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
| Formato txt | `Services/Deck/SunAndMoonDeckFile.cs` | Lee/escribe el formato de la carpeta `Decks` del juego (`Legality: X`, starter sin prefijo, `Nx Nombre`). El parser tolera también el formato de export de la app (`N Nombre`, líneas `#`). Lo usan mazos **y** listas. |
| Biblioteca | `Services/Deck/DeckLibrary.cs` | Un `.txt` por conjunto: mazos en `<SunAndMoon>\Decks\`, listas en `<SunAndMoon>\Lists\`, + sidecar `decksmeta.json`. Un solo `DeckRepository` para los dos (`Decks`/`Lists` son vistas filtradas por `Type`). Instancia compartida por proceso (`GetOrLoad`): Buscador y editor operan sobre los mismos archivos, el mismo mazo activo y la misma lista activa. |
| Slots | `CatalogImporter` → `CardCatalog.InitialHoldingSlots` | `filters.json → formats[].initialHoldingSlots` (id/label/required) — alimenta el aviso "Falta: {slot}". |
| UI Buscador | `Views/SearchWindow.xaml(.cs)` | Label del mazo activo + conteos en la barra; `+`/`−` por celda con feedback del `AddCardResult`. Doble click o **Ctrl+click** = agregar. No elige mazo ni muestra su contenido. El selector de **conjunto activo** (listas/mazos/favoritas) vive acá — ver [buscador.md](buscador.md). |
| UI editor | `Views/DeckWindow.xaml(.cs)` | Ventana única. Paneles agrupados **por objeto** ([ADR-051](../DECISIONS.md#adr-051)): izquierda el **mazo abierto** (pestañas **Filtros \| Stats**), centro sus cartas agrupadas Starter/Dynasty/Fate → tipo (vistas Lista/Grilla), derecha la **carta seleccionada** (sin pestañas). El selector de mazo (segmentado Mazos \| Listas, buscador, orden/clan/estado) cuelga del **título**, no de un panel. Barra: nombre + **Buscador** + menú `⋯` ([ADR-043](../DECISIONS.md#adr-043)); vista/zoom/orden en la segunda fila ([ADR-050](../DECISIONS.md#adr-050)). Notas con debounce de 1 s. Confirmaciones y avisos por `Dialogs`, no por `MessageBox`. |
| Export PDF | `Services/Deck/PdfExporter.cs` + `Views/PdfExportWindow.xaml(.cs)` | Arma un PDF para imprimir proxies: grilla 3×3 a tamaño real (63×88 mm), A4 o Carta, margen opcional de 1 mm. La ventana previa lista las cartas del mazo con un contador de copias **independiente** del mazo. El PDF se escribe a mano y embebe los JPEG sin recomprimir ([ADR-049](../DECISIONS.md#adr-049)). Solo mazos: una lista no se imprime. |
| Paridad | `SelfTest.RunGoldenDeckTests` + `RunDeckStoreTests` + `RunCardSetTests` + `RunTrimTests` + `RunDeckStateTests` + `RunPdfExportTests` | **25 golden tests** replicando la secuencia exacta del harness (mismos mazos "Golden N", string exacto del export) + checks propios de persistencia txt, listas, identidad por nombre, recorte, estado de armado y la geometría/estructura del PDF. |

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
lista activa) va al sidecar `%APPDATA%\...\decksmeta.json`, indexado por **nombre de archivo** — las
listas con prefijo `list:` para que no colisionen con un mazo homónimo. El sidecar manda sobre lo
parseado del txt; si está corrupto, se regenera en el próximo guardado.

Cada mutación guarda inmediatamente (`Repo.DeckChanged` → `SaveDeck`): renombrar el mazo renombra el
archivo, borrarlo lo borra, y los nombres duplicados se resuelven con `" (2)"`, `" (3)"`…
Los caracteres inválidos de nombre de archivo se reemplazan por `_` (el nombre del mazo **es** el
nombre del archivo).

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
  y al guardar: es "lo último que tocaste", no la fecha de creación. Se cierra al elegir.
- **Editor, panel izquierdo**: dos pestañas sobre **el mismo objeto**, el mazo abierto —
  **Filtros** (texto sobre sus cartas, chips Starter/Dynasty/Fate, toggle **Recortadas (N)**) y
  **Stats**. Colapsarlo esconde una sola cosa; antes se llevaba el selector de mazo también.
- **Editor, panel derecho**: la **carta seleccionada**, sin pestañas.
- **Lado a lado** (fila **Acomodar lado a lado** del menú `⋯`; era un `⫞` en la barra hasta
  [ADR-050](../DECISIONS.md#adr-050)): acomoda Buscador y editor sobre el área de trabajo, 70 % / 30 %,
  con el editor en vista Lista y todo colapsado. Es el layout de armar — ver
  [ui.md](ui.md).
- **Vista Grilla**: mismos cuatro tamaños de celda que el Buscador (`－`/`＋` o **Ctrl + rueda**) y la
  misma previsualización con **Alt**. Click derecho copia el arte. En el tamaño más chico los
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

Del total de la batería, **78 checks** son del deckbuilder: los 25 golden + persistencia txt /
`DeckLibrary` + resolución tolerante de nombres + los 21 de `RunCardSetTests` (listas como `.txt`,
`SetInList` vs `AddCard`, migración e identidad por nombre de `usercards.json`) + los 14 de
`RunTrimTests` (recorte) + los 10 de `RunDeckStateTests` (mazo terminado vs pendiente). Si se toca
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
[ADR-029](../DECISIONS.md#adr-029) duplicar mazo.

## Puntos abiertos

- Goldfish (sandbox de testeo) — fuera de alcance; la spec del handoff lo cubre si algún día se quiere.
- Grupos de usuario (carpetas de mazos) y subgrupos dentro del mazo (`deck_card_groups`, en desuso en
  la app) — no portados.
- Buscador embebido en el editor: hoy el flujo es "Buscador `+`/`−` sobre el mazo activo" (como la app).
