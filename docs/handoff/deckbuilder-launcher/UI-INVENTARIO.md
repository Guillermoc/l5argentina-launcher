# UI-INVENTARIO — Pantallas de mazo

Referencia de comportamiento (no de estética). El launcher WPF define su propio layout.
Fuentes: `src/DecksViewModel.kt`, `src/DeckDetailViewModel.kt`, `DecksScreen.kt`,
`DeckDetailScreen.kt`. Commit `a9a48f1`.

## Pantalla 1 — Lista de mazos (`DecksScreen` / `DecksViewModel`)

- **Header**: barra de búsqueda (filtra mazos por nombre y por cartas que contienen) + botón
  "nuevo mazo" (➕) + filtro por clan. Sugerencias en dos secciones: mazos que matchean / cartas
  que matchean.
- **Toggle Mazos / Listas**: dos secciones (`section_type`); se muestra una a la vez. Mazos y
  listas son universos separados.
- **Grupos de usuario** (carpetas): "Sin Grupo" (expandido) + grupos nombrados (colapsables),
  con drag-and-drop para reordenar y mover mazos entre grupos. Botón "+ Agregar grupo".
- **Tarjeta de mazo**: imagen del stronghold (recorte superior) si el mazo tiene stronghold
  descargado; nombre; subtítulo con clan + conteos `(dynasty/fate)` o total (listas); menú ⋮.
- **Tap en la tarjeta** → **activa** el mazo (no navega).
- **Menú ⋮**: Abrir (→ detalle) · Importar · Exportar · Exportar PDF · — · Renombrar · Editar
  notas · Asignar grupo · — · Eliminar. ⚠️ **No hay "Duplicar".**
- **Crear mazo** (modal): nombre + toggle "es lista" (+ nombre corto de lista si aplica) +
  grupo opcional. Al crear, el mazo **se activa automáticamente**.
- **Export** → copia el texto al portapapeles + Toast.

## Pantalla 2 — Detalle / editor de mazo (`DeckDetailScreen` / `DeckDetailViewModel`)

Ruta `deck_detail/{deckId}` — opera sobre ese mazo (no necesariamente el activo).

- **Header**: back · título `nombre (dynasty/fate)` · botones PDF · **Estadísticas** (BarChart) ·
  ⋮. El menú ⋮: Importar · Exportar · Exportar PDF · [Probar mazo (Goldfish) — solo decks] · — ·
  Renombrar · Editar notas · Asignar grupo · [Activar — si no es el activo] · — · Limpiar
  recortadas · [Cartas huérfanas (N) — si hay] · Eliminar.
- **Quick bar** (colapsable): contadores **Dynasty N/40** y **Fate N/40** (rojo si `<40`) — o
  total para listas — + toggle "Recortadas (N)". Es **el único indicador de validez** y no
  bloquea nada.
- **Grilla de cartas** (`LazyVerticalGrid`, 1–3 columnas, persistidas): agrupada por sección
  Starter/Dynasty/Fate → subtipo, colapsable en 3 niveles. Cada celda (`HalfCardCell`) tiene
  contador `[+][qty/max][−]` y, en col-1, botón eliminar. Bajo Starter aparecen filas
  **"Falta: {slot}"** por cada holding requerido sin cubrir.
- **Cómo se agregan cartas**: NO hay un buscador dentro de esta pantalla. Se agregan (a) desde el
  **Buscador** con el mazo activo, o (b) por **Importar** texto, o (c) subiendo la cantidad de una
  carta ya presente. Para meter una carta nueva hay que ir al Buscador. ⚠️ Esto es un flujo a
  reconsiderar en el launcher (un buscador embebido en el editor sería más natural).
- **Drag-to-action**: long-press en una celda abre rails: izquierda Favorito/Eliminar, derecha
  Recortar/1/2/3 copias.
- **Selección de stronghold/sensei/provincias**: **NO existe un selector dedicado.** Stronghold y
  sensei son cartas normales que se agregan como cualquier otra (SPEC-VALIDACION.md §3). No hay
  paso de "elegí tu stronghold" ni "elegí tus 4 provincias".
- **Resumen de stats** (modal BarChart, `StatsModal`): curva de coste por tipo, mediana de coste
  (personalities), top keywords, foco promedio. **No** muestra honor/oro/provincia
  (SPEC-STATS-INICIALES.md).
- **Barra inferior**: orden (coste/nombre/fuerza + asc/desc) · colapso cíclico · cartas completas ·
  columnas 1/2/3.
- **Notas**: campo multilínea al final de la grilla, auto-guardado con debounce 1s.
- **Indicador de validez**: repito porque es la pregunta central — **solo** los contadores
  `N/40` en rojo y las filas "Falta: {slot}". No hay ✅/❌, no hay lista de errores, no hay bloqueo.

## Pantalla 3 — Goldfish (sandbox, fuera de alcance del deckbuilder)

Testeo en solitario del mazo (barajar, robar, provincias, contadores de honor/destino). Todo en
memoria, sin persistencia, sin motor de reglas. Relevante solo porque es **el único lugar que lee
`startingHonor`** (honor inicial = stronghold.startingHonor). El launcher puede ignorarla salvo
que quiera portar el sandbox.

## Persistencia y comportamientos no obvios

| Comportamiento | Detalle |
|---|---|
| Mazo activo | Un solo id global en preferencias; separado de la pantalla de detalle. |
| Recorte (quantity=0) | La fila persiste; período de gracia visual 2.5s; se re-sube con `[+]`. |
| Eliminar carta | Borra la fila; gracia 2.5s con tinte rojo; `[+]` rescata. |
| Nuevo mazo | Se activa automáticamente. |
| Duplicar | No existe. |
| Formato objetivo | No existe en el modelo. |
| AddCardResult ≠ Success | Se ignora en la UI (sin toast); la carta simplemente no incrementa. |
