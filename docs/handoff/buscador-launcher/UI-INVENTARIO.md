# UI-INVENTARIO — Pantalla Buscador

Descripción de la pantalla tal como está en el commit `a9a48f1` (SearchScreen.kt + componentes).
Es referencia de comportamiento, no de estética: el launcher WPF define su propio layout.

## Layout (de arriba a abajo)

1. **Barra de búsqueda** (48dp, ícono lupa, X para borrar) + ícono de filtros (embudo) a la
   derecha que abre el FilterModal. La barra soporta los prefijos de operador
   (`k.` `c.` `t.` `tx.` — ver SPEC-BUSQUEDA-TEXTO.md §1).
2. **Fila contador + resumen**: `"N resultados · <resumen de filtros activos>"` — el contador
   siempre visible y en rojo acento; el resumen es un compacto de los criterios activos
   (`summaryOf`, `src/SearchViewModel.kt:579-601`; p. ej. `personality/holding · crab · F≥5 ·
   incl.baneadas`), tap → diálogo con el texto completo. Si hay filtros activos
   (`!criteria.isEmpty`), aparece a la derecha un ícono ↻ = **limpiar rápido** (resetea criteria
   preservando el nombre tipeado — SPEC-FILTROS.md §Flujo).
3. **Sugerencias inline** (cuando la barra tiene foco): lista de hasta 8 — en modo NAME con ≥2
   chars muestra `nombre + tipo en itálica + set` y marca "Bloqueado por filtros" si la carta no
   está en el resultado actual; en modos operador muestra los valores que matchean.
4. **Barra quick-filter** (3 tercios): **"Ver ocultas"** (`includeHidden`) · **"Solo favoritas"**
   (`onlyFavorites`) · **"Solo lista"** (toggle en memoria + ícono para reabrir el selector de
   lista). Íconos con tint rojo cuando activos.
5. **Zona de resultados** — dos vistas superpuestas:
   - **Carousel** (default): carta central grande (prev/next asomando) + tira de miniaturas
     scrubbable abajo. Swipe-up desde la tira levanta la grilla.
   - **Grilla** (overlay animado): `LazyVerticalGrid` con zoom de 1 a 5 columnas
     (`gridZoomLevel` 0–4, default 4=5 col), badge de cantidad en mazo, ★ favorita, pastilla de
     estado (BANNED etc.) compacta. Se descarta con swipe-down y sincroniza el índice del
     carousel con el scroll.
   - Ambas consumen **`displayCards`** (orden por Set + expansión por impresión aplicados),
     nunca `results` crudo.
6. **Barra inferior**: label del orden actual + flecha asc/desc (abre el **SortModal**) ·
   3 pastillas = 3 **tabs de búsqueda independientes** (cada uno con criteria, barra, scroll y
   visor propios; el resultado del tab se cachea para cambiar sin flash).

## Qué pasa al tocar una carta

- **Tap en la carta central del carousel / celda de grilla** → abre **CardViewerModal** por
  **índice** dentro de `displayCards` (no por id — con "Ver todas las impresiones" los ids se
  repiten).
- **Visor** (full-screen): imagen grande con pinch-to-zoom; zonas de tap invisibles 25%
  izquierda/derecha = carta anterior/siguiente; barra superior con acciones
  [copiar imagen] | [👁 ocultar] [★ favorita] [🔖 lista (si hay lista activa)] y [X];
  pastilla de estado (labelText/labelColor) sobre la imagen; panel de **note** (ruling) de altura
  fija; stepper − qty + para el mazo activo (respeta maxCopies/banned vía repositorio de mazos);
  marco de color si `highlightColor`.
- **Barra de acciones del carousel** (columna flotante a la derecha de la carta central):
  ocultar · favorita · lista (si hay) · toggle de **errata/nota** (overlay scrolleable sobre la
  carta cuando tiene `note`) · agregar/quitar del mazo activo con contador `qty/max`.

## Ordenamiento

- **Campos** (SortModal, radio): Nombre (default) · Costo · Fuerza · Chi · Honor personal ·
  Set. Dirección asc (default) / desc. Semántica exacta (NULLs al final, desempates, orden
  canónico de sets) en SPEC-FILTROS.md §15.
- El SortModal incluye además el switch **"Ver todas las impresiones"** (`expandPrintings`):
  expande cada carta en una entrada por impresión; solo visualización.
- El ícono/label de orden se pinta en rojo cuando el orden difiere del default (campo ≠ NAME o
  descendente).

## Comportamientos no obvios

| Comportamiento | Detalle |
|---|---|
| Persistencia de filtros | **Ninguna entre sesiones** (estado en ViewModel). Único persistente: el preset manual del modal (guardar/cargar, 1 slot, DataStore) y el idioma de la app. |
| 3 tabs | Estados 100% independientes; cambiar de tab restaura scroll/carousel/grid y muestra el último resultado cacheado al instante. |
| Aplicar filtros | Precarga el resultado antes de cerrar el modal (sin flash); resetea posición del carousel; `searchEpoch++` fuerza re-anclaje de las vistas. |
| Ocultar carta | Tiene **gracia de 2 s** (`pendingHideIds`): el ícono queda marcado y la carta se oculta recién a los 2 s; tocar de nuevo cancela. Al consumarse, cierra el visor. |
| Contador de resultados | Cuenta `results` (post filtro de lista), no las entradas expandidas por impresión. |
| Sin pull-to-refresh | La actualización de datos es por sync (pantalla aparte), no desde el Buscador. |
| Sin paginación | La query trae todo el resultado; el render es lazy (grid/carousel virtualizados). |
| Agregar al mazo | Los botones +/− operan sobre el **mazo activo** global; sin mazo activo los botones no hacen nada (`addCard` retorna temprano). Fuera de alcance del launcher (Mazos excluido) pero explica íconos de la UI. |
| Reset de errata | El overlay de errata/nota del carousel se re-muestra al cambiar de carta (`showErrata` se resetea). |

## Estado por tab (referencia completa)

`SearchTabState` (`src/SearchViewModel.kt:51-69`): criteria, barText, filterModal (draft),
cardViewer (índice), scrollIndex/Offset (carousel-tira), gridScrollIndex/Offset, gridZoomLevel,
carouselIndex, soloListActive, selectedListId/Name, expandPrintings, searchEpoch.
