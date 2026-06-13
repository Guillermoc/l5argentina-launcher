# Guía de Estilos — L5Argentina

Referencia visual de la app Android para construir una app de escritorio con apariencia equivalente.
Estética: **pergamino oscuro / Rokugán feudal japonés** — fondo marrón muy oscuro, texto color marfil cálido, acentos en rojo lacado y oro.

---

## 1. Paleta de colores

Todos los valores son `#AARRGGBB` o `#RRGGBB` (alfa indicado aparte cuando aplica).

### Base (paleta primaria)

| Rol | Hex | Uso |
|---|---|---|
| `BackgroundDark` | `#1B0A04` | Fondo principal de toda la app (marrón casi negro) |
| `TextPrimary` | `#F8E6C1` | Texto principal, títulos (marfil/pergamino cálido) |
| `TextSecondary` | `#BFA98A` | Texto secundario, metadatos, subtítulos (marrón claro apagado) |
| `AccentRed` | `#7B1C1C` | Acento principal: botones, selección, contadores activos (rojo lacado) |
| `AccentRedLight` | `#9E2E2E` | Hover / parte alta de degradados |
| `AccentRedDark` | `#5A1414` | Contenedor presionado / parte baja de degradados |

### Superficies

| Rol | Hex | Uso |
|---|---|---|
| `SurfaceDark` | `#2A1008` | Tarjetas, paneles, barras inferiores |
| `SurfaceVariant` | `#3A1A0C` | Superficies elevadas, chips no seleccionados, toggles |
| `OutlineColor` | `#5A3020` | Bordes hairline, divisores, contornos de inputs |

### Inputs (campos de texto)

Los campos de entrada **invierten** el esquema: fondo claro tipo pergamino con texto oscuro.

| Rol | Hex | Uso |
|---|---|---|
| `InputBackground` | `#F8E6C1` | Fondo de campos de búsqueda/texto (igual que TextPrimary) |
| `InputPlaceholder` | `#8A6A4A` | Placeholder, texto tenue dentro del input (marrón cálido) |

### Acentos en oro (decorativo)

| Rol | Hex | Uso |
|---|---|---|
| `GoldAccent` | `#C9A961` | Encabezados de sección, elementos ornamentales |
| `GoldDim` | `#7A6432` | Oro apagado para detalles secundarios |
| `HighlightGold` | `#FFD700` | Realce intenso (etiquetas de estado destacadas) |

### Estado / utilidad

| Rol | Hex | Uso |
|---|---|---|
| `DisabledText` | `#7A6A5A` | Texto/íconos deshabilitados |
| `BannedRed` | `#FF0000` | Color de error, cartas baneadas |
| `DangerRed` | `#E57373` | Acciones destructivas (borrar, mantenimiento) |
| `White` | `#FFFFFF` | onError, raros realces |
| `Black` | `#000000` | Overlays de etiqueta sobre imágenes |

### Colores de clan (acentos por facción)

Extraídos de los blasones (*mon*) de cada clan. Se usan como borde/realce de carta y filtros.

| Clan | Hex |
|---|---|
| Crab (Cangrejo) | `#55637D` |
| Crane (Grulla) | `#A5C8DD` |
| Dragon (Dragón) | `#5E8E80` |
| Lion (León) | `#CBB465` |
| Mantis | `#4A7B3F` |
| Phoenix (Fénix) | `#CB8D59` |
| Scorpion (Escorpión) | `#9A4639` |
| Spider (Araña) | `#4A4A4A` |
| Unicorn (Unicornio) | `#7A6299` |

> Sin clan → transparente (sin realce).

---

## 2. Tipografía

### Fuentes

- **`L5RTitles`** (fuente custom, `l5rtitles.ttf`): títulos, nombres de carta, etiquetas y todo el *chrome* de UI (botones, headers, tabs, nombres de grupo). Da el carácter de la marca. Solo peso Normal.
- **`Playfair Display`** (`playfair_display.ttf`): serif disponible para texto ornamental/editorial.
- **Sans-serif del sistema** (`FontFamily.Default`): texto de cuerpo, metadatos y habilidades de carta — prioriza legibilidad.

> En escritorio: usar la `.ttf` de L5RTitles para títulos/UI; un sans-serif neutro (p. ej. el del sistema) para cuerpo. Las fuentes están en `app/src/main/res/font/`.

### Escala tipográfica

| Estilo | Fuente | Tamaño / interlínea | Color | Uso |
|---|---|---|---|---|
| `headlineMedium` | L5RTitles | 30 / 34 | TextPrimary | Títulos de pantalla ("Buscador", "Mazos") |
| `titleLarge` | L5RTitles | 18 / 24 | TextPrimary | Nombres de carta en lista |
| `titleMedium` | L5RTitles | 16 / 22 | TextPrimary | Encabezados de sección, nombres de grupo |
| `labelMedium` | L5RTitles | 18 / 20 | TextPrimary | Labels de botones |
| `labelSmall` | L5RTitles | 16 / 20 | TextSecondary | Texto pequeño de UI |
| `bodyMedium` | Sans-serif | 14 / 20 | TextSecondary | Tipo de carta, metadatos |
| `bodySmall` | Sans-serif | 13 / 18 | TextPrimary | Texto de habilidad de carta |

Tamaños en `sp` (Android); en escritorio mapear a `px`/`pt` 1:1 como punto de partida.

---

## 3. Esquema de tema (mapeo Material)

La app usa un **único esquema oscuro fijo** (sin modo claro):

```
primary            = AccentRed       (#7B1C1C)
onPrimary          = TextPrimary     (#F8E6C1)
primaryContainer   = AccentRedDark   (#5A1414)
secondary          = AccentRedLight  (#9E2E2E)
background         = BackgroundDark  (#1B0A04)
onBackground       = TextPrimary
surface            = SurfaceDark     (#2A1008)
onSurface          = TextPrimary
surfaceVariant     = SurfaceVariant  (#3A1A0C)
onSurfaceVariant   = TextSecondary   (#BFA98A)
outline            = OutlineColor    (#5A3020)
error              = BannedRed        (#FF0000)
onError            = White
```

---

## 4. Formas, bordes y elevación

- **Esquinas redondeadas** ubicuas. Radios usados según el componente:
  - Items de carta / chips pequeños: **4–8 dp**
  - Tarjetas y paneles: **8–14 dp**
  - Visor de carta / contenedores grandes: **18 dp**
  - Pastillas de contador (stepper +/−): **30 dp** (cápsula)
- **Bordes hairline** de 1 dp en `OutlineColor` para divisores y contornos de inputs.
- **Borde de realce de carta:** cuando una carta tiene estado/clan, se dibuja `border(2dp, color)` + fondo `color` al ~12% de alfa + padding interno antes del recorte.
- **Sin sombras Material fuertes**: la profundidad se logra con variaciones de tono (Dark → Variant) y bordes, no con elevación pronunciada.

---

## 5. Texturas de fondo

La app usa imágenes de pergamino como fondo en ciertas superficies (no solo color plano):

- `background_drawer` — textura usada en modales full-screen (FilterModal) y el drawer, renderizada con recorte tipo *crop*.
- Pantallas ornamentales (Reglas) dibujan gradientes sobre canvas encima del fondo.

> En escritorio: emular con una textura de papel/pergamino oscuro tileada o un degradado sutil sobre `BackgroundDark`, manteniendo el color base si no se dispone del asset.

---

## 6. Componentes recurrentes

- **Header de pantalla:** título `headlineMedium` (L5RTitles) a la izquierda/centro + íconos de acción `Outlined` a la derecha, tint `TextPrimary`.
- **Barra de búsqueda:** alto 48 dp, fondo `InputBackground` (pergamino claro), texto oscuro, íconos `Outlined` en `TextPrimary`.
- **Chips / filtros:** seleccionado = fondo `AccentRed`; no seleccionado = `SurfaceVariant` (~45% alfa) con borde `OutlineColor`.
- **Toggles (Switch):** track activo `AccentRed`.
- **Íconos de acción:** estilo *Outlined*, tint `AccentRed` cuando están activos / `TextSecondary` cuando inactivos.
- **Barras inferiores:** fondo `SurfaceDark` al ~82% de alfa, padding ~12/6 dp, sin divisor superior.
- **Contador / stepper:** cápsula glass (`#C6140A06`, borde `OutlineColor`), botón `+` con degradado vertical `AccentRedLight → AccentRed`.
- **Pastilla de estado (banned/restricted):** rombo decorativo 45° + texto en mayúsculas, `Bold`, `letterSpacing` 1–2 sp; fondo `color×0.15` + borde 1 dp del color.
- **Placeholder de imagen:** obligatorio cuando no hay imagen de carta — rectángulo oscuro liso.
- **Acentos en oro:** reservados para encabezados de sección y ornamentos, no para acciones.

---

## 7. Principios de diseño

1. **Oscuro fijo, sin tema claro.** Todo parte de `#1B0A04` con texto marfil `#F8E6C1`.
2. **Cálido, no neutro.** Todos los grises/negros tienen tinte marrón/sepia; evitar grises fríos puros.
3. **Rojo lacado = acción/selección.** El oro es decorativo, nunca interactivo.
4. **Marca por la tipografía.** L5RTitles en todo el chrome; el cuerpo legible va en sans-serif.
5. **Profundidad por tono y borde**, no por sombras.
6. **Color de clan como identidad.** Bordes/realces por facción usando la paleta de clan.
7. **Inputs invertidos** (pergamino claro) para destacar zonas de entrada sobre el fondo oscuro.

---

*Fuente de verdad:* `app/src/main/java/com/guillermoc/l5argentina/ui/theme/` (`Color.kt`, `Type.kt`, `Theme.kt`).
