# ROADMAP — L5Argentina Launcher

Qué falta. **Se lee al empezar cualquier sesión**, junto con [index/OVERVIEW.md](index/OVERVIEW.md).

Leyenda: ⏳ en curso/esperando · 🔜 próximo · ⬜ pendiente · ⏸️ diferido · 🔁 recurrente · ✅ hecho.

**Estado hoy:** v0.7.0 lista para publicar (export a PDF, espacio de trabajo Buscador+Mazos, pasada de
UI sobre botoneras y paneles, y el aviso de novedades del launcher). Batería en verde.
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
  ✅ Branch protection en `main` — se mantiene **contra force-push y borrado**, no como gate de
  review: el flujo es push directo, sin PRs ([ADR-034](DECISIONS.md#adr-034)).
- ✅ **Dependabot al día (2026-07-26).** Se aplicaron a mano los 9 bumps pendientes (System.Text.Json
  8.0.5 → 10.0.10, Costura.Fody 5.7.0 → 6.2.0, Fody 6.8.0 → 6.9.3 y las 5 GitHub Actions). El salto
  de `System.Text.Json` **rompía la compilación** en net48 y hubo que declarar tres fachadas del
  framework a mano — ver [index/build-verificacion.md](index/build-verificacion.md).
- 🔁 **Revisar las ramas `dependabot/*` antes de cada push** (`git ls-remote origin
  'refs/heads/dependabot/*'`): con los PR deshabilitados el bot no puede avisar de otra forma
  ([ADR-034](DECISIONS.md#adr-034)). Las ramas ya aplicadas **no se borran solas**; conviene
  limpiarlas para que la próxima revisión no muestre ruido viejo.
- ⬜ Decidir si se rehabilitan los PR **solo** para que Dependabot pueda abrirlos (el flujo de trabajo
  del mantenedor seguiría siendo push directo).
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
- ✅ Filtrar por un conjunto cualquiera —lista, mazo, favoritas u ocultas— desde un solo selector
  ([ADR-045](DECISIONS.md#adr-045)).
- ✅ Chip "Solo holdings iniciales" (`onlyInitialHoldings`): el motor y su golden estaban desde el
  principio, faltaba el chip.
- ✅ Resumen de filtros activos al lado del contador, con el detalle a un click.
- ✅ Click derecho sobre una celda = copiar el arte a tamaño completo.
- ✅ Zoom de la grilla en tres tamaños de celda.
- ✅ Previsualización con **Alt**: pasar sobre una carta la abre grande al lado del cursor.
- ✅ Favoritas salió del selector de conjuntos (ya tenía chip propio y `★` por celda).
- ⬜ Contador de cartas en el selector de conjunto (hoy solo se ve en la solapa Listas del editor).
- ⬜ Renombrar una lista desde la barra del Buscador (hoy hay que ir a la ventana de Mazos).
- ⬜ `Ctrl+click` para agregar también desde la vista de tabla (hoy solo en la grilla, donde el
  overlay se puede leer; en la tabla sigue el doble click) — ver [ADR-041](DECISIONS.md#adr-041).

**Mazos** ([index/mazos.md](index/mazos.md))
- ✅ Listas editables con el mismo editor que los mazos, como `.txt` de `Lists\`
  ([ADR-045](DECISIONS.md#adr-045)).
- ✅ Recorte: bajar una carta a 0 la conserva en el mazo, grisada, en vez de borrarla
  ([ADR-046](DECISIONS.md#adr-046)).
- ✅ Selector de mazo colgado del **título**: buscar conjuntos por nombre o por carta, ordenar por
  recientes o alfabético, filtrar por clan y por estado de armado
  ([ADR-047](DECISIONS.md#adr-047), [ADR-051](DECISIONS.md#adr-051)).
- ✅ Paneles agrupados **por objeto**: izquierda el mazo abierto (**Filtros | Stats**), derecha la
  carta ([ADR-051](DECISIONS.md#adr-051)).
- ✅ Pasada sobre las botoneras de las dos ventanas ([ADR-050](DECISIONS.md#adr-050)) y paneles
  ajustables con el mouse.
- ✅ Buscador y Mazos **abren** juntos como un solo espacio de trabajo (cerrar el editor cierra solo el
  editor; `Ctrl+D` lo reabre) ([ADR-052](DECISIONS.md#adr-052)).
- ✅ Botón **lado a lado** (`⫞`): Buscador 70 % / editor 30 %, en vista Lista y todo colapsado.
- ✅ Las miniaturas aparecen al abrir y al scrollear: cola propia de decodificación en vez de los
  bindings `IsAsync` de WPF, que las despachaban a cuentagotas ([ADR-053](DECISIONS.md#adr-053)).
- ⏸️ Goldfish (sandbox de testeo) — fuera de alcance; la spec del handoff lo cubre si se quiere.
- ⏸️ Grupos de usuario (carpetas de mazos) y subgrupos dentro del mazo — no portados.
- ✅ Export PDF: grilla 3×3 a tamaño real para imprimir proxies, con copias ajustables por carta
  ([ADR-049](DECISIONS.md#adr-049)).
- ⬜ Buscador embebido en el editor (hoy el flujo es "Buscador `+`/`−` sobre el mazo activo").
- ⬜ Favoritas y ocultas **dentro** del editor. Hoy se filtran desde el Buscador y se copian a una
  lista con `⎘`; editarlas ahí adentro pediría una capa de indirección sobre todo `DeckWindow` para
  algo que no es un archivo.

**Ventanas**
- ✅ Mínimos bajados a 720×520 y **modo angosto**: bajo 1000 DIP los paneles laterales se cierran y
  los rieles se apagan.
- ✅ Zoom en cuatro tamaños (`－`/`＋` y **Ctrl + rueda**) y previsualización con **Alt** en las
  **dos** ventanas.
- ✅ Botón `?` con el listado de atajos de teclado y mouse en ambas ventanas.
- ✅ Segundo escalón de angostura: bajo 900 DIP se esconde lo accesorio de la barra, y los mínimos
  bajaron a 460 para que el 30 % del layout lado a lado entre sin irse de pantalla.
- ⬜ Previsualización con Alt en la vista de **tabla** del Buscador (hoy solo en la grilla).

**Launcher**
- ✅ El acceso a Cartas y mazos salió del grupo de utilidades del header y quedó al lado de JUGAR.
- ✅ El pack de imágenes propio dejó de aparecer duplicado en el overlay de packs
  ([ADR-048](DECISIONS.md#adr-048)).
- ✅ La pantalla avisa si hay algo pendiente —o dice **"Todo al día"**—, sin tener que abrir el modal
  de packs. Un pack en versión 1.0 no cuenta como novedad ([ADR-054](DECISIONS.md#adr-054)).
- ✅ Los packs de imágenes son una sola familia y el pool dice cuáles hay: un pack nuevo de la
  comunidad aparece solo, sin tocar el launcher ni el manifest ([ADR-055](DECISIONS.md#adr-055)).
- ✅ Un pack **nuevo** avisa aunque salga en 1.0: `knownImagePacks` separa "nunca lo vimos" de "nunca
  lo bajaste", con siembra en la primera vista para no inventar novedades
  ([ADR-056](DECISIONS.md#adr-056)).

**Estado del usuario**
- ⬜ Aviso al usuario cuando una actualización de la base deja nombres sin resolver (hoy esas
  entradas se descartan en silencio) — ver [ADR-044](DECISIONS.md#adr-044).
- ⬜ Escribir el contrato del `cards_db.json` en `technical/contratos/` (qué puede cambiar entre
  versiones sin romper el estado del usuario: los `id` sí, los **nombres no**).

**URLs / operación**
- ⬜ Confirmar con el mantenedor que `DefaultManifestUrl` y `ReleasesPageUrl` de `AppConstants` son
  las definitivas de producción (el comentario que las llamaba placeholders `TODO(URL)` ya se
  corrigió; falta la confirmación de que las URLs en sí son las finales).

---

## 5. Releases publicadas

| Versión | Fecha | Contenido |
|---|---|---|
| v0.1.0 | 2026-06-14 | v1 funcional (detección, manifest, descarga verificada, backups, aplicar base, lanzar, aviso de update) + rediseño visual + i18n + self-test + repo público/MIT/SECURITY/dependabot. |
| v0.1.1 | 2026-06-14 | SEC-2 (TLS 1.2), SEC-4 (un solo `.xml`), ajustes de textos. |
| v0.2.0 | 2026-07-07 | Buscador de cartas (MVP → V3), packs de imágenes y catálogo desde el pool. |
| v0.3.0 | 2026-07-11 | Mazos (deckbuilder) + V4 del Buscador + fixes de rendimiento. |
| v0.6.0 | 2026-07-28 | **Conjuntos de cartas**: listas editables con el mismo editor que los mazos, como `.txt` en `Lists` ([ADR-045](DECISIONS.md#adr-045)); identidad del estado del usuario **por nombre de carta** y no por id, con migración de `usercards.json` ([ADR-044](DECISIONS.md#adr-044)); **recorte** de cartas ([ADR-046](DECISIONS.md#adr-046)); panel de abrir conjuntos con orden, clan y **estado de armado** ([ADR-047](DECISIONS.md#adr-047)); chip de holdings iniciales, resumen de filtros activos, zoom de grilla en 4 tamaños, previsualización con **Alt**, copiar imagen con click derecho, listado de atajos (`?`), ventanas **lado a lado** y adaptación a ventanas angostas; el pack de imágenes propio dejó de duplicarse ([ADR-048](DECISIONS.md#adr-048)) y el acceso a Cartas y mazos quedó junto a JUGAR. |
| v0.4.0 | 2026-07-26 | Pasada de UX: taxonomía de controles (rieles de colapso, pestañas adosadas, segmentados, menús), ventanas acotadas al área de trabajo, arte de carta redondeado y sin marco, listas manejadas desde la barra, diálogos propios (fuera los `MessageBox`), confirmación al cerrar, `Ctrl+click` para agregar al mazo, copiar imagen al portapapeles y **un solo mazo activo** con la ventana de Mazos como fuente de verdad. |
