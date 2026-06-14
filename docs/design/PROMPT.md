# Prompt para Claude Code — Rediseño visual del L5Argentina Launcher (WPF)

> **Antes de codear, mirá el mockup:** `L5Argentina Launcher — Diseño.html` (canvas con todas las pantallas y estados).
> **Tokens y estilos listos para pegar:** `Theme.xaml` (este folder).
>
> El **MVP ya funciona** — toda la lógica (chequeo de manifest, descarga, aplicar base, backup, lanzar el juego) está implementada y **no se toca**. Esto es **sólo la capa visual / XAML**: estilos, plantillas de control, layout y estados visuales.

---

## Contexto

`L5Argentina Launcher` es una app de escritorio **WPF (.NET)**, **una sola ventana**, lanzador del juego *Sun and Moon* (Legend of the Five Rings). Es la app hermana de la app Android L5Argentina y comparte su estética: **Rokugán / pergamino**, rojo lacado para acciones, oro decorativo, tipografía de marca tipo pincel. **Sin modo claro.**

El rediseño mantiene la dirección **pergamino cálido (naranja)** del MVP actual, pero más prolijo: rampa de fondo más rica, viñeteado en los bordes, textura de papel sutil, paneles marrón translúcido con bordes oro finos, jerarquía tipográfica clara.

---

## Reglas de diseño (las que mandan)

1. **Ventana fija, no redimensionable, centrada en pantalla.** Tamaño cliente recomendado **1080 × 720**. (`ResizeMode="NoResize"`, `WindowStartupLocation="CenterScreen"`.)
2. **Fondo pergamino** (`ParchmentBackground` en `Theme.xaml`): rampa radial cálida + textura de papel (PNG tileable a ~10% opacidad) + sombra de follaje arriba-derecha (un PNG suave o un `RadialGradientBrush` oscuro). El viñeteado de bordes se hace con un `Border` overlay con `DropShadowEffect` interno o un PNG de viñeta.
3. **Tipografía:**
   - **Marca / pincel** (`BrushFont`) → título de la app, headers de sección ("Elegí con qué base jugar", "Noticias y torneos"), nombres de tarjeta ("Comunidad", "Original"), **labels de todos los botones**. Hoy es un fallback serif; registrá la fuente real **L5RTitles** en `/Fonts/` y apuntá `BrushFont` ahí (ver `Theme.xaml`).
   - **Body** (`BodyFont` = Segoe UI) → descripciones, rutas, líneas de estado, textos de ayuda, validaciones.
4. **Inputs invertidos:** los campos de texto van en **pergamino claro con texto oscuro** (`InvertedTextBox`), al revés que el resto de la UI. Placeholder tenue en itálica.
5. **Colores:** usá **sólo** los tokens de `Theme.xaml`. Rojo lacado para primario/selección, oro para bordes destacados/estado/decorativo, marfil/tan para texto. No inventes colores nuevos.
6. **Logo:** hay un slot `L5x` (placeholder). Reemplazalo por el ícono real (PNG/SVG) cuando esté.

---

## Layout de la ventana principal (de arriba hacia abajo)

Usá un `Grid` con filas: `Auto` (header), `Auto` (banner), `*` (body), `Auto` (footer), `Auto` (status bar).

1. **Title bar (opcional, custom):** el mockup muestra una barra de título oscura propia (logo + "L5Argentina Launcher" + min/max/close). Requiere `WindowStyle="None"` + `WindowChrome`. Si preferís cero trabajo extra, dejá la barra nativa de Windows — el diseño no depende de la custom. **Es opcional.**
2. **Header:** logo (38px) + título "L5Argentina Launcher" (pincel, ~30px) sobre subtítulo "Legend of the Five Rings · Sun and Moon" (body, tan). A la derecha, dos `GhostButton`: **Buscar actualizaciones** y **Configuración**.
3. **Banner de actualización (condicional):** sólo visible si hay versión nueva del launcher. Borde oro, fondo rojo oscuro, punto dorado + texto + `GhostButton` "Ir a descargas". `Visibility` bindeado a `HasLauncherUpdate`.
4. **Selección de base:** header "Elegí con qué base jugar" + dos `BaseCardRadio` lado a lado (`GroupName` compartido, una u otra):
   - **Comunidad:** título, bajada "Base curada L5Argentina (s_extended)", "Versión: X.Y.Z", y una **línea de estado** (oro): "Actualización disponible" / "Descargada" / etc.
   - **Original:** título, bajada "Tu base sin nuestras modificaciones", estado "Respaldo local disponible".
   - Seleccionada → rojo lacado + borde oro + radio dorado lleno. No seleccionada → superficie apagada + borde fino. Deshabilitada → atenuada (ver estado ⑥).
5. **Imágenes propias:** panel chico (`InfoPanel`): "Imágenes propias: actualización disponible (v1.0)" / "al día (v1.0)".
6. **Noticias y torneos:** header + panel placeholder con borde **dasheado** ("Próximamente: novedades de la comunidad e info de torneos"). Bloque existe pero es funcionalidad futura.
7. **Footer:** izquierda → ruta de instalación (body chico) + "Launcher v1.0.0"; derecha → botón grande **JUGAR** (`PrimaryButtonLarge`).
8. **Status bar (abajo de todo):** una línea de estado + una `ProgressBar` (`LProgressBar`) que **sólo aparece durante operaciones** (`Visibility` bindeada).

---

## Estados de la ventana principal (variantes a clavar)

Cada estado en el mockup, sección **"Ventana principal"**. Bindealos a propiedades del VM.

| # | Estado | Qué cambia |
|---|--------|-----------|
| ① | **Conectado · al día** (default feliz) | Sin banner. Comunidad seleccionada, estado "Descargada" (oro). Imágenes "al día (v1.0)". JUGAR habilitado. Status: "Listo para jugar." |
| ② | **Conectado · con actualización** | Banner visible. Comunidad estado "Actualización disponible" (oro). Imágenes "actualización disponible". Status oro. |
| ③ | **Cargando · chequeando** | `ProgressBar IsIndeterminate`. Status "Buscando actualizaciones…". **Botones deshabilitados** (ghost + JUGAR). Estado de tarjeta "Comprobando…". |
| ④ | **Descargando · aplicando** | `ProgressBar` con % real. Status "Descargando… X MB de Y MB" / "Aplicando base…" / "Sincronizando imágenes…". Controles deshabilitados. |
| ⑤ | **Sin conexión · offline** | Sin banner. Status oro: "Sin conexión al manifest — modo offline…". Versiones muestran lo **cacheado**. Igual se puede jugar (con lo aplicado o con Original). |
| ⑥ | **Original no disponible** | Tarjeta Original **deshabilitada/atenuada**: "No disponible (no se capturó backup)". Comunidad sigue seleccionable. |
| ⑦ | **Recién lanzado** | Status oro: "Sun and Moon iniciado. ¡A jugar!". (El botón puede mostrarse en pressed/atenuado un instante.) |
| ⑧ | **Error** | Diálogo de error (overlay) + status en tono error: "Error: la verificación de integridad falló…". |

---

## Primer arranque (ventana modal, sólo la primera vez)

Mockup, sección **"Primer arranque"**. Ventana propia ~**720 × 574**, centrada.

- Logo + título "Primer arranque" + subtítulo.
- Texto: pedir dónde está instalado *Sun and Moon* (y avisar que se guarda backup de la base original).
- Panel "Carpeta de Sun and Moon": `InvertedTextBox` (muestra la ruta o "(sin seleccionar)") + `GhostButton` "Elegir Sun and Moon.exe…".
- **Mensaje de validación** (rojo, sólo si la carpeta no es válida): ícono `!` + texto.
- Footer: `GhostButton` "Cancelar" + `PrimaryButton` **CONTINUAR** (deshabilitado hasta que la carpeta sea válida).
- **Sub-estado advertencia:** si la base ya parece modificada, diálogo "¿Continuar igual?" con **Sí** (primario) / **No** (ghost).

Variantes en el mockup: sin seleccionar (CONTINUAR off) · ruta válida (CONTINUAR on) · ruta inválida (validación) · diálogo de advertencia.

---

## Configuración (modal sobre la ventana principal)

Mockup, sección **"Configuración"**. Modal centrado ~**560** de ancho, con backdrop oscurecido (`rgba(12,7,3,0.6)` + blur) sobre la ventana principal.

- Título "Configuración".
- "Carpeta de Sun and Moon": `InvertedTextBox` con la ruta + `GhostButton` "Cambiar…".
- "URL del manifest": texto de ayuda (body, tan) + `InvertedTextBox` editable + link "Restaurar URL por defecto" (oro, subrayado).
- **Mensaje de validación** (rojo, condicional — ej. URL inválida).
- Footer: `GhostButton` "Cancelar" + `PrimaryButton` **GUARDAR**.

---

## Diálogos del sistema

Mockup, sección **"Componentes" → Diálogos**. Card centrada (~440px), borde según tipo, ícono circular, título (pincel) + body (Segoe UI) + botones a la derecha.

- **Confirmación / advertencia:** borde + ícono **oro**. Ej. "¿Continuar igual?" → Sí / No.
- **Error:** borde + ícono **rojo accent**. Ej. "Error de integridad" → Cerrar / Reintentar.

Implementalos como overlay dentro de la ventana (un `Grid` que ocupa todo, con backdrop semitransparente y la card centrada), no como `MessageBox` del sistema.

---

## Componentes (todos en `Theme.xaml`)

| Componente | Style key | Estados |
|---|---|---|
| Botón primario | `PrimaryButton` / `PrimaryButtonLarge` | normal · hover (más claro) · pressed (más oscuro) · disabled (apagado) |
| Botón ghost | `GhostButton` | normal · hover (borde oro) · disabled |
| Input invertido | `InvertedTextBox` | normal · foco (borde oro) · inválido (borde rojo, vía `Tag="invalid"`) |
| Tarjeta de base | `BaseCardRadio` | seleccionada (rojo+oro) · no seleccionada · deshabilitada |
| Barra de progreso | `LProgressBar` | indeterminada (`IsIndeterminate`) · con % |
| Banner / paneles / diálogos | (markup, ver mockup) | — |

---

## Lo que NO se debe hacer

- ❌ **No tocar la lógica** del launcher (manifest, descarga, backup, aplicar base, lanzar juego). Esto es sólo XAML/estilos/estados.
- ❌ No usar `MessageBox` nativo para advertencia/error/confirmación — usá los diálogos overlay del diseño.
- ❌ No agregar modo claro ni temas alternativos.
- ❌ No inventar colores fuera de los tokens de `Theme.xaml`.
- ❌ No usar emojis como íconos. Glyphs simples (líneas/diamante) o íconos vectoriales.
- ❌ No hacer la ventana redimensionable (rompe el layout fijo).
- ❌ No dejar el input con fondo oscuro: los inputs **van invertidos** (pergamino claro).
- ❌ No mostrar la barra de progreso cuando no hay operación en curso.

---

## Criterio de aceptación

- [ ] Ventana fija 1080×720, centrada, fondo pergamino cálido con textura + viñeteado.
- [ ] Header con logo, título pincel, subtítulo y dos ghost buttons a la derecha.
- [ ] Dos tarjetas de base tipo radio; la seleccionada en rojo lacado con borde oro, la otra apagada.
- [ ] Los 8 estados de la ventana principal se reflejan visualmente (banner, líneas de estado, progreso, controles deshabilitados, tarjeta Original deshabilitada, etc.).
- [ ] Primer arranque: input invertido, validación condicional, CONTINUAR deshabilitado hasta ruta válida, diálogo de advertencia.
- [ ] Configuración: modal con backdrop, los dos campos, link de restaurar URL, validación, GUARDAR/Cancelar.
- [ ] Diálogos de advertencia y error como overlay (no `MessageBox`).
- [ ] Botones con sus 4 estados; inputs con foco/inválido; progreso indeterminado y con %.
- [ ] Todo usa los tokens y estilos de `Theme.xaml`; cero colores hardcodeados sueltos.
- [ ] La fuente de marca queda lista para enchufar L5RTitles (hoy fallback).
