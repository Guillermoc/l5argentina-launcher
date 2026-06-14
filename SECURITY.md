# Política de seguridad

L5Argentina Launcher descarga y aplica archivos en las máquinas de la comunidad, así que la
seguridad es central. Gracias por reportar problemas de forma responsable.

## Reportar una vulnerabilidad

**No abras un issue público para vulnerabilidades.** Usá un canal privado:

- **GitHub → pestaña "Security" → "Report a vulnerability"** (Private Vulnerability Reporting).
  Si no aparece el botón, el mantenedor debe activarlo en
  *Settings → Code security and analysis → Private vulnerability reporting*.
- Alternativa: contactar a un mantenedor por el Discord/WhatsApp de la comunidad L5Argentina.

Incluí: versión del launcher, pasos para reproducir, impacto y, si aplica, un PoC. Respondemos lo
antes posible y coordinamos la divulgación una vez que haya un arreglo.

## Versiones soportadas

Se da soporte a la **última release** publicada en
[Releases](https://github.com/Guillermoc/l5argentina-launcher/releases). Las versiones anteriores no
reciben parches.

## Modelo de seguridad (qué garantiza el launcher)

- **Solo descarga datos (XML/JSON/ZIP), nunca código.** Sin auto-update del exe, sin plugins, sin WebView.
- **Integridad por SHA-256:** cada archivo se verifica contra el manifest antes de usarlo; si no
  coincide, aborta sin tocar la instalación.
- **HTTPS + TLS 1.2+** obligatorio; las descargas se **confinan al origen del manifest** (no se siguen
  URLs absolutas a otros dominios ni rutas que escapen de su directorio).
- **Extracción zip-slip-safe** del pack de imágenes.
- **Link de actualización hardcodeado** en el exe (nunca se toma del manifest → no es vector de phishing).
- **Backups** antes de cada sobrescritura (la base Original + copias con timestamp → reversible).

Detalle completo del threat model en [docs/launcher-spec.md](docs/launcher-spec.md) (§8–§10) y un
resumen en el [README](README.md).

## Verificá lo que ejecutás

- Bajá el `.exe` **solo** desde [Releases](https://github.com/Guillermoc/l5argentina-launcher/releases)
  de este repo.
- Verificá su **SHA-256** contra el `.sha256.txt` publicado (y el segundo canal de la comunidad):

  ```powershell
  Get-FileHash .\L5ArgentinaLauncher.exe -Algorithm SHA256
  ```

- Hasta que el binario esté **firmado** (SignPath), Windows SmartScreen mostrará "aplicación no
  reconocida"; es esperable. Ver [docs/USUARIOS.md](docs/USUARIOS.md).
