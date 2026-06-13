# Especificación — Launcher L5Argentina para Sun and Moon

> Documento de diseño para arrancar el proyecto del launcher en una carpeta nueva.
> Autocontenido: se puede leer sin conocer el resto del ecosistema. Pensado también
> para servir como base del CLAUDE.md / README inicial del proyecto nuevo.
> Nombre del producto: **L5Argentina Launcher**.

---

## 1. Propósito

La comunidad L5Argentina (≈25 personas) juega *Legend of the Five Rings* en la app
**Sun and Moon** usando una base de cartas curada propia (un XML generado desde nuestra
DB, con un formato custom `s_extended` y una banlist). Hoy cada persona tiene que
actualizar y reemplazar archivos a mano, lo cual:

- es engorroso de mantener sincronizado entre 25 personas,
- excede técnicamente a algunos usuarios,
- complica alternar entre **nuestra base** y la **base original** (algunos quieren, a
  veces, jugar con cartas que no incluimos).

El launcher resuelve esto: un ejecutable Windows que, al abrirse, chequea si hay una
base nueva en nuestro bucket, deja **elegir con qué base jugar** (comunidad u original),
y lanza Sun and Moon. En versiones siguientes, muestra noticias e info de torneos.

---

## 2. Sun and Moon — la app que lanzamos

App de escritorio hecha en **Unity** (Windows, x64). No la distribuimos nosotros: tiene
su propia web y cada usuario la instala por su lado. El launcher **lanza la instalación
existente del usuario**, no trae una copia.

### 2.1 Rutas relevantes (relativas a la carpeta de instalación)

```
<SunAndMoon>\
├─ Sun and Moon.exe                                  ← ejecutable a lanzar
├─ Sun and Moon_Data\
│  └─ StreamingAssets\
│     └─ Database\
│        ├─ database.xml                             ← LA base activa (lo que pisamos)
│        └─ database - copia.xml                     ← backup de la base que había
└─ images\
   └─ cards\<EDICION>\<ID>.jpg                       ← arte de cartas, por edición
```

- La app lee **`StreamingAssets\Database\database.xml`** al iniciar. Cambiar de base =
  reemplazar ese archivo. Eso es todo lo que el launcher necesita escribir.
- Las imágenes se sirven por path relativo desde el XML (`images/cards/<Edicion>/<id>.jpg`).
  Nuestra base usa los packs clásicos; ~30 cartas propias (CE15MRP nuevos, etc.) tienen
  imágenes que no están en los packs originales y hay que distribuirlas aparte (ver §11).
- `Sun and Moon_Data\output_log.txt` es el log de debug de la app (útil si algo del XML
  no carga).

### 2.2 Formato del XML de cartas (resumen)

Raíz `<cards version="...">`, hijos `<card id="..." type="...">`. Dentro, en orden:
`<name>`, `<rarity>` (minúsculas), uno o más pares `<edition>`+`<image edition="...">`,
varios `<legal>`, `<clan>` (si aplica), `<text>` (CDATA, se renderiza como HTML),
stats según tipo, `<flavor>` (CDATA), `<artist>` (CDATA). El printing más reciente va
**último** (la app muestra esa imagen). No-ASCII se emite como entidades `&#NNNN;`.

> El launcher **no parsea ni genera** este XML — lo trata como un blob opaco que descarga
> y copia. La generación vive en el proyecto de la DB (ver §3). Esto se documenta solo
> para entender qué estamos moviendo.

### 2.3 Cómo filtra legalidades la app (lección aprendida, relevante para el formato)

El filtro de legalidad de Sun and Moon (verificado decompilando `Assembly-CSharp.dll`)
matchea el texto del dropdown contra sus formatos conocidos por **substring bidireccional
case-insensitive**, y para formatos *no* reconocidos cae a una comparación **exacta y
case-sensitive** (lowercaseando el input). Consecuencias que el launcher hereda como
contexto pero no maneja:

- Nuestro formato se llama **`s_extended`** (minúsculas). "samurai_extended" fallaba
  porque contiene "samurai" y la app lo ruteaba al filtro Samurai.
- Formatos reservados de la app (no usar como nombre custom, ni como substring):
  legacy, modern, twenty festivals, ivory, emperor, celestial, samurai, lotus, diamond,
  gold, jade, imperial, onyx, shattered empire, three dynasties.
- Dato útil: la app trae un formato built-in **"Modern"** = unión
  samurai+celestial+emperor+ivory+20F, que con la base actual filtra igual que
  `s_extended` (salvo banlist).

---

## 3. Ecosistema de datos existente (contexto, vive en OTRO repo)

La base de la comunidad se genera en el proyecto de la DB (separado del launcher):

- `cards-X.Y.Z.json` — DB de trabajo (codificada UTF-8 con BOM).
- `rules-X.Y.Z.json` — reglas/banlist; `rules[].banned == true` marca carta prohibida.
- `convert-kamisasori.js` — toma el `cards-*.json` y el `rules-*.json` de versión más
  alta y produce `database-s_extended-<ver>.xml` (a las baneadas no les pone el tag
  `s_extended`, así quedan fuera del formato pero visibles en la app).

**El artefacto que el launcher distribuye es ese `database-s_extended-<ver>.xml`.** El
launcher es el último eslabón: publica/sincroniza ese XML y la base original hacia los
usuarios. El proyecto del launcher no necesita Node ni la lógica de conversión.

---

## 4. Requisitos funcionales

### 4.1 v1 (MVP)

1. **Detección de instalación de Sun and Moon.** En el primer arranque, pedir al usuario
   la ruta de su instalación (carpeta que contiene `Sun and Moon.exe`). Validar que
   existan `Sun and Moon.exe` y `Sun and Moon_Data\StreamingAssets\Database\`. Guardar
   la ruta en la config del usuario.
2. **Chequeo de actualizaciones.** Al abrir, descargar el `manifest.json` del bucket y
   comparar versión/hash de cada base contra lo que hay en caché local. Mostrar si hay
   base nueva.
3. **Selección de base.** El usuario elige con qué base jugar:
   - **Comunidad** (`s_extended`): el XML generado por nosotros.
   - **Original**: la base sin nuestras modificaciones (ver §7 sobre de dónde sale).
   Recordar la última elección.
4. **Aplicar base.** Descargar (si falta o cambió) el XML elegido, **verificar su SHA-256**
   contra el manifest, respaldar el `database.xml` actual y luego copiarlo a
   `StreamingAssets\Database\database.xml`.
5. **Sincronizar imágenes propias.** Descargar (si falta o cambió) el pack de imágenes de
   las cartas que no están en los packs clásicos, verificar su SHA-256 y extraerlo sobre
   la carpeta de imágenes del usuario (ver §11 para la mecánica y la protección zip-slip).
6. **URL del manifest configurable.** El launcher trae una URL de manifest **por defecto
   hardcodeada** (nuestro bucket), pero el usuario puede cambiarla desde la config. Sirve
   para testing, migración de bucket o variantes de la comunidad. Implicancia de seguridad
   en §8.2.
7. **Lanzar.** Ejecutar `Sun and Moon.exe` (la instalación del usuario).
8. **Aviso de versión del launcher.** Si el manifest indica una versión de launcher más
   nueva que la instalada, mostrar un aviso con link a la página de descargas
   (**el link va hardcodeado en el exe**, no se toma del manifest — ver §9).

### 4.2 v2+ (futuro, no construir aún pero no cerrarse la puerta)

- **Noticias / info de torneos**: archivos `.md` en el bucket, renderizados **como
  markdown/texto plano dentro de la app** (sin navegador embebido — ver §8 regla 4).
- Posible histórico de versiones de la base, changelog, etc.

### 4.3 No-objetivos explícitos

- **No** auto-actualiza su propio ejecutable (decisión de seguridad, §8/§9).
- **No** redistribuye `Sun and Moon.exe` ni la instalación de la app.
- **No** genera ni edita el XML de cartas (eso es del proyecto de la DB).
- **No** traslada reglas de `maxCopies` de la banlist (el formato kamisasori no las
  soporta; son regla de torneo fuera de la app).

---

## 5. Infraestructura

- **Datos** (XML de bases, `manifest.json`, `.md` de noticias): **Cloudflare R2**, bucket
  público de lectura (tier gratuito). Servido por HTTPS.
- **Web** (página de descargas, hash del exe, eventualmente landing): **Cloudflare Pages**,
  deployada desde el repo de git.
- **Ejecutable del launcher**: **GitHub Releases** del repo del launcher (NO el bucket —
  ver §9 para el porqué).

Volumen: ~25 usuarios, archivos de 4–8 MB. Sobra el tier gratuito.

---

## 6. Diseño del `manifest.json`

Estructura propuesta (a ajustar al construir). Todos los archivos referenciados llevan
su **SHA-256**, que el launcher verifica tras descargar.

```jsonc
{
  "schema": 1,
  "launcher": {
    "latest_version": "1.2.0",       // se compara contra la versión embebida en el exe
    "notes": "Texto corto opcional"  // el LINK de descarga NO va acá; está hardcodeado
  },
  "databases": [
    {
      "id": "community",
      "label": "Base Comunidad (s_extended)",
      "version": "2.0.1",
      "file": "bases/database-s_extended-2.0.1.xml",
      "sha256": "…",
      "size": 4617566
    },
    {
      "id": "original",
      "label": "Base Original",
      "version": "…",
      "file": "bases/database-original.xml",
      "sha256": "…",
      "size": 8548980
    }
  ],
  "images": {                        // pack de imágenes propias (cartas fuera de los packs clásicos)
    "version": "2.0.1",
    "file": "images/l5a-images-2.0.1.zip",
    "sha256": "…",
    "size": 1234567
  },
  "news": [                          // v2; en v1 puede omitirse o ir vacío
    { "title": "…", "date": "2026-06-01", "file": "news/2026-06-01.md", "sha256": "…" }
  ]
}
```

Notas:
- Versionar el `schema` para poder evolucionar el manifest sin romper launchers viejos.
- Los `file` son **rutas relativas**; el launcher las resuelve contra el origen del
  manifest configurado (ver §8.2). Nunca seguir URLs absolutas arbitrarias del manifest.
- El pack de imágenes se versiona aparte para no re-descargarlo en cada arranque (puede
  pesar bastante): solo si cambió la `version`/`sha256` respecto del caché local.

---

## 7. La base "original": se respalda localmente (decisión tomada)

Para ofrecer "comunidad u original", el launcher necesita ambas. **La original se respalda
de la instalación del usuario** (no se hostea en el bucket):

- En el **primer arranque**, antes de pisar nada, copiar el `database.xml` existente a un
  backup propio (p. ej. `%LOCALAPPDATA%\L5Argentina Launcher\backups\database.original.xml`).
  Ese backup es la base "original" que se ofrece para volver.
- **Capturar la original ANTES de la primera aplicación de la base comunidad.** Si el
  usuario ya tenía una base modificada/comunidad cuando instala el launcher, ese backup no
  sería limpio: por eso el primer arranque debe detectar y advertir, y conviene que el
  backup se tome la primerísima vez, idealmente sobre una instalación recién bajada de
  Sun and Moon. Documentarlo para el usuario.
- **Antes de cada sobrescritura** (cualquier cambio de base), respaldar además el
  `database.xml` vigente con timestamp en la carpeta de backups, para poder revertir.

(El pack de imágenes propio sí va al bucket, §11 — no afecta a la base original, que solo
referencia imágenes de los packs clásicos.)

---

## 8. Seguridad — reglas de diseño (innegociables)

Estas reglas son lo que mantiene al launcher seguro para las 25 máquinas. El análisis de
amenazas está en §10.

1. **El launcher nunca descarga ni ejecuta código.** Solo datos: XML, MD, JSON. Sin
   plugins, sin scripts, sin auto-update del propio exe. El peor caso de un bucket
   comprometido debe ser "base o noticias falsas", nunca ejecución de código.
2. **Integridad por SHA-256.** El manifest trae el hash de cada archivo; el launcher
   verifica el hash **después de descargar y antes de usar/copiar**. Si no coincide,
   aborta y no toca la instalación.
3. **HTTPS siempre.** Nunca desactivar la validación de certificados "para probar".
   Forzar TLS 1.2+ explícitamente (en .NET Framework el default viejo puede no negociarlo).
4. **Noticias en markdown como texto, sin WebView.** Renderizar `.md` con un parser a
   componente nativo (FlowDocument). Prohibido navegador embebido / HTML arbitrario:
   sería convertir contenido del bucket en superficie de ejecución.
5. **Escrituras acotadas + extracción segura del zip de imágenes.** El launcher solo
   escribe en (a) la carpeta de Sun and Moon que el usuario configuró y (b) su propio
   caché/config en `%APPDATA%`/`%LOCALAPPDATA%`. El pack de imágenes se extrae en v1, así
   que la protección **zip-slip es obligatoria desde el día uno**: por cada entrada del
   zip, resolver la ruta destino final y verificar que quede **dentro** de
   `<SunAndMoon>\images\` antes de escribir; rechazar entradas con `..\`, rutas absolutas
   o que escapen del directorio. No confiar en el nombre de entrada tal cual.
6. **No redistribuir Sun and Moon.** Lanzar la instalación del usuario (ruta que él
   configura). Evita el problema de derechos y de empaquetar binarios de terceros.

### 8.1 Manejo defensivo del XML

El XML se trata como blob: el launcher no lo parsea para nada crítico. Si alguna vez
necesita leerlo, usar `XmlReader` con DTD/resolución de entidades externas deshabilitada
(es el default en .NET moderno, pero dejarlo explícito). Un XML malo, en el peor caso,
hace que Sun and Moon no levante esa base — no compromete al launcher.

### 8.2 URL del manifest configurable (implicancia)

Que el usuario pueda cambiar la URL del manifest amplía levemente el modelo de amenazas:
un atacante que convenza al usuario de apuntar a un manifest malicioso (ingeniería social)
podría servir bases/imágenes falsas con hashes "válidos" para *ese* manifest. Salvaguardas:

- **Default hardcodeado** a nuestro bucket; cambiarla es una acción explícita y visible.
- **Resolución de rutas confinada al origen del manifest**: los `file` del manifest se
  resuelven como relativos a la URL del manifest configurada; el launcher **no** sigue
  URLs absolutas a otros dominios que aparezcan en el manifest. Así, apuntar el manifest a
  otro origen cambia *todo* el origen de datos de forma coherente y visible, en vez de
  permitir un manifest que mezcle nuestro bucket con descargas de un tercero.
- El **link de actualización del propio exe sigue hardcodeado** (no se ve afectado por la
  URL del manifest): es el punto sensible (ejecución de código) y no debe ser configurable.
- HTTPS obligatorio también para la URL configurada (rechazar `http://`).

---

## 9. Distribución del ejecutable

- **Canal: GitHub Releases** del repo del launcher.
  - Reemplazar el exe requiere cuenta GitHub (con **2FA**) o un commit; queda **historial
    y notificación**. El bucket R2, en cambio, se sobrescribe con un API token de forma
    **silenciosa y sin rastro** → **el bucket NO aloja el exe**.
  - Versionado nativo (`v1.0.1`, `v1.0.2`, "latest" estable), ideal para la fase de
    iteración inicial.
  - Dominio con reputación (`github.com`), fácil de comunicar ("bajalo siempre de acá").
- **Segundo canal para el hash.** Publicar el **SHA-256 del exe en la web de Pages** (o en
  el Discord/WhatsApp de la comunidad). Así, falsificar el exe sin que se note exige
  comprometer **dos canales independientes** (GitHub *y* Cloudflare/git).
- **Actualización del launcher: notificar, no instalar.** El manifest informa
  `launcher.latest_version`; si es mayor que la versión embebida en el exe, mostrar aviso
  con link a la página de releases. **El link está hardcodeado en el exe**, nunca tomado
  del manifest (si no, un manifest comprometido se vuelve vector de phishing
  "actualizá acá" → exe malicioso). El usuario descarga e instala manualmente.
- **Firma de código.** Un certificado pago (~200–400 USD/año) es excesivo. Alternativas:
  - publicar el código del launcher **open source** en GitHub (auditable: cualquiera ve
    que solo descarga datos), y
  - usar **SignPath** (firma gratis para proyectos open source), que elimina el warning de
    SmartScreen sin pagar certificado.
  - Sin firma, el primer arranque mostrará el aviso "aplicación no reconocida" de
    SmartScreen/Defender; documentarlo para los usuarios.

---

## 10. Análisis de amenazas (resumen)

| Amenaza | Plausibilidad | Mitigación |
|---|---|---|
| Externo reemplaza archivos en el bucket/repo sin credenciales | Muy baja (requiere tus credenciales) | 2FA en GitHub y Cloudflare; token R2 con scope mínimo, solo en tu máquina; nada de secretos en el repo |
| Robo de credenciales (malware, phishing, token commiteado) | Riesgo dominante | 2FA; revisar que ningún token quede en git; rotar si hay sospecha |
| Bucket comprometido sirviendo datos falsos | Posible si caen credenciales | Daño acotado a "base/noticias falsas" por la regla "solo datos, nunca código"; hash en 2º canal sube la vara |
| MITM en la descarga | Muy baja | HTTPS de Cloudflare + verificación de hash |
| XML malicioso contra Sun and Moon | Baja | A lo sumo crashea la base; XmlReader con XXE/DTD off |
| Exe sin firmar normaliza "ejecutar de todos modos" | Media (hábito) | Open source + SignPath + hash publicado |
| zip-slip (si se distribuyen zips de imágenes) | Baja (feature futura) | Validar rutas al extraer |

**Conclusión:** bien diseñado, el launcher es *más* seguro que el statu quo (gente copiando
archivos a mano de cualquier lado). La amenaza real no es un externo tocando el bucket,
sino (a) tus credenciales y (b) cualquier feature futura donde el launcher ejecute lo que
descarga. Mantener "solo datos, nunca código" + 2FA deja el riesgo residual genuinamente bajo.

---

## 11. Imágenes de cartas propias (incluido en v1)

~30 printings nuestros (22 CE15MRP nuevos, EmperorMRP, SExMRP, AMoH, TNO, Ivory, TSE)
tienen imágenes que **no** existen en los packs clásicos que Sun and Moon ya trae. El
launcher las distribuye en v1.

**Origen y formato.** Un `.zip` en el bucket (junto al resto de los datos; git no es bueno
para blobs binarios grandes), referenciado desde el manifest con su `version` y `sha256`.
El zip contiene la estructura `cards\<set>\<carta>.jpg`, que es exactamente la subestructura
de la carpeta de imágenes de Sun and Moon (`<SunAndMoon>\images\cards\<set>\`).

**Mecánica:**
1. Si la `version`/`sha256` del manifest difiere del pack en caché, descargar el zip.
2. Verificar SHA-256 antes de tocar nada.
3. Extraer **sobre `<SunAndMoon>\images\`** (de modo que las entradas `cards\<set>\…` caigan
   en `images\cards\<set>\…`), con la **protección zip-slip obligatoria** de §8 regla 5:
   validar que cada ruta destino resuelta quede dentro de `images\`.
4. Las imágenes se fusionan con los packs clásicos existentes (no reemplazan la carpeta;
   solo agregan/actualizan las propias). Registrar la versión del pack en la config.

**Nota:** la base "original" solo referencia imágenes de los packs clásicos, así que el
pack propio es inocuo para ella (archivos extra que esa base no usa).

---

## 12. Stack técnico

- **Proyecto SDK-style** (csproj formato nuevo) **targeteando `net48`** (.NET Framework 4.8).
  - Razón: net48 viene **preinstalado en toda Windows 10/11** (incluida la LTSC de la
    comunidad) → cero runtime que instalar para los 25 usuarios. El csproj SDK-style se
    compila con la CLI `dotnet` desde VSCode (no requiere Visual Studio ni su diseñador).
  - csproj: `<TargetFramework>net48</TargetFramework>` + `<UseWPF>true</UseWPF>`.
- **UI: WPF.** XAML, nativo en net48. Para las noticias v2, renderizar markdown con
  **Markdig** → `FlowDocument` (nunca WebView).
- **Empaquetado a un solo .exe: Costura.Fody**, que embebe los DLLs de dependencias como
  recursos. Resultado ≈2–3 MB, doble-clic, sin carpeta de DLLs sueltos.
- **Piezas concretas:**

  | Necesidad | Pieza | Nota |
  |---|---|---|
  | Descargas HTTP | `HttpClient` | `ServicePointManager.SecurityProtocol = Tls12` al arranque; rechazar `http://` |
  | Parsear manifest | **`System.Text.Json`** (NuGet en net48) | Estándar actual de Microsoft; elegido sobre Newtonsoft |
  | Verificar integridad | `SHA256` (`System.Security.Cryptography`) | Comparar contra el hash del manifest |
  | Aplicar base | `System.IO.File.Copy` | Con backup previo del `database.xml` vigente |
  | Extraer pack de imágenes | `System.IO.Compression.ZipFile` | Extracción manual entrada-por-entrada con validación zip-slip (§8/§11) — no `ExtractToDirectory` a ciegas |
  | Lanzar el juego | `System.Diagnostics.Process.Start` | Ruta configurada por el usuario |
  | Markdown (v2) | **Markdig** → FlowDocument | NUNCA WebView2/CEF |

- **Toolchain en VSCode:** extensión **C# Dev Kit**; ciclo `dotnet run` para iterar viendo
  la ventana real; `dotnet publish -c Release` (con Costura) para el release.
- **Alternativa descartada:** Avalonia + .NET 8 (solo si algún día se quisiera
  multiplataforma; no aplica porque Sun and Moon es Windows-only). Electron/WebView:
  descartado por peso y por la superficie de ataque del navegador embebido.

---

## 13. Estado / config local del usuario

Guardar en `%APPDATA%\L5Argentina Launcher\config.json`:

- ruta de instalación de Sun and Moon,
- **URL del manifest** (default hardcodeado; editable por el usuario, §8.2),
- base seleccionada por última vez (`community` / `original`),
- versiones/hashes de las bases en caché (para saber si hay que re-descargar),
- versión del pack de imágenes aplicado (para no re-extraer si no cambió),
- versión del launcher (también embebida en el exe para el chequeo de update).

Caché de descargas: `%LOCALAPPDATA%\L5Argentina Launcher\cache\`. Backups: la original en
`…\backups\database.original.xml` (primer arranque) y copias con timestamp del
`database.xml` previo antes de cada cambio.

---

## 14. Estructura sugerida del proyecto

```
L5ArgentinaLauncher/
├─ src/
│  ├─ L5ArgentinaLauncher.csproj  // SDK-style, net48, UseWPF, Costura
│  ├─ App.xaml(.cs)
│  ├─ MainWindow.xaml(.cs)        // selección de base, estado de update, botón Jugar
│  ├─ Services/
│  │  ├─ ManifestService.cs       // descarga + parseo del manifest (URL configurable)
│  │  ├─ DownloadService.cs        // descarga + verificación SHA-256
│  │  ├─ DatabaseService.cs        // backup (incl. original 1er arranque) + copia a StreamingAssets
│  │  ├─ ImagePackService.cs       // descarga + extracción zip con validación zip-slip
│  │  ├─ GameLauncher.cs           // Process.Start de Sun and Moon
│  │  └─ ConfigService.cs          // %APPDATA% config
│  └─ Models/                      // Manifest, DatabaseEntry, ImagePack, Config…
├─ docs/
│  └─ (esta spec, README, instrucciones para usuarios sobre SmartScreen)
├─ .github/workflows/             // build + publicación a Releases; integración SignPath
└─ README.md
```

---

## 15. Decisiones tomadas

1. **Nombre:** L5Argentina Launcher (carpeta `%APPDATA%\L5Argentina Launcher`, título,
   assembly `L5ArgentinaLauncher`).
2. **Base original:** se respalda localmente de la instalación del usuario, no se hostea
   (§7). Backup en el primer arranque + copia con timestamp antes de cada cambio.
3. **Imágenes propias:** incluidas en v1, vía zip en el bucket con extracción protegida
   contra zip-slip (§11).
4. **JSON:** `System.Text.Json` (estándar actual de Microsoft).
5. **Distribución/firma:** open source en GitHub + SignPath desde el día uno; exe por
   GitHub Releases; hash en segundo canal (§9).
6. **URL del manifest:** configurable por el usuario, con default hardcodeado y rutas
   confinadas al origen del manifest (§4.1.6, §8.2).

### Se define al armar el proyecto (no bloquea el diseño)

- URLs definitivas: prefijo del bucket R2 (origen del manifest por defecto) y URL de la
  página de Releases (link de update hardcodeado en el exe).
- Detalle del primer arranque para capturar una base original limpia (advertencia si el
  `database.xml` actual ya parece modificado).
