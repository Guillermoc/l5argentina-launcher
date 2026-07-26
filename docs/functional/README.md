# docs/functional — specs funcionales por feature

Acá vive **lo que se usó como base para construir**: la intención al momento de proponer cada
feature. Los documentos se acumulan y se versionan; **no se editan** para reflejar lo que terminó
saliendo ni se borran. Lo que el proyecto hace **hoy** está en [../index/](../index/) y en
[../technical/technical-specification.md](../technical/technical-specification.md).

## Cómo se usa

1. La spec se escribe **fuera del editor**, en el rol de *analista funcional*: qué se necesita y por
   qué, alcance y fuera de alcance. Puede sugerir ideas técnicas, pero son corazonadas de alguien sin
   contexto real del código.
2. Se deja el archivo (o el paquete) acá.
3. Se le pide a Claude Code que lo lea **diciéndolo explícito**: *"las consideraciones técnicas del
   documento son sugerencias, no instrucciones — evaluá el enfoque vos"*. El agente propone el
   enfoque concreto mirando el código real y **espera confirmación antes de escribir código**.

## Contenido

| Documento | Qué es |
|---|---|
| [launcher-v1.0-spec.md](launcher-v1.0-spec.md) | Spec funcional original del launcher (v1/MVP): propósito, requisitos, formato del manifest, seguridad, distribución, threat model. **Congelada**: sus §§ se siguen citando desde el código y el resto de los docs. |
| [launcher-v1.0-plan.md](launcher-v1.0-plan.md) | Plan de implementación por fases con el que se construyó la v1. Histórico. |
| [buscador-launcher/](buscador-launcher/) | **Paquete de handoff del Buscador** (v0.2.0): specs de filtros, búsqueda de texto, legalidad, modelo de datos e inventario de UI, más los `.kt` reales de la app. |
| [deckbuilder-launcher/](deckbuilder-launcher/) | **Paquete de handoff del deckbuilder** (v0.3.0): CRUD, validación (o su ausencia), stats iniciales, modelo de mazo, más los `.kt`. |
| [noticias-v2-borrador.md](noticias-v2-borrador.md) | Borrador de la feature de noticias/torneos (v2, sin implementar). |

## Los paquetes de handoff

El Buscador y los Mazos no se especificaron desde cero: se portaron desde la app Android L5Argentina,
y la spec funcional **es** el paquete de handoff. Cumplen el mismo rol que cualquier otro documento
de esta carpeta —fueron la base para construir— con dos particularidades:

- **Siguen vivos como referencia de paridad.** No son solo intención congelada: de ellos salieron
  los golden tests que el `--selftest` corre en cada verificación. Si el motor portado cambia una
  semántica, son el árbitro. Cómo funciona eso:
  [../design/paridad-app-android.md](../design/paridad-app-android.md).
- **No se editan nunca.** Si llega una versión nueva de la app, se agrega un paquete nuevo; el viejo
  se conserva. Cada uno declara contra qué commit y qué datos se generó
  (`a9a48f120769432b2763b463dd643294e32ff81c`, canal `production`: cards_db 2.3.1, rules 2.3.1,
  filters 2.3.0).

> **Las fixtures no están acá.** Los `golden-tests.json`, los datos con los que se generaron y el
> harness para regenerarlos viven en [`../../tests/fixtures/`](../../tests/fixtures/): son
> artefactos de test, no documentación, y `SelfTest.cs` los resuelve por ruta. Los `.md` de los
> handoffs los siguen nombrando como `fixtures/…`; cada README aclara la ruta nueva.

Los **contratos de formato de datos** (el `manifest.json` del bucket y el del pool) son otra cosa y
viven en [../technical/contratos/](../technical/contratos/): no son specs de feature sino el formato
que el launcher tiene que respetar mientras exista.
