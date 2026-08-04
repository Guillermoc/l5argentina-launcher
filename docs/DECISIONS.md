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
| [049](#adr-049) | El PDF de impresión se escribe a mano, sin librería | 2026-07-29 | Vigente |
| [050](#adr-050) | En la barra van las acciones frecuentes; el resto, al componente que le toca | 2026-07-29 | Vigente |
| [051](#adr-051) | Los paneles se agrupan por objeto, no por lado de la pantalla | 2026-07-29 | Vigente |
| [052](#adr-052) | Buscador y Mazos son un solo espacio de trabajo; correcciones de la pasada de UI | 2026-07-30 | Vigente |
| [053](#adr-053) | Las miniaturas se decodifican en una cola propia, no con `IsAsync` | 2026-07-30 | Vigente |
| [054](#adr-054) | El launcher dice si hay novedades; un pack en 1.0 no es una novedad | 2026-07-30 | Vigente |
| [055](#adr-055) | Los packs de imágenes son una sola familia; el pool dice cuáles hay | 2026-07-30 | Vigente |
| [056](#adr-056) | Un pack **nuevo** avisa aunque salga en 1.0 | 2026-07-30 | Vigente |

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

**Fecha:** 2026-07-28 · **Estado:** vigente **en parte** — la identificación por hash sigue; la
exclusión de la lista **la revierte [ADR-055](#adr-055)**.

**Decisión.** El pack de imágenes propio de la comunidad —la sección `images` del manifest del
launcher, la que muestra la barra "Imágenes Samurai Extended"— **también está publicado en el pool**.
`LauncherEngine.ImagePacks` lo excluye de la lista del overlay comparando el **sha256** contra
`Manifest.Images.Sha256`.

> **Revisado por [ADR-055](#adr-055).** Sacarlo de la lista atacaba el síntoma: el problema era que
> cada camino llevaba su propio registro de "aplicado". Hoy aparece en la lista como un pack más
> —marcado "Incluido · se aplica al jugar"— y los dos registros se escriben juntos. Lo que sigue
> vigente de este ADR es **identificarlo por hash y no por id**, por las razones de abajo.

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
- Los `appliedImagePacks["samuraiEx"]` que ya estén en un `config.json` quedan ahí. *(Con
  [ADR-055](#adr-055) dejaron de ser inertes: el pack vuelve a la lista y ese registro es justamente
  lo que evita que se ofrezca de nuevo.)*
- Cubierto en el selftest con un pool que publica el mismo zip que el manifest del launcher.

---

<a id="adr-049"></a>
## ADR-049 — El PDF de impresión se escribe a mano, sin librería

**Fecha:** 2026-07-29 · **Estado:** vigente.

**Decisión.** `Services/Deck/PdfExporter.cs` emite el PDF byte a byte. No se agregó ninguna
dependencia de PDF al proyecto.

**El problema.** La app Android usa `android.graphics.pdf.PdfDocument`, que es parte de la
plataforma. En .NET Framework 4.8 no hay equivalente: o entra una librería, o se escribe el archivo.

**Por qué a mano.** El PDF que necesitamos es el caso más simple que existe: **imágenes en una
grilla, sin una sola letra**. Lo que hace difícil escribir un PDF —fuentes, encoding de texto,
tipografía embebida— acá no aparece. Y hay un motivo mejor: **las imágenes de las cartas ya son
JPEG**, y un PDF sabe llevar JPEG crudo adentro con el filtro `/DCTDecode`. Los bytes del `.jpg`
pasan al archivo **sin decodificar ni recomprimir**, así que la impresión sale con exactamente la
calidad del original. Una librería genérica decodifica a bitmap y vuelve a comprimir: se pierde
calidad, o pesa más, o las dos cosas.

Se descartó **PdfSharp** (la única opción madura para net48): su versión para Framework está
congelada, suma ~1 MB al exe vía Costura y su `XImage` recomprime. Se descartó **imprimir con XPS**
y dejar que el usuario elija "Microsoft Print to PDF": depende de que esa impresora exista y no
permite fijar el tamaño físico de la carta, que es el único requisito que de verdad importa acá.

**Lo que sí necesita cuidado.** Dos cosas, y las dos tienen check:

- **La tabla `xref`.** Es el error clásico del PDF escrito a mano: si el offset del objeto N no cae
  justo sobre `N 0 obj`, algunos visores reconstruyen la tabla y otros directamente no abren el
  archivo. `XrefOffsetsResolve` en el selftest recorre la tabla y verifica cada offset.
- **Solo baseline.** `/DCTDecode` garantiza JPEG baseline; un progresivo (SOF2) o un CMYK lo abren
  algunos visores y otros no. `PdfImage.TryLoad` lee el SOF y, si no es baseline gris o YCbCr,
  recodifica con `JpegBitmapEncoder`. **No es código muerto**: de las 8856 imágenes de la
  instalación real, 8 son progresivas (los promos de Samurai Extended y `GGG001`) y pasan por ahí.

**Consecuencias.**

- Una carta repetida embebe su JPEG **una sola vez** y lo referencia N veces; sin eso un mazo con
  3 copias de todo pesaría el triple.
- El origen de coordenadas del PDF es abajo-izquierda y el de la app es arriba-izquierda: la fila se
  espeja en Y. Sin eso el mazo se imprime al revés, y es el tipo de bug que no se nota leyendo el
  código. Tiene check propio.
- Si algún día hiciera falta **texto** en el PDF (nombre del mazo, marcas de corte), esta decisión se
  revisa: ahí sí la balanza se corre hacia una librería.

---

<a id="adr-050"></a>
## ADR-050 — En la barra van las acciones frecuentes; el resto, al componente que le toca

**Fecha:** 2026-07-29 · **Estado:** vigente.

**El problema.** Las barras habían crecido por acumulación: **22 controles permanentes** en el
Buscador (11 arriba, 11 abajo) y 12 en el editor. Muchos eran botones para cosas que no son acciones
—el tamaño de celda, la dirección del orden— y varios estaban duplicados o mezclados con los
filtros. Es el mismo problema que [ADR-043](#adr-043) resolvió para las seis acciones de mazo y que
[ADR-035](#adr-035) resolvió para el colapso de paneles, pero en la barra del Buscador nunca se había
hecho la pasada.

**La regla.** En la barra queda lo que se **usa seguido**. Cada otra cosa va al componente que le
corresponde según **qué tipo de cosa es**:

| Era | Es | Por qué |
|---|---|---|
| `－`/`＋` de zoom (2 botones × 2 ventanas) | desplegable pegado al segmentado Lista\|Grilla | El tamaño de celda es una **propiedad de la vista**, igual que Lista o Grilla. Cuatro botones para cuatro escalones era caro; el desplegable además **dice en qué tamaño está**, que los `－`/`＋` no decían |
| combo de orden + `↑`/`↓` aparte | un solo desplegable, con la flecha en cada opción | Orden y dirección son **un concepto**: había que mirar dos controles para saber cómo estaba ordenado |
| `＋` y `🗑` pegados al selector de conjunto | filas **adentro** del desplegable | Son operaciones **sobre lo que ese desplegable lista**, y se usan una vez cada tanto. Es el patrón que ese mismo combo ya usaba con "Usar mazo…" |
| botón "Mazos" | nada (queda el nombre del mazo, ya clickeable) + `Ctrl+D` | Llamaba **al mismo handler**: dos controles pegados para una acción |
| chip "Limpiar filtros" siempre visible | visible solo si hay filtros | Un botón que la mitad del tiempo no hace nada. Ahora su presencia **es** la señal de que hay filtros |
| `⫞` lado a lado en la barra del editor | fila del menú `⋯` — **revertido por [ADR-052](#adr-052)**: vuelve a la barra | Es una acción de **ventana**, no de mazo. El criterio era correcto pero la frecuencia de uso pesa más: se usa en cada sesión de armado |

**Consecuencias.** El Buscador queda en ~14 controles y el editor en ~8. La regla es la que hay que
aplicar la próxima vez que aparezca la tentación de "le agrego un botoncito a la barra": si no es una
acción frecuente, la pregunta es *de qué componente es*.

**Dos trampas de layout que aparecieron y conviene no repetir.**

- **Un `Popup` adentro de un `StackPanel` de barra se mide como hijo** y le come el ancho deseado a
  los controles que vienen después (el orden y la ayuda se iban de la ventana). Los popups van en la
  raíz, como `HoverPreview`.
- **Dos columnas `*` compitiendo dejan sin ancho al grupo de la derecha.** La fila 1 del Buscador
  pasó a `DockPanel` con ese grupo anclado a la derecha y primero en el XAML: cobra su tamaño antes
  que nada y lo que cede es la caja de búsqueda.

**Nota de método, porque costó.** El diagnóstico inicial fue **un artefacto de medición, no un bug**:
las capturas con `PrintWindow` salían cortadas porque el proceso que capturaba no era *DPI-aware* y
dimensionaba el bitmap con las coordenadas virtualizadas (1323) en vez de las físicas (1654, monitor
a 125 %). La UI siempre había entrado. Para verificar layout a futuro: `SetProcessDPIAware()` antes
de capturar, o mejor, medir con **UI Automation** (`BoundingRectangle`), que da números y no píxeles
para interpretar.

---

<a id="adr-051"></a>
## ADR-051 — Los paneles se agrupan por objeto, no por lado de la pantalla

**Fecha:** 2026-07-29 · **Estado:** vigente.

**El problema.** El editor tenía cuatro pestañas repartidas en dos paneles colapsables: izquierda
**Abrir | Filtros**, derecha **Carta | Stats**. El agrupamiento era por *lado de la pantalla* —
izquierda "controles", derecha "lecturas" — y esa lectura se cae cuando se mira **sobre qué objeto
opera cada una**:

| Pestaña | Su objeto |
|---|---|
| Abrir | la **biblioteca** de mazos (otro mazo) |
| Filtros | las cartas del **mazo abierto** |
| Stats | el **mazo abierto**, como un todo |
| Carta | **una carta** |

"Filtros" y "Stats" hablan del mismo objeto y estaban en paneles opuestos; "Abrir" es de otro alcance
entero y competía por un slot con "Filtros", que sí se usa mientras se trabaja. El costo concreto:
**colapsar un panel para ver más cartas apagaba dos cosas sin relación entre sí**. Cerrar la izquierda
para ganar ancho se llevaba el selector de mazo *y* los filtros.

**Decisión.** Agrupar por objeto:

- **Panel izquierdo → el mazo abierto**: pestañas **Filtros | Stats**. Filtrarlo y medirlo son dos
  lecturas de lo mismo; colapsar el panel esconde una sola cosa.
- **Panel derecho → la carta seleccionada**, y nada más. **Sin pestañas**: una sola cosa no necesita
  elegirse.
- **Elegir qué mazo abrir deja de ser un panel** y pasa a colgar del **título del mazo**, que ya
  estaba arriba a la izquierda y es el lugar natural — el mismo patrón que el Buscador, donde el
  nombre del mazo activo ya es clickeable.

**Por qué un popover y no un `ComboBox`.** "Abrir" no era una lista: tenía segmentado Mazos\|Listas,
buscador por nombre **o por carta contenida**, y selectores de orden, clan y estado de armado. Un
combo habría perdido todo eso. El `DeckPickerPopup` se lleva el bloque **entero**, sin recortarle
nada, y se cierra solo al elegir — que es cuando cumplió su objetivo.

**Por qué el título y no un botón al lado.** Un botón "Abrir mazo…" habría sumado un control más a la
barra, que es justo lo que [ADR-050](#adr-050) acaba de sacar. El título se lee como título (misma
tipografía y tamaño) y solo el `▾` y el fondo en hover delatan que es un disparador.

**Consecuencias.**

- El picker necesita `AutomationProperties.Name` puesto a mano: su contenido es un panel y no un
  texto, así que sin eso el control de navegación principal **no tiene nombre accesible**.
- El texto de "no hay mazo abierto" decía "elegí un mazo **a la izquierda**": se corrigió, y es el
  tipo de mentira que un cambio de layout deja sin que el compilador se entere.
- El panel derecho quedó con un encabezado fijo "Carta" en vez de pestañas.

---

<a id="adr-052"></a>
## ADR-052 — Buscador y Mazos son un solo espacio de trabajo

**Fecha:** 2026-07-30 · **Estado:** vigente.

**Decisión.** Abrir el Buscador abre **también** el editor de Mazos (`DeckWindow.OpenWorkspace`). El
editor sigue siendo **único por proceso**; de Buscadores se pueden tener varios (`Ctrl+N`). Cerrar el
editor cierra **solo el editor**: el único dueño de la aplicación sigue siendo `MainWindow`
([ADR-039](#adr-039)).

**Por qué.** Los `+`/`−` del Buscador escriben siempre contra el mazo activo, y el único lugar donde
se ve ese mazo es el editor. Tenerlo cerrado es un estado en el que la mitad de la interfaz opera
sobre algo invisible, y abrirlo era un paso manual en el **único flujo real** de la app.

**Descartado: acoplar también el cierre.** La primera versión hacía que cerrar Mazos cerrara la
aplicación entera, con aviso, razonando que si van siempre juntos "cerrar solo Mazos" reconstruye el
estado que queremos evitar. Está mal: **abrir juntos no es vivir atados**. Un botón de cerrar que se
lleva la aplicación entera traiciona lo que el usuario pidió —cerrar una ventana—, y el estado que
buscábamos evitar es recuperable con `Ctrl+D`, que reabre el editor en el mismo mazo. Un segundo dueño
de la aplicación tampoco era gratis: dejaba sin salida al "volver al Launcher sin perder el trabajo".

**Consecuencia buscada.** No hace falta **ningún control** en la barra del Buscador para llegar a
Mazos: ya está abierta. Queda `Ctrl+D` para traerla al frente, y el nombre del mazo activo vuelve a
ser una **etiqueta** — que fuera clickeable era confuso, porque nada lo delataba salvo el cursor.

### Correcciones a la pasada de UI anterior

Lo que no sobrevivió al uso de [ADR-050](#adr-050)/[ADR-051](#adr-051), más un bug que salió con ellas:

- **El lado a lado vuelve a ser un botón de la barra.** El criterio de ADR-050 —es una acción de
  ventana, no de mazo— sigue siendo cierto, pero la **frecuencia manda**: se usa en cada sesión de
  armado, y enterrarlo en el `⋯` le agregaba dos clicks a lo que más se hace.
- **El selector del título necesita borde permanente.** Se había dejado el fondo solo en hover para
  que se leyera como título; el resultado fue que **no se leía como control** y había que descubrirlo
  pasando el mouse. Ahora el borde y el `▾` están siempre.
- **El ícono del lado a lado se dibuja, no es un glifo.** `⫞` (U+2ADE) **no está en Segoe UI** y caía
  a un fallback ilegible; los bloques `▊▎` se fusionan en una mancha. Dos `Border` de anchos
  desiguales dibujan el 70/30 y no dependen de la cobertura de ninguna fuente. Regla general: para un
  ícono que no sea de los básicos, **dibujarlo**.

- **El combo de keywords se comía la primera letra.** Al abrir el desplegable por código, el
  `ComboBox` editable re-emite su texto sobre el editor y lo deja **entero seleccionado**; la tecla
  siguiente lo reemplazaba (se tipeaba `bushi` y quedaba `ushi`). El autocomplete abre el desplegable
  en el primer carácter, así que pasaba en cada palabra nueva. Ahora el caret se colapsa **después** de
  tocar `IsDropDownOpen`, no antes. Gotcha general: en un `ComboBox` editable, todo lo que reemita el
  texto —abrir el popup, mover `SelectedItem`— vuelve con el texto seleccionado.

### Sin ningún mazo abierto

Estado real solo en una instalación nueva o si se borraron todos (con al menos uno, se abre solo el
activo o el primero). Antes quedaba la ventana entera vacía sin decir por dónde empezar, y el `＋` para
crear vive **dentro** del picker. Ahora: las pestañas Filtros/Stats se **apagan** —no hay nada que
filtrar ni medir—, el panel explica qué hacer, y **el picker se abre solo**, porque crear un mazo es
literalmente la única acción disponible.

### El parpadeo de la previsualización con Alt

Con `PlacementMode.Mouse`, WPF resuelve el desborde **volteando** el popup al otro lado del cursor.
Contra el borde derecho eso lo deja justo **debajo del mouse**, y ahí arranca un ciclo: el popup tapa
la celda → salta su `MouseLeave` → se cierra → el mouse vuelve a estar sobre la celda → `MouseMove` la
reabre. Parpadeo infinito.

Lo que hay que garantizar no es "que entre" sino que **el cursor nunca quede adentro del popup**. Por
eso la posición se calcula a mano (`HoverPreviewPlacer`, `PlacementMode.Absolute`) y el volteo corre el
popup **su ancho completo más el espacio**, en vez de espejar el offset. El área de trabajo se pide al
monitor donde está el cursor (`SystemParameters.WorkArea` solo conoce el primario, y esta máquina tiene
dos pantallas).

La geometría quedó como **función pura** (`ComputePosition`) y el selftest la barre en toda la pantalla
con paso de 8 px verificando el invariante, más el caso patológico de un área más chica que el propio
popup. Era verificable sin UI y no había motivo para dejarlo sin cubrir.

---

## ADR-053 — Las miniaturas se decodifican en una cola propia, no con `IsAsync`

**Fecha:** 2026-07-30 · **Estado:** vigente.

**Síntoma.** Abrir el Buscador y ver la grilla entera con los nombres pero **sin una sola imagen**
durante decenas de segundos; después, todas de golpe.

**Medición** (caché del SO caliente, proceso ocioso, binding `{Binding Thumb, IsAsync=True}`):

| t | qué se ve | CPU acumulada del proceso |
|---|---|---|
| 1,7 s | ventana lista, 6332 resultados, cero imágenes | — |
| 1,7 → 23 s | nada | +0,25 s en 21 s (≈1 %) |
| 23 s | **una** carta | +0,02 s |
| 39 s | todas | +0,55 s de golpe |

La tercera columna es la que decide: durante los 20 s en blanco **nadie trabaja**. No es el disco, no
es el antivirus, no es el decode. Se descartaron por experimento, no por razonamiento:

- **input**: mandarle `WM_MOUSEMOVE` a la ventana no adelanta nada — la cadencia es temporal, tandas
  cada ~16 s;
- **el pipeline de URI de WPF** (`UriSource` → `FileWebRequest`, que en frío puede quedarse en
  detección de proxy): leer con `FileStream` da exactamente lo mismo;
- **quitar `IsAsync`**: la ventana muestra **todo** de entrada, pagando el decode de la pantalla
  entera en el thread de UI.

O sea: decodificar una pantalla cuesta unos segundos de UI; los 40 s los ponía el planificador de
bindings asíncronos de WPF, que los despacha a prioridad baja del dispatcher.

**Decisión.** Binding sincrónico + `ThumbnailLoader` propio. `GridCell.Thumb` devuelve lo que ya esté
decodificado (`ThumbnailCache.Peek`, sin tocar disco) y, la primera vez, encola el resto; el resultado
vuelve por `PropertyChanged` al dispatcher que lo pidió. Tres workers del thread pool.

**Dos detalles que no son obvios.**

- **LIFO.** Se atiende el último pedido primero: al scrollear rápido, lo que quedó en pantalla es lo
  último que se pidió. Con FIFO se decodifica primero todo lo que ya pasó de largo y lo visible espera
  al final — exactamente el defecto que estábamos arreglando, en chiquito.
- **No se descarta nada.** Un pedido viejo igual cuesta ~10 ms y su resultado queda en el caché;
  descartarlo por antigüedad arriesga dejar en blanco una celda que **sigue visible**, que es peor que
  decodificar de más.

**Verificación.** Es de pipeline de UI, no entra al selftest (§5.1): se verificó midiendo. Después del
cambio, la grilla aparece **con las miniaturas ya puestas** (~2,4 s) y, scrolleando de a tres páginas
con `ScrollPattern`, a los **150 ms** de cada salto todas las celdas visibles tienen arte. El editor de
Mazos comparte `GridCell` y arrastraba el mismo defecto: se arregló con el mismo cambio.

---

## ADR-054 — El launcher dice si hay novedades; un pack en 1.0 no es una novedad

**Fecha:** 2026-07-30 · **Estado:** vigente.

**Problema.** El estado de las descargas estaba **repartido**: la base tenía su línea, el pack propio la
suya, y los packs del pool solo se veían **adentro del modal**. Desde la pantalla no se podía saber si
faltaba algo sin abrir el modal y revisar fila por fila. "No veo nada raro" no significaba nada.

**Decisión.** Un aviso fijo en el **header**, debajo de los botones, que **siempre dice algo**:
`Hay novedades: …` o **`Todo al día.`**. Que exista el caso "no hay nada" es la mitad del valor: sin
él, la ausencia de aviso es ambigua entre "estás al día" y "no miré".

Va en el header y no en una franja propia por una restricción concreta: el lienzo de `MainWindow` es
**fijo** (1080×720 escalado), así que cada fila nueva se la come el cuerpo — la primera versión, una
franja con caja propia entre el banner y las bases, le recortaba el placeholder de Noticias. Debajo de
los botones del header había lugar libre, y encima es donde uno mira al abrir.

**Qué cuenta.** Base Comunidad por descargar, el pack de imágenes propio, y los packs del pool
pendientes. El update del launcher **no** entra en la enumeración: ya tiene su banner arriba, en
grande, con su botón. El catálogo del Buscador tampoco: se sincroniza solo y no hay nada que el
usuario pueda hacer al respecto.

**La regla del 1.0.** Un pack de imágenes en su **versión inicial** no genera pendiente. Los packs por
arco son **catálogo, no novedades**: son ~20 arcos de arte opcional que se bajan cuando uno quiere, y
marcarlos todos como "te falta esto" en cada arranque convierte el aviso en ruido permanente — y un
aviso que siempre está prendido se aprende a ignorar, con lo cual deja de servir para lo que sí
importa. Que un pack pase de 1.0 **sí** es novedad: significa que se republicó con imágenes nuevas o
corregidas. Se compara numéricamente, así que `1`, `1.0` y `1.0.0` son lo mismo.

En el modal la distinción se ve igual: un pack republicado sin bajar dice **"Novedad · falta
descargar"** en oro; uno en 1.0 sin bajar sigue en "No descargado" apagado. Se puede bajar igual — no
está escondido, solo no está destacado.

**La fila de imágenes pasa a resumir todos los packs** (`Imágenes de cartas: 8 de 8 packs
descargados`, con `· N con novedades` en oro cuando corresponde). Antes mostraba un solo pack pero
tenía al lado el botón que abre el listado de los otros, así que se leía como si hablara de todos: con
dos packs actualizados no cambiaba **nada** ahí, y encima contradecía al aviso del header. El resto de
ese hilo está en [ADR-055](#adr-055).

Con el manifest de producción de hoy la regla deja **1** pendiente (`promo` 1.2) y descarta los seis
arcos en 1.0, que es exactamente el resultado buscado.

**Límite conocido.** Si un pack se republica **sin** subir la versión, no se cuenta. Es deliberado: la
versión es lo único que el pool promete como "esto cambió". El pack igual aparece con "Actualización
disponible" al abrir el listado, porque ahí sí se compara por hash.

**Honestidad del "todo al día".** Si el manifest del pool no se pudo cargar, no hay lista de packs, y
"cero pendientes" no es lo mismo que "no sé": en ese caso la franja lo dice
(`Todo al día, salvo los packs de imágenes: no se pudieron consultar`). Sin eso, la primera vez que el
pool estuviera caído el aviso mentiría, y un aviso que mintió una vez ya no se cree.

---

## ADR-055 — Los packs de imágenes son una sola familia; el pool dice cuáles hay

**Fecha:** 2026-07-30 · **Estado:** vigente. **Revisa** [ADR-048](#adr-048).

**El error.** El launcher trataba al pack de imágenes de la comunidad como una **categoría**: sección
`images` (objeto, no lista) en el manifest del bucket, dos campos sueltos en `config.json`
(`appliedImagePackVersion`/`Sha256`), métodos propios (`ImagesNeedSync`/`SyncImagesAsync`), una fila
propia en la pantalla y hasta el **nombre hardcodeado en el `.resx`** ("Imágenes Samurai Extended").
Todo eso asume que las cartas de la comunidad van a ir **siempre a ese único pack**. El día que salga
una expa, hay un segundo pack de la comunidad y la fila sigue nombrando al primero.

**Decisión.** Los packs de imágenes son **una sola familia** y el **manifest del pool** es quien dice
cuáles hay: cualquier pack nuevo aparece solo por su `type: "images"`, sea de un arco o de la
comunidad, sin tocar una línea del launcher ni el manifest del bucket. Se acabaron la fila propia y el
nombre fijo: la pantalla dice `Imágenes de cartas: N de M packs descargados` y el listado los muestra
a todos.

**Qué queda de la sección `images` del bucket.** Deja de significar "el pack de la comunidad" y pasa a
significar **"este pack se aplica solo al apretar JUGAR"** — una propiedad de *un* pack, no una
familia. Se lo sigue reconociendo por hash y se lo marca en el listado con
**"Incluido · se aplica al jugar"**. Los packs de la comunidad que vengan después son packs del pool
como cualquier otro: se bajan a mano desde el listado.

**Qué se revisa de ADR-048.** Aquel ADR sacaba el pack incluido de la lista del pool porque aparecía
duplicado. El diagnóstico estaba a medias: el problema real no era que **apareciera** en la lista sino
que cada camino llevaba **su propio registro de aplicado**, así que uno decía "al día" y el otro
"actualización disponible" sobre el mismo archivo. Ahora aparece una sola vez (el pool es la única
fuente de la lista) y los dos registros se escriben y se leen juntos, con lo cual no se contradicen ni
se baja dos veces. Esconderlo, además, tenía un costo que no habíamos visto: el único pack que el
launcher aplica solo era el único que no se podía ver en ninguna lista.

**Sin migración de config.** Los dos campos sueltos se conservan y se siguen escribiendo: cambiar el
formato del `config.json` de todos los usuarios para ahorrar dos campos no se paga sola.

**Punto abierto** — resuelto enseguida en [ADR-056](#adr-056): con [ADR-054](#adr-054), un pack nuevo
que aparece en 1.0 no se destacaba, y una expa de la comunidad sale justamente en 1.0.

---

## ADR-056 — Un pack nuevo avisa aunque salga en 1.0

**Fecha:** 2026-07-30 · **Estado:** vigente. **Completa** [ADR-054](#adr-054).

**El choque.** ADR-054 dice que un pack en versión inicial no es novedad, porque los ~20 arcos son
catálogo opcional y marcarlos todos sería ruido permanente. [ADR-055](#adr-055) dice que un pack nuevo
de la comunidad —una expa— aparece en el pool como cualquier otro. Y una expa nueva sale en **1.0**:
juntas, las dos reglas garantizaban que lo más importante que puede pasar no se avise nunca.

**La distinción que faltaba.** "Pack que nunca vimos" no es lo mismo que "pack viejo que nunca
bajaste". El primero es una novedad; el segundo es una decisión que el usuario ya tomó. Para separarlos
alcanza con recordar qué ids se vieron: `config.knownImagePacks`.

**La siembra es la clave.** La primera vez que este launcher ve el pool, anota **todo** lo que hay como
conocido. Sin eso, estrenar esta versión marcaría los ~20 arcos de siempre como novedades — exactamente
el ruido que ADR-054 evita, con el agravante de aparecer una sola vez y quemar la confianza en el
aviso. Después de sembrar, lo que aparezca es genuinamente nuevo. Por eso `knownImagePacks` es `null`
—"todavía no se sembró"— y no una lista vacía, que significaría "no había ningún pack".

**Regla final:** un pack es pendiente si no lo tenés aplicado **y** (se republicó **o** es nuevo). Baja
de novedad al descargarlo; de ahí en más manda la versión, como cualquier otro. En el listado se
distinguen: **"Pack nuevo · falta descargar"** contra **"Novedad · falta descargar"**.

**Costo.** Un campo más en `config.json` que solo crece, con ids de packs. Se paga con la única cosa
que el aviso tiene que acertar sí o sí.

---

## ADR-057 — Las etiquetas del mazo son pilas, y el cruce es una pila más

**Fecha:** 2026-08-01 · **Estado:** vigente.

**El pedido.** Armar un mazo en la mesa es apilar: las que quitan honor de un lado, las que piden
magistrados del otro, y en el medio las que hacen las dos cosas. El editor mostraba el mazo con el
único eje que trae el juego —Starter/Dynasty/Fate → tipo—, que sirve para contar y para validar, pero
no dice nada sobre para qué está cada carta en **ese** mazo.

**Qué se guarda y dónde.** Un vocabulario de etiquetas por mazo (nombre + color) y la asignación
carta → etiquetas. Va al sidecar `decksmeta.json`, al lado de las recortadas, y **por nombre de
carta** ([ADR-044](#adr-044)). Nunca al `.txt`: ese archivo lo lee el juego y las etiquetas no
significan nada para él ([ADR-022](#adr-022), [ADR-024](#adr-024)). Por eso también se guardan sin
reescribir el `.txt` (`DeckLibrary.SaveMeta`): tocar un archivo ajeno para algo que no le importa es
riesgo sin beneficio.

**Por mazo, no por carta.** La etiqueta describe el **rol de la carta en este mazo**: la misma carta
puede ser "deshonor" acá y "defensa" allá. Global —un store propio, como las favoritas— hubiera
permitido reusar la clasificación en el mazo siguiente y filtrar por etiqueta en el Buscador, pero
obliga a que una carta signifique lo mismo en todos lados, y toca `CardSearchCriteria`, que está
cubierto por golden. Duplicar el mazo se lleva las etiquetas, que cubre buena parte del caso de reuso.

**La decisión de fondo: la carta está en todas sus pilas.** Al agrupar por etiqueta hay una sección
por etiqueta —en el orden en que se crearon, con `Sin etiquetar` al final— y **una carta aparece en
todas las suyas**: la que quita honor y pide magistrados está en las dos. Los conteos por sección, en
consecuencia, **no** suman el mazo: son conjuntos que se solapan, no cajas. La cabecera agrega
`· N también en otra` para que el solapamiento sea un dato visible y no una sorpresa aritmética.

**El primer intento fue una partición y estaba mal.** La primera versión ponía cada carta en **una
sola** sección, la de su combinación exacta (`Deshonor`, `Magistrados`, `Deshonor + Magistrados`,
`Sin etiquetar`). El argumento era que así los conteos cierran con el mazo y el cruce queda como un
conjunto con nombre propio. Los dos son ciertos y los dos son irrelevantes: **la pregunta que se hace
armando no es "¿en qué caja va esta carta?" sino "¿cuánto deshonor tengo?"**, y con una partición la
pila `Deshonor` **miente por defecto** — se saltea justamente las cartas que hacen las dos cosas, que
suelen ser las mejores del paquete. Para contestar había que sumar secciones a mano, que es
exactamente el trabajo que la vista tenía que ahorrar. El cruce como sección separada resuelve un
problema de contabilidad que nadie tenía.

Se conserva el dato que la partición daba gratis —cuántas cartas de la pila están además en otra— como
un número en la cabecera, y los puntitos de color dicen en cuáles. Eso cubre "quiero ver el cruce" sin
romper "quiero ver el paquete completo".

**Costo aceptado: la carta se dibuja más de una vez.** Con tres etiquetas sobre la misma carta, la
carta se ve tres veces. Es correcto: pertenece a los tres conjuntos. Comparten `GridCell`, así que no
se paga decodificación de más.

**Los puntitos de color son parte de la decisión.** Cada carta lleva los puntos de sus etiquetas
también en la vista por tipo. Sin eso, el solapamiento solo se ve cambiando de agrupación; con eso, se
ve siempre y no hay que elegir entre "ver el mazo como se juega" y "ver cómo lo pensé".

**El color: uno por defecto, elegible después.** Crear una etiqueta no pregunta el color —se asigna
por orden de la paleta— porque en el medio de armar un mazo, elegir un color es una decisión que no
viene al caso. Pero el color **es** cómo se reconoce la etiqueta de un vistazo en los puntitos, así
que se cambia desde su menú, con las diez muestras a la vista. La paleta es **cerrada**: son los
colores que se leen sobre el fondo oscuro y como punto de 8 px encima del arte de una carta; un
selector libre habilita el gris sobre gris. `DeckTags.SetColor` rechaza cualquier valor de afuera.

**El click derecho cambió de significado.** Antes copiaba el arte directo; ahora abre un menú con las
etiquetas de la carta y, al pie, "Copiar imagen". Etiquetar tiene que estar donde está la carta —es el
gesto de la mesa y se hace muchas veces seguidas— y copiar el arte es ocasional; el costo es un click
más en la acción menos frecuente.

**Lo que no cambia.** Nada de esto toca `DeckRepository`, `AddCard`, el export, el `.txt` ni el PDF:
las etiquetas son una capa de lectura sobre el mismo mazo. No hay golden que las cubra —la app no las
tiene— así que se verifican con checks propios (§5.1): persistencia por nombre, que no asomen en el
`.txt`, renombrar/borrar, que la etiqueta acompañe a la carta hasta que se la saca del mazo, y que la
carta compartida aparezca **en las dos pilas** con su conteo de solapamiento.

**Sobre `deck_card_groups`.** La app Android tenía una tabla de subgrupos dentro del mazo, en desuso y
sin portar. Esto **no** es ese modelo: aquel era un grupo excluyente por carta (una carta, un grupo);
acá una carta está en las pilas que haga falta, que es la mitad del pedido.

---

## ADR-058 — El panel de filtros es un control compartido, no dos paneles parecidos

**Fecha:** 2026-08-02 · **Estado:** vigente.

**El pedido.** Filtrar las cartas **del mazo abierto** con los filtros del Buscador: tipo, clan,
keywords, stats. Adentro del editor, sin salir a otra ventana, porque lo que se está haciendo es
mirar el mazo (con sus cantidades y sus pilas), no navegar el catálogo.

**La alternativa obvia era copiar.** El panel del Buscador son ~190 líneas de XAML más los
constructores de chips, sets y filas de stats. Copiarlo a `DeckWindow` habría sido más rápido y
habría garantizado que las dos copias se separen: el primer filtro nuevo entra en una sola, y a los
seis meses "los filtros" significan dos cosas distintas según la ventana. Se extrajo a
`Views/CardFilterPanel.xaml(.cs)`, que usan las dos.

**El seam.** El control **no busca nada**: llena un `CardSearchCriteria` (`Fill`) y avisa que algo
cambió. Quién lo aplica es del dueño — el Buscador, sobre el catálogo; el editor, sobre las cartas
del mazo. Lo que solo existe en el Buscador (la barra con sus modos, los toggles rápidos, el conjunto
activo, el orden) lo agrega `SearchWindow` después de `Fill`. Los **presets** se quedaron afuera por
lo mismo: guardan conjunto activo, favoritas y orden, que en un mazo no significan nada.

**Cómo filtra el editor.** Corre el **mismo motor** contra el catálogo entero y se queda con los ids
que además están en el mazo. Suena redundante —6.400 cartas para filtrar 80— pero cuesta
milisegundos y evita lo caro: una segunda implementación de la semántica de filtros que se despegue
de la portada, que es justo lo que los golden protegen. El resultado entra como un `.Where()` más en
el pipeline que ya existía, así que agrupar (por tipo o por etiqueta), ordenar, las recortadas y los
chips Starter/Dynasty/Fate siguen funcionando igual.

**Dos filtros se fuerzan adentro del mazo: baneadas y ocultas siempre entran.** Son filtros para
**navegar el catálogo**; sobre un mazo lo único que pueden hacer es esconder cartas que el mazo tiene
de verdad. Una baneada que quedó en el `.txt` tiene que verse justamente para poder sacarla. Por eso
el editor esconde la sección "Otros" y fija `IncludeBanned`/`IncludeHidden` en true: un control
visible que solo puede mentir es peor que no tenerlo.

**Los prefijos también.** `tx.` (texto de carta) y `k.`/`c.`/`t.` + Enter (keyword, clan, tipo)
funcionan en la caja del editor igual que en la barra del Buscador. `ParseBar` pasó a ser público y
estático en el control: es el único pedazo de todo esto que se puede verificar de a un caso, y ahora
tiene dos llamadores. Se le sumaron checks al selftest, incluido el que importa — **`tx.` se evalúa
antes que `t.`**, o "tx.algo" se leería como el tipo "x.algo".

**Costo asumido.** Una mudanza grande sobre una ventana que acababa de tener una pasada de UX
([ADR-050](#adr-050), [ADR-051](#adr-051)), y el selftest no cubre UI. Se verificó a mano: el panel
del Buscador quedó idéntico y el del editor filtra (chip Tipo=Holding sobre un mazo Crab deja
`Dynasty (18) [11] → Holding (18) [11]`).
