# Diseño transversal — seguridad

Reglas que atraviesan red, descarga, extracción y escritura en disco. **Innegociables**: si un cambio
las incumple, el cambio está mal, no la regla. El threat model completo está en la spec funcional
v1.0 §8–§10 ([../functional/launcher-v1.0-spec.md](../functional/launcher-v1.0-spec.md)); lo
diferido a propósito, en [../ROADMAP.md](../ROADMAP.md) §1.

**Principio rector ([ADR-001](../DECISIONS.md#adr-001)):** el launcher solo descarga **datos**
(XML / JSON / ZIP), nunca código. Sin auto-update del exe, sin plugins, sin navegador embebido. El
peor caso de un bucket comprometido es "datos falsos", jamás ejecución de código.

---

## Las seis reglas

| # | Regla | Dónde vive |
|---|---|---|
| 1 | **Solo datos, nunca código.** Sin plugins, sin scripts, sin WebView. | Todo el proyecto |
| 2 | **SHA-256 de todo lo descargado**, verificado antes de usarlo. | `DownloadService` |
| 3 | **HTTPS siempre** (TLS 1.2+). `file://`/localhost solo bajo `#if DEBUG`. | `App.OnStartup`, `ManifestService.EnsureAllowedScheme` |
| 4 | **Confinamiento al origen del manifest.** Nunca seguir URLs de terceros ni escapar de su directorio. | `ManifestService.ResolveFileUri` / `ResolveSameOriginUrl` |
| 5 | **Protección zip-slip** obligatoria al extraer. | `ImagePackService` |
| 6 | **Link de update hardcodeado** en el exe, nunca del manifest. | `AppConstants.ReleasesPageUrl` |

---

## Detalle

**Integridad por SHA-256.** Cada archivo (la base `.zip`/`.xml`, el pack de imágenes, los artefactos
del catálogo) se descarga a un `.part`, se hashea al vuelo y **solo se promueve al destino final si
el hash coincide** con el del manifest. Si no coincide: excepción y no queda archivo residual.
([ADR-006](../DECISIONS.md#adr-006))

**HTTPS + TLS 1.2.** Se fija `SecurityProtocolType.Tls12` al arranque (ni 1.1 ni 1.0) y se rechaza
`http://`. El escape para `file://` y `http://localhost` existe **solo en build DEBUG**, para testear
contra un bucket local; en Release es estricto. Nunca desactivar la validación de certificados
"para probar". ([ADR-014](../DECISIONS.md#adr-014))

**Confinamiento al origen.** Los `file` del manifest del launcher se resuelven como **relativos** a
la URL del manifest, validando mismo esquema+host+puerto y que queden **dentro de su directorio**.
Las URLs absolutas del manifest del pool se aceptan solo si son del **mismo origen**. Se rechazan
dominios ajenos y los escapes con `../`. Apuntar el manifest a otro origen cambia *todo* el origen de
datos de forma coherente y visible; no permite mezclar.
([ADR-007](../DECISIONS.md#adr-007))

**Protección zip-slip.** Al extraer un pack, por cada entrada se resuelve la ruta destino final y se
verifica que caiga **dentro** de `<SunAndMoon>\images\` antes de escribir; se rechazan `..\`, rutas
absolutas y cualquier escape. La comparación usa la raíz canónica con separador final, para que
`images-evil` no pase por `images`.

**Link de actualización hardcodeado.** El aviso "hay versión nueva" lleva a una URL embebida en el
exe. Si viniera del manifest, un manifest comprometido se volvería vector de phishing hacia un exe
malicioso — el único punto donde el daño sería ejecución de código.
([ADR-009](../DECISIONS.md#adr-009))

**Escrituras acotadas + backups reversibles.** El launcher solo escribe en (a) la carpeta de Sun and
Moon que el usuario configuró y (b) su propio `%APPDATA%`/`%LOCALAPPDATA%`. Antes de pisar el
`database.xml` respalda la Original (primer arranque, y nunca se sobrescribe) y hace una copia con
timestamp en cada cambio → siempre se puede revertir.

**XML tratado como blob opaco.** El launcher no parsea la base para nada crítico: solo la copia y la
verifica. La heurística de "base ya modificada" del primer arranque es un stream-scan de texto, no un
parser. Si alguna vez hiciera falta leerlo, `XmlReader` con DTD/entidades externas deshabilitadas.

**Escrituras atómicas.** Config con `.tmp` + replace, descargas con `.part` + replace: nunca queda un
estado a medias.

**Canal de distribución endurecido.** El exe va por GitHub Releases (requiere 2FA, deja historial y
notificación), no por el bucket R2 (que se sobrescribe con un token, sin rastro). Además se publica
el SHA-256 del exe en un segundo canal: falsificarlo exige comprometer dos canales independientes.

---

## Verificación

El self-test cubre exactamente lo crítico y corre en CI: hash correcto / hash inválido que aborta /
sin archivo residual, rechazo de origen ajeno y de `../`, zip-slip rechazado, base `.zip` con un solo
`.xml`, backups (Original + timestamped) y validación same-origin del pool.
Ver [../index/build-verificacion.md](../index/build-verificacion.md).

## Lo que se sabe que falta

El manifest **no está firmado** (SEC-1), no hay topes anti zip-bomb (SEC-3) y la URL del manifest
acepta cualquier HTTPS (SEC-5). Los tres están diferidos con criterio explícito en
[ADR-015](../DECISIONS.md#adr-015) — no son olvidos.
