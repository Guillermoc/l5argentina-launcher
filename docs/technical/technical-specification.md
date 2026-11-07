# Especificación técnica *as-built* — L5Argentina Launcher

Qué hace el launcher hoy y cómo está construido, en prosa y de corrido. Es el documento para leer
entero: para consulta rápida están los índices ([../index/](../index/)), que son densos a propósito.

**Versión descrita:** v0.11.0 · **Última pasada contra el código:** 2026-11-07.
Se actualiza en cada **cierre de fase o release**, sección por sección, contra el código real (o
contra `docs/index/`, que debería estar al día). No en cada commit.

---

## Tabla de contenidos

1. [Panorama](#1-panorama)
2. [Arranque y ciclo de vida](#2-arranque-y-ciclo-de-vida)
3. [Datos externos: los dos manifests](#3-datos-externos-los-dos-manifests)
4. [Descarga y verificación](#4-descarga-y-verificación)
5. [Archivos del juego: bases, backups e imágenes](#5-archivos-del-juego-bases-backups-e-imágenes)
6. [Configuración y estado local](#6-configuración-y-estado-local)
7. [El catálogo de cartas](#7-el-catálogo-de-cartas)
8. [El motor de búsqueda](#8-el-motor-de-búsqueda)
9. [Mazos: repositorio y persistencia](#9-mazos-repositorio-y-persistencia)
10. [Presentación: ventanas, tema e idioma](#10-presentación-ventanas-tema-e-idioma)
11. [Seguridad como propiedad transversal](#11-seguridad-como-propiedad-transversal)
12. [Verificación y entrega](#12-verificación-y-entrega)
13. [Límites conocidos](#13-límites-conocidos)

## Feature → secciones relevantes

Este documento está organizado por **capa**, no por feature. Si venís a entender una cosa puntual:

| Feature | Leer |
|---|---|
| Primer arranque y detección de Sun and Moon | [2](#2-arranque-y-ciclo-de-vida), [5](#5-archivos-del-juego-bases-backups-e-imágenes), [6](#6-configuración-y-estado-local) |
| Elegir y aplicar base (Comunidad / Original) | [3](#3-datos-externos-los-dos-manifests), [4](#4-descarga-y-verificación), [5](#5-archivos-del-juego-bases-backups-e-imágenes) |
| Aviso de versión nueva del launcher | [3](#3-datos-externos-los-dos-manifests), [11](#11-seguridad-como-propiedad-transversal), [12](#12-verificación-y-entrega) |
| Packs de imágenes | [3](#3-datos-externos-los-dos-manifests), [4](#4-descarga-y-verificación), [5](#5-archivos-del-juego-bases-backups-e-imágenes) |
| Buscador de cartas | [7](#7-el-catálogo-de-cartas), [8](#8-el-motor-de-búsqueda), [10](#10-presentación-ventanas-tema-e-idioma) |
| Favoritas, ocultas, listas y presets | [6](#6-configuración-y-estado-local), [8](#8-el-motor-de-búsqueda) |
| Editor de Mazos | [9](#9-mazos-repositorio-y-persistencia), [10](#10-presentación-ventanas-tema-e-idioma) |
| Etiquetas de las cartas del mazo | [9](#9-mazos-repositorio-y-persistencia), [6](#6-configuración-y-estado-local) |
| Ajuste de oro por carta | [9](#9-mazos-repositorio-y-persistencia) |
| Filtrar las cartas del mazo | [8](#8-el-motor-de-búsqueda), [9](#9-mazos-repositorio-y-persistencia), [10](#10-presentación-ventanas-tema-e-idioma) |
| Exportar el mazo a PDF para imprimir | [9](#9-mazos-repositorio-y-persistencia) |
| Que el mazo armado se juegue directo | [9](#9-mazos-repositorio-y-persistencia), [6](#6-configuración-y-estado-local) |
| Noticias (sin implementar) | [13](#13-límites-conocidos) |
| Seguridad del launcher | [11](#11-seguridad-como-propiedad-transversal), [4](#4-descarga-y-verificación) |
| Cómo se compila, verifica y publica | [12](#12-verificación-y-entrega) |

---

## 1. Panorama

La comunidad L5Argentina (~25 personas) juega *Legend of the Five Rings* en **Sun and Moon**, una app
de escritorio hecha en Unity que lee su base de cartas de un único archivo:
`Sun and Moon_Data\StreamingAssets\Database\database.xml`. La comunidad mantiene una base curada
propia, con un formato custom `s_extended` y su banlist. Antes del launcher, mantenerla sincronizada
era copiar archivos a mano entre 25 personas.

El launcher es un ejecutable Windows que hace tres cosas: **sincroniza** esa base (y las imágenes que
la acompañan) desde un bucket Cloudflare R2 verificando su integridad, **deja elegir** con qué base
jugar (la de la comunidad o la original del usuario) y **lanza** el juego. Alrededor de eso creció un
**Buscador de cartas** y un **editor de Mazos**, ambos portados desde la app Android de la comunidad,
que convierten al launcher en la herramienta de mesa: buscar una carta, armar un mazo y jugarlo sin
salir de ahí.

Está construido en **WPF sobre .NET Framework 4.8**, con csproj SDK-style compilado por la CLI
`dotnet`, y se empaqueta con **Costura.Fody** en un único `.exe` de ~7 MB. La elección de net48 no es
nostalgia: viene preinstalado en toda Windows 10/11, así que el usuario no instala ningún runtime.
Esa misma restricción explica varias decisiones más abajo (nada de dependencias nativas, TLS 1.2 como
techo).

La regla que ordena todo el diseño es una sola: **el launcher solo descarga datos, nunca código**.
No se auto-actualiza, no tiene plugins y no embebe un navegador. El peor caso de un bucket
comprometido es que alguien reciba cartas falsas, no que le ejecuten algo en la máquina.

## 2. Arranque y ciclo de vida

`App.OnStartup` hace tres cosas antes que nada: fija `SecurityProtocolType.Tls12` (en .NET Framework
el default histórico puede no negociar TLS 1.2 contra Cloudflare), atiende los flags de debug
—`--selftest`, `--buscador`, `--mazos`, todos bajo `#if DEBUG`— y decide si el usuario ya tiene una
instalación configurada.

Si `InstallationService.IsValidInstall` falla —no hay ruta guardada, o falta el `Sun and Moon.exe` o
la carpeta `Database`— se abre `FirstRunWindow`. Ahí el usuario elige su `Sun and Moon.exe`, el
launcher valida la instalación y, **antes de tocar nada**, copia el `database.xml` existente a
`backups\database.original.xml`. Esa copia es, de ahí en más, la base "Original" que se ofrece: no se
hostea en ningún lado ni se vuelve a capturar nunca. Como el usuario puede estar instalando el
launcher sobre una instalación que ya venía modificada, antes de copiar se corre una heurística
—`LooksLikeCommunityBase`, un stream-scan buscando `s_extended`, sin parsear el XML— y si da positivo
se le avisa que esa "Original" puede no ser limpia.

Con la instalación resuelta se abre `MainWindow`, que es la pantalla de marca: selector de base,
estado de sincronización, banner de update, botón JUGAR y los accesos al Buscador, a los Mazos, a
Configuración y a los Packs de imágenes. Toda la lógica de "qué hay que bajar y qué hay que aplicar"
vive detrás, en `LauncherEngine`; la ventana orquesta y muestra.

Al abrir, la búsqueda de novedades corre **en segundo plano** y no bloquea nada (ADR-064): los dos
manifests se piden en paralelo, con un tope de 15 segundos cada uno, y la pantalla sigue usable
mientras tanto. La regla de qué se baja es una sola: la base Comunidad y el catálogo del Buscador se
bajan solos apenas se sabe que cambiaron; los packs de imágenes, todos, se bajan a mano y la pantalla
avisa cuando falta alguno, con un botón que baja de una todos los pendientes (ADR-065). JUGAR solo **aplica** la base que ya está en caché —pisar el
`database.xml` con el juego abierto no se hace— y, si la búsqueda sigue, espera a que la base esté,
no al catálogo.

## 3. Datos externos: los dos manifests

El launcher consume **dos** manifests distintos, con reglas de confianza distintas.

El **manifest del launcher** vive en nuestro bucket R2 y su URL —`AppConstants.DefaultManifestUrl`—
es editable por el usuario desde Configuración, con el default hardcodeado y restaurable. Trae la
versión más reciente del launcher (solo el número: el link de descarga jamás sale de acá), la lista
de bases, el pack de imágenes recomendado, una sección opcional `catalog` y un `news[]`
todavía sin usar. Todas sus rutas son **relativas**: se resuelven contra la URL del manifest
exigiendo mismo esquema, host y puerto, y que queden dentro de su directorio. Un manifest no puede
mandar al launcher a descargar de otro dominio ni escapar con `../`.

El **manifest del pool** es el del CDN de la app Android (canal `production`), y su URL está
**hardcodeada**. Publica una lista plana de paquetes con URL absoluta y SHA-256 —el hash se agregó a
ese pipeline específicamente para el launcher; la app Android ignora el campo—. De acá salen los
packs de imágenes oficiales por arco (Lotus, Samurai, Celestial, Emperor, Ivory, Twenty Festivals,
Promo) y, desde v0.2.0, el catálogo del Buscador. El pool publica **también** el pack Samurai
Extended, que es el mismo archivo que la sección `images` de nuestro manifest: el launcher lo
reconoce por hash y lo muestra en el listado como uno más, marcado como recomendado, manteniendo en
sincronía los dos registros de "aplicado" que tuvo históricamente
([ADR-048](../DECISIONS.md#adr-048), [ADR-055](../DECISIONS.md#adr-055)). Como sus URLs son absolutas se
validan distinto: se exige **mismo origen** que el manifest, sin exigir mismo directorio, porque el
pool publica el manifest en `/production/` y los archivos en `/pool/`. La confianza sigue confinada
al mismo bucket.

Que el catálogo venga del pool y no de nuestro bucket es deliberado: evita mantener copias
sincronizadas a mano y hereda el pipeline de la app. La sección `catalog` del manifest propio quedó
como respaldo y puede retirarse.

## 4. Descarga y verificación

`DownloadService` implementa la regla de integridad: descarga a un archivo temporal `.part` mientras
calcula el SHA-256 al vuelo, lo compara contra el del manifest y **solo entonces** promueve el
archivo al destino final. Si el hash no coincide, lanza y borra el temporal: no queda un archivo a
medias que un arranque posterior pueda tomar por bueno. El progreso se reporta por
`IProgress<DownloadProgress>` para que la barra de la ventana principal muestre el avance real.

Decidir si hay que descargar algo no implica rehashear la caché en cada arranque: `config.json`
guarda la `version` y el `sha256` de cada cosa ya aplicada, y esa marca se compara contra el
manifest. Vale para la base de comunidad, para el pack de imágenes propio, para cada pack del pool y
para cada uno de los tres archivos del catálogo.

El `HttpClient` es único para todo el proceso, con un user-agent propio y timeout de 5 minutos: hay
packs de imágenes que pesan cientos de megas. Los manifests, que pesan unos KB, tienen un tope propio
de 15 segundos por pedido (`ManifestService.FetchTimeout`): con el de 5 minutos, una red colgada
dejaba la pantalla buscando actualizaciones sin fin (ADR-064). Como las descargas de fondo guardan la
config desde otro hilo, `ConfigService.Save` es con lock.

## 5. Archivos del juego: bases, backups e imágenes

Aplicar una base es la operación más delicada del launcher, porque pisa un archivo del usuario.
`DatabaseService.ApplyDatabase` primero copia el `database.xml` vigente a
`backups\database.<timestamp>.xml` —con milisegundos y un contador de unicidad, para que dos cambios
seguidos no se pisen— y recién después copia encima la base elegida. La "Original" nunca entra en
esa rotación: se capturó una sola vez y se conserva intacta.

La base de comunidad puede publicarse como `.xml` crudo o como `.zip`. Si es zip, el hash se verifica
sobre el zip descargado y después `ZipUtil.ExtractSingleXml` saca el XML: exige que haya
**exactamente un** `.xml` adentro y rechaza el archivo si hay cero o varios, para que un zip ambiguo
no decida por su cuenta qué se copia sobre la instalación.

Las imágenes son **una sola familia** y el manifest del pool es quien dice cuáles hay: todo lo que
tenga `type: "images"` es un pack, venga de un arco oficial o de la comunidad, y uno nuevo aparece solo
sin tocar el launcher (ADR-055). La sección `images` del manifest del bucket no significa "el pack de
la comunidad" sino **"este es el pack recomendado"** —una propiedad de un pack, no una categoría—; se
lo reconoce por hash, en el listado sale marcado y avisa aunque esté en 1.0. Como todos los packs, se
baja a mano: JUGAR solo aplica la base (ADR-064). En ambos casos `ImagePackService` extrae **entrada por
entrada** sobre `<SunAndMoon>\images\`, resolviendo la ruta destino final y verificando que caiga
dentro de esa carpeta antes de escribir: es la protección zip-slip, obligatoria desde el día uno. La
comparación se hace contra la raíz canónica con separador final, así `images-evil` no pasa por
`images`. Los packs se **fusionan** con lo que ya había (agregan y actualizan, no reemplazan la
carpeta) y, como pesan hasta ~280 MB, el zip se borra después de aplicarse: lo que queda es el
registro por hash en la config. Las imágenes le sirven al juego y al Buscador por igual.

Lanzar el juego es lo más simple del sistema: `Process.Start` sobre el exe que el usuario configuró.
El launcher no redistribuye Sun and Moon ni sabe nada de su instalación más allá de las cuatro rutas
que centraliza `InstallationService`.

## 6. Configuración y estado local

El estado del usuario se reparte entre `%APPDATA%` —lo que es configuración— y `%LOCALAPPDATA%` —lo
que es regenerable o pesado—. En el primero viven `config.json` (instalación, URL del manifest, base
seleccionada y aplicada, hashes cacheados, packs aplicados y **packs ya vistos**, que es lo que
permite distinguir un pack nuevo de uno viejo que nunca se bajó), `usercards.json` (favoritas y ocultas
del Buscador), `presets.json` (presets de filtros) y `decksmeta.json` (el sidecar de los mazos y las
listas). En el segundo, la caché de descargas —incluidas `catalog\` y `packs\`— y los backups.

Todas esas rutas salen de `AppConstants`, y ninguna se arma a mano en otro lado. El motivo es la
variable de entorno **`L5A_DATA_ROOT`**: si está seteada, todo el árbol de estado cuelga de ahí. Es
lo que le permite al self-test ejercitar el pipeline real sin tocar los datos del usuario, y deja
abierta la puerta a un uso portable. Está activa también en Release.

Las escrituras son atómicas en los dos casos que importan: la config va a `.tmp` y se reemplaza; las
descargas van a `.part` y se promueven. Nunca queda un estado intermedio en disco.

## 7. El catálogo de cartas

El Buscador **no** lee el `database.xml`. Consume los mismos tres artefactos que la app Android
—`cards.json` (dentro de `cards_db-X.Y.Z.zip`), `rules.json` y `filters.json`— porque el XML no tiene
campos que el Buscador necesita: las keywords, que son una derivación editorial con correcciones
manuales; las legalidades por printing; el flag `current`. Parsear el XML habría obligado a replicar
esas derivaciones y a mantenerlas sincronizadas con la app. Así, el `database.xml` sigue siendo para
el launcher lo que siempre fue: un blob opaco que se copia y se verifica.

Los tres archivos se sincronizan a `cache\catalog\` desde el pool (o desde la sección `catalog` como
respaldo), con nombre versionado —`cards_db-2.3.1.zip`— porque la URL del pool no lleva versión y
`CatalogStore` elige por versión más alta; las versiones viejas se borran al sincronizar.
`CatalogStore` acepta también un `cards-X.Y.Z.json` suelto, y trata `rules` y `filters` como
opcionales: sin `rules` no hay banlist, sin `filters` no hay orden canónico de sets ni autocomplete
de keywords, pero el Buscador abre igual. Si no hay `cards`, avisa y no abre.

`CatalogImporter` es un port 1:1 del importer de la app —`CardImporter` + `RulesImporter.applyRules`
+ la ingestión de `filters.json`— y produce el mismo estado final en memoria que un sync completo del
celular: 6.394 cartas, 48 sets, 838 códigos de keyword y los slots de holding inicial que después
usan los mazos.

## 8. El motor de búsqueda

`CardSearchEngine` es la traducción del SQL de `CardQueryCompiler` a predicados en memoria. No usa
SQLite: 6.394 cartas filtran en milisegundos con LINQ, y meter SQLite habría significado un binario
nativo, justo lo que rompe el exe único de Costura. La contrapartida es que las semánticas de SQLite
hay que replicarlas a mano, y están replicadas explícitamente: `LIKE` es un contains
case-insensitive **solo-ASCII**, `IN` y `=` comparan Ordinal (BINARY), los stats nulos van al final
del orden, los empates respetan el orden de inserción y el orden por Set usa el rank de printing en
lugar del alfabético.

La fidelidad no la garantiza compartir código: la garantizan **53 golden tests** generados
ejecutando la lógica real de la app sobre SQLite. Cada caso es un criteria serializado más los ids
esperados, y un flag que dice si el orden importa o solo el conjunto. Corren dentro del self-test.

De ahí sale la regla más importante para trabajar en este dominio: **los bugs conocidos de la app se
portan a propósito**. La rareza es case-sensitive, las keywords hacen substring contra el subtítulo,
los formatos tienen un mismatch entre `20F` y `TwentyFestivals`. Si el motor los "arreglara", los
expected dejarían de valer y se perdería la referencia contra la app. Lo que sí se arregla es la
**experiencia**: el chip de rareza manda `P` y `p`, así el usuario ve las 495 promos, y el motor
sigue intacto. Esa separación —semántica portada, interacción libre— es la que permite tener paridad
verificable y una UI de escritorio decente al mismo tiempo.

Sobre esa base, la ventana del Buscador ofrece filtros que aplican en vivo (sin modal ni botón
Aplicar), operadores de barra (`tx.`, `k.`, `c.`, `t.`), vista de grilla virtualizada o tabla con
orden por cabecera, expansión por impresión, favoritas y ocultas, un selector de **conjunto activo**
—una lista, un mazo, las favoritas o las ocultas— por el que se puede filtrar,
presets de filtros y varias ventanas simultáneas con `Ctrl+N`. Las imágenes se leen de la instalación
del usuario con la cadena de fallback de la app y un alias extra —la base referencia las promos como
`cards/P/` y el juego las guarda en `cards/Promo/`—; el Buscador es 100 % lectura y no toca ni la red
ni la instalación.

## 9. Mazos: repositorio y persistencia

`DeckRepository` es un port 1:1 de `DeckRepository.kt`, incluido el orden exacto de los chequeos de
`AddCard` —baneada, después slot de holding inicial, después tope de copias—, el manejo de
cantidades con `keepRowAtZero`, el import/export en el formato de texto de la app y los espejos de UI
de las specs (conteos Dynasty/Fate, slots sin cubrir, honor inicial, clan del primer stronghold).
Lo respaldan **25 golden tests** que replican la secuencia exacta del harness original, con los
mismos nombres de mazo y el string de export byte a byte. Hasta los nombres engañosos se conservan:
`NoActiveDeck` significa en realidad "carta o mazo inexistente", y se dejó así para poder leer el C#
contra el Kotlin.

Donde el launcher se aparta por completo de la app es en la **persistencia**. La app usa SQLite; acá
cada mazo es un `.txt` en `<SunAndMoon>\Decks\`, exactamente los archivos que el juego carga, y cada
lista un `.txt` en `<SunAndMoon>\Lists\`, la carpeta hermana que el juego no lee. El mazo
que se arma en el launcher se juega directo, sin exportar nada, y los mazos que el usuario ya tenía
aparecen solos la primera vez. Lo que el formato del juego no puede representar —notas, modo de
orden, fecha, orden en la lista, cuál es el mazo y la lista activos, y qué cartas están
recortadas— va a un sidecar `decksmeta.json` indexado por nombre de archivo: si se pierde, no se
pierde ningún mazo.

El **recorte** es el caso más claro de esa división: bajar una carta a 0 en el editor no la borra,
la deja en el mazo pero fuera de juego, para poder probar una versión sin perderla de vista. El
archivo del juego no tiene forma de expresar eso, y escribirle una marca propia sería arriesgar que
el juego no la entienda, así que la marca vive en el sidecar y el `.txt` sale limpio.

Las **etiquetas** siguen exactamente el mismo camino y son el otro extra del launcher sobre el
deckbuilder de la app. Cada mazo tiene su propio vocabulario —un nombre y un color de una paleta
cerrada— y cada carta lleva las que se le pongan: son el gesto de armar en la mesa, apilar las cartas
por lo que hacen. Al agrupar el centro por etiqueta, **una carta aparece en todas sus pilas**: la que
quita honor y además pide magistrados está en las dos, porque la pregunta al armar es "cuánto
deshonor tengo" y una pila que se saltea las compartidas contesta de menos. La consecuencia buscada
es que los conteos por sección **no** suman el mazo —son conjuntos que se solapan, no cajas—, y por
eso cada cabecera dice cuántas de sus cartas están también en otra. Una primera versión repartía las
cartas en secciones por combinación exacta, con el cruce como sección aparte: cerraba las cuentas y
contestaba la pregunta equivocada (ADR-057). Se guardan en el sidecar, por nombre de carta, y se
persisten **sin reescribir el `.txt`**: reescribir un archivo ajeno por un dato que al juego no le
significa nada es riesgo sin beneficio.

El **ajuste de oro** es la otra pieza que solo tiene el launcher (ADR-059): la app ordena las
personalities por el coste impreso, pero lo que se paga de verdad depende del clan del stronghold
—dos menos si es el clan propio, dos más si es uno de los otros cuatro clanes grandes—, así que ese
orden mentía con la regla apagada. La regla se recalcula contra el stronghold del momento en vez de
materializarse por carta —cambiar de stronghold reordena el mazo entero sin tocar el sidecar— y
admite un ajuste manual de −3 a +3 que la pisa. Con la regla prendida, el bloque de personalities se
parte además en escalones por coste **efectivo** (`DeckGold.CostSections`): al revés que las pilas
de etiquetas, esto sí es una partición —cada carta cae en un solo escalón y los conteos por sección
suman el bloque— porque el coste efectivo de una carta es uno solo.

De ahí sale una regla que gobierna todo el estado del usuario: **una carta se referencia por nombre,
nunca por su id de catálogo**. El id lo asigna quien genera la base de la comunidad y puede
reasignarse entre versiones; el nombre es lo único que el juego y el launcher acuerdan. Los mazos ya
lo cumplían por venir del formato del juego, y en la v0.6.0 se extendió a favoritas y ocultas, con
migración automática del archivo viejo.

**Hay un solo mazo activo y una sola forma de cambiarlo**: el seleccionado en la ventana de Mazos.
`DeckLibrary` es un singleton de proceso, así que todas las ventanas del Buscador —incluidas las que
abre `Ctrl+N`— comparten ese mazo y reaccionan a `ActiveDeckChanged`; el Buscador solo lo refleja con
nombre y conteos, y agrega cartas con `+`, doble click o `Ctrl+click`.

Esa decisión trae dos consecuencias que hubo que resolver. La primera es que **el nombre del mazo es
el nombre del archivo**: se sanitizan los caracteres inválidos, los duplicados se desambiguan con
`" (2)"`, renombrar mueve el archivo y borrar lo borra. La segunda es que Sun and Moon escribe los
nombres de carta distinto del catálogo —strongholds sin el sufijo `(1)`, case cambiado, `- exp`
omitido—, así que el parser de `.txt` usa una resolución tolerante (`GetCardByNameRelaxed`) que el
`ImportCards` **no** usa: ese mantiene el match exacto de la app, porque de él dependen los goldens.
Al reescribir un archivo, cada entrada conserva el nombre con el que vino y las líneas que no se
reconocieron se copian verbatim: el launcher nunca le rompe el mazo al juego.

El `.txt` admite además dos anotaciones del juego que el launcher no entendía: el prefijo
`Proxy <tipo>: `, que hace que Sun and Moon apoye la carta en la mesa en vez de meterla en el mazo,
y una edición entre paréntesis (`Productive Mine (GoC)`). `DeckCardEntry` las lee y las devuelve
intactas, sin construir un modelo de "carta en la mesa" que el deckbuilder no tiene, y los starter
holdings salen siempre marcados `Proxy holding: ` para tenerlos sobre la mesa desde el arranque sin
depender de una opción del juego (ADR-060). El portapapeles las lleva como opción, nunca por
default: duplicar mazo reutiliza el export limpio y copiar a mano para otro mazo sí las incluye.

Las cartas del mazo se filtran además con **los mismos filtros que el Buscador** —keywords, texto de
carta, tipo, clan, designador, stats, edición y rareza—, porque el panel se extrajo a un control
compartido (`CardFilterPanel`) en vez de copiarse: copiado, el primer filtro nuevo habría entrado en
una sola ventana. El control no busca nada; llena un `CardSearchCriteria` y avisa que cambió. El
editor corre el **mismo motor** contra el catálogo entero y se queda con los ids que además están en
el mazo, así que la semántica de filtros es literalmente la misma —la que protegen los 53 goldens— y
no hay una segunda implementación que se despegue. Adentro del mazo se fuerzan dos criterios: las
baneadas y las ocultas **siempre entran**, porque son filtros para navegar el catálogo y sobre un mazo
lo único que podrían hacer es esconder cartas que el mazo tiene de verdad (ADR-058).

No hay motor de validación, igual que en la app: solo los rechazos de `AddCard` y los indicadores
blandos (Dynasty/Fate en rojo si bajan de 40, "Falta: {slot}"). Dos strongholds siguen siendo
posibles. La única divergencia deliberada es que acá el rechazo **se informa** —la app lo ignora en
silencio—, porque un `+` que no hace nada sin explicar por qué es un bug de experiencia, no paridad.

Los mazos se editan en su ventana propia y también desde el Buscador, que tiene un panel derecho con
el mazo activo y botones `+`/`−` por carta. Ambas ventanas comparten la misma instancia de
`DeckLibrary`, así que comparten el mazo activo y ven los mismos archivos. Las **listas** son el
mismo modelo con otro tipo: se editan con el mismo editor, tienen tope de una copia por carta y sus
mutaciones no pasan por `AddCard` —una lista es material de armado, no un mazo legal, y no le aplican
ni la banlist ni la regla de slot inicial—.

Del mazo sale además un **PDF para imprimir proxies**: nueve cartas por hoja en una grilla 3×3
centrada en A4 o Carta, cada una a su tamaño físico real de 63×88 mm, con un margen opcional de
1 mm entre cartas. En A3 la hoja sale apaisada y la grilla pasa a 6×3 = 18, exactamente el doble
—es la lectura literal de qué es un A3: dos A4 verticales uno al lado del otro, sin rotar la carta
(ADR-061). Una pantalla previa deja elegir cuántas copias imprimir de cada carta, arrancando
en la cantidad que tiene el mazo; las recortadas no participan. `PdfExporter` **emite el archivo a
mano**, sin librería de PDF: el documento no tiene una sola letra, y como las imágenes del juego ya
son JPEG, sus bytes pasan al PDF sin decodificar ni recomprimir vía el filtro `/DCTDecode`, así que
la impresión conserva la calidad exacta del original. Lo que ese filtro no acepta —JPEG progresivo o
CMYK— se recodifica antes; de las 8856 imágenes de una instalación completa, 8 caen en ese caso. El
JPEG de una carta repetida se embebe una sola vez y se referencia N veces.

## 10. Presentación: ventanas, tema e idioma

Son cinco ventanas con dos criterios distintos. `MainWindow` es la pantalla de marca: fija en
810×540 (3:2), no redimensionable, con chrome propio; el diseño se compone en un lienzo de 1080×720 y
se escala con un `Viewbox`, así que todo lo que se agregue se dibuja en esas coordenadas.
`FirstRunWindow` y `PdfExportWindow` son diálogos modales. `SearchWindow` y `DeckWindow`, en cambio, son ventanas de
trabajo: 1323×798 por defecto, redimensionables y maximizables, con paneles laterales colapsables
calibrados para que en 1080p a 125 % entren la mayor cantidad de cartas posible. Esos paneles se
**arrastran** desde su borde y colapsan al pasarse del mínimo, y se agrupan **por objeto**: en el
editor, la izquierda es el mazo abierto (filtros y stats) y la derecha es la carta seleccionada;
elegir qué mazo abrir cuelga del título, no de un panel (ADR-051). Ambas se acotan al
**área de trabajo** del monitor al abrir y al maximizar (`WindowSizingFix`): con chrome propio,
Windows maximiza contra el monitor completo y la barra de tareas tapa el borde inferior.

Las dos ventanas de trabajo **se adaptan al ancho en dos escalones**: bajo 1000 DIP cierran sus
paneles laterales y apagan los rieles, y bajo 900 esconden lo accesorio de la barra —el mazo activo,
los conteos con el "Falta: {slot}"— porque con los paneles ya cerrados el piso lo pone la
barra de botones. Eso permite un mínimo de 460 y habilita el botón de **lado a lado**, que acomoda el
Buscador al 70 % y el editor al 30 % para armar mirando las dos cosas a la vez. En la barra queda solo
lo que se usa seguido: el tamaño de celda, el orden y el CRUD de listas viven en el componente que les
corresponde (ADR-050). La grilla de cartas
tiene cuatro tamaños de celda (con `Ctrl + rueda`), y con **Alt** apretado pasar el mouse sobre una
carta la abre grande al lado del cursor, garantizando que el popup **nunca quede debajo del cursor**
—si queda, tapa la celda y arranca un parpadeo infinito— (ADR-052).

El Buscador y el editor **abren juntos** (`OpenWorkspace`): los `+`/`−` escriben contra el mazo activo
y el editor es el único lugar donde se lo ve. El editor es único por proceso, de Buscadores puede haber
varios, y abrir juntos no es cerrar juntos: cerrar el editor cierra solo el editor y `Ctrl+D` lo
reabre. Las ventanas que abren otra la corren en diagonal, para que no se tapen al píxel (ADR-052).

Las miniaturas de la grilla se decodifican en una **cola propia** —tres workers, atendida LIFO para que
lo que está en pantalla gane— y no con los bindings `IsAsync` de WPF, que las despachaban a cuentagotas:
medido, la primera miniatura tardaba 23 segundos con la caché caliente y el proceso ocioso (ADR-053).

Los controles siguen una taxonomía explícita —una forma por rol— en vez del chip universal de la
primera versión: rieles verticales para colapsar paneles, pestañas adosadas para cambiar el contenido
de un panel, segmentados para selección exclusiva, píldoras solo para filtros on/off, y menús
desplegables para las acciones ocasionales. El arte de carta se dibuja como `ImageBrush` sobre un
borde redondeado, sin marco: los escaneos son rectangulares y las esquinas blancas asomaban. Algunos
`.jpg` del pool traen un DPI horizontal absurdo (`XResolution=1`) y WPF los compone en franjas: se
renormalizan a 96×96 al decodificar, sin tocar el archivo, que es ajeno (ADR-063).

No se usa ningún `MessageBox` del sistema. En `MainWindow` los errores, confirmaciones, Configuración
y el menú de packs son overlays dentro del lienzo escalado; el Buscador y el editor, que no tienen
overlay propio, usan `Views/Dialogs.cs` (`Prompt`/`Confirm`/`Info`), ventanas modales con el tema.
Cerrar `MainWindow` cierra la aplicación entera y por eso confirma primero; las otras ventanas se
cierran solas. La pantalla principal lleva además un **aviso de novedades** que siempre dice algo —lo
que falta bajar, o "Todo al día"—, con una regla explícita de qué es novedad: un pack de imágenes en su
versión inicial no lo es, salvo que sea un pack **nuevo**, que aparece después de la primera vez que
este launcher vio el pool, o que sea el **recomendado** sin bajar (ADR-054, ADR-056, ADR-064). Mientras
corre la búsqueda dice "Verificando…" en vez de adelantar un "Todo al día". La estética —pergamino cálido, rojo lacado
para la acción, oro decorativo, tipografía de marca L5RTitles— vive en `Themes/Theme.xaml`, que
además tematiza hasta las scrollbars; los estilos que comparten el Buscador y el editor están en
`Views/CardUiStyles.xaml`, y el panel de detalle de carta y el de filtros son controles
compartidos por las dos ventanas.

El texto de las cartas viene en un HTML acotado que se renderiza a `Inline`s de WPF con
`CardTextRenderer`. No hay navegador embebido en ninguna parte, y no lo va a haber: sería convertir
contenido descargado en superficie de ejecución.

Todos los textos de UI —481 claves— viven en `Strings.resx`, usados con `{loc:Loc Clave}` en XAML y
`Strings.Get/Format` en C#. No hay literales de UI en el código. Con eso, agregar un idioma es crear
`Strings.<cultura>.resx` y setear `CurrentUICulture`, sin tocar una línea. Hasta los nombres de los
packs de imágenes salen de claves `Pack_<id>` con fallback al id crudo, así un pack nuevo publicado
en el manifest aparece en la UI sin necesidad de recompilar.

Una nota para quien venga a modificar la UI: no hay MVVM. `SearchWindow.xaml.cs` (~1920 líneas) y
`DeckWindow.xaml.cs` (~2210) concentran estado y presentación en code-behind, mientras los servicios
de dominio no saben nada de la UI. Es la convención vigente, no una decisión registrada. La excepción
son los dos controles compartidos —el detalle de carta y el panel de filtros (~590 líneas)—, que sí
tienen una interfaz explícita con su dueño porque los usan dos ventanas.

## 11. Seguridad como propiedad transversal

Seis reglas ordenan todo lo anterior, y ninguna es negociable: solo datos y nunca código; SHA-256 de
todo lo descargado verificado antes de usarlo; HTTPS siempre, con TLS 1.2 forzado y el escape a
`file://`/localhost únicamente en builds DEBUG; confinamiento al origen del manifest; protección
zip-slip obligatoria al extraer; y link de actualización hardcodeado en el exe.

La última merece su explicación. El launcher **notifica pero no instala**: si el manifest dice que
hay una versión más nueva, se muestra un banner con un link que está embebido en el ejecutable. Si
ese link viniera del manifest, un manifest comprometido se convertiría en vector de phishing hacia un
exe malicioso — el único punto de todo el sistema donde el daño sería ejecución de código. Por lo
mismo, el exe se publica solo en GitHub Releases, que exige 2FA y deja historial, y nunca en el
bucket R2, que se sobrescribe con un API token sin dejar rastro; y su SHA-256 se publica por un
segundo canal, de modo que falsificarlo exige comprometer dos canales independientes.

Hay hardening que se difirió con criterio explícito, no por olvido: el manifest **no está firmado**,
no hay topes anti zip-bomb al extraer y la URL del manifest acepta cualquier HTTPS. Los tres protegen
la integridad de los **datos** ante un bucket comprometido; ninguno cambia el techo del daño, que
sigue siendo "cartas falsas" gracias a la primera regla. El riesgo dominante real del proyecto son
las credenciales del mantenedor, y eso está cubierto con 2FA en GitHub y Cloudflare, token R2 con
scope mínimo, secret scanning y branch protection.

## 12. Verificación y entrega

La verificación es un harness embebido, no un proyecto de tests: `--selftest`, compilado solo en
DEBUG, setea `L5A_DATA_ROOT` a una carpeta temporal, monta un bucket y una instalación falsos en
`%TEMP%`, corre el pipeline **real** y reporta PASS/FAIL con exit code. Son un par de centenares de
checks —el número al día vive en [build-verificacion.md](../index/build-verificacion.md)—: el
pipeline de descarga y aplicación (hash correcto, hash inválido que aborta sin dejar archivo, rechazo
de origen ajeno y de `../`, zip-slip, base con un solo `.xml`, backups), los 53 golden del Buscador,
los 25 del deckbuilder y la persistencia de mazos en `.txt`. Corre en CI en cada push y PR a `main`.

Tocar red, descarga, extracción, el motor del Buscador o el `DeckRepository` obliga a correr el
self-test y dejarlo en verde. Si el cambio *pretende* modificar una semántica portada, primero se
regeneran los expected afectados con el harness del handoff y se anota qué cambió; recién ahí el
verde vale.

La entrega es un tag `vX.Y.Z`: el workflow de release compila en Release, firma con SignPath si el
secret está cargado, calcula el SHA-256 y publica el exe junto con su hash en GitHub Releases. La
firma está cableada pero todavía inactiva —falta la aprobación de la SignPath Foundation—, así que
por ahora el primer arranque muestra el aviso de SmartScreen, documentado para los usuarios.

## 13. Límites conocidos

Las **noticias y torneos** existen como sección placeholder en la UI y como modelo `NewsEntry` /
`NewsService`, pero no están implementadas: falta definir el formato de los `.md` y la sección `news`
del manifest. Cuando se hagan, se renderizan con Markdig a `FlowDocument`; nunca en un WebView.

El **Goldfish** (sandbox de testeo de mazos) y los grupos de usuario están en el handoff del
deckbuilder pero no se portaron; los subgrupos dentro del mazo (`deck_card_groups`, un grupo
excluyente por carta) quedaron descartados: las etiquetas cubren el caso con un modelo mejor. Falta
poder etiquetar varias cartas de una: hoy es carta por carta, porque el centro del editor no tiene
modelo de selección múltiple. En el Buscador falta el
contador de cartas en el selector de conjunto y poder renombrar una lista sin ir a la ventana de
Mazos; favoritas y ocultas se filtran y se copian a una lista, pero no se editan dentro del editor.

Y sobre el propio launcher: sigue **sin firmar** y el manifest sigue **sin firmar**. Las constantes
de URL de `AppConstants` apuntan a producción; falta confirmar con el mantenedor que son las
definitivas.

Todo eso, con su estado y prioridad, está en [../ROADMAP.md](../ROADMAP.md).
