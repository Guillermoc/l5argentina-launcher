using System.Collections.Generic;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>
    /// PLACEHOLDER (v2). Las noticias/torneos vendrán como .md en el bucket y se renderizarán
    /// como markdown a FlowDocument con Markdig — NUNCA con WebView (spec §4.2, §8 regla 4).
    /// El formato exacto de los .md y su sección en el manifest se define más adelante.
    /// </summary>
    public class NewsService
    {
        public IReadOnlyList<NewsEntry> GetNews(Manifest manifest)
        {
            // v2: descargar/verificar/renderizar. Por ahora devolvemos lo que traiga el manifest
            // (o vacío) sin procesarlo, para que la UI muestre el placeholder.
            return manifest?.News ?? new List<NewsEntry>();
        }
    }
}
