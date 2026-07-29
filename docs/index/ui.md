# Índice — UI, tema e i18n

Estado actual de la capa visual. Dominio: `src/MainWindow.xaml(.cs)`, `src/Views/*`,
`src/Themes/Theme.xaml`, `src/Resources/*`, `src/Assets/*`.

Guía visual (tokens, paleta, tipografía): [../design/style-guide.md](../design/style-guide.md).
Handoff original del rediseño: [../design/visual/](../design/visual/).

---

## Ventanas

| Ventana | Tamaño | Notas |
|---|---|---|
| `MainWindow` | **810×540 fija** (3:2), `ResizeMode=NoResize`, `WindowStyle=None` + `WindowChrome` propio | El diseño se **compone a 1080×720** y se escala con un `Viewbox Stretch=Uniform` (810×540 = 75 %). Todo lo que se agregue va en el lienzo de 1080×720. |
| `FirstRunWindow` | diálogo modal | Detección de instalación + captura de la base Original. Devuelve `true`/`false` a `App`. |
| `SearchWindow` | 1323×798, `Min` **460×460**, redimensionable/maximizable | Chrome propio (`CaptionHeight=35`). `Ctrl+N` abre otra instancia sobre el mismo catálogo. |
| `DeckWindow` | 1323×798, `Min` **460×460** | Mismo chrome. `DeckWindow.Open(...)` reusa la ventana si ya está abierta. El panel izquierdo tiene **dos pestañas** (`PanelTab`, el mismo idioma que Carta \| Stats): **Abrir** —segmentado Mazos \| Listas, buscador y selectores de orden/clan/estado— y **Filtros** sobre las cartas del mazo abierto. Elegir qué abrir y filtrar lo abierto son tareas distintas y competían por el mismo espacio. En modo lista desaparecen `−`/qty/`+`, los conteos Dynasty/Fate, el estado de armado y el toggle de recortadas. |
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

**Lado a lado** — la fila **Acomodar lado a lado** del menú `⋯` del editor acomoda las dos ventanas sobre el área de trabajo:
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
| Acción dentro de un popup | `MenuRow` (ancho completo, texto a la izquierda, hover rojo) | menú `⋯` del editor de Mazos |

**Copiar con click derecho** — sobre cualquier celda de grilla (Buscador y editor).
`CardImageClipboard` **re-decodifica el archivo a tamaño completo**: la miniatura de la grilla se
decodifica a ~340 px para no reventar la memoria, y pegar eso en el Discord daría una imagen borrosa.
Lo mismo usa la previsualización con Alt.

**Previsualización con Alt** (`HoverPreview`, en **ambas** ventanas) — `Popup` con `Placement=Mouse` y offset
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
