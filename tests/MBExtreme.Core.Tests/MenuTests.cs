using System.Text.RegularExpressions;
using MBExtreme.UI;
using Xunit;

namespace MBExtreme.Core.Tests;

public class MenuTests
{
    [Fact]
    public void Chaves_do_menu_nao_se_repetem_nem_entre_modulos_e_paginas()
    {
        var modules = NavigationMap.Modules.Select(m => m.Key).ToList();
        var pages = NavigationMap.Modules.SelectMany(m => m.Tiles).Select(t => t.Key).ToList();
        Assert.Equal(modules.Count, modules.Distinct().Count());
        Assert.Equal(pages.Count, pages.Distinct().Count());
        // se um módulo tiver a mesma chave de uma página, o clique na página reabre o menu do módulo e "não acontece nada" (bug real da beta-32)
        Assert.Empty(modules.Intersect(pages));
    }

    [Fact]
    public void Todo_modulo_tem_pelo_menos_uma_pagina_e_todo_texto_esta_preenchido()
    {
        foreach (var module in NavigationMap.Modules)
        {
            Assert.NotEmpty(module.Tiles);
            Assert.False(string.IsNullOrWhiteSpace(module.Title));
            Assert.All(module.Tiles, t => { Assert.False(string.IsNullOrWhiteSpace(t.Title)); Assert.False(string.IsNullOrWhiteSpace(t.Description)); });
        }
    }

    private static string? FindFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Toda_pagina_do_menu_tem_uma_tela_criada_pela_janela_principal()
    {
        string? path = FindFile(Path.Combine("src", "MBExtreme.App", "MainWindow.xaml.cs"));
        Assert.NotNull(path);
        string code = File.ReadAllText(path!);
        var missing = NavigationMap.Modules.SelectMany(m => m.Tiles).Select(t => t.Key).Where(k => !code.Contains($"\"{k}\"")).ToList();
        Assert.True(missing.Count == 0, "Páginas sem tela em MainWindow.CreateInner: " + string.Join(", ", missing));
    }

    [Fact]
    public void Estudio_e_vídeos_curtos_ficam_no_mesmo_modulo_e_o_benchmark_reune_os_testes()
    {
        Assert.Equal("studio", NavigationMap.ModuleOf("record")!.Key);
        Assert.Equal("studio", NavigationMap.ModuleOf("shorts")!.Key);
        Assert.Equal("studio", NavigationMap.ModuleOf("studio")!.Key);
        foreach (string page in new[] { "gamefps", "bench", "stress", "compare", "fps" })
            Assert.Equal("benchmark", NavigationMap.ModuleOf(page)!.Key);
    }
}
