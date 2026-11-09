using System.Text.RegularExpressions;

namespace MBExtreme.LicenseServer;

/// <summary>
/// Gera a versão estática do site (páginas públicas) para hospedar no GitHub Pages enquanto o servidor não está no ar.
/// Uso: dotnet run --project src/MBExtreme.LicenseServer -- --export-site pasta [/caminho-base/]
/// Conta, login e afiliados precisam do servidor: no site estático, esses botões levam ao Download (o teste começa no programa).
/// </summary>
public static class StaticSite
{
    public static readonly IReadOnlyList<string> Routes = new[] { "", "planos", "download", "afiliados", "termos", "privacidade" };

    /// <summary>"/MB-Extreme" ou "MB-Extreme/" → "/MB-Extreme/"; vazio → "/".</summary>
    public static string NormalizeBase(string? basePath)
    {
        string b = (basePath ?? "").Trim().Trim('/');
        return b.Length == 0 ? "/" : "/" + b + "/";
    }

    private static readonly Regex ServerOnly = new("(href|action)=\"/(login|register|account|affiliate|logout)[^\"]*\"", RegexOptions.Compiled);
    private static readonly Regex RootLink = new("(href|src|action)=\"/(?!/)", RegexOptions.Compiled);

    /// <summary>Troca links do servidor pelo Download e põe o caminho base nos links que começam com "/".</summary>
    public static string Rewrite(string html, string? basePath)
    {
        string b = NormalizeBase(basePath);
        html = ServerOnly.Replace(html, m => $"{m.Groups[1].Value}=\"/download\"");
        html = RootLink.Replace(html, m => $"{m.Groups[1].Value}=\"{b}");
        return html;
    }

    public static IReadOnlyList<string> Export(SiteOptions site, string outDir, string? basePath, string legalDir, string? webRoot)
    {
        Directory.CreateDirectory(outDir);
        var written = new List<string>();
        foreach (string route in Routes)
        {
            string html = route switch
            {
                "" => SitePages.Home(site, null, false),
                "planos" => SitePages.Plans(site, null),
                "download" => SitePages.Download(site, null),
                "afiliados" => SitePages.AffiliatesLanding(site, null, false),
                "termos" => Legal(site, "Termos de uso", Path.Combine(legalDir, "termos.md")),
                _ => Legal(site, "Política de privacidade", Path.Combine(legalDir, "privacidade.md")),
            };
            string folder = route.Length == 0 ? outDir : Path.Combine(outDir, route);
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "index.html");
            File.WriteAllText(file, Rewrite(html, basePath));
            written.Add(file);
        }

        string notFound = Path.Combine(outDir, "404.html");
        File.WriteAllText(notFound, Rewrite(SiteLayout.Narrow(site, "Página não encontrada",
            "<h1>Página não encontrada</h1><p class=\"muted\">O endereço não existe. <a href=\"/\" style=\"color:var(--accent)\">Voltar para o início</a> ou <a href=\"/download\" style=\"color:var(--accent)\">baixar o MB Extreme</a>.</p>", null), basePath));
        written.Add(notFound);
        File.WriteAllText(Path.Combine(outDir, ".nojekyll"), "");   // o GitHub Pages serve os arquivos como estão

        if (webRoot is not null && Directory.Exists(webRoot))
            foreach (string source in Directory.EnumerateFiles(webRoot, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(outDir, Path.GetRelativePath(webRoot, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, true);
            }
        return written;
    }

    private static string Legal(SiteOptions site, string title, string path)
    {
        string text = File.Exists(path) ? File.ReadAllText(path) : "# " + title + "\n\nTexto em preparação.";
        return SiteLayout.Narrow(site, title, SiteLayout.Markdown(text), null);
    }
}
