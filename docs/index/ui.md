# Índice — UI, tema e i18n

Estado actual de la capa visual. Dominio: `src/MainWindow.xaml(.cs)`, `src/Views/*`,
`src/Themes/Theme.xaml`, `src/Resources/*`, `src/Assets/*`.

Guía visual (tokens, paleta, tipografía): [../design/style-guide.md](../design/style-guide.md).
Handoff original del rediseño: [../design/visual/](../design/visual/).

---

## Ventanas

| Ventana | Tamaño | Notas |
|---|---|---|
| `MainWindow` | **810×540 fija** (3:2), `ResizeMode=NoResize`, `WindowStyle=None` + `WindowChrome` propio | El diseño se **compone a 1080×720** y se escala con un `Viewbox Stretch=Uniform` (810×540 = 75 %). Todo lo que se agregue va en el lienzo de 1080×720 — y como el alto es fijo, **lo que se agrega arriba se lo saca al cuerpo**: el aviso de novedades terminó dentro del header por eso (ver abajo). |
| `FirstRunWindow` | diálogo modal | Detección de instalación + captura de la base Original. Devuelve `true`/`false` a `App`. |
| `SearchWindow` | 1323×798, `Min` **460×460**, redimensionable/maximizable | Chrome propio (`CaptionHeight=35`). `Ctrl+N` abre otra instancia sobre el mismo catálogo. |
| `DeckWindow` | 1323×798, `Min` **460×460** | Mismo chrome. `DeckWindow.Open(...)` reusa la ventana si ya está abierta. Los paneles se agrupan **por objeto** ([ADR-051](../DECISIONS.md#adr-051)): el izquierdo es el **mazo abierto** (pestañas `PanelTab` **Filtros \| Stats**) y el derecho es la **carta seleccionada**, sin pestañas. Elegir qué mazo abrir **no es un panel**: cuelga del título (ver abajo). En modo lista desaparecen `−`/qty/`+`, los conteos Dynasty/Fate, el estado de armado y el toggle de recortadas. |
| `PdfExportWindow` | 620×720, `Min` **480×420**, modal | Pantalla previa del PDF de impresión, abierta desde el menú `⋯` del editor. Mismo chrome, sin maximizar. Una fila por carta con su contador de copias (el número gris al lado es lo que tiene el mazo); las filas en 0 se apagan al 42 %, el mismo gesto visual que una carta recortada. Al pie: hoja A4 \| Carta (segmentado) y el check de margen. |

**Ayuda de atajos** — un `?` discreto al final de la barra de cada ventana abre un `Dialogs.Info`
con el listado completo de atajos de teclado y mouse. El texto vive entero en `Strings.resx`
(`Help_ShortcutsSearch` / `Help_ShortcutsDeck`), así que agregar un atajo es editar el `.resx` y no
tocar código — y si se olvida, se nota al leerlo.

**Qué NO es un botón de la barra** ([ADR-050](../DECISIONS.md#adr-050)). La pasada de racionalización
dejó una regla: en la barra van las **acciones frecuentes**; lo demás va al componente que le
corresponde. En concreto: el **zoom** es una propiedad de la vista, así que es un desplegable pegado
al segmentado Lista\|Grilla (antes eran dos botones `－`/`＋` por ventana); **orden y dirección** son
un solo concepto y ahora son un solo desplegable donde la flecha es parte de la opción (antes un
combo más un `↑`/`↓`); **crear y borrar lista** son operaciones sobre lo que el selector de conjunto
lista, así que viven **adentro** de ese desplegable, como "Usar mazo…"; **lado a lado** es una acción
de ventana y bajó al menú `⋯`; **Limpiar filtros** aparece solo cuando hay filtros puestos, así su
presencia *es* la señal; y el botón "Mazos" del Buscador se fue porque llamaba al mismo handler que
el nombre del mazo, que ya es clickeable (queda `Ctrl+D` para cuando la barra está achicada).

**La fila 1 del Buscador es un `DockPanel`, no un `Grid`.** El grupo de vista/orden va anclado a la
derecha y **primero en el XAML**, que en un `DockPanel` es el orden de reparto: cobra su ancho antes
que nada y lo que cede es la caja de búsqueda, que se achica sin romperse. Con dos columnas `*`
compitiendo, ese grupo se quedaba sin ancho. **Los `Popup` no van adentro de un `StackPanel` de
barra**: se miden como hijo y le comen el ancho deseado a los controles que vienen después; viven en
la raíz de la ventana, como `HoverPreview`.

**Zoom de grilla** — cuatro tamaños de celda en las dos ventanas, con el desplegable del segmentado o **Ctrl + rueda**
(`PreviewMouseWheel`, que marca `Handled` solo con Ctrl para no robarle el scroll a la rueda pelada).
En el Buscador el tamaño viaja por `DependencyProperty` a la plantilla; en el editor las celdas se
construyen en código, así que se rearma el cuerpo. **En el nivel mínimo la barra de acciones de la
celda no entra** (mide ~160 contra 124 de celda) y se esconde: en el Buscador por un `DataTrigger`
sobre `IsTinyZoom` puesto **último** en los triggers, para que gane sobre hover y selección; en el
editor queda solo la cantidad. Es el mismo criterio que la barra de ventana en modo compacto: a
menos espacio, se va lo que tiene otro camino.

**Buscador y Mazos abren juntos** ([ADR-052](../DECISIONS.md#adr-052)): `DeckWindow.OpenWorkspace`
abre el editor y después un Buscador, en ese orden para que el Buscador quede al frente. El editor es
**único**; de Buscadores puede haber varios (`Ctrl+N`). Los `+`/`−` escriben contra el mazo activo y
el editor es el único lugar donde se lo ve, así que "Buscador sin editor" es un estado que preferimos
que no exista — pero **abrir juntos no es cerrar juntos**: cerrar el editor cierra solo el editor, y el
único dueño de la aplicación sigue siendo `MainWindow`. Por eso en la barra del Buscador **no hay
ningún control** para llegar a Mazos —ya está abierta— y el nombre del mazo activo es una **etiqueta**,
no un botón: que fuera clickeable era confuso porque nada lo delataba salvo el cursor. Queda `Ctrl+D`,
que además la **reabre** en el mismo mazo si se la cerró.

**Sin ningún mazo abierto** (instalación nueva, o se borraron todos): las pestañas Filtros/Stats se
apagan —no hay nada que filtrar ni medir—, el panel explica qué hacer y **el picker se abre solo**,
porque el `＋` vive adentro y crear es la única acción disponible.

**El título del mazo es el selector** (`TitlePicker` + `DeckPickerPopup`). Antes era la pestaña
"Abrir" del panel izquierdo, y ahí competía con los filtros del mazo ya abierto —otra tarea sobre
otro objeto— y colapsar el panel apagaba las dos de una. El popover se lleva el bloque **entero** (
segmentado Mazos \| Listas, buscador por nombre o por carta contenida, orden, clan, estado, `＋` y la
lista) porque un `ComboBox` habría perdido todo eso, y se cierra solo al elegir. Se lee como título —misma
tipografía y tamaño— pero con **borde y `▾` permanentes**: dejarlos solo en hover fue un error, porque
no se leía como control y había que descubrirlo pasando el mouse ([ADR-052](../DECISIONS.md#adr-052)).
Un botón "Abrir mazo…" al lado habría sumado el control que [ADR-050](../DECISIONS.md#adr-050) saca.
Necesita `AutomationProperties.Name` a mano: su contenido es un panel, no un texto, así que sin eso el
control de navegación principal no tiene nombre accesible.

**Paneles ajustables con el mouse.** La columna del riel hace dos cosas: un `GridSplitter`
(`PaneSplitter`) ocupa la altura completa y arrastrarlo mueve el borde del panel; el chevron
(`PaneRail`) quedó como un **nudo de 62 px centrado** encima, y sigue colapsando de un click. Antes la
píldora ocupaba toda la columna y no dejaba dónde agarrar. Arrastrar por debajo de **180 DIP**
colapsa el panel en vez de dejarlo inservible —empujar el borde hasta el fondo es justo lo que pide
un colapso— y el ancho elegido se recuerda para cuando se lo reabra. Al soltar, la columna central se
devuelve a `*`: el `GridSplitter` la convierte a píxeles fijos y con eso la ventana dejaba de
repartir bien el ancho al redimensionarse.

**Dos escalones de angostura.** Bajo **1000 DIP** las dos ventanas cierran sus paneles laterales y
**apagan los rieles** (con un tooltip que lo explica): a ese ancho los paneles —270/260 y 405— no
dejan una zona central usable, y un riel que abre algo que no entra es peor que un riel apagado. Cada
ventana recuerda lo que el usuario tenía abierto (`_wantFilters`/`_wantDetail`,
`_wantDecks`/`_wantSide`) y lo restaura al agrandarse.

Bajo **900 DIP** entra el segundo escalón (`ApplyCompactMode`), porque con los paneles ya cerrados
**el piso lo pone la barra de botones**. Se esconde lo accesorio: en el Buscador el bloque del mazo
activo, el resumen de filtros y el chip de impresiones (el zoom ya no: es parte del segmentado de
vista, y sacarlo dejaría el grupo cortado por la mitad); en el editor los conteos con el
"Falta: {slot}" y el orden. Nada de eso es accionable desde la barra y el cuerpo del mazo repite los conteos. Con eso
los mínimos pudieron bajar a **460×460**, que es lo que necesita el 30 % del layout lado a lado.

La segunda fila de la barra del Buscador es un **`WrapPanel`, no un `Grid`**: por debajo de ~1000 DIP
sus grupos no entran en una línea, y bajar a la siguiente se lee mejor que recortarse. Cada grupo es
un `StackPanel`, así que la envoltura corta entre grupos y nunca separa un control de su etiqueta.

**Íconos que no son glifos.** El botón de lado a lado dibuja su ícono con dos `Border` de anchos
desiguales (el 70/30) en vez de usar un carácter: `⫞` (U+2ADE) **no está en Segoe UI** y caía a un
fallback ilegible, y los bloques `▊▎` se fusionan en una mancha. Para cualquier ícono que no sea de los
básicos, dibujarlo ([ADR-052](../DECISIONS.md#adr-052)).

**Lado a lado** — el botón de los dos paneles en la barra del editor (estuvo brevemente en el menú `⋯`,
ver [ADR-052](../DECISIONS.md#adr-052)) acomoda las dos ventanas sobre el área de trabajo:
Buscador al 70 % izquierdo con su panel de detalle cerrado, editor al 30 % derecho en vista Lista y
con los dos paneles cerrados. Si no hay Buscador abierto, lo abre. Es el layout de armar: se busca
en una ventana y se ve el mazo crecer en la otra. El reparto **no es un 70/30 ciego**: se calcula el
ancho del editor primero (acotado por su mínimo) y el Buscador se queda con el resto, así la suma da
siempre el área de trabajo y ninguna ventana se va de pantalla — en 1366 el editor pide 460 y el
Buscador queda en 906.

**Dimensionado de `SearchWindow`/`DeckWindow`:** `Views/WindowSizingFix.cs` las acota al **área de
trabajo** del monitor, en dos momentos que con `WindowStyle=None` fallaban por separado — al
maximizar (responde `WM_GETMINMAXINFO`; sin eso Windows maximiza contra el monitor entero porque el
chrome propio le saca el marco que lo acotaba) y al abrir (el tamaño de diseño puede no
entrar en el área útil y `CenterScreen` centra contra la pantalla completa). Reemplaza
al margen de 7 DIP que antes compensaba el desborde a mano.

**Ventanas en cascada** (`WindowSizingFix.CascadeFrom`): cuando una ventana abre otra —el espacio de
trabajo, `Ctrl+N`, el botón "Buscador" del editor— la nueva se corre 34 DIP en diagonal. Las dos
tienen el mismo tamaño de diseño y las dos se abren centradas, así que si no coincidían **al píxel** y
parecían una sola. Hay que poner `WindowStartupLocation.Manual` **antes** de tocar `Left`/`Top`: con
`CenterScreen`, WPF recalcula la posición al mostrar y pisa lo que se le haya puesto.

> **Trampa de unidades, ya pisada dos veces** (la otra está en [ADR-050](../DECISIONS.md#adr-050)):
> `Window.Left`/`Top` viven en las unidades del escritorio virtual y los rectángulos de monitor de
> Win32 vienen en **píxeles de dispositivo**. Compararlos con dos pantallas a distinta escala da
> resultados sin sentido — la primera versión de la cascada no corría nada porque creía que el
> desplazamiento "no entraba" en pantalla. `CascadeFrom` no valida nada: de que entre se encarga
> `ClampToWorkArea` en `SourceInitialized`, que hace la cuenta en el espacio correcto.

**Aviso de novedades** (`PendingStrip`, en el **header**, bajo los botones) — **siempre** dice algo:
`Hay novedades: …` con punto dorado, o `Todo al día.` apagado ([ADR-054](../DECISIONS.md#adr-054)).
Es solo texto, y va ahí y no en una franja propia porque el lienzo tiene **alto fijo**: una fila nueva
se la come el cuerpo, y la primera versión le recortaba el placeholder de Noticias.

La fila de imágenes resume **todos** los packs (`Imágenes de cartas: N de M packs descargados`,
`· K con novedades` en oro cuando corresponde). Antes mostraba un solo pack —el de la comunidad, con
el nombre fijo en el `.resx`— al lado del botón que abre el listado de los otros: se leía como si
hablara de todos, y con un segundo pack de la comunidad habría seguido nombrando al primero
([ADR-055](../DECISIONS.md#adr-055)).

**No se usan `MessageBox` del sistema** ([ADR-013](../DECISIONS.md#adr-013)). En `MainWindow` los
errores, confirmaciones, Configuración y Packs de imágenes son *overlays* dentro del lienzo escalado;
en Buscador y Mazos, que no tienen overlay propio, van por `Views/Dialogs.cs`
(`Prompt` / `Confirm` / `Ask` / `Info`), ventanas modales sin chrome con el borde y el fondo del tema.
`Confirm` pone el botón destructivo **fuera** del default: para borrar hay que elegirlo y un Enter de
reflejo cancela. `Ask` es su gemelo no destructivo ("¿abro el PDF?"): mismo diálogo de dos botones,
pero el que acepta es el default y no va en rojo.

**Cerrar `MainWindow` cierra la aplicación entera** y por eso confirma primero
([ADR-039](../DECISIONS.md#adr-039)); el mensaje cambia si hay una descarga en curso. Buscador y
Mazos se cierran solos, sin preguntar.

**Convención vigente: code-behind, sin MVVM.** `SearchWindow.xaml.cs` (~1540 líneas) y
`DeckWindow.xaml.cs` (~1010) concentran estado y lógica de presentación; los servicios de dominio no
conocen la UI. No está registrado como decisión formal: si alguna vez se revisa, es una discusión
abierta, no una regla.

---

## Tema

`Themes/Theme.xaml` es el `ResourceDictionary` app-wide y la **fuente de verdad** de la paleta:
pergamino cálido (naranja), rojo lacado para acción/selección, oro decorativo, sin modo claro.
Incluye los estilos de control (`PrimaryButton`, `GhostButton`, `InvertedTextBox`, `BaseCardRadio`,
`LProgressBar`) y una **ScrollBar temática** (antes quedaba la gris nativa de WPF).

Tokens vigentes (`src/Themes/Theme.xaml`):

| Grupo | Claves |
|---|---|
| Pergamino (degradados) | `ParchHiColor #CF9243` · `ParchMidColor #A85F2B` · `ParchDarkColor #6B391B` · `ParchDeepColor #3F2210` |
| Superficies | `Surface #2A1710` · `SurfaceGlass #DB22130B` · `SurfaceGlassSoft #BD28170D` · `BackgroundScrim #66140A05` |
| Rojo (acción) | `Red #7D1F1C` · `RedHi #9A2A26` · `RedLo #5C1412` · `RedAccent #C8161C` |
| Oro (decorativo) | `Gold #C9A961` · `GoldSoft #B1924F` · `GoldDim #7D6638` |
| Texto | `Text #F5E6C8` · `TextDim #CDAE84` · `TextFaint #9A7C58` · `ErrorText #EC6A5E` |
| Bordes | `Outline #5A3A22` · `OutlineSoft #3AC9A961` |
| Inputs (invertidos) | `InputText #2C1810` · `InputPlace #937A52` · `InputBorder #B89A63` |
| Fuentes | `BrushFont` = L5RTitles (embebida) · `BodyFont` = Segoe UI |

La [guía de estilos](../design/style-guide.md) es la de la **app Android**: sirve como criterio
(qué es acento, qué es decorativo, jerarquía tipográfica), pero sus valores no son los de acá.

`Views/CardUiStyles.xaml` agrega los estilos compartidos entre Buscador y editor de Mazos (chips,
checkboxes, celdas de carta).

**Taxonomía de controles** ([ADR-035](../DECISIONS.md#adr-035)): cada rol tiene su forma, y la
píldora quedó reservada al único rol que le corresponde. Todos viven en `CardUiStyles.xaml`:

| Rol | Estilo | Dónde |
|---|---|---|
| Filtro on/off | `FilterChip` (píldora, rojo = activo) | quick filters del Buscador |
| Ídem, etiqueta larga y subordinada | `FilterChipSmall` | "Ver todas las impresiones" |
| Selección exclusiva | `SegmentLeft` + `SegmentRight` (bloque único con divisoria) | Lista \| Grilla, Mazos \| Listas |
| Acción con estado en el glifo | `IconToggle` (sin relleno al activarse) | dirección de orden `↑`/`↓` |
| Colapsar panel lateral | `PaneRail` (riel de altura completa entre panel y centro) | ambas ventanas |
| Cambiar contenido del panel | `PanelTab` (pestaña adosada sobre una línea base) | Carta \| Stats (solo Mazos; el Buscador dejó de tener pestañas, [ADR-042](../DECISIONS.md#adr-042)) |
| Selector que convive con toggles | `ChipCombo` (mismo template del ComboBox, paleta oscura) | orden, conjunto activo, mazo activo |
| Acción puntual de barra | `IconButton` (círculo de 26 con glifo) | nueva lista `＋`, borrar lista `🗑`, zoom `－`/`＋`, lado a lado `⫞`, ayuda `?` |
| Acción sobre la carta del detalle | `DetailActionBtn` (píldora chata con glifo + etiqueta) | `+ Mazo`, `☆ Favorita`, `≡ A la lista`… |
| Acción dentro de un popup | `MenuRow` (ancho completo, texto a la izquierda, hover rojo) | menú `⋯` del editor de Mazos, menú de carta y de etiqueta |
| Elegir un color | `ColorSwatch` (círculo de 22; el elegido lleva borde dorado grueso, no un tilde) | paleta del menú de etiqueta |
| Elegir un valor de una escala corta | tira de `FilterChip` angostos, el vigente tildado | ajuste de oro (−3 … +3) del menú de carta |

**Click derecho: copiar en el Buscador, menú en el editor.** En el Buscador copia el arte directo. En
el editor de Mazos abre el **menú de la carta**: **Copiar imagen** primero y siempre —también en la
vista Lista—, después el **ajuste de oro** si ese modo está prendido ([ADR-059](../DECISIONS.md#adr-059))
y al pie sus **etiquetas** con `＋ Etiqueta nueva…` ([ADR-057](../DECISIONS.md#adr-057)). Copiar
encabeza por ser lo único del menú que no depende del mazo: al final se movía de lugar según cuántas
etiquetas hubiera. En las dos, `CardImageClipboard` **re-decodifica el archivo a
tamaño completo**: la miniatura de la grilla se decodifica a ~340 px para no reventar la memoria, y
pegar eso en el Discord daría una imagen borrosa. Lo mismo usa la previsualización con Alt.

**Menús contextuales** (`CardMenuPopup`, `TagMenuPopup` del editor) — `Popup` con
`Placement=MousePoint` y `StaysOpen=False`, con filas `MenuRow` armadas en código. No se usa
`ContextMenu` de WPF: su template no sigue el tema y el resto de la aplicación ya resuelve los menús
con popups (el `⋯` de la barra).

**El panel de filtros es un control, no un pedazo de ventana.** `Views/CardFilterPanel.xaml(.cs)` lo
usan el Buscador y el editor de Mazos ([ADR-058](../DECISIONS.md#adr-058)). Merge su propio
`CardUiStyles.xaml`: los estilos no viven en `App.xaml`, así que un control que se instancia en dos
ventanas tiene que traerlos. Expone `Changed` (toggles: acción discreta) y `TextTyped` (ráfaga de
teclas) por separado, para que cada dueño decida su debounce.

**Los chips con contenido compuesto necesitan `AutomationProperties.Name`.** Un chip de etiqueta es
un punto de color + un `TextBlock`, o sea que su contenido no es texto y el control se queda sin
nombre accesible. Mismo caso que el selector de mazo, que es un panel y no un string.

**Previsualización con Alt** (`HoverPreview`, en **ambas** ventanas) — `Popup` posicionado a mano por
`HoverPreviewPlacer` en `Placement=Absolute`. **No** se usa `Placement=Mouse`: WPF resuelve el desborde
*volteando* el popup al otro lado del cursor, y contra el borde derecho eso lo deja justo debajo del
mouse → tapa la celda → `MouseLeave` → cierra → reabre → parpadeo infinito. El invariante que hay que
sostener no es "que entre" sino que **el cursor nunca quede adentro**, y el volteo corre el popup su
ancho completo más el espacio en vez de espejar el offset. El área de trabajo se pide al monitor donde
está el cursor (`SystemParameters.WorkArea` solo conoce el primario). La geometría es una función pura
y el selftest la barre en toda la pantalla ([ADR-052](../DECISIONS.md#adr-052)). Antes: offset
26,26: si quedara bajo el cursor taparía la celda, el `MouseLeave` lo cerraría y se abriría en loop.
`IsHitTestVisible=False` — es para mirar, no se clickea. El modificador se lee en el `MouseMove` y no
por `KeyDown`: en WPF Alt llega como `Key.System` y compite con la activación de menús.

**Estado atenuado** (carta recortada del editor, [ADR-046](../DECISIONS.md#adr-046)): `Opacity`
0.42 sobre la fila o la celda entera, nombre y cantidad en `TextDim`, sufijo "· recortada" y el `−`
apagado. No hay estilo nuevo: es el mismo control con menos presencia, que es lo que significa.

**Animación sobre un cuerpo que se reconstruye entero.** `RebuildDeckBody` rehace el árbol visual en
cada mutación, así que no hay elemento viejo que animar. En vez de diffear, se anota qué carta cambió
(`_animCardId` + `_anim`) y **la fila nueva arranca animándose desde el estado anterior**: recortar
entra con `Opacity` 1 → 0.42, rescatar al revés, 220 ms con `CubicEase`/`EaseOut` y `FillBehavior.Stop`
para que quede en el valor local. Se consume una sola vez, así que el resto del cuerpo aparece sin
animar y no hay parpadeo general.

**Selector de dos niveles** (conjunto activo del Buscador): un `ComboBox` común cuyos ítems incluyen
**acciones** —`Usar mazo…` y `← Volver a listas`— que no seleccionan nada: cambian el `ItemsSource` y
vuelven a abrir el desplegable (`IsDropDownOpen` en `BeginInvoke`, para que el reabrir no pelee con
el cierre del click). Sale más barato que un menú jerárquico y no rompe el teclado: las listas, que
son lo que se elige a diario, quedan a un click; los mazos, que suelen ser muchos, a dos.

`FilterChip`, `SegmentTemplate` e `IconToggle` tienen trigger de `IsEnabled=False` (opacidad 0.4 +
cursor `Arrow`): sin él, un chip deshabilitado se veía idéntico a uno activo.

El template del `ComboBox` toma fondo, borde, color de texto y `CornerRadius` (en `Tag`) de
**propiedades**, no de recursos fijos: por eso `ChipCombo` lo reusa entero cambiando solo la paleta.
Los combos que **sí** son campos de entrada (keywords, texto de carta, filtro de mazo) siguen con el
pergamino claro del estilo implícito.

Fuentes embebidas: `l5rtitles.ttf` (marca) y `playfair_display.ttf`. Fondos, logos e íconos viven en
`src/Assets/` como recursos WPF (`pack://application:,,,/Assets/...`). Las fuentes de origen y los
PNG del diseño están en [../things/](../things/).

---

## Texto de carta

`Views/CardTextRenderer.cs` convierte el HTML acotado de las cartas (`<b>`, `<i>`, `<br>`, bullets) a
`Inline`s de WPF. **Nunca** se usa un navegador embebido: sería convertir contenido descargado en
superficie de ejecución ([../design/seguridad.md](../design/seguridad.md)).

`Views/CardDetailPanel.xaml(.cs)` es el detalle compartido entre Buscador y editor (arte, texto
renderizado, etiquetas UNIQUE/BANNED data-driven de `rules.json`, acciones de favorita/oculta/lista).
La imagen escala con `Viewbox` si el panel está angosto. `CardImageResolver` (dentro del mismo
archivo) implementa la cadena de fallback de imágenes descrita en [buscador.md](buscador.md).
**Copiar imagen** manda al portapapeles el bitmap ya cargado (no vuelve a leer el disco: el panel es
100 % lectura) y se deshabilita si la imagen no se resolvió; si el portapapeles está tomado por otro
proceso el error se reporta en el panel y no se propaga.

**El arte se dibuja como `ImageBrush` sobre un `Border` redondeado, no como `Image`**
([ADR-037](../DECISIONS.md#adr-037)): las cartas de L5R son escaneos **rectangulares** de cartas con
esquinas redondeadas, así que sin recortar asoman cuatro puntitas blancas. El marco del color del
clan pasó a ser el *placeholder*: solo se dibuja cuando no hay imagen. Mismo criterio en las celdas
de grilla de las dos ventanas, donde además el borde vuelve en hover y selección.

`Views/Dialogs.cs` reúne los diálogos chicos de las dos ventanas grandes; vivía como un `Prompt`
privado dentro de `DeckWindow` hasta que el Buscador necesitó lo mismo para crear listas.

---

## i18n

**Todos** los textos de UI viven en `src/Resources/Strings.resx` (327 claves, español, cultura
neutral). En XAML se usan con `{loc:Loc Clave}` (`Resources/LocExtension.cs`) y en C# con
`Strings.Get` / `Strings.Format` (`Resources/Strings.cs`, un `ResourceManager`).

Para agregar otro idioma: crear `Strings.<cultura>.resx` con las mismas claves y setear
`CurrentUICulture`. Sin tocar código ([ADR-012](../DECISIONS.md#adr-012)).

Convención: **texto nuevo en la UI ⇒ clave nueva en el resx**, nunca un literal en XAML/C#.
Los nombres amigables de los packs de imágenes salen de claves `Pack_<id>` (fallback: el id crudo).

---

## Decisiones que aplican

[ADR-012](../DECISIONS.md#adr-012) i18n total ·
[ADR-013](../DECISIONS.md#adr-013) ventana fija + overlays ·
[ADR-019](../DECISIONS.md#adr-019) UX desktop en el Buscador ·
[ADR-001](../DECISIONS.md#adr-001) sin WebView.
