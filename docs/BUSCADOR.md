# Buscador de cartas — arquitectura y decisiones

MVP del buscador dentro del launcher, con la **misma semántica de filtros que la app Android
L5Argentina**. La especificación completa de esa semántica vive en el paquete de handoff
(`docs/handoff/buscador-launcher/`); este doc cubre el lado launcher: qué se construyó, qué
decisiones se tomaron y qué falta.

## Qué hay (MVP)

| Pieza | Archivo | Qué hace |
|---|---|---|
| Modelo | `src/Models/CardCatalog.cs` | `Card` espejo de la tabla `cards` de la app (columnas pipe-delimited incluidas) + catálogo. |
| Importer | `src/Services/Search/CatalogImporter.cs` | Port 1:1 de `CardImporter` + `RulesImporter.applyRules` + ingestión de filters.json. Mismo estado final que un sync completo de la app. |
| Motor | `src/Services/Search/CardSearchEngine.cs` | Port del SQL de `CardQueryCompiler` a predicados en memoria. Semánticas SQLite replicadas: LIKE = contains case-insensitive solo-ASCII; IN/= = Ordinal (BINARY); NULLs de stats al final; empates = orden de inserción (sort estable). Incluye orden por Set en memoria (`presortSets` + rank por printing). |
| Criteria | `src/Services/Search/CardSearchCriteria.cs` | Port de `CardSearchCriteria.kt`; nombres JSON compatibles con los golden. |
| Carga | `src/Services/Search/CatalogStore.cs` | Encuentra el trío `cards_db-X.Y.Z.zip` / `rules-X.Y.Z.json` / `filters-X.Y.Z.json` (versión más alta) en `cache/catalog/`. |
| Sync | `LauncherEngine.SyncCatalogAsync` | Descarga el catálogo desde la sección opcional `catalog` del manifest, con SHA-256 y confinamiento al origen (mismas reglas §8 que bases/imágenes). |
| UI | `src/Views/SearchWindow.xaml(.cs)` | Ventana redimensionable: panel de filtros persistente (aplican **en vivo**, sin modal), tabla virtualizada, panel de detalle con imagen + texto renderizado. Ctrl+F enfoca la búsqueda. |
| Render | `src/Views/CardTextRenderer.cs` | `<b>/<i>/<br>` + bullets → Inlines de WPF (sin WebView, regla §8). |
| Paridad | `SelfTest.RunGoldenSearchTests` | Corre los **53 golden tests** generados con la lógica real de la app. `--selftest` = 91 checks. |

**Verificación**: los golden pasan 53/53 sin ajustes. Si se toca el motor o el importer,
correr `--selftest`; si se decide *cambiar* una semántica, regenerar los expected de los casos
afectados con el harness del handoff (`fixtures/generator/`) y anotarlo.

## Decisiones tomadas

1. **Fuente de datos = cards.json (+ rules + filters), no el database.xml.** El Buscador
   depende de campos que el XML no tiene: `keywords` (derivación editorial con correcciones
   manuales), legalidades por printing, flag `current` (MODELO-DATOS.md §5). Parsear el XML
   habría obligado a replicar derivaciones frágiles. El `database.xml` sigue siendo blob opaco
   para el launcher.
2. **Motor en memoria, sin SQLite.** 6.394 cartas filtran en milisegundos con LINQ; SQLite
   habría metido un binario nativo (fricción con Costura/exe único). La fidelidad la garantizan
   los golden, no el motor compartido.
3. **Bugs conocidos: se portan, la UI compensa.** Para mantener los golden byte a byte:
   - Rareza case-sensitive (⚠️ SPEC-FILTROS §7): el motor porta el bug; el chip de la UI manda
     ambas variantes (`P` y `p`), así el usuario ve las 495 promos y los golden siguen en verde.
   - Subtitle-substring en keywords (⚠️ §2): portado tal cual (toggle "Incluir subtítulos").
   - Formato/legalidad: sin UI (la app tampoco la tiene) y evita el mismatch `20F`/`TwentyFestivals`.
4. **UX desktop, no port de la UI móvil.** Filtros siempre visibles aplicando en vivo (el modal
   con draft + "Aplicar" es un patrón de celu); tabla con columnas en vez de carousel; detalle
   fijo a la derecha en vez de visor fullscreen. Sin persistencia de filtros (igual que la app).
5. **Imágenes**: se leen de la instalación de Sun and Moon (`<install>\images\...`) con la
   cadena de fallback de la app (imagePath → printing_images → placeholder de texto), más la
   carpeta del propio exe como raíz alternativa (caso: launcher copiado dentro de la carpeta
   del juego). Si una imagen no aparece, el detalle muestra la ruta donde se buscó.
   ⚠️ Alias de carpeta: la DB referencia las promos como `images/cards/P/…` pero Sun and Moon
   las guarda en `images/cards/Promo/…` — la resolución prueba ambas (solo afecta archivos;
   el filtro/orden por set sigue usando el código `P`). El Buscador es 100 % lectura: no toca
   red ni la instalación.

## Packs de imágenes (manifest del pool)

El launcher lee además el **manifest de producción del CDN de la app**
(`AppConstants.PoolManifestUrl` → `/production/manifest.json`, mismo bucket R2), que lista
packages con URL absoluta y **SHA-256** (agregado al pipeline de la app para esto; la app
Android ignora el campo). Seguridad: URLs absolutas aceptadas SOLO si son del mismo origen
que el manifest (`ManifestService.ResolveSameOriginUrl`) + hash verificado, como todo.

El botón **"Packs de imágenes…"** (fila de imágenes de la ventana principal) abre el menú
con los packs `type=images` del manifest (Lotus, Samurai, …, Promo, Samurai Extended):
descarga opcional pack por pack, extracción sobre `<SunAndMoon>\images\` con la protección
zip-slip de `ImagePackService` (los zips traen `cards/<set>/…`, mismo layout que `images\`),
borrado del zip tras aplicar (pesan hasta ~280 MB) y estado por pack en la config
(`appliedImagePacks`, por hash). Ids nuevos del manifest aparecen solos; el nombre amigable
sale de `Pack_<id>` en el resx (fallback: el id).

Esto también le sirve al juego: completa las imágenes de Sun and Moon, no solo del Buscador.

**El catálogo del Buscador también se sincroniza desde el pool** (fuente primaria): en
"Buscar actualizaciones", el launcher toma los packages `database`/`rules`/`filters` del
manifest de production, los descarga a `cache/catalog/` con nombre versionado
(`cards_db-2.3.1.zip` — la URL del pool no lleva versión en el nombre) y limpia las
versiones viejas. La sección `catalog` del manifest del launcher (abajo) queda como
**fallback** si el pool no está disponible; se puede retirar del bucket cuando quieran.

## Cómo publicar el catálogo en el bucket (fallback, ya no necesario con el pool)

Agregar al `manifest.json` la sección opcional (los `file` son relativos al manifest, como
siempre):

```json
"catalog": {
  "cards":   { "version": "2.3.1", "file": "catalog/cards_db-2.3.1.zip",  "sha256": "…", "size": 900193 },
  "rules":   { "version": "2.3.1", "file": "catalog/rules-2.3.1.json",    "sha256": "…", "size": 64240 },
  "filters": { "version": "2.3.0", "file": "catalog/filters-2.3.0.json",  "sha256": "…", "size": 137738 }
}
```

Son los mismos artefactos del canal `production` del CDN de la app Android (copias de
referencia en `docs/handoff/buscador-launcher/fixtures/data/`). El launcher los baja a
`cache/catalog/` en "Buscar actualizaciones". Sin la sección, el botón Buscador avisa que no
hay catálogo; para desarrollo se pueden copiar los archivos a mano a esa carpeta (respeta
`L5A_DATA_ROOT`).

## V2 (implementado)

- **Grilla de miniaturas** (toggle Lista/Grilla en la barra superior): filas virtualizadas
  (chunking manual — WPF nativo no trae wrap panel virtualizado), thumbnails decodificados
  lazy (`DecodePixelWidth` + binding `IsAsync` + retención débil para que la memoria no crezca
  con miles de cartas). Punto rojo = baneada; click = detalle.
- **"Ver todas las impresiones"** (chip "Impresiones", solo en grilla): port de
  `displayCards`/`expandCard` (SPEC-FILTROS §15) en `CardSearchEngine.ExpandForDisplay` —
  con filtro de Edición muestra solo las impresiones de esos sets, y si ninguna coincide
  conserva la preferida para no perder la carta. Cubierto por 3 checks del selftest.
- **Favoritas / ocultas**: persistidas en `%APPDATA%\...\usercards.json`
  (`UserCardDataStore`, equivale a `user_card_data` de la app). Acciones en el panel de
  detalle; quick filters en "Otros" (Solo favoritas / Ver ocultas / Solo ocultas).
  Divergencia deliberada: activar "Solo ocultas" acá NO resetea los demás filtros (en la
  app sí); las semánticas de filtrado son las del motor (golden `solo-favoritas`, etc.).
- **Autocomplete de keywords estilo app**: matching por "contiene" (OrdinalIgnoreCase) sobre
  los 838 códigos mientras se tipea, en ambos combos (incluir/excluir).

## Pendientes (v3)

- Listas de cartas (van junto con Mazos si algún día se encara).
- Preset de filtros, tabs múltiples, operadores `k.`/`c.`/`t.`/`tx.` en la barra.
- Navegación por teclado en la grilla; sincronizar scroll entre lista y grilla.
