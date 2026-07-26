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
| `SearchWindow` | 1470×840, `Min` 1060×640, redimensionable/maximizable | Chrome propio (`CaptionHeight=35`). `Ctrl+N` abre otra instancia sobre el mismo catálogo. |
| `DeckWindow` | 1470×840, `Min` 1060×640 | Mismo chrome. `DeckWindow.Open(...)` reusa la ventana si ya está abierta. |

**No se usan `MessageBox` del sistema.** Errores, confirmaciones, Configuración y Packs de imágenes
son *overlays* dentro de la ventana, con el tema de la app ([ADR-013](../DECISIONS.md#adr-013)).

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

---

## i18n

**Todos** los textos de UI viven en `src/Resources/Strings.resx` (311 claves, español, cultura
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
