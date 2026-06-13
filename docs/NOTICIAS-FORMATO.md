# Formato de noticias (v2) — PLACEHOLDER, a definir

> Esta feature es de **v2** y todavía **no está implementada**. Este documento es un marcador
> para cuando definamos el formato. La UI ya tiene una sección "Noticias y torneos" con un
> placeholder, y el modelo `NewsEntry` / `NewsService` existen como stubs.

## Decisiones de diseño ya tomadas (de la spec)

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
