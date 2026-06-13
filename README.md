# L5Argentina Launcher

Launcher de Windows para la comunidad **L5Argentina** que juega *Legend of the Five Rings* en
la app **Sun and Moon**. Al abrirse, chequea si hay una base de cartas nueva en nuestro bucket,
deja elegir **con qué base jugar** (Comunidad `s_extended` u Original), sincroniza las imágenes
de cartas propias y lanza Sun and Moon. En el futuro mostrará noticias e info de torneos.

> Diseño completo en [`docs/launcher-spec.md`](docs/launcher-spec.md). Guía visual en
> [`docs/Style_guide.md`](docs/Style_guide.md). Plan de implementación en [`docs/PLAN.md`](docs/PLAN.md).

## Principio de seguridad

**Solo descarga datos (XML, JSON, MD, ZIP de imágenes), nunca código.** Verifica el **SHA-256**
de cada archivo contra el manifest antes de usarlo, fuerza **HTTPS**, confina las descargas al
origen del manifest, y extrae el ZIP de imágenes con **protección zip-slip**. No se auto-actualiza
el propio ejecutable (el link de descarga está hardcodeado, nunca se toma del manifest). Ver §8 de
la spec.

## Estado

v1 (MVP) implementada y verificada: detección de instalación, primer arranque con captura de la
base Original, chequeo de manifest, selección y aplicación de base con backups, sync de imágenes
con verificación de hash y zip-slip, lanzamiento, y aviso de update del launcher. Las noticias (v2)
están como placeholder.

## URLs de producción

Configuradas en [`src/AppConstants.cs`](src/AppConstants.cs):

| Constante | Valor |
|---|---|
| `DefaultManifestUrl` | `https://pub-4ab8e43f10604d7fa0f9402a8259a855.r2.dev/sunandmoon/manifest.json` |
| `ReleasesPageUrl` | `https://github.com/Guillermoc/l5argentina-launcher/releases` (link del aviso de update, **hardcodeado a propósito**). |

## Requisitos

- **Para usar el launcher:** Windows 10/11 con .NET Framework 4.8 (viene preinstalado). Nada más.
- **Para compilar:** .NET SDK 8.x (`dotnet`). No requiere Visual Studio.

## Compilar y correr

```sh
# Desde la raíz del repo
dotnet build src/L5ArgentinaLauncher.csproj -c Debug     # compilar
dotnet run  --project src/L5ArgentinaLauncher.csproj     # correr la ventana
```

### Release (un solo .exe)

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Release
# Resultado: src/bin/Release/net48/L5ArgentinaLauncher.exe  (~7 MB, autocontenido vía Costura)
```

Para distribuir alcanza con **ese único `.exe`** (no necesita los `.dll`, `.pdb` ni `.exe.config`
que quedan al lado). Verificado: corre standalone.

### Self-test (solo Debug)

Un harness de verificación monta un bucket+instalación falsos en `%TEMP%` y ejercita el pipeline
real (hash, confinamiento de origen, zip-slip, backups) sin tocar tu instalación real:

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Debug
src/bin/Debug/net48/L5ArgentinaLauncher.exe --selftest C:\ruta\reporte.txt
```

## Distribución y firma

- El `.exe` se publica en **GitHub Releases** (no en el bucket — ver spec §9).
- El **SHA-256 del exe** se publica en un segundo canal (web/Discord).
- Firma de código con **SignPath** (gratis para open source) para evitar el warning de SmartScreen.
  Workflow en [`.github/workflows/release.yml`](.github/workflows/release.yml) (integración de
  SignPath dejada como TODO hasta configurar la cuenta).
- Sin firma, el primer arranque muestra el aviso "aplicación no reconocida" de SmartScreen. Ver
  instrucciones para usuarios en [`docs/USUARIOS.md`](docs/USUARIOS.md).

## Arquitectura

```
src/
├─ App.xaml(.cs)              arranque, TLS 1.2+, ruteo de primer arranque, --selftest
├─ MainWindow.xaml(.cs)       selección de base, banner de update, sync, botón Jugar
├─ AppConstants.cs            URLs (TODO), versión, rutas de %APPDATA%/%LOCALAPPDATA%
├─ Themes/Theme.xaml          paleta, fuentes y estilos (guía de estilos L5Argentina)
├─ Models/                    Manifest, UserConfig
├─ Views/                     FirstRunWindow, SettingsWindow
└─ Services/
   ├─ ManifestService         fetch + parseo + ResolveFileUri (confinado al origen, HTTPS-only)
   ├─ DownloadService         descarga + verificación SHA-256
   ├─ DatabaseService         backup original (1er arranque) + backup timestamped + aplicar base
   ├─ ImagePackService        extracción del zip con protección zip-slip
   ├─ GameLauncher            Process.Start de Sun and Moon
   ├─ ConfigService           %APPDATA% config
   ├─ LauncherEngine          coordinador del flujo
   └─ NewsService             placeholder (v2)
```
