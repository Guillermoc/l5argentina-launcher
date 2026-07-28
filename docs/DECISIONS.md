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
| [025](#adr-025) | Sin UX de "recorte" (`keepRowAtZero` sin usar) | 2026-07-11 | **Sustituida por [046](#adr-046)** |
| [026](#adr-026) | Validación = la de la app, pero el rechazo se informa | 2026-07-11 | Vigente |
| [027](#adr-027) | `NoActiveDeck` conserva su nombre engañoso | 2026-07-11 | Vigente |
| [028](#adr-028) | Listas ≠ mazos | 2026-07-11 | **Sustituida por [045](#adr-045)** |
| [029](#adr-029) | Duplicar mazo, extra que la app no tiene | 2026-07-11 | Vigente |
| [030](#adr-030) | Extras de `SortField` sin cobertura golden | 2026-07-11 | Vigente |
| [031](#adr-031) | Documentación en cuatro capas + as-built | 2026-07-26 | Vigente |
| [032](#adr-032) | El selftest se corre al cerrar, no durante | 2026-07-26 | Vigente |
| [033](#adr-033) | Qué entra a la batería de verificación, y cómo se da de baja | 2026-07-26 | Vigente |
| [034](#adr-034) | A `main` directo y sin PRs, pero con confirmación siempre | 2026-07-26 | Vigente |
| [035](#adr-035) | Una forma de control por rol: la píldora deja de ser universal | 2026-07-26 | Vigente |
| [036](#adr-036) | Las ventanas grandes se acotan al área de trabajo | 2026-07-26 | Vigente |
| [037](#adr-037) | El arte va redondeado y sin marco; el marco es el placeholder | 2026-07-26 | Vigente |
| [038](#adr-038) | Las listas se manejan en la barra, no en el panel de filtros | 2026-07-26 | Vigente |
| [039](#adr-039) | Cerrar el launcher confirma y cierra todo | 2026-07-26 | Vigente |
| [040](#adr-040) | Los diálogos salen de un helper propio, no de `MessageBox` | 2026-07-26 | Vigente |
| [041](#adr-041) | Ctrl+click agrega al mazo; el overlay es la ayuda visual | 2026-07-26 | Vigente |
| [042](#adr-042) | Un solo mazo activo, y Mazos es su única fuente de verdad | 2026-07-26 | Vigente |
| [043](#adr-043) | La barra del editor: una acción visible y el resto en un menú | 2026-07-26 | Vigente |
| [044](#adr-044) | La identidad persistida de una carta es su nombre, no su id | 2026-07-28 | Vigente |
| [045](#adr-045) | Listas y mazos son el mismo conjunto, en carpetas distintas | 2026-07-28 | Vigente |
| [046](#adr-046) | El recorte se implementa, y vive en el sidecar y no en el `.txt` | 2026-07-28 | Vigente |
| [047](#adr-047) | "Mazo terminado": un criterio propio del launcher, y blando | 2026-07-28 | Vigente |
| [048](#adr-048) | El pack propio se identifica por hash, no por id | 2026-07-28 | Vigente |

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

**Fecha:** 2026-07-11 · **Estado:** **sustituida por [ADR-046](#adr-046)** (2026-07-28). La lectura
de fondo resultó incompleta: el recorte no era solo la gracia táctil de 2,5 s, era poder bajar una
carta a 0 sin perderla de vista.

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

**Fecha:** 2026-07-11 · **Estado:** **sustituida por [ADR-045](#adr-045)** (2026-07-28). Lo de abajo
es la foto de por qué se separaron; el argumento de persistencia se dio vuelta con
[ADR-044](#adr-044).

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

---

<a id="adr-034"></a>
## ADR-034 — A `main` directo y sin pull requests, pero con confirmación siempre

**Fecha:** 2026-07-26 · **Estado:** vigente.

> **Corregido el mismo día.** La primera redacción de este ADR decía que commitear y pushear no
> requerían confirmación. Fue un error de interpretación del agente, no la decisión del mantenedor:
> "push directo" se refería a **adónde** va el commit (a `main`, sin PR), no a hacerlo sin permiso.
> Se corrige en el cuerpo en vez de superseder, porque la decisión nunca cambió — lo que estaba mal
> era el registro.

**Contexto.** El repo es público (open source, MIT) pero lo mantiene **una sola persona**. Los pull
requests están **deshabilitados** en la configuración de GitHub, así que el intento de abrir uno
falla; `main` tiene branch protection desde que el repo se hizo público, aunque no exige PR.

**Decisión.** Dos cosas separadas, que conviene no confundir:

1. **Mecanismo:** commits directos a `main`, sin ramas ni pull requests (salvo pedido explícito).
2. **Autorización:** ninguna. Commitear y pushear **siempre** requieren un sí explícito del
   mantenedor, por chico que sea el cambio. El orden es selftest → pedir permiso → commit → chequear
   Dependabot → pedir permiso → push.

La branch protection **se mantiene**, pero por un motivo distinto del que parecía: impedir
*force-push* y borrado de `main`.

**Por qué.** Un PR sin nadie que lo revise es ceremonia sin beneficio: agrega pasos y no agrega una
sola mirada. Pero la confirmación humana antes de cada commit sí agrega algo, y no es lo mismo que un
review: es el punto donde el mantenedor decide **qué queda registrado en la historia del proyecto y
cuándo**. Y la protección contra reescritura de historia vale por una razón concreta: el exe se
distribuye por GitHub Releases justamente porque reemplazar algo ahí **deja historial y
notificación** ([ADR-009](#adr-009)). Si `main` se pudiera reescribir en silencio, esa garantía —que
es parte del modelo de seguridad— se caería. La protección no está para frenar al mantenedor, está
para que el historial sea evidencia.

**Consecuencias.** El CI (`build.yml`) corre **solo en push a `main`**: el trigger `pull_request` es
configuración muerta mientras los PR estén deshabilitados. O sea que la verificación automática llega
*después* de que el cambio entró — por eso el checklist local de [ADR-032](#adr-032) (build +
selftest antes de cerrar) es la primera línea y CI la segunda, y por eso el selftest va **antes** del
commit y no después.

**Nota operativa:** con los PR deshabilitados, **Dependabot no puede abrir PRs**: solo empuja ramas
`dependabot/*` que quedan invisibles salvo que alguien mire la lista de ramas (al 2026-07-26 hay 9
esperando). Por eso el chequeo `git ls-remote origin 'refs/heads/dependabot/*'` es un paso previo al
push: no para actuar por cuenta propia, sino para que el mantenedor decida si pushea igual o
actualiza primero.

---

## ADR-035 — Una forma de control por rol: la píldora deja de ser el control universal

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** La primera versión del Buscador (y por herencia la de Mazos) usaba el mismo estilo
`FilterChip` —píldora redondeada, rojo cuando está activa— para cinco roles distintos: colapsar un
panel lateral, cambiar el contenido del panel derecho, elegir entre dos vistas mutuamente
excluyentes, prender/apagar un filtro y voltear la dirección de orden. La consecuencia era que todo
se veía igualmente "encendible": "Grilla" y "Solo favoritas" eran visualmente el mismo control
aunque uno seleccione y el otro filtre, y `↓` (descendente) se leía como "filtro activo" porque se
pintaba de rojo igual que un filtro.

**Decisión.** Cada rol tiene su forma, y la píldora queda reservada al único para el que es correcta
(filtro on/off). Los estilos viven en `Views/CardUiStyles.xaml` y se aplican **igual en las dos
ventanas**; la tabla completa está en [index/ui.md](index/ui.md). Los dos cambios de fondo:

- **Colapso de paneles → `PaneRail`.** Riel vertical de altura completa entre el panel y el centro,
  con un chevron que apunta adónde se va el borde. Reemplaza a los chips "Filtros"/"Panel" de la
  barra superior. **Su columna (18 DIP) nunca colapsa**: es la única forma de reabrir el panel.
- **Contenido del panel → `PanelTab`.** Pestaña adosada sobre una línea base de ancho completo: la
  activa tapa la línea con su fondo opaco y no dibuja borde inferior, así se funde con el contenido.

**Por qué.** La forma de un control es lo que comunica qué hace antes de leer la etiqueta. Un riel
pegado al panel dice "esto abre y cierra *este* panel" sin texto; un chip en la barra superior
llamado "Panel" no dice ni cuál panel ni qué le va a pasar. Lo mismo con las pestañas: dos caras del
mismo panel no son dos filtros independientes, y adosarlas al contenido lo hace evidente.

**Consecuencias.**
- El colapso libera 270 (Filtros) / 405 (Panel) DIP en vez de 280 / 415, porque el riel se queda.
  Contra una celda de 226 **sigue sumando una columna de cartas**: la calibración de anchos no se
  rompe.
- Se agregó trigger de `IsEnabled=False` a los chips (opacidad 0.4 + cursor `Arrow`). No estaba, y
  por eso "Ver todas las impresiones" se veía habilitado en vista Lista aunque no hacía nada.
- Cuatro claves del `.resx` quedaron huérfanas al desaparecer las etiquetas de los toggles
  (`Search_PaneFilters`, `Search_PaneDetail`, `Deck_PaneDecks`, `Deck_PaneSide`) y se borraron; los
  `*Tip` pasaron a ser el ToolTip del riel. El resx queda en 307 claves.
- Es un cambio 100 % de presentación: no toca motor, criterios, persistencia ni atajos, y no agrega
  checks al selftest ([ADR-033](#adr-033) excluye UI/XAML/layout de la batería).

**Segunda pasada (mismo día).** Aplicando la taxonomía al resto de la barra aparecieron tres cosas
más, que son la misma decisión y no una nueva:

- **`ChipCombo`.** Los selectores de orden, lista activa y mazo activo usaban el ComboBox pergamino
  claro, pensado para *campos de entrada*. Al lado de los chips pesaban más que ellos y se leían como
  cajas de búsqueda. Se les dio la paleta de los toggles. Para no duplicar el template de ~50 líneas,
  el `ControlTemplate` del ComboBox pasó a tomar fondo, borde, color de texto y `CornerRadius` (en
  `Tag`) de **propiedades** en vez de recursos fijos; `ChipCombo` es un `BasedOn` que solo cambia
  valores. Los combos que sí son entrada (keywords, texto de carta, filtro de mazo) no se tocaron.
- **El selector de mazo activo salió del panel colapsable** a la barra superior, al lado de "Mazos…".
  Estaba arriba de las pestañas, que es donde peor queda una vez que las pestañas son pestañas; y
  sobre todo: es el destino de todos los `+`/`−`, así que colapsar el panel no debería esconderlo. Se
  perdió el botón `✎` del panel, que hacía exactamente lo mismo que "Mazos…".
- **Los quick filters pasaron a `FilterChipSmall`.** Son el rol de la píldora, pero no son lo más
  importante de su fila; el tamaño ahora lo dice.

**Descartado.** Pestañas verticales con texto rotado sobre el borde exterior del panel: más
distintivas, pero comen ~26 DIP de ancho útil y el texto rotado se lee peor. Convertir los rieles en
`GridSplitter` (colapsar *y* redimensionar) quedó fuera de alcance: el pedido era conservar el
comportamiento, no agregarle uno nuevo.

---

## ADR-036 — Las ventanas grandes se acotan al área de trabajo, no a la pantalla

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Problema.** El Buscador y Mazos quedaban parcialmente tapados por la barra de tareas, y por dos
causas distintas que se veían igual:

1. **Al maximizar.** Con `WindowStyle=None` + `WindowChrome`, Windows maximiza contra el monitor
   completo en vez del área de trabajo: el marco que normalmente lo acota no existe. Había un parche
   —`RootLayout.Margin = 7` al maximizar— que solo disimulaba el desborde recortando el contenido
   por dentro; la ventana seguía siendo más grande que la pantalla útil.
2. **Al abrir.** El alto de diseño (840 DIP) **no entra** en un 1080p a 125 %, que deja ~826 DIP
   útiles, y `WindowStartupLocation=CenterScreen` centra contra la pantalla entera y no contra el
   área de trabajo. El borde inferior caía debajo de la barra desde el primer arranque.

**Decisión.** `Views/WindowSizingFix.cs`, enganchado desde el constructor de ambas ventanas:
responde `WM_GETMINMAXINFO` con el `rcWork` del monitor donde está la ventana (para el maximizado) y
recorta/reubica la ventana dentro de ese mismo rectángulo en `SourceInitialized` (para la apertura).
Se trabaja en píxeles de dispositivo —lo que espera la API— y se convierte a DIP con
`CompositionTarget.TransformFromDevice` recién al tocar el `Window`. El margen de 7 DIP se eliminó:
con la ventana bien acotada, dejaría un hueco.

**Por qué un helper compartido y no código en cada ventana.** Son las dos únicas ventanas
redimensionables y tienen chrome idéntico; `MainWindow` es fija y no le aplica. Duplicar el
P/Invoke en dos code-behinds que ya son largos no aporta nada.

**Consecuencia con varios monitores:** el rectángulo sale de `MonitorFromWindow`, así que la ventana
se acota al monitor donde efectivamente está, no al primario.

---

## ADR-037 — El arte de la carta se dibuja redondeado y sin marco; el marco es el placeholder

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** Las cartas de L5R que sirve el pool son **escaneos rectangulares de cartas físicas con
esquinas redondeadas**: cada jpg trae cuatro puntitas blancas en las esquinas, restos de la mesa de
escaneo. Se dibujaban con `<Image Stretch="Uniform">` sobre un `Border` con fondo oscuro y borde de
2 px —el color del clan en el detalle, oro tenue en las celdas de grilla—. Resultado: un marco
rectangular contra el que las puntitas blancas resaltaban, y una franja de fondo visible por el
letterbox de `Uniform` (la celda es 226×322 = 0.702 y la carta ~0.716).

**Decisión.** Donde hay arte, el arte **es** la celda:

- Se dibuja como `ImageBrush` con `Stretch="UniformToFill"` sobre un `Border` con `CornerRadius`, no
  como `Image`. Un `Border` **sí** recorta su propio `Background` con el radio; un `Image` hijo no se
  recortaría. `UniformToFill` además elimina el letterbox (recorta ~2 % del ancho, invisible).
- **El marco pasa a ser el placeholder**: borde y fondo solo se dibujan cuando no hay imagen, que es
  cuando hay algo que enmarcar (el nombre de la carta como texto). En las celdas de grilla el borde
  igual vuelve en *hover* y en selección, donde sí comunica estado.
- En el detalle, el alto del `Border` lo calcula `SetArt` desde el aspecto real del bitmap
  (`PixelHeight/PixelWidth`), así `UniformToFill` no recorta nada y las esquinas del radio caen
  justo sobre las del escaneo.

**Por qué no un `OpacityMask` o un `Clip`.** Ambos se aplican sobre los *bounds del elemento*, no
sobre el rectángulo que la imagen ocupa realmente; con `Stretch="Uniform"` y letterbox, el radio
habría caído en el aire y no sobre el borde de la carta. Un `VisualBrush` como máscara además cuesta
más que un `ImageBrush` en una grilla virtualizada de miles de celdas.

**Consecuencia sobre la paridad.** El marco del color del clan en el detalle era un port del
`CardListItem`/`CardViewerModal` de la app Android. Esto **diverge a propósito**: la regla de paridad
([ADR-018](#adr-018)) cubre la semántica de los motores portados, no el chrome visual, y la app corre
en pantallas donde el problema no se ve igual. Queda anotado en la doc de paridad.

---

## ADR-038 — Las listas se manejan en la barra, no en el panel de filtros

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** El Buscador tenía las listas partidas en dos lugares: el combo de lista activa y el
toggle "Solo lista" en la barra superior, pero **crear** y **borrar** listas vivían en una sección
del panel de filtros, con un `TextBox` fijo "Nueva lista:" y un link "Borrar la lista seleccionada".
Con el panel de filtros colapsado —que es el estado útil cuando estás armando— no había forma de
crear una lista.

**Decisión.** Todo el ciclo de vida de las listas vive en la barra, pegado al combo que las elige:
`Solo lista` · combo · `+` · `🗑` (`IconButton`). El nombre de la lista nueva se pide con
`TextPrompt`, el mismo diálogo de una línea que ya usaba `DeckWindow` para los mazos — extraído a
`Views/TextPrompt.cs` en este cambio, en vez de duplicar sus ~70 líneas.

**Por qué.** Elegir, crear y borrar una lista son la misma tarea, y ninguna es un filtro: son
navegación sobre material de armado ([ADR-028](#adr-028)). Tenerlas en el panel de filtros las ataba
a un panel que se colapsa, y obligaba a un `TextBox` siempre presente para una acción que se usa una
vez cada tanto.

**Consecuencia.** El botón de borrar se apaga cuando no hay lista elegida, en vez de avisar recién
al hacer click. Lo mismo el `≡ A la lista` del detalle — que **antes no existía**: el `≡` estaba en
las celdas de la grilla pero el panel de detalle no tenía cómo agregar a la lista.

---

## ADR-039 — Cerrar el launcher confirma y cierra todo; las demás ventanas no preguntan

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** `MainWindow`, el Buscador y el editor de Mazos son ventanas hermanas del mismo
proceso. Cerrar cualquiera de ellas era instantáneo y sin aviso, y con el `ShutdownMode` por defecto
(`OnLastWindowClose`) cerrar el launcher con el Buscador abierto dejaba la aplicación viva sin
ventana principal: un estado raro, donde el usuario cree que cerró y sigue habiendo proceso.

**Decisión.** Asimetría deliberada:

- **`MainWindow` confirma y después llama a `Application.Current.Shutdown()`**, que se lleva Buscador
  y Mazos. El mensaje cambia si hay una descarga o una aplicación de base en curso (`_busy`), donde
  cerrar sí tiene consecuencia.
- **Buscador y Mazos se cierran solos, sin preguntar.** Cerrar el Buscador no tiene consecuencia
  sobre nada: no hay estado sin guardar (los mazos se persisten en cada cambio, favoritas y listas
  también).

**Detalle de implementación.** El overlay de confirmación de `MainWindow` es `async` y `Closing` es
sincrónico, así que el handler **siempre cancela el cierre** (`e.Cancel = true`) y lo reintenta
después de la respuesta, con un flag `_closeConfirmed` que evita re-preguntar cuando el `Shutdown`
vuelve a pasar por `Closing`.

**Por qué no confirmar en todas.** Una confirmación que aparece siempre deja de leerse. Se paga
donde hay algo que perder —el proceso entero— y no donde no hay nada.

---

## ADR-040 — Los diálogos de Buscador y Mazos salen de un helper propio, no de `MessageBox`

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** [ADR-013](#adr-013) prohíbe los `MessageBox` del sistema, pero solo `MainWindow` tenía
con qué reemplazarlos: su `DialogOverlay` vive dentro del lienzo de 1080×720 escalado por `Viewbox` y
no es reutilizable desde otras ventanas. El editor de Mazos había quedado con **cuatro**
`MessageBox.Show` (confirmar borrado de mazo, portapapeles vacío, resultado de importar) y el
Buscador borraba listas **sin confirmar nada**.

**Decisión.** `Views/Dialogs.cs`: `Prompt`, `Confirm` e `Info` como ventanas modales sin chrome, con
el borde y el fondo del tema, compartidas por las dos ventanas grandes. Los cuatro `MessageBox`
pasaron a usarlo y el borrado de lista ganó su confirmación.

En `Confirm` el botón destructivo **no** es el default y va en `ErrorText`; el default es Cancelar.
Un diálogo de borrado donde Enter borra es una trampa.

**Por qué no extender el overlay de `MainWindow`.** Está atado a su `Viewbox` y a su lienzo fijo; una
ventana modal chica es menos código y se comporta bien en ventanas redimensionables. `MainWindow`
sigue con su overlay: ahí el overlay es parte del diseño de la pantalla, no un parche.

**Efecto secundario que valía la pena:** crear una lista con un nombre que ya existe ya no es un
no-op silencioso — deja esa lista como activa, que es lo que el usuario estaba pidiendo.

---

## ADR-041 — Ctrl+click agrega al mazo; el overlay es la ayuda visual y el blanco del click

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** Agregar una carta al mazo desde la grilla se hacía con el `+` de la barra de acciones
(un botón de ~20 px que aparece al pasar el mouse) o con doble click, que no está anunciado en
ningún lado. Armar un mazo es una acción repetitiva: merece un gesto de un solo click y un blanco
grande.

**Decisión.** **Ctrl+click sobre cualquier parte de la carta** agrega al mazo activo, con dos ayudas
visuales:

1. Con Ctrl apretado se prenden los bordes de **todas** las celdas visibles — comunica que hay un
   modo activo, no solo que la celda de abajo del mouse hace algo.
2. La celda bajo el mouse se cubre con un overlay oscuro con "＋ Agregar al mazo". Ese overlay va
   **último** en el `Grid` de la celda, así que tapa también la barra de acciones: el Ctrl+click cae
   siempre sobre él y burbujea al handler de la celda. La ayuda visual y el blanco del click son la
   misma cosa.

**Implementación.** `SearchWindow.IsAddModifierDown` es una `DependencyProperty` de la ventana, no un
flag por celda: un solo `SetValue` y todas las celdas *realizadas* reaccionan por binding, contra
recorrer los miles de `GridCell` del catálogo en cada `KeyDown`. Se sincroniza en `KeyDown`/`KeyUp` y
se fuerza a `false` en `Deactivated`, porque un Alt+Tab con Ctrl apretado nunca entrega el `KeyUp` y
el modo quedaría pegado.

**Trampa de WPF que costó una vuelta.** El overlay tenía `Visibility="Collapsed"` como **atributo
local** además del `Setter` en su `Style`. En WPF un valor local le gana a cualquier trigger de
Style, así que el `MultiDataTrigger` no podía mostrarlo nunca. La barra de acciones de la celda
—que funciona— no tiene el atributo local: la visibilidad va **solo** por el `Style`. Copiar ese
patrón es la regla para cualquier pieza de la celda que aparezca por trigger.

**Alcance.** Solo la grilla. En la tabla el doble click sigue siendo el gesto: no hay dónde poner un
overlay que se lea, y una fila ya es un blanco grande.

---

## ADR-042 — Un solo mazo activo, y la ventana de Mazos es su única fuente de verdad

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** Había dos lugares para decidir sobre qué mazo se trabaja, y no eran el mismo:

- En el **Buscador**, un combo de mazo activo en la barra: cambiarlo movía el destino de los `+`/`−`.
- En **Mazos**, un mazo *seleccionado* (el que se ve y se edita) distinto del *activo*, con un botón
  "Activar" y un badge "● Activo" para reconciliarlos.

Dos selectores para un concepto que el usuario piensa como uno solo — "el mazo que estoy armando"—,
y un paso extra ("Activar") cuya única razón de ser era que podían diferir. Además el Buscador tenía
una pestaña "Mazo" que duplicaba, peor y más chica, lo que la ventana de Mazos ya muestra entero.

**Decisión.** Un solo concepto:

- **Seleccionar un mazo en la ventana de Mazos lo activa.** Se van "Activar", el badge "● Activo" y
  el prefijo `●` del rail: la selección ya es la marca.
- **El Buscador no elige mazo.** El combo pasa a ser un **label** con nombre + conteos blandos
  (Total · D · F, en rojo si no llegan a 40; `!` con los slots faltantes en el tooltip). Clickearlo
  abre la ventana de Mazos, que es donde se cambia.
- **Se va la pestaña "Mazo"** del panel derecho, y con ella la tira de pestañas: el panel queda solo
  con el detalle de carta. El contenido del mazo se ve en su ventana.

**Sobre "todas las ventanas apuntan al mismo mazo": ya era así.** `DeckLibrary.GetOrLoad` devuelve un
singleton de proceso y toda ventana del Buscador ya se suscribía a `ActiveDeckChanged`; `Ctrl+N`
siempre compartió mazo activo. Lo que faltaba no era el mecanismo sino que hubiera **una sola manera
de cambiarlo**.

**Qué se pierde y por qué se acepta.** Ya no se ve el contenido del mazo mientras se busca, ni se
cambia de mazo sin abrir la otra ventana. Lo primero lo cubre la ventana de Mazos, que muestra las
cartas en grande y agrupadas; lo segundo es un click más en una acción que se hace una vez por
sesión de armado, no una vez por carta. A cambio desaparece la clase de bug más molesta del modelo
anterior: estar editando un mazo en una ventana y agregando cartas a otro distinto desde la otra.

**Los conteos se conservan** en la barra, en una línea. Es el dato que se mira mientras se agrega
("¿llegué a 40?") y no justificaba una pestaña.

---

## ADR-043 — La barra del editor de Mazos: una acción visible y el resto en un menú

**Fecha:** 2026-07-26 · **Estado:** vigente.

**Contexto.** La primera fila del editor tenía seis botones permanentes —Importar, Exportar,
Duplicar, Renombrar, Eliminar, Carpeta— que ocupaban media barra, competían por atención con el
nombre del mazo y, con etiquetas de una palabra, tampoco se entendían: "Carpeta" no dice qué
carpeta, "Importar" no dice de dónde. Todas son acciones que se usan una vez cada tanto, o nunca.

**Decisión.** La barra queda con **el nombre del mazo a la izquierda** y, a la derecha, dos cosas:

- **Buscador** — visible y con peso. Es la acción frecuente del editor: las cartas nuevas se agregan
  desde el Buscador, así que el camino de ida merece un botón. Abre otra ventana sobre el mismo
  catálogo, igual que el `Ctrl+N` del Buscador.
- **`⋯`** — un `IconButton` que abre un popup (`MenuRow`) con renombrar, duplicar, importar,
  exportar, abrir carpeta y —separado y en rojo— eliminar.

**Efecto lateral que era la mitad del problema:** en un menú hay lugar para etiquetas que se
entienden. "Carpeta" pasó a "Abrir la carpeta Decks", "Importar" a "Importar del portapapeles…",
y las que abren un diálogo llevan puntos suspensivos. Los nombres cortos siguen existiendo para los
títulos de esos diálogos.

**Por qué un `Popup` y no un `ContextMenu`.** El tema no tiene estilo de `MenuItem` y habría que
escribirlo entero; el `Popup` con botones reusa el lenguaje del popup del `ComboBox`, que ya existe.
Se cierra solo (`StaysOpen=False`) y además cada acción lo cierra antes de abrir su diálogo.

**Detalle de posicionamiento:** el popup se ancla al botón con `HorizontalOffset` negativo para
alinear por el borde derecho; con el offset original se pasaba del borde de la ventana y WPF lo
corría solo, dejándolo desalineado.

---

<a id="adr-044"></a>
## ADR-044 — La identidad persistida de una carta es su nombre, no su id

**Fecha:** 2026-07-28 · **Estado:** vigente.

**Decisión.** Todo estado del usuario que referencie una carta se guarda en disco por **nombre**.
El `id` del catálogo es una clave **de sesión**: vale mientras esté cargado ese catálogo, y no
cruza el disco. La traducción nombre ⇄ id ocurre en el borde (al cargar y al guardar), y en memoria
se sigue trabajando con ids, que es lo que matchea `CardSearchEngine`.

**Por qué.** Es contraintuitivo —un id parece más estable que un nombre— pero acá es al revés:

- El `id` sale del `cards_db.json` y lo asigna quien **genera la base de la comunidad**. Entre dos
  versiones de la base puede desaparecer o, peor, **reasignarse a otra carta**.
- El **nombre** es lo único que el juego y el launcher acuerdan: es lo que Sun and Moon escribe en
  sus `.txt` de mazo, y por eso los mazos ya sobrevivían a los cambios de base sin que nadie lo
  hubiera decidido explícitamente.

El riesgo no era "se pierde una favorita" sino el silencioso: una lista de ninjas que después de
actualizar la base contiene una holding, sin un solo error a la vista.

**Consecuencias.**

- `usercards.json` pasó a **v2**: favoritas y ocultas por nombre, y sin listas (ver
  [ADR-045](#adr-045)). La migración desde v1 traduce los ids que todavía resuelven, respalda el
  archivo viejo en `usercards.v1.json` y no se vuelve a correr.
- Los ids que ya no resuelven se pierden en la migración. Estaban rotos igual.
- La lectura usa `GetCardByNameRelaxed`, la misma cadena que el parser de `.txt`: un archivo
  editado a mano con el nombre que usa el juego resuelve igual que un mazo.
- El nombre tampoco es una clave perfecta: con nombres repetidos gana la primera carta en orden de
  inserción (`_cardsByName`, port de `getByName LIMIT 1`). Es la **misma** ambigüedad que ya
  aceptamos en todos los mazos y la misma que tiene el juego: no se agrega riesgo, se iguala.
- **Corolario para quien genera la base:** renombrar una carta en `cards_db.json` equivale a
  borrarla y crear otra — rompe listas, favoritas y mazos del usuario. Cambiar un `id` no rompe
  nada.
- Verificado en el selftest barajando los ids del catálogo y comprobando que las favoritas siguen
  apuntando a la misma carta.

**Al agregar estado nuevo:** si referencia cartas, se guarda por nombre. Sin excepciones.

---

<a id="adr-045"></a>
## ADR-045 — Listas y mazos son el mismo conjunto, en carpetas distintas

**Fecha:** 2026-07-28 · **Estado:** vigente. **Sustituye a [ADR-028](#adr-028).**

**Decisión.** Una lista y un mazo son la misma cosa —un conjunto de cartas, `Models.Deck` con
`Type` "deck" o "list", como en la app Android— y se editan con el mismo editor. Lo único que los
distingue:

| | Mazo | Lista |
|---|---|---|
| Archivo | `<juego>\Decks\<Nombre>.txt` | `<juego>\Lists\<Nombre>.txt` |
| Lo lee el juego | sí | **no** |
| Tope por carta | `MaxCopies` de la carta | **1** |
| Validación | conteos Dynasty/Fate, slots | ninguna |

`Lists\` es **hermana** de `Decks\`, no está adentro: el juego escanea su carpeta de mazos y no
vale la pena arriesgar que una lista le aparezca como mazo jugable.

**Por qué cambia respecto de ADR-028.** ADR-028 las separó por persistencia (JSON con ids vs `.txt`
con nombres) y el argumento se dio vuelta con [ADR-044](#adr-044): si el nombre es la clave durable,
el `.txt` es el formato **correcto**, no el frágil. Y una vez que comparten formato, mantener dos
modelos para la misma cosa solo cuesta: el usuario quiere editar una lista como un mazo y filtrar
por un mazo como si fuera una lista.

**Lo que NO se tocó.** `DeckRepository` es port 1:1 con 25 golden y ya traía el soporte de
`type="list"` de la app (tope 1 en `AddCard`, `isList` en `ImportCards`). Las mutaciones de lista de
la UI **no** pasan por `AddCard`: usan `SetInList`, método nuevo que no aplica el rechazo de
baneadas ni el de slot inicial cubierto. Una lista es material de armado, no un mazo legal — querer
las cuatro versiones de Bamboo Harvesters que estás evaluando es el caso normal. `AddCard` queda
exactamente como está, y el selftest verifica **las dos** semánticas por separado.

**El conjunto activo como filtro.** El selector de la barra del Buscador elige un conjunto que puede
ser una lista, un mazo, las favoritas o las ocultas; "Solo conjunto" filtra por sus cartas. El motor
no se enteró: la UI **materializa** el conjunto elegido en `UserCardData.Lists` al momento de
buscar, que es exactamente lo que los golden verifican. Sumar un tipo de conjunto nuevo no toca el
motor ni obliga a regenerar nada.

**Consecuencias.**

- El desplegable tiene dos niveles: listas + favoritas + ocultas arriba, y una entrada "Usar mazo…"
  que recién ahí baja a los mazos (suelen ser muchos y no son lo que se elige habitualmente).
- El `≡` de las celdas sigue cargando **solo listas**: con un mazo elegido están los `+`/`−`, y con
  favoritas está el `★`. Se apaga en vez de inventar un significado.
- Favoritas y ocultas se pueden **copiar a una lista** (`⎘`). No son archivos, así que no se editan
  en el editor: se bajan a una lista, que sí lo es.
- `CardSearchCriteria` gana `listKind` para que un preset sepa a qué tipo de conjunto apuntaba. Es
  un campo **agregado**: los golden serializados sin él deserializan igual.
- Un mazo y una lista pueden llamarse igual (carpetas distintas). El sidecar `decksmeta.json`
  prefija las listas con `list:` para que no colisionen; los mazos conservan la clave pelada.
- Un preset cuyo conjunto ya no existe ahora **lo dice** en vez de devolver 0 resultados en silencio.

---

<a id="adr-046"></a>
## ADR-046 — El recorte se implementa, y vive en el sidecar y no en el `.txt`

**Fecha:** 2026-07-28 · **Estado:** vigente. **Sustituye a [ADR-025](#adr-025).**

**Decisión.** Bajar una carta a 0 en el editor de mazos la deja **recortada**: la entrada persiste,
la carta se ve grisada en su lugar y el `+` la devuelve. Sacarla del todo es otra acción, el `✕`.
Solo aplica a mazos: en una lista la carta está o no está ([ADR-045](#adr-045)).

Las recortadas se guardan en el sidecar `decksmeta.json`, **no** en el `.txt` del mazo.

**Por qué se revierte ADR-025.** Esa decisión leyó el recorte como la gracia táctil de 2,5 s de la
app —un patrón móvil para no re-tocar un botón chico— y concluyó que en desktop no aportaba. Es
cierto de la gracia, pero el recorte es otra cosa: es poder bajar una carta a 0 **sin perderla de
vista** mientras se prueba una versión del mazo. El motor estaba entero desde el primer día
(`keepRowAtZero`, `DeleteCardEntry`, `RemoveAllTrimmed`, todos con sus goldens); faltaba solo la UI
y el dónde se guarda.

**Por qué en el sidecar y no en el `.txt`.** La alternativa evaluada era marcarlas con `# 0x Nombre`
en el propio archivo del mazo, que tiene a favor dejar el mazo entero en un solo archivo. Se
descartó: **ese archivo lo lee Sun and Moon**, ninguno de los mazos reales de la instalación tiene
una sola línea con `#`, y si el juego las tolera al leer es una incógnita que no vale la pena correr
—romperle un mazo al usuario cuesta infinitamente más que lo que aporta la portabilidad—. El sidecar,
además, es exactamente lo que declara ser desde [ADR-022](#adr-022): *lo que el formato del juego no
puede representar*. Y una carta recortada es justo eso — no forma parte del mazo que se juega.

**Consecuencias.**

- El `.txt` del juego **nunca** ve una recortada: `SunAndMoonDeckFile.Write` ya filtraba
  `Quantity > 0` y no hubo que tocarlo.
- **El `.txt` manda.** Si el nombre aparece en el archivo con cantidad, la marca de recorte del
  sidecar se ignora: lo que se juega es lo que dice el archivo del juego.
- Si se pierde el sidecar se pierden las recortadas, no el mazo — la misma garantía de siempre.
- Se guardan por **nombre** de carta, como todo lo que cruza el disco ([ADR-044](#adr-044)).
- El toggle "Recortadas" arranca **prendido**, al revés que la app. Sin el período de gracia, ocultar
  la carta en el mismo instante en que la recortás deja al usuario sin ver lo que acaba de hacer.
  Apagarlo muestra el mazo tal como se juega.
- Los conteos de grupo y tipo cuentan solo lo jugable: una recortada suma 0 y **no** cuenta como
  carta distinta, aunque se esté viendo.
- Sigue sin haber gracia de 2,5 s ni undo del `✕`: esa parte de ADR-025 se mantiene.

**⚠️ Paridad que ahora se ve.** `DeckClan` toma el primer stronghold **sin mirar la cantidad**, igual
que `DecksViewModel.kt:122` de la app. Con el recorte disponible eso se vuelve observable: recortar
el stronghold deja el mazo etiquetado con su clan igual. Es la semántica portada y se deja como está
([ADR-018](#adr-018)); la UI no lo compensa porque el subtítulo del mazo no es un dato con el que se
tomen decisiones.

---

<a id="adr-047"></a>
## ADR-047 — "Mazo terminado": un criterio propio del launcher, y blando

**Fecha:** 2026-07-28 · **Estado:** vigente.

**Decisión.** El editor puede filtrar los mazos por **terminados** o **pendientes**. Un mazo está
terminado cuando cumple las cuatro cosas a la vez:

1. cubre el slot de **Border Keep**,
2. cubre el slot de **Bamboo Harvesters**,
3. tiene al menos un **stronghold**,
4. **cada lado** —Dynasty y Fate— queda entre **40 y 50** cartas.

Las listas nunca cuentan como terminadas. Vive en `DeckRepository.IsDeckComplete`, marcado como
extra fuera del port.

**Por qué se escribe.** Esto **no existe en la app Android**: es un criterio del mantenedor, y sin
dejarlo anotado el próximo que lea `IsDeckComplete` no va a saber de dónde salieron esos números ni
si puede tocarlos. Los dos slots no están hardcodeados por nombre: salen de
`UncoveredRequiredSlots`, o sea de `filters.json → formats[].initialHoldingSlots` con
`required: true` — si la comunidad agrega un tercer slot requerido, el criterio lo toma solo.

**Consecuencias.**

- Es un indicador **blando**, en la línea de [ADR-026](#adr-026): no bloquea nada, no impide jugar
  un mazo de 38 cartas ni valida legalidad de torneo. Solo ordena la biblioteca.
- El `✓` del subtítulo muestra el mismo criterio por el que filtra el combo, para que el filtro no
  sea una caja negra.
- Una carta **recortada** ([ADR-046](#adr-046)) no cubre su slot ni suma al lado: recortar el Border
  Keep devuelve el mazo a "pendiente". Es lo correcto —no se juega— y está verificado.
- 40 es el `SideTarget` que ya existía (el objetivo blando de la app); 50 es el tope nuevo.
- Sin cobertura golden posible: la app no tiene el concepto. Se cubre con checks propios.

**Descartado:** validar legalidad de formato o banlist como parte de "terminado". El launcher no
tiene motor de reglas y no lo va a tener ([ADR-026](#adr-026)); mezclar "armado" con "legal" haría
que el ✓ prometiera algo que no puede verificar.

---

<a id="adr-048"></a>
## ADR-048 — El pack propio se identifica por hash, no por id

**Fecha:** 2026-07-28 · **Estado:** vigente.

**Decisión.** El pack de imágenes propio de la comunidad —la sección `images` del manifest del
launcher, la que muestra la barra "Imágenes Samurai Extended"— **también está publicado en el pool**.
`LauncherEngine.ImagePacks` lo excluye de la lista del overlay comparando el **sha256** contra
`Manifest.Images.Sha256`.

**El problema.** Aparecía en los dos lados: en la barra de info y en la lista de packs. Peor que la
duplicación visual es que **cada mitad lleva su propio registro de "aplicado"** —
`appliedImagePackVersion`/`appliedImagePackSha256` por un lado, `appliedImagePacks[id]` por el otro—,
así que aplicarlo desde un camino dejaba al otro diciendo "actualización disponible" sobre el mismo
archivo, y el usuario se lo bajaba dos veces (~30 MB). Verificado en el `config.json` de la máquina
de dev: `1da9fdc1…` versión 1.1 registrado en las dos claves.

**Por qué por hash y no por id.** Filtrar `samuraiEx` a mano sería una constante nueva que hay que
mantener sincronizada con lo que publique el pool, y que miente el día que el id cambie. El hash es
el dato que ya tenemos de las dos partes y **se auto-corrige**: si algún día los contenidos se
separan de verdad, los hashes dejan de coincidir y el pack del pool vuelve a aparecer solo, sin que
nadie toque código.

**Consecuencias.**

- Gana la barra de info: ese camino ya aplica el pack durante la sincronización normal, sin que el
  usuario tenga que entrar a un overlay.
- Los `appliedImagePacks["samuraiEx"]` que ya estén en un `config.json` quedan ahí. Son inertes —un
  registro de algo que ya no se lista— y no hace falta migrarlos.
- Cubierto en el selftest con un pool que publica el mismo zip que el manifest del launcher.
