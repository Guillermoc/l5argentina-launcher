# Handoff — Rediseño visual del L5Argentina Launcher (WPF)

> **Material histórico (2026-06-14).** Es el paquete con el que se hizo el rediseño visual de la
> v0.1.0, no el estado actual: el tema vigente es `src/Themes/Theme.xaml` y la guía de marca,
> [`../style-guide.md`](../style-guide.md). Se conserva porque el mockup HTML sigue siendo la
> referencia de cómo tienen que verse las pantallas y los estados.

Capa visual del lanzador de *Sun and Moon*. El MVP ya funciona; esto es **sólo el XAML/estilos/estados**, no la lógica.

## Archivos

1. **`PROMPT.md`** — el prompt para Claude Code. Leelo primero: contexto, layout, los 8 estados, modales, componentes, qué NO hacer, criterio de aceptación.
2. **`Theme.xaml`** — `ResourceDictionary` listo para pegar: tokens de color, fuentes y estilos de control (`PrimaryButton`, `GhostButton`, `InvertedTextBox`, `BaseCardRadio`, `LProgressBar`).
3. **Mockup visual:** [`L5Argentina Launcher - Diseño.html`](<L5Argentina Launcher - Diseño.html>) — canvas con todas las pantallas y estados (Sistema · Ventana principal · Primer arranque · Configuración · Componentes). Abrilo para ver el diseño objetivo.

## Pendientes del lado tuyo

- **Fuente de marca:** registrar el archivo real **L5RTitles** en `/Fonts/` y apuntar `BrushFont` ahí (hoy usa un fallback serif).
- **Logo:** reemplazar el slot `L5x` por el ícono real (PNG/SVG).
- **Texturas:** PNG de papel tileable (~10% opacidad) + sombra/viñeta para el fondo pergamino.

## Decisiones

- Estética: **pergamino cálido (naranja)** del MVP, refinado. Sin modo claro.
- Ventana **fija 1080×720**, centrada, no redimensionable.
- Inputs **invertidos** (pergamino claro, texto oscuro).
- Title bar custom es **opcional** (la nativa de Windows sirve).
