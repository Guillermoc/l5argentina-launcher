# Formato del `.txt` de mazos de Sun and Moon

El contrato de la carpeta `<instalación>\Decks\`. **No lo controlamos**: lo define el juego, y el
launcher escribe ahí ([ADR-022](../../DECISIONS.md#adr-022)) — si nos desviamos, el jugador abre Sun
and Moon y no encuentra su mazo.

De §1 a §6 y §9 está **leído del código del juego**, no inferido de los archivos:
`Assembly-CSharp.dll` (Unity/Mono, en `Sun and Moon_Data\Managed\`), clases `Deck`, `Persistent`,
`Table`, `CardDisplay` y `ListDeckContents`. Revisado el **2026-08-27** contra la build del
2026-06-14 (`.player_version.txt`). Las divergencias del launcher están marcadas con ⚠️.

§7 y §8 son del lado nuestro: lo que el launcher **escribe** en ese `.txt` y el **otro** formato de
texto con el que un mazo entra y sale, el del portapapeles — que es el de la app Android y no acepta
lo mismo.

---

## 1. El archivo

`Deck.GetSlush()`:

- **`.txt`** → `File.ReadAllText`. **`.snm`** → los mismos bytes comprimidos con LZMA: es lo que
  produce el botón **Lock** del editor del juego (`Deck.locked`), que además borra el `.txt`.
  ⚠️ El launcher solo enumera `*.txt`: un mazo bloqueado desaparece de su editor.
- Antes de parsear se hace `Replace("\r\n", "\n")` y se corta por `\n`. Los dos finales de línea
  sirven; el launcher escribe CRLF.
- El **nombre del mazo es el nombre del archivo** sin la extensión.

## 2. La línea

`Deck.GenerateCards`, en este orden exacto:

| Orden | Regla | Ejemplo |
|---|---|---|
| 1 | `Trim(' ')`; si queda vacía, se saltea | |
| 2 | Si **contiene** `Legality:` (en cualquier posición), lo que sigue es la legalidad y termina la línea | `Legality: s_extended` |
| 3 | Si **empieza** con `#`, es comentario y se ignora | `# Dynasty - (40) [12]` |
| 4 | `ReplaceCommonTerms`: normaliza el sufijo de experiencia y las comillas (§5) | `Yogo Koji XP` → `Yogo Koji - exp` |
| 5 | `TrimEdition`: lo que esté entre `(` y `)` es la **edición** | `Productive Mine (GoC)` |
| 6 | `TrimProxyType`: prefijo `Proxy <tipo>: ` (§4) | `Proxy holding: Border Keep` |
| 7 | `GetCardQuantityInt`: la **cantidad** del primer token (§3) | `2x Jade Works` |
| 8 | `VerifyTextAgainstDatabase`: se busca el nombre; si no está, se consulta el Oracle online | |

Una línea que no resuelve **no rompe nada**: queda sin carta. En el editor del juego se pinta de
rojo; en el launcher va a `Deck.Unknown` y se reescribe verbatim
([ADR-024](../../DECISIONS.md#adr-024)).

### Gramática, entonces

```
[Proxy <tipo>: ][<cantidad>[x] ]<nombre>[ (<edición>)]
```

## 3. Cantidad

`Persistent.TrimCardLine`: se mira el **primer token** (hasta el primer espacio), se le borran las
`x` y se intenta parsear. Valen `2x Jade Works`, `2 Jade Works` y `10x Jade Works`; sin token
numérico la cantidad es **1** (así se escriben el stronghold y el sensei).

Hay una salvaguarda: **si el primer token tiene una coma, no es cantidad**. Es lo que hace que
`1,000 Cuts Technique` se lea como nombre y no como "mil copias de Cuts Technique".

## 4. Proxies — `Proxy <tipo>: `

`Persistent.TrimProxyType`:

- `"Proxy "` se busca **sin distinguir mayúsculas**; el separador es el **primer `": "`** de la línea.
- El texto entre ambos tiene que ser **exactamente uno de estos doce** (comparado sin distinguir
  mayúsculas): `holding`, `personality`, `ring`, `spell`, `strategy`, `follower`, `item`, `sensei`,
  `stronghold`, `celestial`, `event`, `region`.
  **No existe `wind`**, aunque sí sea un tipo de carta. Cualquier otro texto y la línea se lee como
  carta común — con lo cual el nombre no matchea y queda en rojo.
- El prefijo se saca **antes** de la cantidad: `Proxy holding: 3x Productive Mine` pone tres.

**Qué hace en la mesa** (`Table.SetTable`): la carta **no entra al mazo**. Se instancia aparte, boca
arriba, en una grilla de 5 por fila al costado (`xDistanceBetweenProxies` / `zDistanceBetweenProxies`).
Y el tipo no es decorativo: `CardDisplay.OnCardLoaded` **pisa `card.type`** con ese texto, de donde
salen el dorso (`strategy`/`follower`/`item`/`spell`/`ring`/`other` → fate; el resto → dynasty) y los
chequeos de dónde se puede soltar (`Table.AddToFief` y `AddToProvinces` piden `"holding"`,
`ReplaceRegionWith` pide `"region"`).

> **El tipo se guarda con la capitalización que escribiste**, pero todas las comparaciones
> posteriores del juego son **ordinales en minúscula**. `Proxy Item:` deja `type = "Item"`, que no
> matchea `"item"`: sale con dorso dynasty y no lo toma el fief. **Escribir el tipo en minúscula.**

## 5. Nombres

`VerifyTextAgainstDatabase` busca en minúsculas, **primero el nombre entero** (con el paréntesis
incluido) y **después sin el paréntesis**. Si varias cartas comparten nombre y no diste edición,
gana la **última** de la lista; con edición, la primera cuyo `ContainsEdition` matchee.

`StringExtensions.ReplaceCommonTerms` normaliza antes de buscar:

| Se escribe | Se busca |
|---|---|
| ` exp`, ` experienced`, ` Experienced`, ` XP`, ` xp`, ` -exp`, ` - XP` | ` - exp` |
| ` Inexperienced`, ` inexperienced` | ` - inexp` |
| `&#149;`, doble espacio, comillas curvas (`’ ‘ “ ”`) | se limpian |

⚠️ El launcher resuelve con `DeckRepository.GetCardByNameRelaxed` (exacto → trim → stronghold sin
`(N)` → case-insensitive → `- exp`). **No** implementa la familia ` XP` / ` experienced`: un txt
hecho a mano con `Yogo Koji XP` lo resuelve el juego y nosotros no (queda cruda, no se pierde).

## 6. Trampas del juego

Tres cosas que el juego hace y conviene tener presentes:

- **`Deck.AddCard` descarta en silencio** cualquier carta cuyo nombre de catálogo contenga `(2)`.
  Por eso el launcher escribe los strongholds sin el sufijo de versión (`SunAndMoonNameOf`).
- **El flag `isProxy` se pega al objeto `Card` compartido de la base.** `Table.SetTable` lo pone en
  `true` y nadie lo limpia hasta el próximo `SetTable` de esa misma carta. Consecuencia: después de
  jugar una partida, en la **misma sesión**, cualquier mazo que traiga esa carta la vuelve a mandar a
  la mesa (con tipo vacío). Y si la carta aparece **dos veces** en el mismo archivo, `proxies.Add`
  tira `ArgumentException: An element with the same key already exists` y ese mazo queda cargado a
  medias. Se limpia reiniciando el juego.
- **`TrimProxyType` no valida que el `": "` esté después del `"Proxy "`**: una línea como
  `Algo: texto Proxy holding` hace un `Substring` de largo negativo y revienta.
  ⚠️ El launcher chequea el orden y trata esa línea como carta común.

## 7. Lo que escribe el launcher

`Services/Deck/SunAndMoonDeckFile.Write`:

```
Legality: s_extended

The Seventh Tower                    ← starter: stronghold, sensei, wind (sin cantidad si es 1)
Proxy holding: Border Keep - exp2    ← starter holdings, siempre marcados (ADR-060)
Proxy holding: Bamboo Harvesters

2x Jade Works                        ← dynasty, un bloque por tipo en el orden canónico
1x Productive Mine (GoC)             ← la edición que traía el archivo se reescribe igual

3x Knife in the Darkness             ← fate

Proxy Holding: Jade Works            ← líneas crudas (Deck.Unknown), verbatim, al final
```

- Los **starter holdings** (grupo starter + tipo `holding`) salen siempre con `Proxy holding: `, en
  minúscula ([ADR-060](../../DECISIONS.md#adr-060)). En las **listas** no: `Lists\` no lo lee el juego.
- El prefijo y la edición que traía cada línea se conservan y se reescriben igual.
- Las **recortadas** (`quantity = 0`) no se escriben — el archivo no tiene forma de expresarlas
  ([ADR-046](../../DECISIONS.md#adr-046)).
- Cada carta se referencia por **nombre**, nunca por id ([ADR-044](../../DECISIONS.md#adr-044)), y
  con el nombre exacto que traía el archivo si difería del catálogo (`SourceName`).

El **portapapeles** es otro formato y va aparte: **§8**.

### Divergencias conocidas del parser del launcher

| Tema | El juego | El launcher |
|---|---|---|
| `Legality:` | en cualquier posición de la línea; gana la **última** | tiene que abrir la línea; gana la **primera** |
| Edición | primer `(` de la línea, siempre | solo el paréntesis **final**, y solo si el nombre entero no resolvió |
| Sufijos ` XP` / ` experienced` | se normalizan a ` - exp` | no (§5) |
| `.snm` | los lee | no los enumera |
| Misma carta con y sin prefijo de proxy | dos destinos distintos (mazo y mesa) | una entrada por carta: la segunda línea se conserva **cruda** para no cambiarle el sentido al archivo |

## 8. El portapapeles — `⋯ → Exportar` / `Importar`

**No es este formato.** Copiar y pegar usan el **formato de texto de la app Android**, portado 1:1 en
`DeckRepository.ExportDeck` / `ImportCards`, con una sola cosa agregada por el launcher: los prefijos
de proxy al copiar ([ADR-060](../../DECISIONS.md#adr-060)). Se documenta acá porque es el otro camino
por el que un mazo entra y sale como texto, y **no acepta lo mismo** que el `.txt` del juego.

### Lo que sale al copiar

```
# <cada línea de las notas del mazo>
# <Nombre del mazo> - (<cartas>) [<distintas>]
# <Grupo> - (N) [M]
# <Tipo> - (N) [M]
<cantidad> <Nombre>
```

Ejemplo real (es el string exacto que fija el golden `export-roundtrip`):

```
# Golden 18 - (8) [4]
# Starter - (1) [1]
# Stronghold - (1) [1]
The Seventh Tower
# Dynasty - (5) [2]
# Holding - (2) [1]
2 Jade Works
# Personality - (3) [1]
3 Hida Akeno
# Fate - (2) [1]
# Strategy - (2) [1]
2 A Brave New World
```

- `(N)` es la cantidad de cartas y `[M]` cuántas distintas.
- **La cantidad se omite cuando es 1** (`The Seventh Tower`, no `1 The Seventh Tower`).
- Grupos en orden `starter → dynasty → fate`; adentro, los tipos en orden **alfabético ordinal** y
  las cartas por nombre. Un grupo vacío no imprime su cabecera.
- El nombre es el **del catálogo**: acá no se usa el `SourceName` del archivo ni se escribe la
  edición.
- **No salen** las recortadas (`quantity = 0`) ni las líneas crudas de `Deck.Unknown`. Tampoco el
  `Legality:` ni el `# sort_mode:`.
- El botón de copiar prende `withProxyMarkers`, así que cada línea puede llevar adelante
  `Proxy <tipo>: ` con la misma regla del §7 (starter holdings forzados + el prefijo que traía el
  archivo). Es lo que hace que pegar el resultado en un `.txt` de `Decks\` ya venga marcado.

### Lo que acepta al pegar

`ImportCards` es el port exacto del import de la app y **no se toca**
([ADR-023](../../DECISIONS.md#adr-023)):

| | |
|---|---|
| Línea de carta | `<cantidad> <Nombre>` o `<Nombre>` solo (cantidad 1) |
| `Nx Nombre` | ✅ pero **por el borde**: la regex de la app pide un espacio después del número, así que el launcher lo traduce antes de entregar el texto (abajo) |
| Nombre | **match exacto** contra el catálogo. Sin la cadena tolerante del parser de txt (§5): ni case-insensitive, ni `- exp` implícito, ni edición entre paréntesis |
| `# <algo> - (N) [M]` | cabecera: se descarta |
| `# sort_mode: X` | setea el modo de orden del mazo |
| `# <cualquier otra cosa>` | se acumula como **nota** del mazo |
| Se rechaza (cuenta como "no encontrada") | carta inexistente, carta **baneada**, y una segunda carta de un **slot inicial** ya cubierto |
| Tope | `MaxCopies` (1 en las listas). Lo que se pasa **se recorta**, no se descarta la línea |
| Sobre lo que ya hay | **suma**, y resetea el `sortOrder` a 0 (el `REPLACE` de la fila, igual que la app) |

### El borde: `NormalizeForImport`

`ImportCards` es el port y no se toca, así que lo que se adapta es el **texto**, en el handler de la
ventana (`SunAndMoonDeckFile.NormalizeForImport`). Dos ajustes, línea por línea:

| | |
|---|---|
| `Proxy <tipo>: Nombre` → `Nombre` | para que pegar en el launcher lo que el launcher copió cierre el círculo |
| `2x Jade Works` → `2 Jade Works` | única forma de cantidad que el import de la app reconoce; sin esto, **pegar un `.txt` de `Decks\` rebotaba en todas las líneas con cantidad** |

Las líneas `#` y las que no matchean quedan como estaban.

Con eso, pegar un `.txt` del juego entra completo. Lo que **sigue** rebotando de un archivo así:

- el header `Legality: …`, que no es una carta y se reporta como no encontrada (ruido en el informe,
  nada más);
- la edición entre paréntesis: `ImportCards` la leería como parte del nombre. **No** se saca en el
  borde a propósito — sacarla implicaría elegir una impresión, que es una decisión, no una traducción;
- los nombres que necesitan la resolución tolerante del §5 (un stronghold escrito sin su `(1)`,
  un `- exp` omitido, otro case). Esos los resuelve el parser del `.txt`, no el import.

### Qué sobrevive a la ida y vuelta

| | Va y viene |
|---|---|
| Cartas y cantidades | ✅ |
| Prefijo de proxy | ✅ (se marca al copiar, se saca al pegar) |
| Notas del mazo | ✅ (salen como `#`, vuelven como notas) |
| `Legality:` | ❌ ni se exporta ni se importa |
| Edición entre paréntesis | ❌ no se exporta |
| Nombre original del archivo (`SourceName`) | ❌ sale el del catálogo |
| Recortadas y líneas crudas | ❌ no salen |
| Etiquetas y ajuste de oro | ❌ viven en el sidecar, no en el texto |
| `sort_mode` | ❌ se importa si está, pero el export no lo escribe |

> **Por qué los marcadores son opt-in.** Este mismo par export→import es `DuplicateDeck`. Si el
> export marcara siempre, el import —que hace match exacto— no encontraría
> `Proxy holding: Border Keep` y **duplicar un mazo perdería los starter holdings**. Por eso
> `ExportDeck` sin argumentos queda exactamente como estaba, y la marca la prende solo el botón de
> copiar.

## 9. El editor del propio juego

`ListDeckContents` es un **editor de líneas**: guarda el texto crudo de cada renglón
(`DeckEntryItem.label`) y al salvar lo reescribe tal cual. Editar un mazo adentro del juego
**no pierde** las líneas que no reconoce —incluidas las de proxy, que igual pinta en rojo porque
`VerifyTextAgainstDatabase` no saca el prefijo— ni los comentarios `#`.
