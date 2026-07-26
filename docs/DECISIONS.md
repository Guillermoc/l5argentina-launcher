# DECISIONS.md — registro de decisiones de arquitectura (ADR)

Un archivo, se **agrega al final**, nunca se reescribe la historia. Cada entrada dice qué se decidió
y **por qué**; si una decisión se revierte, se agrega una nueva que la supersede (y se marca la
vieja como *Superseded por ADR-XXX*).

Se escribe una entrada cuando se decide algo **no obvio**: un tradeoff, un camino descartado, una
regla que un agente futuro podría "arreglar" sin saber por qué está así.

Las fechas son las del commit que materializó la decisión. Los ADR-001 a ADR-011 se registraron
retroactivamente (2026-07-26) a partir de la spec funcional v1.0, los comentarios del código y el
backlog; su fecha es la del momento en que se tomaron.

| # | Decisión | Fecha | Estado |
|---|---|---|---|
| [001](#adr-001) | Solo datos, nunca código | 2026-06-12 | Vigente (innegociable) |
| [002](#adr-002) | WPF sobre .NET Framework 4.8 con csproj SDK-style | 2026-06-12 | Vigente |
| [003](#adr-003) | Un solo `.exe` con Costura.Fody | 2026-06-13 | Vigente |
| [004](#adr-004) | `System.Text.Json` en vez de Newtonsoft | 2026-06-13 | Vigente |
| [005](#adr-005) | La base "Original" se respalda local, no se hostea | 2026-06-13 | Vigente |
| [006](#adr-006) | Integridad por SHA-256 con descarga a `.part` | 2026-06-13 | Vigente (innegociable) |
| [007](#adr-007) | Confinamiento al origen del manifest | 2026-06-13 | Vigente (innegociable) |
| [008](#adr-008) | URL del manifest configurable, default hardcodeado | 2026-06-13 | Vigente |
| [009](#adr-009) | Exe por GitHub Releases; link de update hardcodeado | 2026-06-13 | Vigente (innegociable) |
| [010](#adr-010) | `L5A_DATA_ROOT` como hook de testing/portabilidad | 2026-06-13 | Vigente |
| [011](#adr-011) | Self-test embebido en vez de proyecto de tests | 2026-06-13 | Vigente |
| [012](#adr-012) | i18n total en `Strings.resx` con un solo idioma | 2026-06-14 | Vigente |
| [013](#adr-013) | Ventana fija 810×540 con `Viewbox` + overlays propios | 2026-06-14 | Vigente |
| [014](#adr-014) | TLS solo 1.2 y base `.zip` con exactamente un `.xml` | 2026-06-14 | Vigente |
| [015](#adr-015) | Diferir firma del manifest y hardening de datos | 2026-06-14 | Vigente (revisable) |
| [016](#adr-016) | El Buscador usa `cards.json`, no el `database.xml` | 2026-07-07 | Vigente |
| [017](#adr-017) | Motor de búsqueda en memoria, sin SQLite | 2026-07-07 | Vigente |
| [018](#adr-018) | Los bugs de la app se portan; la UI compensa | 2026-07-07 | Vigente |
| [019](#adr-019) | UX de escritorio, no port de la UI móvil | 2026-07-07 | Vigente |
| [020](#adr-020) | Imágenes leídas de la instalación, con fallbacks | 2026-07-07 | Vigente |
| [021](#adr-021) | El pool (CDN de la app) como fuente primaria | 2026-07-07 | Vigente |
| [022](#adr-022) | Los mazos se persisten como `.txt` del juego | 2026-07-11 | Vigente |
| [023](#adr-023) | Resolución tolerante de nombres solo en el parser txt | 2026-07-11 | Vigente |
| [024](#adr-024) | Las líneas no reconocidas nunca se pierden | 2026-07-11 | Vigente |
| [025](#adr-025) | Sin UX de "recorte" (`keepRowAtZero` sin usar) | 2026-07-11 | Vigente |
| [026](#adr-026) | Validación = la de la app, pero el rechazo se informa | 2026-07-11 | Vigente |
| [027](#adr-027) | `NoActiveDeck` conserva su nombre engañoso | 2026-07-11 | Vigente |
| [028](#adr-028) | Listas ≠ mazos | 2026-07-11 | Vigente |
| [029](#adr-029) | Duplicar mazo, extra que la app no tiene | 2026-07-11 | Vigente |
| [030](#adr-030) | Extras de `SortField` sin cobertura golden | 2026-07-11 | Vigente |
| [031](#adr-031) | Documentación en cuatro capas + as-built | 2026-07-26 | Vigente |
| [032](#adr-032) | El selftest se corre al cerrar, no durante | 2026-07-26 | Vigente |
| [033](#adr-033) | Qué entra a la batería de verificación, y cómo se da de baja | 2026-07-26 | Vigente |

---

<a id="adr-001"></a>
## ADR-001 — Solo datos, nunca código

**Fecha:** 2026-06-12 · **Estado:** vigente, innegociable.

**Contexto.** El launcher corre en ~25 máquinas de la comunidad y descarga archivos de un bucket
operado por una sola persona. El riesgo dominante del threat model no es un externo tocando el
bucket, sino el robo de credenciales del mantenedor.

**Decisión.** El launcher nunca descarga ni ejecuta código: solo XML, JSON, ZIP de imágenes y (a
futuro) MD. Sin auto-update del propio exe, sin plugins, sin scripts, sin navegador embebido.

**Por qué.** Acota el peor caso de un bucket comprometido a "datos falsos" — cartas o noticias
mentirosas — en vez de ejecución de código en la PC del usuario. Todas las demás mitigaciones
(hash, HTTPS, confinamiento) suben la vara, pero esta es la que define el techo del daño.

**Consecuencias.** Las noticias v2 se renderizan con Markdig → `FlowDocument`, nunca WebView. El
texto HTML de las cartas se renderiza a `Inline`s propios (`CardTextRenderer`). Cualquier feature que
implique ejecutar lo descargado se rechaza de entrada.

---

<a id="adr-002"></a>
## ADR-002 — WPF sobre .NET Framework 4.8 con csproj SDK-style

**Fecha:** 2026-06-12 · **Estado:** vigente.

**Contexto.** Los usuarios son jugadores, no técnicos; algunos están en Windows 10 LTSC. Instalar un
runtime sería fricción real. La alternativa moderna (Avalonia + .NET 8) exigía instalar runtime o
publicar self-contained.

**Decisión.** Target `net48` (preinstalado en toda Windows 10/11), UI en WPF, csproj **SDK-style**
compilado con la CLI `dotnet` (sin Visual Studio).

**Por qué.** Cero runtime que instalar + toolchain moderno. Sun and Moon es Windows-only, así que
multiplataforma no aporta nada.

**Consecuencias.** Se usa `Microsoft.NETFramework.ReferenceAssemblies` para no depender del Developer
Pack. Algunas APIs modernas no están disponibles y `SecurityProtocolType.Tls13` no se puede usar
(no garantizado en net48).

---

<a id="adr-003"></a>
## ADR-003 — Un solo `.exe` con Costura.Fody

**Fecha:** 2026-06-13 · **Estado:** vigente.

**Decisión.** Empaquetar con Costura.Fody: los DLLs de dependencias se embeben como recursos.
Se distribuye **únicamente** `L5ArgentinaLauncher.exe`.

**Por qué.** "Bajá este archivo y hacé doble clic" es la instrucción más simple posible para la
comunidad; una carpeta con DLLs sueltos invita a que se copie mal o se pierda.

**Consecuencias.** Hacen falta binding redirects para la cadena de `System.Text.Json`
(`AutoGenerateBindingRedirects`). Nada de dependencias nativas: es parte de por qué el motor de
búsqueda no usa SQLite ([ADR-017](#adr-017)).

---

<a id="adr-004"></a>
## ADR-004 — `System.Text.Json` en vez de Newtonsoft

**Fecha:** 2026-06-13 · **Estado:** vigente.

**Decisión.** Todo el JSON (manifests, config, stores) usa `System.Text.Json` (paquete NuGet en net48).

**Por qué.** Es el estándar actual de Microsoft y evita arrastrar Newtonsoft solo por costumbre.

**Consecuencias.** Los modelos anotan `[JsonPropertyName]` explícito (el manifest usa `snake_case` en
`latest_version`) y el parseo del manifest va con `PropertyNameCaseInsensitive = true`.

---

<a id="adr-005"></a>
## ADR-005 — La base "Original" se respalda local, no se hostea

**Fecha:** 2026-06-13 · **Estado:** vigente.

**Contexto.** El launcher ofrece jugar con la base de la comunidad o con la original de Sun and Moon.
Hostear la original en el bucket implicaba redistribuir datos de terceros y mantener una copia más.

**Decisión.** En el primer arranque, antes de tocar nada, se copia el `database.xml` del usuario a
`backups\database.original.xml`. Esa copia **es** la base "Original".

**Por qué.** Evita redistribuir contenido ajeno y garantiza que "volver atrás" devuelve exactamente
lo que el usuario tenía.

**Consecuencias.** Si el usuario instala el launcher sobre una instalación ya modificada, su
"Original" no es limpia: por eso existe la heurística `LooksLikeCommunityBase` (stream-scan de
`s_extended`, sin parsear) y el aviso del primer arranque. El backup nunca se sobrescribe.

---

<a id="adr-006"></a>
## ADR-006 — Integridad por SHA-256 con descarga a `.part`

**Fecha:** 2026-06-13 · **Estado:** vigente, innegociable.

**Decisión.** Todo archivo descargado se baja a `<destino>.part`, se le calcula el SHA-256 al vuelo y
recién se promueve al destino final si coincide con el del manifest. Si no coincide: excepción y
**no queda archivo**.

**Por qué.** Protege integridad en tránsito y ante compromisos parciales, y evita dejar archivos a
medias que un arranque posterior tome por buenos.

**Consecuencias.** El estado "¿hay que redescargar?" se decide comparando el hash del manifest contra
el guardado en `config.json`, sin re-hashear la caché en cada arranque. El hash **no** protege contra
un origen comprometido que cambie archivo y manifest a la vez: eso lo cubriría firmar el manifest
([ADR-015](#adr-015)).

---

<a id="adr-007"></a>
## ADR-007 — Confinamiento al origen del manifest

**Fecha:** 2026-06-13 · **Estado:** vigente, innegociable.

**Decisión.** Los `file` del manifest del launcher son **relativos** y se resuelven contra la URL del
manifest, exigiendo mismo esquema+host+puerto y quedar bajo su directorio (`ResolveFileUri`). Las
URLs **absolutas** del manifest del pool se aceptan solo si son del **mismo origen**
(`ResolveSameOriginUrl`), sin exigir mismo directorio porque el pool publica el manifest en
`/production/` y los archivos en `/pool/`.

**Por qué.** Apuntar el manifest a otro origen cambia *todo* el origen de datos de forma coherente y
visible, en vez de permitir un manifest que mezcle nuestro bucket con descargas de un tercero.

**Consecuencias.** Un manifest no puede referenciar dominios ajenos ni escapar con `../`. Está
cubierto por checks del self-test (rechazo de otro origen y de `../`).

---

<a id="adr-008"></a>
## ADR-008 — URL del manifest configurable, con default hardcodeado

**Fecha:** 2026-06-13 · **Estado:** vigente.

**Decisión.** El usuario puede cambiar la URL del manifest desde Configuración; el default está
hardcodeado en el exe y se puede restaurar. Se exige HTTPS.

**Por qué.** Sirve para testing, migración de bucket o variantes de la comunidad. El riesgo agregado
(ingeniería social: "pegá esta URL de soporte") queda acotado por [ADR-001](#adr-001) y
[ADR-007](#adr-007): el peor caso sigue siendo datos falsos.

**Consecuencias.** El link de update **no** es configurable ([ADR-009](#adr-009)). Cerrar del todo
este vector requiere firmar el manifest (SEC-5 → SEC-1, diferido).

---

<a id="adr-009"></a>
## ADR-009 — Exe por GitHub Releases; link de update hardcodeado

**Fecha:** 2026-06-13 · **Estado:** vigente, innegociable.

**Decisión.** El ejecutable se publica **solo** en GitHub Releases, nunca en el bucket R2. El aviso
"hay versión nueva" usa `AppConstants.ReleasesPageUrl`, embebida en el exe; el manifest solo aporta
el número de versión. El SHA-256 del exe se publica en un segundo canal.

**Por qué.** Reemplazar el exe en GitHub exige cuenta con 2FA y deja historial y notificación; el
bucket R2 se sobrescribe con un API token de forma silenciosa. Y si el link viniera del manifest, un
manifest comprometido se convertiría en vector de phishing hacia un exe malicioso — el único punto
donde el daño sería ejecución de código.

**Consecuencias.** El launcher **notifica, no instala**. Falsificar el exe sin que se note exige
comprometer dos canales independientes (GitHub y Cloudflare/git).

---

<a id="adr-010"></a>
## ADR-010 — `L5A_DATA_ROOT` como hook de testing/portabilidad

**Fecha:** 2026-06-13 · **Estado:** vigente.

**Decisión.** Si la variable de entorno `L5A_DATA_ROOT` está seteada, config, caché, backups y la
carpeta `Decks` de fallback cuelgan de ahí en vez de `%APPDATA%`/`%LOCALAPPDATA%`. Está **en Release
también**, no solo en DEBUG.

**Por qué.** Permite que el self-test ejercite el pipeline real sin tocar los datos del usuario, y
deja la puerta abierta a un uso portable (todo en un pendrive).

**Consecuencias.** Ninguna ruta se arma a mano fuera de `AppConstants`: si se agrega estado nuevo,
tiene que salir de ahí o se rompe el aislamiento del self-test.

**Límite explícito:** la variable mueve el estado **propio** del launcher, no los archivos del juego.
El `database.xml`, `images\` y los `.txt` de `Decks\` cuelgan de la instalación configurada y no se
redirigen nunca — redirigir `Decks` rompería que el juego lea el mazo ([ADR-022](#adr-022)). O sea
que `L5A_DATA_ROOT` **no** alcanza para aislar una prueba manual de mazos: para eso hay que apuntar
`sunAndMoonPath` a una copia del juego o dejarlo vacío. (Aclarado el 2026-07-26, después de que un
health check detectara que la documentación prometía una protección que no existía.)

---

<a id="adr-011"></a>
## ADR-011 — Self-test embebido en vez de proyecto de tests

**Fecha:** 2026-06-13 · **Estado:** vigente.

**Contexto.** Lo crítico a verificar es el pipeline real (red → hash → extracción → escritura sobre
la instalación), no unidades aisladas. Un proyecto de tests separado obligaba a un segundo csproj y a
exponer internals.

**Decisión.** Un harness `--selftest` compilado **solo en DEBUG** que monta bucket e instalación
falsos en `%TEMP%`, corre el pipeline real y reporta PASS/FAIL con exit code. Corre en CI.

**Por qué.** Verifica lo que importa de punta a punta, no cuesta nada en el exe de Release y es una
sola orden que cualquiera (persona o agente) puede correr.

**Consecuencias.** El harness creció hasta 177 checks e incorporó los golden tests de paridad. Es el
criterio de aceptación de cualquier cambio en red/descarga/extracción, motor del Buscador o
`DeckRepository`. Si el exe corre fuera del repo, los bloques golden se omiten (SKIP): un reporte
válido no trae FAIL ni SKIP (el total sube con la batería: ver [ADR-033](#adr-033)).

---

<a id="adr-012"></a>
## ADR-012 — i18n total en `Strings.resx` con un solo idioma

**Fecha:** 2026-06-14 · **Estado:** vigente.

**Decisión.** **Todos** los textos de UI viven en `src/Resources/Strings.resx` (español, cultura
neutral), usados con `{loc:Loc Clave}` en XAML y `Strings.Get/Format` en C#. No hay literales de UI
en el código.

**Por qué.** Agregar un idioma después implica crear `Strings.<cultura>.resx` y setear
`CurrentUICulture`, sin tocar código. Además centraliza la corrección de textos (que en un proyecto
de comunidad se ajustan seguido) en un solo archivo.

**Consecuencias.** Texto nuevo ⇒ clave nueva, siempre. Los nombres de packs de imágenes se resuelven
por convención `Pack_<id>` con fallback al id crudo, así un pack nuevo del manifest no rompe la UI.

---

<a id="adr-013"></a>
## ADR-013 — Ventana fija 810×540 con `Viewbox` + overlays propios

**Fecha:** 2026-06-14 · **Estado:** vigente.

**Decisión.** La ventana principal es fija, no redimensionable, con chrome propio. El diseño se
compone en un lienzo de **1080×720** y se escala con un `Viewbox` a 810×540 (3:2). Errores,
confirmaciones, Configuración y Packs de imágenes son overlays dentro de la ventana, no `MessageBox`
del sistema.

**Por qué.** Estética de marca coherente (pergamino Rokugán) y un layout que no hay que rediseñar
para cada tamaño. El `MessageBox` nativo rompe la estética y no se puede tematizar.

**Consecuencias.** Todo lo que se agregue a la ventana principal se dibuja en coordenadas de
1080×720. El Buscador y el editor **sí** son redimensionables: son ventanas de trabajo, no la
pantalla de marca.

---

<a id="adr-014"></a>
## ADR-014 — TLS solo 1.2 y base `.zip` con exactamente un `.xml`

**Fecha:** 2026-06-14 · **Estado:** vigente.

**Decisión.** `ServicePointManager.SecurityProtocol = Tls12` (sin 1.1/1.0, que estaban habilitados
antes) y `ZipUtil.ExtractSingleXml` rechaza un zip de base con 0 o más de un `.xml`.

**Por qué.** Coincidir con lo documentado (SEC-2) y no dejar que un zip ambiguo decida por su cuenta
qué archivo se copia sobre el `database.xml` del usuario (SEC-4).

**Consecuencias.** `Tls13` no se usa: el valor del enum no está garantizado en net48 y Cloudflare
negocia 1.2 sin problema.

---

<a id="adr-015"></a>
## ADR-015 — Diferir la firma del manifest y el hardening de datos

**Fecha:** 2026-06-14 · **Estado:** vigente, revisable.

**Contexto.** El análisis de seguridad levantó SEC-1 (manifest sin firmar), SEC-3 (sin topes
anti zip-bomb) y SEC-5 (URL de manifest arbitraria).

**Decisión.** Se difieren los tres. El foco queda en firmar el **exe** (SEC-6, SignPath).

**Por qué.** El criterio del mantenedor es que lo crítico es que el launcher **no comprometa la PC**
del usuario, y eso ya está cubierto ([ADR-001](#adr-001), [ADR-006](#adr-006), [ADR-007](#adr-007),
zip-slip, escrituras acotadas con backups). Lo que falta protege la **integridad de los datos** ante
un bucket comprometido: el peor caso ahí es "cartas falsas o que algo se rompa", no ejecución de
código. Las credenciales ya están con 2FA en GitHub y Cloudflare.

**Consecuencias.** Se revisa si el bucket pasa a ser crítico o si lo opera más gente. SEC-1 cerraría
de paso SEC-5. El plan técnico de cada uno está guardado en [ROADMAP.md](ROADMAP.md).

---

<a id="adr-016"></a>
## ADR-016 — El Buscador usa `cards.json`, no el `database.xml`

**Fecha:** 2026-07-07 · **Estado:** vigente.

**Contexto.** El launcher ya distribuye el `database.xml`; parecía natural que el Buscador leyera de
ahí.

**Decisión.** El Buscador consume `cards.json` + `rules.json` + `filters.json` — los mismos
artefactos que la app Android — y **no** el XML.

**Por qué.** El XML no tiene campos que el Buscador necesita: keywords (derivación editorial con
correcciones manuales), legalidades por printing, flag `current`. Parsearlo obligaba a replicar
derivaciones frágiles y a mantenerlas sincronizadas con la app.

**Consecuencias.** El `database.xml` sigue siendo un blob opaco para el launcher (se copia y verifica,
nunca se parsea). Aparece una dependencia nueva: el catálogo hay que sincronizarlo
([ADR-021](#adr-021)) y sin él el Buscador no abre.

---

<a id="adr-017"></a>
## ADR-017 — Motor de búsqueda en memoria, sin SQLite

**Fecha:** 2026-07-07 · **Estado:** vigente.

**Decisión.** El SQL de `CardQueryCompiler` se porta a predicados LINQ en memoria
(`CardSearchEngine`), replicando las semánticas de SQLite: LIKE = contains case-insensitive
solo-ASCII, `IN`/`=` = Ordinal (BINARY), NULLs de stats al final, empates por orden de inserción.

**Por qué.** 6.394 cartas filtran en milisegundos; SQLite habría metido un binario nativo, con
fricción directa contra el exe único de Costura ([ADR-003](#adr-003)). La fidelidad no la garantiza
compartir motor, la garantizan los golden tests.

**Consecuencias.** Cambiar una semántica del motor implica regenerar los golden afectados con el
harness del contrato y anotarlo. Sin eso, el self-test se pone en rojo — que es exactamente la idea.

---

<a id="adr-018"></a>
## ADR-018 — Los bugs de la app se portan; la UI compensa

**Fecha:** 2026-07-07 · **Estado:** vigente.

**Contexto.** Las specs del contrato marcan con ⚠️ varios bugs de la app (rareza case-sensitive,
subtitle-substring en keywords, mismatch de formato `20F`/`TwentyFestivals`).

**Decisión.** El motor los porta tal cual. Los arreglos de UX se hacen en la **capa UI**: por
ejemplo, el chip de rareza manda `P` **y** `p`, así se ven las 495 promos sin tocar el motor.

**Por qué.** Los golden tests son la garantía de paridad: si el motor "arregla" un bug, los expected
dejan de valer y se pierde la referencia contra la app. Separando motor y UI se consiguen las dos
cosas: paridad verificable y una experiencia correcta.

**Consecuencias.** Al leer el motor hay que resistir la tentación de arreglar lo que parece un error:
si algo se ve mal en la UI, el arreglo va en la UI.

---

<a id="adr-019"></a>
## ADR-019 — UX de escritorio, no port de la UI móvil

**Fecha:** 2026-07-07 · **Estado:** vigente.

**Decisión.** La semántica se porta 1:1, la interacción no: filtros siempre visibles que aplican en
vivo (en vez del modal con draft + "Aplicar" del celular), tabla con columnas en vez de carousel,
detalle fijo a la derecha en vez de visor fullscreen, `Ctrl+N` para varias ventanas en vez de tabs.

**Por qué.** El patrón móvil existe por falta de espacio; en 1080p es fricción pura.

**Consecuencias.** Aparecen divergencias deliberadas y documentadas (p. ej. "Solo ocultas" no resetea
los demás filtros, cosa que en la app sí pasa). Las divergencias van anotadas; el criterio es que
ninguna cambie *qué cartas* devuelve una consulta.

---

<a id="adr-020"></a>
## ADR-020 — Imágenes leídas de la instalación, con fallbacks

**Fecha:** 2026-07-07 · **Estado:** vigente.

**Decisión.** El arte se lee de `<SunAndMoon>\images\...` con la cadena de fallback de la app
(`imagePath` → `printing_images` → placeholder de texto), más la carpeta del propio exe como raíz
alternativa. Se prueban `images/cards/P/` **y** `images/cards/Promo/`.

**Por qué.** No duplicar cientos de MB de imágenes: las que el juego ya tiene alcanzan, y los packs
del pool completan el resto sirviendo a los dos. El alias `P`/`Promo` existe porque la DB y el juego
nombran distinto la misma carpeta.

**Consecuencias.** El Buscador es 100 % lectura: no toca red ni la instalación. Si falta una imagen,
el detalle muestra la ruta donde se buscó (diagnóstico barato para el usuario).

---

<a id="adr-021"></a>
## ADR-021 — El pool (CDN de la app) como fuente primaria del catálogo

**Fecha:** 2026-07-07 · **Estado:** vigente.

**Contexto.** El catálogo se podía publicar en nuestro bucket (sección `catalog` del manifest) o
tomarse del canal `production` del CDN de la app Android, que ya lo publica versionado y con hash.

**Decisión.** El pool es la fuente primaria (catálogo **y** packs de imágenes oficiales). La sección
`catalog` del manifest del launcher queda como **fallback** y puede retirarse del bucket.

**Por qué.** Evita mantener copias sincronizadas a mano y hereda el pipeline de la app. El `sha256`
de los packages se agregó a ese pipeline justamente para esto (la app Android ignora el campo).

**Consecuencias.** Las URLs del pool son absolutas: se validan con `ResolveSameOriginUrl`
([ADR-007](#adr-007)). Los nombres locales llevan versión (`cards_db-2.3.1.zip`) porque la URL del
pool no la lleva, y `CatalogStore` elige por versión más alta; las versiones viejas se limpian al
sincronizar.

---

<a id="adr-022"></a>
## ADR-022 — Los mazos se persisten como `.txt` del juego

**Fecha:** 2026-07-11 · **Estado:** vigente.

**Contexto.** La app Android guarda los mazos en SQLite. El launcher podía tener su propia base o
usar los archivos del juego.

**Decisión.** Cada mazo es un `.txt` en `<SunAndMoon>\Decks\`, en el formato que lee Sun and Moon.
La metadata que el formato no representa (notas, `sortMode`, fecha, orden, mazo activo) va a un
sidecar `decksmeta.json` en `%APPDATA%`, indexado por nombre de archivo.

**Por qué.** El mazo que se arma en el launcher se juega directo, sin exportar ni copiar nada — que
es todo el valor de armar mazos acá en vez de en el celular. Además, los mazos que el usuario ya
tenía aparecen solos.

**Consecuencias.** El nombre del mazo **es** el nombre del archivo (se sanitizan caracteres
inválidos, se desambigua con `" (2)"`). Renombrar mueve el archivo; borrar lo borra. Obliga a
tolerar los nombres de carta del juego ([ADR-023](#adr-023)) y a no destruir contenido ajeno
([ADR-024](#adr-024)). Sin instalación configurada, la carpeta cae en `%APPDATA%\...\Decks`.

---

<a id="adr-023"></a>
## ADR-023 — Resolución tolerante de nombres solo en el parser txt

**Fecha:** 2026-07-11 · **Estado:** vigente.

**Contexto.** Sun and Moon escribe los strongholds sin el sufijo de versión (`… of the Crab` vs
`… (1)`), a veces con case distinto o sin `- exp`. El `ImportCards` de la app hace match exacto.

**Decisión.** `GetCardByNameRelaxed` (exacto → trim → sufijo → case-insensitive → `- exp`) se usa
**solo** al parsear `.txt`. `ImportCards` mantiene el match exacto Ordinal de la app.

**Por qué.** Los txt del juego tienen que abrir bien; los golden del import tienen que seguir
valiendo. Mezclar las dos cosas rompía una u otra.

**Consecuencias.** Al reescribir un txt, cada entrada conserva el nombre original del archivo
(`SourceName`); las cartas nuevas se escriben con el nombre del catálogo sin el sufijo `(N)`.

---

<a id="adr-024"></a>
## ADR-024 — Las líneas no reconocidas nunca se pierden

**Fecha:** 2026-07-11 · **Estado:** vigente.

**Decisión.** Las líneas de un `.txt` que no matchean el catálogo se conservan crudas
(`Deck.Unknown`), se muestran en el editor como "no reconocidas" y se reescriben **verbatim**.

**Por qué.** Esos archivos son del usuario y del juego, no nuestros: una carta de una versión
distinta del catálogo no puede desaparecer porque el launcher no la entendió. Es el equivalente
launcher de las huérfanas del deckbuilder de la app.

**Consecuencias.** Un mazo puede tener entradas que el editor no sabe contar; los indicadores blandos
las ignoran, pero el archivo sigue siendo válido para el juego.

---

<a id="adr-025"></a>
## ADR-025 — Sin UX de "recorte" (`keepRowAtZero` implementado pero sin usar)

**Fecha:** 2026-07-11 · **Estado:** vigente.

**Decisión.** El repositorio implementa `keepRowAtZero` completo (paridad + goldens), pero la UI no
lo usa: llegar a 0 borra la entrada, sin la gracia de 2,5 s de la app.

**Por qué.** El handoff avala que ningún golden depende de filas en 0, y la gracia temporal es un
patrón táctil (evitar re-tocar un botón chico) que en desktop no aporta.

**Consecuencias.** Si algún día se quiere el comportamiento de la app, el motor ya está: es solo UI.

---

<a id="adr-026"></a>
## ADR-026 — Validación = la de la app, pero el rechazo se informa

**Fecha:** 2026-07-11 · **Estado:** vigente.

**Decisión.** No hay motor de validación de mazos: solo los rechazos de `AddCard` (baneada → slot
cubierto → tope de copias, en ese orden) y los indicadores blandos (Dynasty/Fate en rojo si < 40,
"Falta: {slot}"). Dos strongholds siguen permitidos (⚠️ paridad). **Divergencia deliberada:** el
launcher **muestra** el `AddCardResult`; la app lo ignora en silencio.

**Por qué.** Validar más que la app produciría mazos "válidos acá, inválidos allá", y al revés. Pero
que un `+` no haga nada sin explicar por qué es un bug de UX, no paridad.

**Consecuencias.** Cualquier regla de torneo (p. ej. `maxCopies` de la banlist) queda fuera del
launcher, igual que fuera de la app.

---

<a id="adr-027"></a>
## ADR-027 — `NoActiveDeck` conserva su nombre engañoso

**Fecha:** 2026-07-11 · **Estado:** vigente.

**Decisión.** El valor `NoActiveDeck` de `AddCardResult` se llama igual que en el Kotlin, aunque en
realidad significa "carta o mazo inexistente".

**Por qué.** El port se lee en paralelo contra `DeckRepository.kt`. Renombrar mejora la lectura
aislada del C# y empeora la comparación con el original, que es la operación frecuente mientras el
port siga vivo.

**Consecuencias.** Vale para todo el port: los nombres siguen al Kotlin, no al gusto local.

---

<a id="adr-028"></a>
## ADR-028 — Listas ≠ mazos

**Fecha:** 2026-07-11 · **Estado:** vigente.

**Decisión.** Las "listas simples" del Buscador viven en `usercards.json` (junto a favoritas y
ocultas) y **no** son mazos: no van a la carpeta `Decks`. Los mazos `type="list"` del modelo de la
app existen en el repo solo por paridad de goldens; la UI no los crea.

**Por qué.** Son cosas distintas: la lista es material de armado (el chip "Solo lista" filtra los
resultados a sus cartas — "armo la lista de ninjas y después armo el mazo filtrando por ella"); el
mazo es el destino de los `+`/`−` y un archivo que el juego lee.

**Consecuencias.** El selector del panel derecho unifica ambas en la UI (mazos con tope por carta,
listas con tope 1 por toggle), pero la persistencia es distinta y no se mezclan.

---

<a id="adr-029"></a>
## ADR-029 — Duplicar mazo, extra que la app no tiene

**Fecha:** 2026-07-11 · **Estado:** vigente.

**Decisión.** El editor permite duplicar un mazo, implementado como export → mazo nuevo → import.

**Por qué.** Es pedido frecuente y sale gratis reusando dos operaciones ya portadas y cubiertas por
goldens; no toca la semántica de nada.

**Consecuencias.** Un extra explícito del launcher: si alguna vez se compara feature a feature con la
app, está anotado acá.

---

<a id="adr-030"></a>
## ADR-030 — Extras de `SortField` sin cobertura golden

**Fecha:** 2026-07-11 · **Estado:** vigente.

**Decisión.** `SortField` ganó TYPE / CLAN / RARITY / HONOR_REQ / FOCUS para poder ordenar por click
en las cabeceras de la tabla. Los 6 valores originales quedan intactos.

**Por qué.** Ordenar por columna es lo mínimo esperable en una tabla de escritorio, y la app no tiene
tabla.

**Consecuencias.** Esos cinco valores **no** están cubiertos por goldens (no existen en la app). Al
tocar el orden, los 6 originales siguen siendo la referencia verificada; los extras se prueban a ojo.

---

<a id="adr-031"></a>
## ADR-031 — Documentación en cuatro capas + as-built

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** El proyecto vive a lo largo de muchas sesiones de Claude Code. La documentación estaba
completa pero plana (`docs/*.md` mezclando spec, estado, backlog y handoffs), sin una regla que
dijera qué se actualiza cuándo.

**Decisión.** Adoptar el estándar de proyecto docs-driven: `docs/index/` (estado actual por dominio),
`docs/functional/` (specs de feature, versionadas, congeladas), `docs/design/` (decisiones técnicas
transversales), `docs/technical/` (as-built en prosa + contratos externos), más `DECISIONS.md`,
`ROADMAP.md` y un `CLAUDE.md` que rutea. Separar además los roles: la spec funcional se escribe
**fuera** del editor y sus consideraciones técnicas son **sugerencias, no instrucciones**.

**Por qué.** Que una sesión nueva arranque con el contexto correcto sin re-explicar la arquitectura,
que las features se piensen antes de escribirse, y que la documentación no envejezca en silencio (la
tabla de actualización post-cambio del `CLAUDE.md` es el mecanismo).

**Consecuencias.** Los paquetes de handoff quedaron en `docs/functional/` —fueron la base para
construir el Buscador y los Mazos, que es exactamente el rol de esa capa—, pero sus **fixtures
salieron de `docs/`**: los `golden-tests.json`, los datos y el harness de regeneración viven en
`tests/fixtures/<dominio>/`, porque son artefactos de test y no documentación (`SelfTest.cs` los
resuelve por ruta desde ahí). En `docs/technical/contratos/` quedaron solo los contratos de formato
de datos (manifest del bucket y del pool). `launcher-spec.md` pasó a ser
`docs/functional/launcher-v1.0-spec.md` congelado; el backlog pasó a `docs/ROADMAP.md`. El as-built
(`docs/technical/technical-specification.md`) se actualiza en cada cierre de fase, con el mismo peso
que el README — no como línea suelta al final del checklist.

---

<a id="adr-032"></a>
## ADR-032 — El selftest se corre al cerrar un desarrollo, no durante

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** El `--selftest` es la red de seguridad del proyecto ([ADR-011](#adr-011)) y la
costumbre era correrlo después de casi cualquier cambio. Medido: build Debug incremental ≈ 1 s,
selftest ≈ 3 s, reporte de ~180 líneas. El problema no es el tiempo de reloj sino el **ciclo
completo** —compilar, lanzar el exe, leer el reporte— repetido muchas veces dentro de una misma
tarea: consume turnos y contexto de la sesión, y en cambios chicos no aporta información nueva.

**Decisión.** Durante el desarrollo se compila y nada más. El selftest se corre **una vez, al cerrar
un desarrollo**: cuando se da por terminado, cuando se va a commitear o publicar, o cuando se
terminó un cambio en el área crítica (red/descarga/extracción, motor del Buscador, importer,
`DeckRepository`, `SelfTest.cs`, `AppConstants`). No se corre para cambios de documentación,
comentarios ni workflows. Si no se corrió, el agente lo dice explícitamente en vez de reportar
"verificado".

**Por qué.** El valor del selftest está concentrado en dos momentos —antes de dar algo por hecho y
antes de que salga del repo—, no en el medio de una edición. Y existe una segunda red: CI lo corre en
cada push y PR a `main`, así que saltear corridas locales no llega a producción sin ser detectado.

**Consecuencias.** El tradeoff aceptado es que, si un cierre trae varios cambios y el selftest sale
en rojo, hay que aislar cuál lo rompió — a cambio de sesiones más cortas. En cambios grandes sobre el
área crítica sigue valiendo cortar en etapas y cerrar cada una. Al reportar se muestra solo la línea
`RESULTADO` (y los `FAIL` si los hay), no el reporte completo.

---

<a id="adr-033"></a>
## ADR-033 — Qué entra a la batería de verificación, y cómo se da de baja

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** La batería tiene dos mitades de naturaleza distinta que conviene no confundir: 78
**golden tests** generados con la lógica real de la app Android, y el resto, **checks propios**
escritos a mano en `SelfTest.cs`. Sin una regla, cada feature nueva plantea la misma duda —¿esto
lleva test?— y aparecen dos tentaciones opuestas: inflar el harness con checks de UI que no prueban
nada, o dejarlo congelado mientras el proyecto crece. Se suma que el total (177) estaba hardcodeado
en 14 documentos, así que hacerlo crecer implicaba romper la doc en 14 puntos.

**Decisión.**

1. **Los golden no se escriben a mano, nunca.** Se regeneran con el harness de
   `tests/fixtures/*/generator/` cuando cambia la app o cuando se decide cambiar una semántica
   portada. Una feature propia del launcher no genera goldens: se cubre con checks propios.
2. **Los checks propios crecen con el proyecto**, pero con criterio acotado: escritura sobre la
   instalación o el estado del usuario, validación de datos que vienen de la red, y lógica con
   semántica no obvia (comparar versiones, parsers, resolución de nombres, round-trip de
   persistencia). **No** se cubren UI, XAML, textos ni wiring de eventos.
3. **Se dan de baja** cuando la funcionalidad desaparece, o cuando el check quedó desnaturalizado
   (sigue verde pero ya no prueba lo que dice). Borrar un check es una acción explícita, que se
   informa y baja el total. Lo que no se hace nunca es aflojar, comentar o borrar un check para
   llegar al verde.
4. **El total de checks deja de ser una constante citada como regla.** El criterio de aceptación
   pasa a ser "**0 FAIL y ningún SKIP**"; el número queda solo como dato descriptivo en los índices,
   gobernado por la tabla de actualización.

**Por qué.** El harness es un test **de pipeline**: su valor está en las áreas donde una regresión
silenciosa es cara e invisible (pisar el `database.xml`, romper un `.txt` de mazo, aceptar un hash
que no coincide). Cubrir UI ahí es imposible en la práctica y cubrir lo trivial diluye la señal: una
batería que nadie lee con atención es tan mala como no tenerla. Y separar las dos mitades evita el
peor error posible —"arreglar" un golden a mano—, que rompe la paridad sin que nada se ponga rojo.

**Consecuencias.** Al agregar funcionalidad hay que preguntarse explícitamente si entra en alguno de
los tres casos del punto 2, y decirlo cuando no entra ("no sumé checks: es UI"). El criterio de
aceptación es autosostenido, así que la batería puede crecer sin romper la documentación.
