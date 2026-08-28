# docs/technical/contratos — contratos externos

El **formato de los datos** que el launcher consume: lo que no controla y tiene que respetar
mientras exista. No son specs de feature (esas están en [../../functional/](../../functional/), y ahí
viven también los paquetes de handoff de la app Android): son el contrato con el bucket, con el CDN
y con Sun and Moon.

| Contrato | Qué es |
|---|---|
| [manifest.example.json](manifest.example.json) | Ejemplo real del `manifest.json` del bucket, con hashes de verdad. |
| Manifest del pool | Documentado abajo; no tiene archivo de ejemplo propio (hay una copia en `../../../tests/fixtures/buscador/data/manifest-production-2026-07-06.json`). |
| [formato-txt-sun-and-moon.md](formato-txt-sun-and-moon.md) | El `.txt` de la carpeta `Decks\` del juego: gramática de la línea, proxies, ediciones, cantidades y las trampas del parser (leído del `Assembly-CSharp.dll`), lo que escribe el launcher, y el **otro** formato de texto de un mazo — el del portapapeles, que es el de la app Android y no acepta lo mismo. |

---

## El `manifest.json` del bucket

Estructura vigente (los `file` son **relativos** al manifest — nunca URLs absolutas):

```jsonc
{
  "schema": 1,
  "launcher": { "latest_version": "1.0.0", "notes": "" },   // el LINK de descarga NO va acá
  "databases": [ { "id": "community", "label": "…", "version": "2.0.1",
                   "file": "database-2.0.1.zip", "sha256": "…", "size": 786191 } ],
  "images":    { "version": "1.0", "file": "samuraiEx-1.0.zip", "sha256": "…", "size": 346574 },
  "catalog":   { /* opcional, ver abajo */ },
  "news":      [ /* v2, sin implementar */ ]
}
```

- `databases[]`: solo se descarga la entrada con `id == "community"`; la Original es local
  ([ADR-005](../../DECISIONS.md#adr-005)). Puede ser `.zip` (con un único `.xml` adentro) o `.xml`.
- `images`: pack de imágenes propio de la comunidad (las cartas que no están en los packs clásicos).
- `schema`: versionado para poder evolucionar el manifest sin romper launchers viejos.

### Sección `catalog` (fallback del pool)

Hoy el catálogo del Buscador se toma del manifest del pool ([ADR-021](../../DECISIONS.md#adr-021)).
Esta sección queda como respaldo y se puede retirar del bucket:

```json
"catalog": {
  "cards":   { "version": "2.3.1", "file": "catalog/cards_db-2.3.1.zip", "sha256": "…", "size": 900193 },
  "rules":   { "version": "2.3.1", "file": "catalog/rules-2.3.1.json",   "sha256": "…", "size": 64240 },
  "filters": { "version": "2.3.0", "file": "catalog/filters-2.3.0.json", "sha256": "…", "size": 137738 }
}
```

Son los mismos artefactos del canal `production` del CDN de la app (copias de referencia en
`../../../tests/fixtures/buscador/data/`). Sin catálogo por ninguna de las dos vías, el
Buscador avisa y no abre; para desarrollo se pueden copiar los archivos a mano a `cache/catalog/`
(respeta `L5A_DATA_ROOT`).

## El manifest del pool (CDN de la app Android)

`AppConstants.PoolManifestUrl` → canal `production`. Lista plana de `packages` con `id`, `type`
(`database` | `rules` | `filters` | `images` | `changelog`), `version`, `url` **absoluta**,
`sizeBytes` y `sha256`. El campo `sha256` se agregó a ese pipeline para el launcher: la app Android
lo ignora. Las URLs absolutas se aceptan **solo si son del mismo origen** que el manifest
([ADR-007](../../DECISIONS.md#adr-007)).
