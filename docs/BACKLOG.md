# Backlog — L5Argentina Launcher

Estado al día. Leyenda: ✅ hecho · ⏳ en curso/esperando · 🔜 próximo · ⬜ pendiente · ⏸️ diferido.

Severidades del análisis de seguridad: **Alta / Media / Baja**.

> **Decisión de priorización (modelo de amenaza del mantenedor):** lo crítico es que el launcher **no
> comprometa la PC** del usuario — nada de ejecución de código, troyanos ni borrado de archivos — y eso
> **ya está cubierto** (no descarga/ejecuta código, lanza el `.exe` del usuario, zip-slip, escrituras
> acotadas con backups). El hardening que solo protege la **integridad de los datos** ante un bucket
> comprometido (SEC-1/3/5) se **difiere**: el peor caso ahí es "datos falsos" (cartas/imágenes/que se
> rompa), no compromiso de la máquina. Foco real: firmar el **exe** (SEC-6) + 2FA en las credenciales.

---

## 1. Hardening de seguridad (del análisis estático)

### SEC-1 — Firmar el `manifest.json` · **Alta (técnica)** · ⏸️ DIFERIDO
**Decisión:** diferido. No es necesario para el modelo de amenaza (no habilita compromiso de la PC;
el peor caso es datos falsos). Revisar si el bucket/pack se vuelve crítico o si lo opera más gente.

**Problema:** el `sha256` de cada archivo vive en el mismo manifest que los referencia y el manifest
**no está firmado**. Si se compromete el bucket (token R2 robado) o se convence al usuario de cambiar la
URL del manifest, el atacante cambia archivo **y** hash a la vez y el launcher lo acepta. El hash solo
protege integridad en tránsito / compromisos parciales, no un origen comprometido.
Refs: `src/Services/ManifestService.cs` (fetch), `src/MainWindow.xaml.cs` (acepta cualquier manifest HTTPS).
**Nota:** era un tradeoff documentado (spec §10: "bucket comprometido = datos falsos", acotado por
"solo datos, nunca código" + 2FA). El daño tope hoy no es ejecución en el launcher, sino datos falsos
escritos en Sun and Moon. Aun así es el mayor salto de seguridad disponible.
**Fix propuesto:**
- Firma **RSA** (nativa en net48; evita dependencias — Ed25519 requeriría BouncyCastle, +~2-3 MB).
- Clave **privada offline** (NO en el repo ni en el pipeline de R2). Clave **pública embebida** en el exe.
- Publicar `manifest.json` + `manifest.json.sig` (firma *detached*, base64) en el bucket.
- En el launcher: bajar manifest + `.sig` (mismo confinamiento de origen) y **verificar la firma ANTES**
  de deserializar/usar cualquier `sha256`. Si no valida → abortar (igual que hash inválido).
- Entregables: `SigningService`/verificación en `ManifestService`, clave pública en `AppConstants`,
  **script de firmado** para el mantenedor, actualizar README/SECURITY y el bucket.
- Cierra de paso a **SEC-5** (URL arbitraria): solo se aceptan manifests firmados por tu clave.

### SEC-2 — TLS solo 1.2 · **Media** · ✅ HECHO
`src/App.xaml.cs` ahora fija `SecurityProtocolType.Tls12` (antes habilitaba 1.1/1.0, contra los docs).

### SEC-3 — Límites al descomprimir ZIP (anti zip-bomb) · **Media** · ⏸️ DIFERIDO
**Decisión:** diferido (baja prioridad). Solo previene DoS por zip-bomb (llenar disco/colgar) — molesto,
no un compromiso. Barato de agregar si alguna vez molesta.

**Problema:** no hay topes de tamaño/entradas/ratio al extraer. El hash no protege (un zip-bomb tiene
hash válido); un origen comprometido o un zip legítimo malicioso puede llenar disco / colgar la app.
Refs: `src/Services/ImagePackService.cs`, `src/Services/ZipUtil.cs`.
**Fix:** topes **duros en código** (no confiar en el `size` del manifest): tamaño total descomprimido,
cantidad de entradas, tamaño por entrada y ratio de compresión; abortar si se exceden. *Quick win.*

### SEC-4 — Base zip: exigir exactamente un `database.xml` · **Media** · ✅ HECHO
`src/Services/ZipUtil.cs` ahora rechaza si hay 0 o >1 `.xml` en la base.

### SEC-5 — URL de manifest configurable a cualquier HTTPS · **Media/Baja** · ⏸️ DIFERIDO
**Decisión:** diferido. Queda con el warning actual; lo cerraría SEC-1 (también diferido). Riesgo real
acotado: aun apuntando a otro origen, el peor caso es datos falsos, no compromiso de la PC.

**Problema:** alcanza con convencer al usuario de pegar otra URL "de soporte" para tomar control de los
datos. Ref: `src/MainWindow.xaml.cs` (Configuración).
**Fix:** **lo resuelve SEC-1** (solo manifests firmados por nuestra clave). Plan B si no se hace firma:
warning fuerte / "modo avanzado" al cambiarla.

### SEC-6 — Firmar el binario (SignPath) · **Baja** · ⏳
**Problema:** exe sin firmar → SmartScreen "aplicación no reconocida". Reconocido en README/SECURITY.
**Estado:** pipeline ya cableado condicional en `.github/workflows/release.yml`. Falta: cuenta SignPath
Foundation (OSS, aprobación manual), secrets `SIGNPATH_API_TOKEN`/`SIGNPATH_ORGANIZATION_ID`, y ajustar
los 3 slugs del workflow. Ver checklist en el README / conversación.

---

## 2. Operación / GitHub (toggles de la web — los hace el mantenedor)

- ⬜ **2FA** en GitHub **y** Cloudflare (riesgo dominante del threat model, §10). Token R2 con scope mínimo.
- ⬜ **Secret scanning + Push protection** (Settings → Code security and analysis).
- ⬜ **Private vulnerability reporting** (para que funcione el botón del SECURITY.md).
- ⬜ **Dependabot alerts + security updates** (el `dependabot.yml` ya está en el repo).
- ⬜ **Branch protection** en `main` (force-push y deletions off; opcional: required check `build` + PRs).
- ⬜ **SHA-256 del exe en segundo canal** (web/Discord) en cada release.

---

## 3. Producto / v2 (futuro)

- ⬜ **Noticias y torneos**: definir formato de los `.md` + sección `news` del manifest; render markdown
  → FlowDocument con Markdig (NUNCA WebView). Placeholder ya presente en UI. Ver `docs/NOTICIAS-FORMATO.md`.
- ⬜ **Mantenimiento del manifest**: script para regenerar `manifest.json` (hash + size, y firma cuando
  exista SEC-1) al publicar una base/imágenes nuevas.
- ⬜ Histórico de versiones / changelog de la base (nice-to-have).

---

## 4. Hecho (referencia)

- ✅ v1 funcional: detección, manifest, descarga + verificación SHA-256, HTTPS-only, confinamiento al
  origen, zip-slip, backups (original + timestamp), aplicar base (incl. `.zip`), lanzar, aviso de update.
- ✅ Rediseño visual (pergamino, ventana fija 810×540 3:2, overlays) + i18n (`Strings.resx`).
- ✅ Self-test (34 checks) — verde local y en CI.
- ✅ Repo público, MIT, README, SECURITY.md, dependabot.yml.
- ✅ Release **v0.1.0** publicado (exe + sha256) vía GitHub Actions.
- ✅ Manifest corregido subido al bucket.

---

## Orden sugerido

1. ✅ **Quick wins hechos:** SEC-2 (TLS 1.2) + SEC-4 (un solo `database.xml`).
2. **Foco actual:** SEC-6 (SignPath, esperando aprobación OSS) + toggles de §2 (2FA, secret scanning, etc.).
3. **Diferidos** (revisar si cambia el modelo de amenaza): SEC-1, SEC-3, SEC-5.
