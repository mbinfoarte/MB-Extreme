using System.Text.RegularExpressions;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>
/// No WPF um StaticResource só enxerga o que foi definido ANTES no mesmo arquivo: usar um pincel declarado mais
/// para baixo derruba o programa na abertura (StaticResourceHolder). Este teste barra isso em todos os XAML do app.
/// </summary>
public class TemaXamlTests
{
    [Fact]
    public void StaticResource_SoUsaOQueJaFoiDefinido()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "MBExtreme.App"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        string app = Path.Combine(dir!, "src", "MBExtreme.App");
        string theme = File.ReadAllText(Path.Combine(app, "Themes", "Theme.xaml"));
        var themeKeys = Keys(theme).Keys.ToHashSet();

        var problems = new List<string>();
        foreach (string file in Directory.GetFiles(app, "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            string text = File.ReadAllText(file);
            bool isTheme = file.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase);
            var local = Keys(text);
            foreach (Match m in Regex.Matches(text, @"\{StaticResource\s+([A-Za-z0-9_.]+)\}"))
            {
                string key = m.Groups[1].Value;
                if (local.TryGetValue(key, out int defined))
                {
                    if (defined > m.Index) problems.Add($"{Path.GetFileName(file)}: {key} usado antes de ser definido");
                }
                else if (isTheme)
                {
                    problems.Add($"{Path.GetFileName(file)}: {key} não existe no tema");
                }
            }
        }
        Assert.Empty(problems);
        Assert.Contains("AccentGradientVertical", themeKeys);
    }

    private static Dictionary<string, int> Keys(string xaml)
    {
        var keys = new Dictionary<string, int>();
        foreach (Match m in Regex.Matches(xaml, "x:Key=\"([^\"]+)\"")) keys.TryAdd(m.Groups[1].Value, m.Index);
        return keys;
    }
}
