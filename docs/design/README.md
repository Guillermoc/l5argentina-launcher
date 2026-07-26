# docs/design — decisiones técnicas transversales

Documentos que **atraviesan varios módulos**. Si algo aplica a un solo dominio, va en su índice
([../index/](../index/)); si es una decisión puntual con su porqué, va como entrada en
[../DECISIONS.md](../DECISIONS.md). Acá viven las reglas que hay que respetar desde varios lugares a
la vez.

| Documento | Cubre |
|---|---|
| [seguridad.md](seguridad.md) | Las seis reglas innegociables (solo datos, SHA-256, HTTPS, confinamiento de origen, zip-slip, link hardcodeado) y cómo se verifican. Aplica a red, descarga, extracción y escritura. |
| [paridad-app-android.md](paridad-app-android.md) | Cómo se portan el Buscador y el deckbuilder desde la app: golden tests, semánticas de SQLite replicadas, bugs portados a propósito y divergencias deliberadas. |
| [persistencia.md](persistencia.md) | Dónde vive cada archivo de estado, con qué criterio, y qué hacer al agregar estado nuevo. |
| [style-guide.md](style-guide.md) | Guía visual de marca **de la app Android**, traída como referencia de criterio. Los tokens vigentes del launcher están en [../index/ui.md](../index/ui.md) y su fuente de verdad es `src/Themes/Theme.xaml`. |
| [visual/](visual/) | Handoff original del rediseño visual (prompt, `Theme.xaml` de referencia y el mockup HTML con todas las pantallas). Material histórico: el tema vigente es `src/Themes/Theme.xaml`. |
