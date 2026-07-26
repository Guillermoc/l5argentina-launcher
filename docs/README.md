# docs — mapa de la documentación

**¿Primera vez acá?** Leé [`index/OVERVIEW.md`](index/OVERVIEW.md) (qué es el proyecto y dónde está
cada cosa) y [`ROADMAP.md`](ROADMAP.md) (qué falta). Con eso alcanza para ubicarse.

**¿Sos un agente?** El ruteo completo —qué leer según la tarea, qué reglas no se tocan y qué
actualizar después de cada cambio— está en [`../CLAUDE.md`](../CLAUDE.md).

---

## Las cuatro capas

| Capa | Qué contiene | Cuándo se actualiza |
|---|---|---|
| [`index/`](index/) | **Estado actual del código**, por dominio. Denso, para consulta rápida. | Cada vez que cambia el código |
| [`functional/`](functional/) | **Specs de feature**, versionadas: la intención con la que se construyó cada cosa. Incluye los paquetes de handoff de la app Android. | Antes de implementar. No se editan después |
| [`design/`](design/) | **Decisiones técnicas transversales**: seguridad, paridad, persistencia, estilo visual. | Cuando una regla abarca varios módulos |
| [`technical/`](technical/) | El **as-built** en prosa (para leer de corrido) + los contratos de formato de datos. | En cada cierre de fase |

Más dos documentos sueltos: [`DECISIONS.md`](DECISIONS.md) (el registro de ADR: qué se decidió y
**por qué**) y [`ROADMAP.md`](ROADMAP.md) (qué falta).

## Guía rápida

| Quiero… | Ir a |
|---|---|
| Entender el proyecto en 5 minutos | [`index/OVERVIEW.md`](index/OVERVIEW.md) |
| Entenderlo entero, de corrido | [`technical/technical-specification.md`](technical/technical-specification.md) |
| Saber por qué algo está hecho así | [`DECISIONS.md`](DECISIONS.md) |
| Tocar código de un dominio | el índice de ese dominio en [`index/`](index/) |
| Saber qué falta hacer | [`ROADMAP.md`](ROADMAP.md) |
| Usar el launcher como jugador | [`USUARIOS.md`](USUARIOS.md) |

## Lo que no es documentación

- [`things/`](things/) — assets de marca originales (fuentes `.ttf`, logos, fondos, ícono). Los que
  usa la app están copiados en `src/Assets/`; esto es el material fuente.
- [`USUARIOS.md`](USUARIOS.md) — guía para los jugadores de la comunidad, no para desarrollo. Se
  actualiza cuando cambia algo que el usuario ve (instalación, primer arranque, SmartScreen).
- Los datos de los golden tests **no están acá**: viven en [`../tests/`](../tests/), porque son
  artefactos de test y `SelfTest.cs` los resuelve por ruta.
