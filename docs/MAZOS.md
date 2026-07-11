# Mazos / deckbuilder — arquitectura y decisiones

Port del deckbuilder de la app Android L5Argentina al launcher, según el paquete de handoff
(`docs/handoff/deckbuilder-launcher/`). Este doc cubre el lado launcher: qué se construyó,
qué decisiones se tomaron y en qué diverge (a propósito) de la app.

## Qué hay

| Pieza | Archivo | Qué hace |
|---|---|---|
| Modelo | `src/Models/DeckModels.cs` | `Deck`/`DeckCardEntry` (espejo de `decks`/`deck_cards`), `AddCardResult`, `ImportResult`, `DeckGroups` (starter/dynasty/fate + orden de tipos). |
| Motor | `src/Services/Deck/DeckRepository.cs` | **Port 1:1 de DeckRepository.kt**: `AddCard` (banned → slot → maxCopies, en ese orden), `RemoveCard`/`SetCardQuantity` (con `keepRowAtZero`), `ImportCards`/`ExportDeck` (formato de texto de la app), CRUD, y los espejos de UI de las specs (conteos Dynasty/Fate, slots sin cubrir, honor inicial del Goldfish, clan del primer stronghold). |
| Formato txt | `src/Services/Deck/SunAndMoonDeckFile.cs` | Lee/escribe el formato de la carpeta `Decks` de Sun and Moon (`Legality: X`, starter sin prefijo, `Nx Nombre`). El parser tolera también el formato de export de la app (`N Nombre`, líneas `#`). |
| Biblioteca | `src/Services/Deck/DeckLibrary.cs` | **Un .txt por mazo en `<SunAndMoon>\Decks\`** (los mismos archivos que carga el juego) + sidecar `decksmeta.json` en %APPDATA% para lo que el txt no representa (notas, sort_mode, fecha, mazo activo). Crear/renombrar/borrar/duplicar mueve los archivos. |
| Slots | `CatalogImporter` + `CardCatalog.InitialHoldingSlots` | `filters.json → formats[].initialHoldingSlots` (id/label/required) — alimenta el aviso "Falta: {slot}". |
| UI Buscador | `src/Views/SearchWindow.xaml(.cs)` | Panel derecho con selector de mazo/lista activa + contenido con cantidades + botones +/− (con feedback del `AddCardResult` — la app lo ignora). Doble click en carta = agregar. |
| UI editor | `src/Views/DeckWindow.xaml(.cs)` | Ventana única: lista de mazos + filtros (izq., colapsable), cartas agrupadas Starter/Dynasty/Fate → tipo (centro), detalle de carta / stats (der., colapsable). Importar/Exportar por portapapeles, notas con debounce 1s. `--mazos` la abre directo (DEBUG). |
| Detalle | `src/Views/CardDetailPanel.xaml(.cs)` | Panel de detalle compartido entre Buscador y editor (+ `CardImageResolver` y estilos compartidos en `Views/CardUiStyles.xaml`). |
| Paridad | `SelfTest.RunGoldenDeckTests` | Los **25 golden tests** del handoff, replicando la secuencia exacta del harness (mismos mazos "Golden N", string exacto del export). + checks propios de persistencia txt. |

## Decisiones tomadas

1. **Persistencia = los .txt de Sun and Moon.** Los mazos no viven en una DB propia: cada mazo
   es un archivo en la carpeta `Decks` del juego, así el mazo que armás acá se juega directo.
   Los .txt que ya estaban (los que trae el juego o hizo el usuario) se cargan tal cual.
   Metadata que el txt no puede expresar (notas, sort_mode, mazo activo, fecha) va al sidecar
   `decksmeta.json`. Sin instalación configurada, la carpeta cae en `%APPDATA%\...\Decks`.
2. **Nombres del juego ≠ nombres del catálogo — resolución tolerante solo en el parser txt.**
   Sun and Moon escribe los strongholds sin el sufijo de versión (`The Impregnable Fortress of
   the Crab` vs `... (1)/(2)` del catálogo), a veces con case distinto (`Strength of my Father`)
   o sin `- exp`. `GetCardByNameRelaxed` resuelve esa cadena (exacto → trim → sufijo → CI →
   `- exp`) **solo para los txt**; `ImportCards` mantiene el match exacto de la app (golden).
   Al reescribir, cada entrada conserva el nombre original del archivo (`SourceName`) para que
   el juego siga reconociendo su mazo; las cartas nuevas escriben el nombre del catálogo con el
   sufijo `(N)` quitado.
3. **Las líneas que no matchean el catálogo nunca se pierden** (`Deck.Unknown`): se muestran en
   el editor como "no reconocidas" y se reescriben verbatim (equivalente launcher de las
   huérfanas ADR-013).
4. **Sin UX de "recorte"** (quantity=0 con gracia de 2.5s): en el launcher llegar a 0 borra la
   entrada. El repo implementa `keepRowAtZero` completo (paridad + goldens), pero la UI no lo
   usa: el handoff avala que los goldens no dependen de filas en 0.
5. **Validación = la de la app, ni más ni menos.** No hay motor de validación: solo el rechazo
   en `AddCard` (banned/slot/tope) y los indicadores blandos (Dynasty/Fate rojo si <40,
   "Falta: {slot}"). Dos strongholds siguen permitidos (⚠️ paridad). **Divergencia de UX**: acá
   los `AddCardResult` ≠ Success **se informan** (feedback en el panel / status del editor);
   la app los ignora en silencio.
6. **`NoActiveDeck` conserva su nombre engañoso** (= carta o mazo inexistente) para que el port
   se lea contra el Kotlin.
7. **Listas**: las "listas simples" del Buscador siguen en `usercards.json` (no son mazos ni van
   a la carpeta Decks). El selector del panel derecho unifica ambas: mazos (tope por carta) y
   listas (tope 1 por toggle). Los mazos `type="list"` del modelo de la app existen en el repo
   solo por paridad de goldens; la UI no los crea.
8. **Duplicar existe** (export → mazo nuevo → import), aunque la app no lo tiene — extra barato
   y pedido frecuente.
9. **Extras del launcher en el orden del Buscador**: `SortField` ganó TYPE/CLAN/RARITY/
   HONOR_REQ/FOCUS para el click en cabeceras de la tabla. No existen en la app y no los cubren
   los goldens (los 6 originales quedan intactos).

## Layout (pedido específico)

- **Buscador**: filtros a la izquierda y panel Carta|Mazo a la derecha, ambos **colapsables**
  (chips "Filtros"/"Panel"); cada colapso libera ~1 columna de cartas. Grilla por defecto con
  celdas grandes y barra de acciones por celda (−/+ mazo activo, ★, ⊘, ≡ lista activa).
  Toggles rápidos, lista activa, vista y orden viven en la barra superior, no en el panel de
  filtros. **Listas ≠ mazos**: la lista activa es material de armado (el chip "Solo lista"
  filtra los resultados a sus cartas); el mazo activo es el destino de los +/−.
- **Editor**: mazos+filtros | cartas agrupadas | detalle/stats. El centro tiene **dos vistas**
  (chips Lista/Grilla): filas compactas o cartas en imagen (~3 columnas; default, como la app),
  ambas manteniendo las secciones Starter/Dynasty/Fate → tipo y los controles −/qty/+/✕.
  La lista de mazos vive en el rail izquierdo del editor; en el Buscador el mazo activo se
  elige en el combo del panel derecho (respuesta a "no sé dónde poner la selección del mazo").

## Verificación

`--selftest` = **177 checks** (91 del pipeline/Buscador + 53 golden Buscador + 33 del
deckbuilder: 25 golden + persistencia txt/DeckLibrary + resolución de nombres). Si se toca
`DeckRepository` o el importer, correr `--selftest`; si se *cambia* una semántica golden,
regenerar los expected con el harness del handoff (`fixtures/generator/`) y anotarlo.

## Pendientes / ideas

- Goldfish (sandbox de testeo) — fuera de alcance, la spec lo cubre si algún día se quiere.
- Grupos de usuario (carpetas de mazos) y subgrupos dentro del mazo (`deck_card_groups`,
  en desuso en la app) — no portados.
- Export PDF — no portado.
- Buscador embebido en el editor: hoy el flujo es "Buscador +/− sobre el mazo activo"
  (como la app); si molesta, el editor puede ganar una barra de búsqueda propia.
