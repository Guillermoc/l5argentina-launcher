# L5Argentina Launcher

Launcher de escritorio (Windows) para la comunidad **L5Argentina**, que juega *Legend of the Five
Rings* en la app **Sun and Moon**. Resuelve el problema de mantener sincronizada, entre ~25 personas,
nuestra base de cartas curada: en lugar de copiar archivos a mano, el launcher chequea si hay una base
nueva en nuestro bucket, deja **elegir con qué base jugar**, la aplica de forma segura y lanza el juego.

Versión actual: **0.1.0**.

> Diseño/decisiones: [docs/launcher-spec.md](docs/launcher-spec.md) · Guía visual: [docs/Style_guide.md](docs/Style_guide.md) · Plan: [docs/PLAN.md](docs/PLAN.md) · Guía de usuarios: [docs/USUARIOS.md](docs/USUARIOS.md)

---

## Qué hace

- **Detecta la instalación de Sun and Moon.** En el primer arranque pide el `Sun and Moon.exe` y valida
  que la instalación sea correcta. Guarda la ruta en la config del usuario.
- **Respalda tu base "Original".** En ese primer arranque copia tu `database.xml` actual a un backup
  propio, que después se ofrece como la base **Original** (sin nuestras modificaciones). Si detecta que
  la base actual ya parece modificada, avisa.
- **Chequea actualizaciones.** Al abrir, descarga el `manifest.json` del bucket y compara versiones/hashes
  contra lo que hay en caché local.
- **Deja elegir la base:**
  - **Comunidad** (`s_extended`): la base curada por nosotros. Se descarga del bucket (viene comprimida
    en `.zip`; el launcher la verifica, extrae el `database.xml` y lo aplica).
  - **Original**: el backup local capturado en el primer arranque.
  - Recuerda la última elección.
- **Aplica la base de forma segura.** Antes de pisar el `database.xml` vigente, hace un backup con
  timestamp; verifica el **SHA-256** de lo descargado contra el manifest antes de tocar nada.
- **Sincroniza las imágenes propias.** Descarga (solo si cambió) el pack de imágenes de las cartas que no
  están en los packs clásicos, verifica su SHA-256 y lo extrae sobre la carpeta de imágenes con
  **protección zip-slip**.
- **Lanza Sun and Moon** (la instalación del usuario).
- **Avisa si hay un launcher nuevo.** Si el manifest indica una versión más nueva, muestra un banner con
  link a la página de descargas (el link va **hardcodeado en el exe**, nunca se toma del manifest).
- **Configuración:** permite cambiar la carpeta de Sun and Moon y la URL del manifest (con default
  hardcodeado y restaurable). Modo **offline** elegante si el bucket no está disponible.
- **Noticias y torneos:** sección presente como *placeholder* (funcionalidad v2).

---

## Seguridad (resumen)

El principio rector: **el launcher solo descarga datos (XML, JSON, ZIP), nunca código.** Detalle:

- **Integridad por SHA-256**: se verifica el hash de cada archivo descargado *antes* de usarlo; si no
  coincide, aborta sin tocar la instalación.
- **HTTPS obligatorio** y **TLS 1.2+** forzado.
- **Confinamiento al origen del manifest**: los archivos se resuelven como relativos al manifest; nunca
  se siguen URLs absolutas a otros dominios ni rutas que escapen de su directorio.
- **Extracción zip-slip-safe** del pack de imágenes.
- **Sin auto-update del propio exe**: el link de descarga está hardcodeado, no viene del manifest.
- **Backups** antes de cada sobrescritura (la Original y copias con timestamp).

El threat model completo está en la spec (§8–§10).

---

## UI

WPF con estética **Rokugán / pergamino** (rojo lacado para acción, oro decorativo, tipografía de marca
L5RTitles). Ventana **fija 810×540 (3:2)**, no redimensionable, con barra de título propia (el diseño se compone a
1080×720 y se escala con un `Viewbox`).
Diálogos de error/confirmación y la pantalla de Configuración son *overlays* dentro de la ventana
(no `MessageBox` del sistema).

**Textos (i18n):** todos los textos viven en [src/Resources/Strings.resx](src/Resources/Strings.resx)
(español, cultura neutral). En XAML se usan con `{loc:Loc Clave}` y en C# con `Strings.Get/Format`.
Para agregar otro idioma: crear `Strings.<cultura>.resx` con las mismas claves y setear
`CurrentUICulture` — sin tocar código.

---

## Requisitos

- **Para usar el launcher:** Windows 10/11 con .NET Framework 4.8 (viene preinstalado). Nada más.
- **Para compilar:** .NET SDK 8.x (`dotnet`). No requiere Visual Studio.

## Compilar y correr

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Debug     # compilar
dotnet run  --project src/L5ArgentinaLauncher.csproj     # correr la ventana
```

### Release (un solo .exe)

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Release
# Resultado: src/bin/Release/net48/L5ArgentinaLauncher.exe  (~7 MB, autocontenido vía Costura)
```

Para distribuir alcanza con **ese único `.exe`** (no necesita los `.dll`, `.pdb` ni `.exe.config` que
quedan al lado).

### Self-test (solo Debug)

Un harness de verificación monta un bucket+instalación falsos en `%TEMP%` y ejercita el pipeline real
(hash, confinamiento de origen, zip-slip, backups, base zippeada) sin tocar tu instalación:

```sh
src/bin/Debug/net48/L5ArgentinaLauncher.exe --selftest C:\ruta\reporte.txt
```

> Hook de testing: la variable de entorno `L5A_DATA_ROOT` redirige config/caché/backups a otra carpeta.

---

## Configuración del usuario (rutas)

- Config: `%APPDATA%\L5Argentina Launcher\config.json`
- Caché de descargas: `%LOCALAPPDATA%\L5Argentina Launcher\cache\`
- Backups: `%LOCALAPPDATA%\L5Argentina Launcher\backups\` (la Original + copias con timestamp)

## URLs de producción

En [src/AppConstants.cs](src/AppConstants.cs):

| Constante | Valor |
|---|---|
| `DefaultManifestUrl` | `https://pub-4ab8e43f10604d7fa0f9402a8259a855.r2.dev/sunandmoon/manifest.json` |
| `ReleasesPageUrl` | `https://github.com/Guillermoc/l5argentina-launcher/releases` (link del aviso de update, **hardcodeado a propósito**) |

El formato del `manifest.json` y un ejemplo con hashes reales están en
[docs/manifest.example.json](docs/manifest.example.json).

---

## Distribución y firma

- El `.exe` se publica en **GitHub Releases** (no en el bucket — spec §9), vía
  [.github/workflows/release.yml](.github/workflows/release.yml) al pushear un tag `vX.Y.Z`.
- El **SHA-256 del exe** se publica como segundo canal (web/Discord).
- **Firma de código (SignPath):** pendiente. Sin firma, el primer arranque muestra el aviso de
  **SmartScreen** ("aplicación no reconocida"); instrucciones para usuarios en
  [docs/USUARIOS.md](docs/USUARIOS.md).

---

## Arquitectura

```
src/
├─ App.xaml(.cs)              arranque, TLS 1.2+, ruteo de primer arranque, --selftest
├─ MainWindow.xaml(.cs)       selección de base, banner de update, sync, JUGAR, overlays
├─ AppConstants.cs            URLs, versión, rutas de %APPDATA%/%LOCALAPPDATA%
├─ Themes/Theme.xaml          paleta, fuentes, estilos y fondo (guía de estilos L5Argentina)
├─ Resources/
│  ├─ Strings.resx            TODOS los textos de UI (i18n, español)
│  ├─ Strings.cs              accesor (ResourceManager)
│  └─ LocExtension.cs         markup extension {loc:Loc}
├─ Models/                    Manifest, UserConfig
├─ Views/FirstRunWindow       primer arranque (detección + captura de la Original)
└─ Services/
   ├─ ManifestService         fetch + parseo + ResolveFileUri (confinado al origen, HTTPS-only)
   ├─ DownloadService         descarga + verificación SHA-256
   ├─ DatabaseService         backup original + backup timestamped + aplicar base
   ├─ ImagePackService        extracción del zip con protección zip-slip
   ├─ ZipUtil                 extracción del database.xml desde la base .zip
   ├─ GameLauncher            Process.Start de Sun and Moon
   ├─ ConfigService           %APPDATA% config
   ├─ LauncherEngine          coordinador del flujo
   └─ NewsService             placeholder (v2)
```

Stack: **WPF sobre .NET Framework 4.8**, csproj SDK-style compilado con el **.NET SDK 8**, empaquetado en
un único `.exe` con **Costura.Fody**. JSON con `System.Text.Json`.
