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
| `SearchWindow` | 1323×798, `Min` 1060×640, redimensionable/maximizable | Chrome propio (`CaptionHeight=35`). `Ctrl+N` abre otra instancia sobre el mismo catálogo. |
| `DeckWindow` | 1323×798, `Min` 1060×640 | Mismo chrome. `DeckWindow.Open(...)` reusa la ventana si ya está abierta. |

**Dimensionado de `SearchWindow`/`DeckWindow`:** `Views/WindowSizingFix.cs` las acota al **área de
trabajo** del monitor, en dos momentos que con `WindowStyle=None` fallaban por separado — al
maximizar (responde `WM_GETMINMAXINFO`; sin eso Windows maximiza contra el monitor entero porque el
chrome propio le saca el marco que lo acotaba) y al abrir (el tamaño de diseño puede no
entrar en el área útil y `CenterScreen` centra contra la pantalla completa). Reemplaza
al margen de 7 DIP que antes compensaba el desborde a mano.

**No se usan `MessageBox` del sistema** ([ADR-013](../DECISIONS.md#adr-013)). En `MainWindow` los
errores, confirmaciones, Configuración y Packs de imágenes son *overlays* dentro del lienzo escalado;
en Buscador y Mazos, que no tienen overlay propio, van por `Views/Dialogs.cs`
(`Prompt` / `Confirm` / `Info`), ventanas modales sin chrome con el borde y el fondo del tema.
`Confirm` pone el botón destructivo **fuera** del default: para borrar hay que elegirlo y un Enter de
reflejo cancela.

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
| Selección exclusiva | `SegmentLeft` + `SegmentRight` (bloque único con divisoria) | Lista \| Grilla |
| Acción con estado en el glifo | `IconToggle` (sin relleno al activarse) | dirección de orden `↑`/`↓` |
| Colapsar panel lateral | `PaneRail` (riel de altura completa entre panel y centro) | ambas ventanas |
| Cambiar contenido del panel | `PanelTab` (pestaña adosada sobre una línea base) | Carta \| Stats (solo Mazos; el Buscador dejó de tener pestañas, [ADR-042](../DECISIONS.md#adr-042)) |
| Selector que convive con toggles | `ChipCombo` (mismo template del ComboBox, paleta oscura) | orden, lista activa, mazo activo |
| Acción puntual de barra | `IconButton` (círculo de 26 con glifo) | nueva lista `+`, borrar lista `🗑` |
| Acción sobre la carta del detalle | `DetailActionBtn` (píldora chata con glifo + etiqueta) | `+ Mazo`, `☆ Favorita`, `≡ A la lista`… |
| Acción dentro de un popup | `MenuRow` (ancho completo, texto a la izquierda, hover rojo) | menú `⋯` del editor de Mazos |

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
