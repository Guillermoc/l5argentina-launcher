# Noticias y torneos (v2) — borrador de spec funcional

> **Borrador, sin implementar.** Feature de v2: este documento es el marcador para cuando se defina
> el formato. La UI ya tiene una sección "Noticias y torneos" con un placeholder, y el modelo
> `NewsEntry` / `NewsService` existen como stubs. Antes de implementar, esto se completa como spec
> funcional versionada (`noticias-v2.0.md`) y recién ahí se le pide un enfoque técnico a Claude Code.

## Decisiones de diseño ya tomadas (de la spec v1.0)

- Las noticias serán archivos **`.md`** en el bucket, referenciados desde el `manifest.json` en
  una sección `news` (cada entrada con su `sha256`).
- Se renderizan como **markdown a componente nativo** (Markdig → `FlowDocument`).
  **Nunca** un navegador embebido / WebView (spec §8 regla 4): sería convertir el contenido del
  bucket en superficie de ejecución de código.
- Se verifica el **SHA-256** de cada `.md` antes de renderizarlo, igual que las bases e imágenes.

## A definir cuando se implemente

- [ ] Estructura exacta de cada entrada en `manifest.json` → `news[]`
      (¿`title`, `date`, `file`, `sha256`, `pinned`?, `tags`?).
- [ ] Convención de nombres de los `.md` (p. ej. `news/AAAA-MM-DD-slug.md`).
- [ ] Subconjunto de markdown soportado / estilos de `FlowDocument` (acorde a la guía de estilos).
- [ ] ¿Imágenes dentro de las noticias? Si sí, de dónde y con qué verificación.
- [ ] Orden, paginación y "marcar como leído".
