# Índice — Mazos / deckbuilder

Estado actual del editor de mazos. Dominio: `src/Services/Deck/`, `src/Models/DeckModels.cs`,
`src/Views/DeckWindow.xaml(.cs)` y el panel derecho de `SearchWindow`.

Port del deckbuilder de la app Android. La spec funcional con la que se construyó es el paquete de
handoff [../functional/deckbuilder-launcher/](../functional/deckbuilder-launcher/)
(`SPEC-DECK-CRUD.md`, `SPEC-VALIDACION.md`, `SPEC-STATS-INICIALES.md`, `MODELO-DATOS-MAZO.md` y las
fuentes Kotlin), que además sigue siendo la referencia viva de paridad vía sus golden tests, en
[../../tests/fixtures/deckbuilder/](../../tests/fixtures/deckbuilder/).

---

## Piezas

| Pieza | Archivo | Qué hace |
|---|---|---|
| Modelo | `Models/DeckModels.cs` | `Deck`/`DeckCardEntry` (espejo de `decks`/`deck_cards`), `AddCardResult`, `ImportResult`, `DeckGroups` (starter/dynasty/fate + orden de tipos). |
| Motor | `Services/Deck/DeckRepository.cs` | **Port 1:1 de `DeckRepository.kt`**: `AddCard` (banned → slot → maxCopies, **en ese orden**), `RemoveCard`/`SetCardQuantity` (con `keepRowAtZero`), `ImportCards`/`ExportDeck` (formato de texto de la app), CRUD, y los espejos de UI de las specs (conteos Dynasty/Fate, slots sin cubrir, honor inicial del Goldfish, clan del primer stronghold). |
| Formato txt | `Services/Deck/SunAndMoonDeckFile.cs` | Lee/escribe el formato de la carpeta `Decks` del juego (`Legality: X`, starter sin prefijo, `Nx Nombre`). El parser tolera también el formato de export de la app (`N Nombre`, líneas `#`). |
| Biblioteca | `Services/Deck/DeckLibrary.cs` | Un `.txt` por mazo en `<SunAndMoon>\Decks\` + sidecar `decksmeta.json`. Instancia compartida por proceso (`GetOrLoad`): Buscador y editor operan sobre los mismos mazos y el mismo mazo activo. |
| Slots | `CatalogImporter` → `CardCatalog.InitialHoldingSlots` | `filters.json → formats[].initialHoldingSlots` (id/label/required) — alimenta el aviso "Falta: {slot}". |
| UI Buscador | `Views/SearchWindow.xaml(.cs)` | Panel derecho con mazo activo, cantidades y `+`/`−` (con feedback del `AddCardResult`). Doble click = agregar. |
| UI editor | `Views/DeckWindow.xaml(.cs)` | Ventana única: mazos + filtros (izq., colapsable), cartas agrupadas Starter/Dynasty/Fate → tipo (centro, vistas Lista/Grilla), detalle/stats (der., colapsable). Importar/Exportar por portapapeles, notas con debounce de 1 s. |
| Paridad | `SelfTest.RunGoldenDeckTests` + `RunDeckStoreTests` | **25 golden tests** replicando la secuencia exacta del harness (mismos mazos "Golden N", string exacto del export) + checks propios de persistencia txt. |

---

## Persistencia

**Los mazos son archivos del juego.** Cada mazo es un `.txt` en `<SunAndMoon>\Decks\` — el mazo que
se arma acá se juega directo, sin exportar nada. Los `.txt` que ya estaban (los del juego o hechos a
mano) se cargan tal cual. Sin instalación configurada, la carpeta cae en `%APPDATA%\...\Decks`.

Lo que el `.txt` no puede representar (notas, `sortMode`, fecha de creación, orden, mazo activo) va
al sidecar `%APPDATA%\...\decksmeta.json`, indexado por **nombre de archivo**. El sidecar manda sobre
lo parseado del txt; si está corrupto, se regenera en el próximo guardado.

Cada mutación guarda inmediatamente (`Repo.DeckChanged` → `SaveDeck`): renombrar el mazo renombra el
archivo, borrarlo lo borra, y los nombres duplicados se resuelven con `" (2)"`, `" (3)"`…
Los caracteres inválidos de nombre de archivo se reemplazan por `_` (el nombre del mazo **es** el
nombre del archivo).

**Nombres del juego ≠ nombres del catálogo.** Sun and Moon escribe los strongholds sin el sufijo de
versión (`The Impregnable Fortress of the Crab` vs `… (1)/(2)`), a veces con case distinto o sin
`- exp`. `GetCardByNameRelaxed` resuelve esa cadena (exacto → trim → sufijo → case-insensitive →
`- exp`) **solo en el parser de txt**; `ImportCards` mantiene el match exacto de la app
([ADR-023](../DECISIONS.md#adr-023)). Al reescribir, cada entrada conserva el nombre original del
archivo (`SourceName`) y las líneas no reconocidas se reescriben verbatim: el launcher **nunca** le
rompe el mazo al juego ([ADR-024](../DECISIONS.md#adr-024)).

---

## Validación

No hay motor de validación: paridad con la app. Solo existen los rechazos de `AddCard`
(baneada → slot cubierto → tope de copias) y los indicadores blandos (Dynasty/Fate en rojo si < 40,
"Falta: {slot}"). Dos strongholds siguen permitidos (⚠️ paridad).

**Divergencia de UX deliberada**: acá los `AddCardResult` distintos de `Success` **se informan**
(feedback en el panel / status del editor); la app los ignora en silencio
([ADR-026](../DECISIONS.md#adr-026)).

---

## Layout

- **Buscador**: filtros a la izquierda y panel Carta|Mazo a la derecha, ambos colapsables. El mazo
  activo se elige en el combo del panel derecho; la lista activa (material de armado) vive en la
  barra superior. Los `+`/`−` van siempre contra el mazo activo.
- **Editor**: mazos+filtros | cartas agrupadas | detalle/stats. El centro tiene dos vistas (chips
  Lista/Grilla): filas compactas o cartas en imagen (~3 columnas, default como la app), ambas con
  las secciones Starter/Dynasty/Fate → tipo y los controles `−`/qty/`+`/`✕`.

---

## Verificación

`--selftest` = **177 checks** (91 del pipeline/Buscador + 53 golden Buscador + 33 del deckbuilder:
25 golden + persistencia txt/DeckLibrary + resolución de nombres). Si se toca `DeckRepository` o el
importer, correr `--selftest`; si se *cambia* una semántica golden, regenerar los expected con el
harness del handoff (`../../tests/fixtures/deckbuilder/generator/`) y anotarlo.

---

## Decisiones que aplican

[ADR-022](../DECISIONS.md#adr-022) persistencia en los txt del juego ·
[ADR-023](../DECISIONS.md#adr-023) resolución tolerante solo en el parser ·
[ADR-024](../DECISIONS.md#adr-024) líneas no reconocidas verbatim ·
[ADR-025](../DECISIONS.md#adr-025) sin UX de recorte ·
[ADR-026](../DECISIONS.md#adr-026) validación de la app + feedback ·
[ADR-027](../DECISIONS.md#adr-027) `NoActiveDeck` conserva su nombre ·
[ADR-028](../DECISIONS.md#adr-028) listas ≠ mazos ·
[ADR-029](../DECISIONS.md#adr-029) duplicar mazo.

## Puntos abiertos

- Goldfish (sandbox de testeo) — fuera de alcance; la spec del handoff lo cubre si algún día se quiere.
- Grupos de usuario (carpetas de mazos) y subgrupos dentro del mazo (`deck_card_groups`, en desuso en
  la app) — no portados. Export PDF — no portado.
- Buscador embebido en el editor: hoy el flujo es "Buscador `+`/`−` sobre el mazo activo" (como la app).
