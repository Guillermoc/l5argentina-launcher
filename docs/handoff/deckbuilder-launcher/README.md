# Handoff — Deckbuilder para el L5Argentina Launcher

Paquete autocontenido para portar el **Mazos / deckbuilder** de la app Android **L5Argentina** al
**L5Argentina Launcher** (WPF, .NET Framework 4.8, C#). Continúa el handoff del Buscador
(`buscador-launcher/`), que ya está portado. **Alcance: armar/validar/ver un mazo. Sin sync
online, cuentas ni compartir por link.**

- **Repo origen:** L5Argentina (Android), commit **`a9a48f120769432b2763b463dd643294e32ff81c`**.
- **Datos:** canal `production` — cards_db **2.3.1**, rules **2.3.1**, filters **2.3.0**
  (copias en `fixtures/data/`).

## ⚠️ Lo primero que tenés que saber

**No hay un motor de validación de mazo.** La app nunca impide guardar/activar un mazo inválido.
La diferencia real entre un "mazo" y una "lista de cartas simple" (que ya tenés) es **acotada**:

1. **Límite de copias por carta** (`maxCopies`; en listas siempre 1, en mazos `card.maxCopies`).
2. **Rechazo de baneadas** al agregar.
3. **Exclusividad de slots de holding inicial** (no dos cartas del mismo slot).
4. **Partición por `deck_group`** (starter/dynasty/fate) para conteos y export.
5. **Indicadores blandos**: contadores Dynasty/Fate en rojo si `<40`, y avisos de slots sin
   cubrir. **Ninguno bloquea nada.**

Todo lo demás del reglamento (1 stronghold, tamaños mínimos, provincias, legalidad por formato)
**no está implementado**. Detalle exhaustivo en `SPEC-VALIDACION.md`.

## Índice

| Archivo | Contenido |
|---|---|
| [SPEC-VALIDACION.md](SPEC-VALIDACION.md) | **El corazón.** `AddCardResult` completo y su orden de chequeos; qué NO se valida (con ⚠️); stronghold/sensei como cartas normales; indicadores blandos; lista exhaustiva de señales. |
| [SPEC-DECK-CRUD.md](SPEC-DECK-CRUD.md) | Modelo de mazo; crear/renombrar/borrar (no duplicar); mazo activo (qué lo setea, para qué sirve); agregar/quitar cartas; recorte vs borrado. |
| [SPEC-STATS-INICIALES.md](SPEC-STATS-INICIALES.md) | Honor inicial (solo Goldfish, = stronghold.startingHonor); oro/provincia **no se calculan**; stats del StatsModal (curva de coste, mediana, keywords, foco). |
| [MODELO-DATOS-MAZO.md](MODELO-DATOS-MAZO.md) | Entidades `decks`/`deck_cards`/`deck_groups`; relación con `CardEntity`; **formato de import/export** con ejemplo real y reglas. |
| [UI-INVENTARIO.md](UI-INVENTARIO.md) | Lista de mazos, editor (cómo se agregan cartas: desde el Buscador, no hay buscador embebido), no hay selector de stronghold, resumen de stats, indicador de validez. |
| [src/](src/) + [src/INDEX.md](src/INDEX.md) | Los `.kt` reales verbatim (repo, entidades, DAOs, ViewModels; marcada la semántica que vive en UI). |
| [fixtures/golden-tests.json](fixtures/golden-tests.json) | **25 casos** con resultados producidos ejecutando la lógica real (ver abajo). |
| [fixtures/data/](fixtures/data/) | Datos exactos: `cards_db-2.3.1.zip`, `rules-2.3.1.json`, `filters-2.3.0.json`. |
| [fixtures/generator/](fixtures/generator/) | Harness reproducible (driver JDBC + stubs). |

## Golden tests — "lógica real", no armados a mano

El harness de `fixtures/generator/` compila **copias sin modificar** de `DeckRepository`,
`CardImporter`, `RulesImporter` (+ entidades/DAOs) con stubs mínimos de Room, importa el catálogo
real, y ejecuta escenarios a través del **`DeckRepository` real** sobre SQLite. Los DAOs son
dobles JDBC que replican el SQL de cada `@Query` (el de banlist se lee por reflexión).

Cubre (25 casos): agregar banned → rechazo; max de copias en deck (3) vs list (1); Unique (max 1);
slot de holding ocupado vs slots distintos; **dos strongholds permitidos**; import con clamp,
banned, desconocida, conflicto de slot, notas/headers/sort_mode; export round-trip (con el string
exacto); conteos Dynasty/Fate; slots sin cubrir; honor inicial (stronghold, cero, multi-stronghold).

Formato de cada caso: `operations` = secuencia ejecutada; `expect` = `AddCardResult` devuelto por
`DeckRepository.addCard`; para import/export/counts, los valores reales retornados. `cardsUsed`
lista los ids concretos. Los conteos/slots/honor están marcados como **espejos** de expresiones
de ViewModel/Goldfish (citadas en las specs).

### Cómo usarlos en el launcher

- Importá `cards-2.3.1.json` (de `fixtures/data/cards_db-2.3.1.zip`) con tu motor de catálogo ya
  portado del Buscador — trae `max_copies`, `banned`, `deck_group`, `initial_holding_slot`,
  `starting_honor`, etc.
- Ejecutá cada caso sobre tu port de `DeckRepository` y compará contra `expect`.
- Los ⚠️ marcan comportamientos accidentales/ambiguos (nombre `NoActiveDeck`, dos strongholds
  permitidos, honor 0 vs ausente, multi-stronghold no determinista). Decidí por caso si portás el
  comportamiento o lo arreglás; si lo arreglás, regenerá ese golden y anotalo.

## Notas de continuidad con el handoff del Buscador

- El launcher ya tiene `CardEntity` completo, motor de filtros y "listas simples sin validación".
  Este handoff agrega **solo** la capa de mazo encima.
- El generador de golden reusa fuentes del catálogo del handoff del Buscador (ver
  `fixtures/generator/README.md` — no se duplican los `.kt` de catálogo).
