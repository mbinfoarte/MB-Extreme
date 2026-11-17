using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;
using Xunit;

namespace MBExtreme.Core.Tests;

public class CenasTests
{
    private static AppSettings Settings()
    {
        var s = new AppSettings();
        s.StudioCameras.Add(new StudioCameraSetting { Name = "Webcam", On = true });
        s.StudioCameras.Add(new StudioCameraSetting { Name = "http://192.168.0.15:8080/video", On = false });
        s.StudioLowerOn = true;
        return s;
    }

    [Fact]
    public void Primeira_cena_nasce_do_que_esta_ligado()
    {
        var s = Settings();
        var scene = StudioScenes.EnsureCurrent(s);
        Assert.Single(s.StudioScenes);
        Assert.Equal("Cena 1", scene.Name);
        Assert.Equal(StudioSceneItemKind.Capture, scene.Items[0].Kind);
        Assert.True(StudioScenes.Shows(scene, StudioSceneItemKind.Camera, "Webcam"));
        Assert.False(StudioScenes.Shows(scene, StudioSceneItemKind.Camera, "http://192.168.0.15:8080/video"));
        Assert.True(StudioScenes.Shows(scene, StudioSceneItemKind.LowerThird));
        Assert.DoesNotContain(scene.Items, i => i.Kind == StudioSceneItemKind.Pix);
    }

    [Fact]
    public void Cada_cena_liga_so_as_suas_fontes()
    {
        var s = Settings();
        var gravacao = StudioScenes.EnsureCurrent(s);
        var telaCheia = new StudioSceneSetting { Name = "Tela cheia" };
        telaCheia.Items.Add(new StudioSceneItem { Kind = StudioSceneItemKind.Capture });
        telaCheia.Items.Add(new StudioSceneItem { Kind = StudioSceneItemKind.Pix });
        s.StudioScenes.Add(telaCheia);

        StudioScenes.Apply(telaCheia, s);
        Assert.All(s.StudioCameras, c => Assert.False(c.On));
        Assert.True(s.StudioPixOn);
        Assert.False(s.StudioLowerOn);

        StudioScenes.Apply(gravacao, s);
        Assert.True(s.StudioCameras[0].On);
        Assert.False(s.StudioPixOn);
        Assert.True(s.StudioLowerOn);
    }

    [Fact]
    public void Fonte_oculta_fica_na_cena_mas_nao_aparece()
    {
        var s = Settings();
        var scene = StudioScenes.EnsureCurrent(s);
        scene.Items.First(i => i.Kind == StudioSceneItemKind.LowerThird).Hidden = true;
        StudioScenes.Apply(scene, s);
        Assert.False(s.StudioLowerOn);
        Assert.Contains(scene.Items, i => i.Kind == StudioSceneItemKind.LowerThird);
    }

    [Fact]
    public void Mudar_nas_abas_atualiza_a_cena_atual()
    {
        var s = Settings();
        var scene = StudioScenes.EnsureCurrent(s);
        s.StudioCameras[1].On = true;
        s.StudioPixOn = true;
        s.StudioLowerOn = false;
        StudioScenes.Sync(scene, s);
        Assert.True(StudioScenes.Shows(scene, StudioSceneItemKind.Camera, "http://192.168.0.15:8080/video"));
        Assert.True(StudioScenes.Shows(scene, StudioSceneItemKind.Pix));
        Assert.False(StudioScenes.Shows(scene, StudioSceneItemKind.LowerThird));
        Assert.Contains(scene.Items, i => i.Kind == StudioSceneItemKind.LowerThird);   // oculta, não removida
    }

    [Fact]
    public void Adicionar_mostra_so_o_que_falta()
    {
        var s = Settings();
        var scene = StudioScenes.EnsureCurrent(s);
        var addable = StudioScenes.Addable(scene, s);
        Assert.DoesNotContain(addable, i => i.Kind == StudioSceneItemKind.Capture);
        Assert.DoesNotContain(addable, i => i.Kind == StudioSceneItemKind.LowerThird);
        Assert.Contains(addable, i => i.Kind == StudioSceneItemKind.Pix);

        var vazia = new StudioSceneSetting { Name = "Vazia" };
        var all = StudioScenes.Addable(vazia, s);
        Assert.Equal(1 + 2 + 4, all.Count);
    }

    [Fact]
    public void Nomes_e_titulos()
    {
        Assert.Equal("Cena nova", StudioScenes.NextName(new[] { "Cena 1" }));
        Assert.Equal("Cena nova 3", StudioScenes.NextName(new[] { "Cena nova", "cena nova 2" }));
        Assert.Null(StudioScenes.CleanName("   "));
        Assert.Equal("Live YouTube", StudioScenes.CleanName("  Live YouTube "));
        Assert.Equal(40, StudioScenes.CleanName(new string('a', 60))!.Length);

        var s = Settings();
        Assert.Equal("Câmera 1 · Webcam", StudioScenes.Title(new StudioSceneItem { Kind = StudioSceneItemKind.Camera, Camera = "Webcam" }, s));
        Assert.Equal("Câmera 2 · celular", StudioScenes.Title(new StudioSceneItem { Kind = StudioSceneItemKind.Camera, Camera = "http://192.168.0.15:8080/video" }, s));
        Assert.StartsWith("Câmera (não encontrada)", StudioScenes.Title(new StudioSceneItem { Kind = StudioSceneItemKind.Camera, Camera = "Outra" }, s));
    }

    [Fact]
    public void Remover_nunca_deixa_sem_cena_e_acerta_a_escolhida()
    {
        var s = new AppSettings();
        foreach (var n in new[] { "A", "B", "C" }) s.StudioScenes.Add(new StudioSceneSetting { Name = n });
        s.StudioSceneIndex = 2;
        Assert.True(StudioScenes.Remove(s, 0));
        Assert.Equal("C", s.StudioScenes[s.StudioSceneIndex].Name);
        Assert.True(StudioScenes.Remove(s, 1));
        Assert.Equal("B", s.StudioScenes[s.StudioSceneIndex].Name);
        Assert.False(StudioScenes.Remove(s, 0));
        Assert.Single(s.StudioScenes);
    }

    [Fact]
    public void Subir_e_descer()
    {
        var list = new List<string> { "A", "B", "C" };
        Assert.Equal(0, StudioScenes.Move(list, 1, -1));
        Assert.Equal(new[] { "B", "A", "C" }, list);
        Assert.Equal(0, StudioScenes.Move(list, 0, -1));
        Assert.Equal(2, StudioScenes.Move(list, 1, 1));
        Assert.Equal(new[] { "B", "C", "A" }, list);
    }

    [Fact]
    public void Cenas_sobrevivem_ao_salvar()
    {
        var s = Settings();
        StudioScenes.EnsureCurrent(s);
        s.StudioSceneStudioMode = true;
        string json = System.Text.Json.JsonSerializer.Serialize(s);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.True(back.StudioSceneStudioMode);
        Assert.Equal(s.StudioScenes[0].Items.Count, back.StudioScenes[0].Items.Count);
        Assert.Equal("Webcam", back.StudioScenes[0].Items[1].Camera);
    }
}
