# Guía rápida para usuarios — L5Argentina Launcher

Esta guía es para los jugadores de la comunidad. Es cortita.

## 1. Instalar

1. Descargá `L5ArgentinaLauncher.exe` desde la página oficial de descargas (GitHub Releases).
   **Bajalo siempre de ahí**, no de links sueltos.
2. (Recomendado) Verificá el **SHA-256** del archivo contra el que publicamos en el sitio/Discord.
   En PowerShell:
   ```powershell
   Get-FileHash .\L5ArgentinaLauncher.exe -Algorithm SHA256
   ```
3. Guardá el `.exe` donde quieras (Escritorio, una carpeta, lo que sea). Es un único archivo.

## 2. "Windows protegió tu PC" (SmartScreen)

La primera vez, como el `.exe` todavía no tiene reputación, Windows puede mostrar una pantalla
azul **"Windows protegió tu PC"**. Es esperable y no significa que haya un virus.

- Hacé clic en **"Más información"**.
- Después en **"Ejecutar de todas formas"**.

(Cuando firmemos el ejecutable con un certificado, este aviso desaparece.)

## 3. Primer arranque

El launcher te va a pedir **dónde está instalado Sun and Moon**:

- Hacé clic en **"Elegir Sun and Moon.exe…"** y seleccioná el archivo `Sun and Moon.exe`
  dentro de la carpeta donde instalaste el juego.
- El launcher guarda una copia de tu base actual como tu base **Original**, por si algún día
  querés volver a jugar sin nuestras modificaciones.

> **Importante:** lo ideal es hacer este primer arranque sobre una instalación **recién bajada**
> de Sun and Moon (base original limpia). Si tu `database.xml` ya estaba modificado, el launcher
> te avisa: esa copia "Original" no va a ser una base 100% limpia.

## 4. Uso normal

1. Abrí el launcher. Chequea solo si hay novedades.
2. Elegí la base: **Comunidad** (la nuestra, `s_extended`) u **Original**.
3. Apretá **JUGAR**. El launcher baja/actualiza lo que haga falta (verificando integridad),
   reemplaza la base y abre Sun and Moon.

Si aparece un aviso de que **hay una versión nueva del launcher**, entrá al link de descargas y
bajá la nueva. El launcher **no se actualiza solo** (es una decisión de seguridad).

## 5. ¿Algo salió mal?

- El launcher **siempre respalda** tu `database.xml` antes de pisarlo (en
  `%LOCALAPPDATA%\L5Argentina Launcher\backups\`), así que siempre se puede revertir.
- Si dice "sin conexión", podés jugar igual con lo que ya tenés aplicado o con la base Original.
- Si el juego no encuentra una base, podés volver a la **Original** desde el launcher.
