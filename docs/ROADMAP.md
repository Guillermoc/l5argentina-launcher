# ROADMAP — L5Argentina Launcher

Qué falta. **Se lee al empezar cualquier sesión**, junto con [index/OVERVIEW.md](index/OVERVIEW.md).

Leyenda: ⏳ en curso/esperando · 🔜 próximo · ⬜ pendiente · ⏸️ diferido · 🔁 recurrente · ✅ hecho.

**Estado hoy:** v0.3.0 publicada (launcher + Buscador + Mazos), 177 checks en verde.
**Foco actual:** SEC-6 (firma del exe con SignPath, esperando aprobación OSS).

---

## 1. Seguridad

> **Decisión de priorización (modelo de amenaza del mantenedor, [ADR-015](DECISIONS.md#adr-015)):**
> lo crítico es que el launcher **no comprometa la PC** del usuario — nada de ejecución de código,
> troyanos ni borrado de archivos — y eso **ya está cubierto** (no descarga/ejecuta código, lanza el
> `.exe` del usuario, zip-slip, escrituras acotadas con backups). El hardening que solo protege la
> **integridad de los datos** ante un bucket comprometido (SEC-1/3/5) se **difiere**: el peor caso
> ahí es "datos falsos" (cartas/imágenes/que se rompa), no compromiso de la máquina.

### SEC-6 — Firmar el binario (SignPath) · Baja · ⏳ **foco actual**

Exe sin firmar → SmartScreen "aplicación no reconocida" (reconocido en README/SECURITY/USUARIOS).
El pipeline ya está cableado condicional en `.github/workflows/release.yml`.
**Falta:** cuenta SignPath Foundation aprobada (OSS, revisión manual), secrets
`SIGNPATH_API_TOKEN` / `SIGNPATH_ORGANIZATION_ID`, y ajustar los 3 slugs `TODO(SignPath)` del workflow.

### SEC-1 — Firmar el `manifest.json` · Alta (técnica) · ⏸️ diferido

**Problema.** El `sha256` de cada archivo vive en el mismo manifest que los referencia y el manifest
**no está firmado**. Si se compromete el bucket (token R2 robado) o se convence al usuario de cambiar
la URL, el atacante cambia archivo **y** hash a la vez y el launcher lo acepta. El hash solo protege
integridad en tránsito y compromisos parciales, no un origen comprometido.
Refs: `src/Services/ManifestService.cs`, `src/MainWindow.xaml.cs`.

**Fix propuesto.**
- Firma **RSA** (nativa en net48; Ed25519 exigiría BouncyCastle, +2-3 MB).
- Clave **privada offline** (no en el repo ni en el pipeline de R2); clave **pública embebida** en el exe.
- Publicar `manifest.json` + `manifest.json.sig` (firma *detached*, base64) en el bucket.
- En el launcher: bajar manifest + `.sig` (mismo confinamiento de origen) y **verificar la firma
  ANTES** de deserializar o usar cualquier `sha256`. Si no valida → abortar, igual que un hash malo.
- Entregables: verificación en `ManifestService`, clave pública en `AppConstants`, **script de
  firmado** para el mantenedor, actualizar README/SECURITY y el bucket.
- Cierra de paso **SEC-5**: solo se aceptarían manifests firmados por nuestra clave.

**Por qué está diferido:** el peor caso hoy no es ejecución de código sino datos falsos escritos en
Sun and Moon. Revisar si el bucket se vuelve crítico o si lo opera más gente.

### SEC-3 — Límites al descomprimir ZIP (anti zip-bomb) · Media · ⏸️ diferido

No hay topes de tamaño/entradas/ratio al extraer. El hash no protege (un zip-bomb tiene hash válido).
**Fix:** topes **duros en código** (no confiar en el `size` del manifest): tamaño total
descomprimido, cantidad de entradas, tamaño por entrada y ratio de compresión; abortar si se exceden.
Refs: `src/Services/ImagePackService.cs`, `src/Services/ZipUtil.cs`. *Quick win* si alguna vez molesta.
**Por qué está diferido:** solo previene DoS (llenar disco / colgar la app), no compromiso.

### SEC-5 — URL de manifest configurable a cualquier HTTPS · Media/Baja · ⏸️ diferido

Alcanza con convencer al usuario de pegar otra URL "de soporte" para tomar control de los datos.
**Fix:** lo resuelve SEC-1. Plan B si no se hace firma: warning fuerte / "modo avanzado" al cambiarla.
Ref: `src/MainWindow.xaml.cs` (Configuración).

### Hechos

- ✅ **SEC-2** — TLS solo 1.2 (`src/App.xaml.cs`), v0.1.1.
- ✅ **SEC-4** — la base `.zip` exige exactamente un `.xml` (`src/Services/ZipUtil.cs`), v0.1.1.

---

## 2. Operación / GitHub (toggles de la web — los hace el mantenedor)

- ✅ **2FA** en GitHub **y** Cloudflare (riesgo dominante del threat model). Token R2 con scope mínimo.
- ✅ Secret scanning + push protection · ✅ Private vulnerability reporting · ✅ Dependabot ·
  ✅ Branch protection en `main`.
- 🔁 **SHA-256 del exe en segundo canal** (web/Discord) — paso recurrente en **cada** release.

---

## 3. Producto (v2 y más adelante)

- ⬜ **Noticias y torneos.** Definir el formato de los `.md` y la sección `news` del manifest; render
  markdown → `FlowDocument` con Markdig (**nunca** WebView). Placeholder ya presente en la UI y
  `NewsService`/`NewsEntry` como stubs. Borrador de spec:
  [functional/noticias-v2-borrador.md](functional/noticias-v2-borrador.md).
- ⬜ **Mantenimiento del manifest.** Script para regenerar `manifest.json` (hash + size, y firma
  cuando exista SEC-1) al publicar una base o imágenes nuevas.
- ⬜ Histórico de versiones / changelog de la base (nice-to-have).

---

## 4. Pendientes por dominio

**Buscador** ([index/buscador.md](index/buscador.md))
- ⬜ Contador de cartas por lista en el selector.
- ⬜ Renombrar listas.

**Mazos** ([index/mazos.md](index/mazos.md))
- ⏸️ Goldfish (sandbox de testeo) — fuera de alcance; la spec del contrato lo cubre si se quiere.
- ⏸️ Grupos de usuario (carpetas de mazos) y subgrupos dentro del mazo — no portados.
- ⏸️ Export PDF — no portado.
- ⬜ Buscador embebido en el editor (hoy el flujo es "Buscador `+`/`−` sobre el mazo activo").

**URLs / operación**
- ⬜ Confirmar que `DefaultManifestUrl` y `ReleasesPageUrl` de `AppConstants` son las definitivas
  (los comentarios todavía las describen como placeholders `TODO(URL)` heredados de la spec v1.0).

---

## 5. Releases publicadas

| Versión | Fecha | Contenido |
|---|---|---|
| v0.1.0 | 2026-06-14 | v1 funcional (detección, manifest, descarga verificada, backups, aplicar base, lanzar, aviso de update) + rediseño visual + i18n + self-test + repo público/MIT/SECURITY/dependabot. |
| v0.1.1 | 2026-06-14 | SEC-2 (TLS 1.2), SEC-4 (un solo `.xml`), ajustes de textos. |
| v0.2.0 | 2026-07-07 | Buscador de cartas (MVP → V3), packs de imágenes y catálogo desde el pool. |
| v0.3.0 | 2026-07-11 | Mazos (deckbuilder) + V4 del Buscador + fixes de rendimiento. |
