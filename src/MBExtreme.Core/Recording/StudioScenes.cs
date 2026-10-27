using MBExtreme.Core.Settings;

namespace MBExtreme.Core.Recording;

/// <summary>
/// Cenas do estúdio: cada cena guarda as suas fontes (tela, câmeras, Pix, abertura, faixa e logo).
/// Trocar de cena liga no vídeo só o que a cena tem e não está oculto; as configurações de cada fonte
/// (canto, tamanho, textos) continuam as mesmas para todas as cenas.
/// </summary>
public static class StudioScenes
{
    public const int MaxScenes = 20;
    public const string NewSceneName = "Cena nova";

    /// <summary>Uma cena com o que está ligado agora (para quem já usava o estúdio antes das cenas).</summary>
    public static StudioSceneSetting FromCurrent(AppSettings s, string name)
    {
        var scene = new StudioSceneSetting { Name = name };
        scene.Items.Add(new StudioSceneItem { Kind = StudioSceneItemKind.Capture, Locked = true });
        foreach (var cam in s.StudioCameras.Where(c => !string.IsNullOrEmpty(c.Name)))
            scene.Items.Add(new StudioSceneItem { Kind = StudioSceneItemKind.Camera, Camera = cam.Name, Hidden = !cam.On });
        if (s.StudioLowerOn) scene.Items.Add(new StudioSceneItem { Kind = StudioSceneItemKind.LowerThird });
        if (s.StudioLogoOn) scene.Items.Add(new StudioSceneItem { Kind = StudioSceneItemKind.Logo });
        if (s.StudioIntroOn) scene.Items.Add(new StudioSceneItem { Kind = StudioSceneItemKind.Intro });
        if (s.StudioPixOn) scene.Items.Add(new StudioSceneItem { Kind = StudioSceneItemKind.Pix });
        return scene;
    }

    /// <summary>Garante pelo menos uma cena e um índice válido.</summary>
    public static StudioSceneSetting EnsureCurrent(AppSettings s)
    {
        if (s.StudioScenes.Count == 0) s.StudioScenes.Add(FromCurrent(s, "Cena 1"));
        s.StudioSceneIndex = Math.Clamp(s.StudioSceneIndex, 0, s.StudioScenes.Count - 1);
        return s.StudioScenes[s.StudioSceneIndex];
    }

    /// <summary>A fonte aparece no vídeo: está na cena e não está oculta.</summary>
    public static bool Shows(StudioSceneSetting scene, StudioSceneItemKind kind, string? camera = null) =>
        scene.Items.Any(i => i.Kind == kind && !i.Hidden && (kind != StudioSceneItemKind.Camera || string.Equals(i.Camera, camera, StringComparison.Ordinal)));

    /// <summary>Liga e desliga as câmeras e as camadas conforme a cena.</summary>
    public static void Apply(StudioSceneSetting scene, AppSettings s)
    {
        foreach (var cam in s.StudioCameras)
            cam.On = cam.Name is not null && Shows(scene, StudioSceneItemKind.Camera, cam.Name);
        s.StudioPixOn = Shows(scene, StudioSceneItemKind.Pix);
        s.StudioIntroOn = Shows(scene, StudioSceneItemKind.Intro);
        s.StudioLowerOn = Shows(scene, StudioSceneItemKind.LowerThird);
        s.StudioLogoOn = Shows(scene, StudioSceneItemKind.Logo);
    }

    /// <summary>
    /// O inverso de <see cref="Apply"/>: quando a pessoa liga ou desliga algo direto nas abas (uma câmera, o Pix, a faixa),
    /// a cena atual passa a lembrar disso. O que foi ligado e não estava na cena entra nela.
    /// </summary>
    public static void Sync(StudioSceneSetting scene, AppSettings s)
    {
        foreach (var cam in s.StudioCameras.Where(c => !string.IsNullOrEmpty(c.Name)))
            SyncOne(scene, StudioSceneItemKind.Camera, cam.Name, cam.On);
        SyncOne(scene, StudioSceneItemKind.Pix, null, s.StudioPixOn);
        SyncOne(scene, StudioSceneItemKind.Intro, null, s.StudioIntroOn);
        SyncOne(scene, StudioSceneItemKind.LowerThird, null, s.StudioLowerOn);
        SyncOne(scene, StudioSceneItemKind.Logo, null, s.StudioLogoOn);
    }

    private static void SyncOne(StudioSceneSetting scene, StudioSceneItemKind kind, string? camera, bool on)
    {
        var item = scene.Items.FirstOrDefault(i => i.Kind == kind && (kind != StudioSceneItemKind.Camera || string.Equals(i.Camera, camera, StringComparison.Ordinal)));
        if (item is null)
        {
            if (on) scene.Items.Add(new StudioSceneItem { Kind = kind, Camera = camera });
            return;
        }
        item.Hidden = !on;
    }

    /// <summary>Fontes que ainda podem entrar na cena (cada uma só uma vez).</summary>
    public static IReadOnlyList<StudioSceneItem> Addable(StudioSceneSetting scene, AppSettings s)
    {
        var list = new List<StudioSceneItem>();
        if (scene.Items.All(i => i.Kind != StudioSceneItemKind.Capture)) list.Add(new StudioSceneItem { Kind = StudioSceneItemKind.Capture, Locked = true });
        foreach (var cam in s.StudioCameras.Where(c => !string.IsNullOrEmpty(c.Name)))
            if (!scene.Items.Any(i => i.Kind == StudioSceneItemKind.Camera && i.Camera == cam.Name))
                list.Add(new StudioSceneItem { Kind = StudioSceneItemKind.Camera, Camera = cam.Name });
        foreach (var kind in new[] { StudioSceneItemKind.LowerThird, StudioSceneItemKind.Logo, StudioSceneItemKind.Intro, StudioSceneItemKind.Pix })
            if (scene.Items.All(i => i.Kind != kind)) list.Add(new StudioSceneItem { Kind = kind });
        return list;
    }

    /// <summary>Nome da fonte como aparece na lista.</summary>
    public static string Title(StudioSceneItem item, AppSettings s)
    {
        switch (item.Kind)
        {
            case StudioSceneItemKind.Capture:
                return item.CaptureMode switch
                {
                    1 => $"Tela {item.Display + 1}",
                    2 or 3 => "Janela: " + (string.IsNullOrWhiteSpace(item.WindowTitle) ? item.WindowProcess : item.WindowTitle),
                    _ => "Tela ou janela (a de baixo da prévia)",
                };
            case StudioSceneItemKind.Camera:
                int index = s.StudioCameras.FindIndex(c => c.Name == item.Camera);
                string name = CameraSource.IsStream(item.Camera) ? "celular" : item.Camera ?? "";
                return index < 0 ? $"Câmera (não encontrada): {name}" : $"Câmera {index + 1} · {name}";
            case StudioSceneItemKind.Pix: return "Pix (QR na tela)";
            case StudioSceneItemKind.Intro: return "Abertura";
            case StudioSceneItemKind.LowerThird: return "Faixa com nome";
            default: return "Logo";
        }
    }

    /// <summary>
    /// Acha a janela de uma cena entre as abertas: pelo programa e, se ele tiver várias, pela que tem o mesmo título (ou a maior).
    /// </summary>
    public static WindowInfo? FindWindow(StudioSceneItem item, IReadOnlyList<WindowInfo> windows)
    {
        if (string.IsNullOrWhiteSpace(item.WindowProcess)) return null;
        var same = windows.Where(w => string.Equals(w.ProcessName, item.WindowProcess, StringComparison.OrdinalIgnoreCase)).ToList();
        if (same.Count == 0) return null;
        return same.FirstOrDefault(w => string.Equals(w.Title, item.WindowTitle, StringComparison.Ordinal))
               ?? same.OrderByDescending(w => (long)w.Bounds.Width * w.Bounds.Height).First();
    }

    /// <summary>Cenas que podem ficar montadas juntas (troca sem reiniciar).</summary>
    public static bool CanGoLive(AppSettings s, bool vertical) =>
        s.StudioScenesLive && !vertical && s.StudioScenes.Count > 1 && s.StudioScenes.Count <= StudioCommand.MaxLiveScenes;

    /// <summary>"Cena nova", "Cena nova 2", ... sem repetir nome.</summary>
    public static string NextName(IEnumerable<string> existing)
    {
        var names = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(NewSceneName)) return NewSceneName;
        for (int n = 2; ; n++)
            if (!names.Contains($"{NewSceneName} {n}")) return $"{NewSceneName} {n}";
    }

    /// <summary>Nome digitado pela pessoa: sem espaços nas pontas, até 40 letras; vazio vira null.</summary>
    public static string? CleanName(string? text)
    {
        string name = (text ?? "").Trim();
        if (name.Length == 0) return null;
        return name.Length > 40 ? name[..40].TrimEnd() : name;
    }

    /// <summary>Troca um item de lugar com o vizinho (para subir ou descer). Devolve o novo índice.</summary>
    public static int Move<T>(List<T> list, int index, int delta)
    {
        int target = index + delta;
        if (index < 0 || index >= list.Count || target < 0 || target >= list.Count) return index;
        (list[index], list[target]) = (list[target], list[index]);
        return target;
    }

    /// <summary>Tira uma cena e acerta o índice da cena escolhida. Nunca deixa a lista vazia.</summary>
    public static bool Remove(AppSettings s, int index)
    {
        if (s.StudioScenes.Count <= 1 || index < 0 || index >= s.StudioScenes.Count) return false;
        s.StudioScenes.RemoveAt(index);
        if (s.StudioSceneIndex >= index) s.StudioSceneIndex = Math.Max(0, s.StudioSceneIndex - 1);
        s.StudioSceneIndex = Math.Clamp(s.StudioSceneIndex, 0, s.StudioScenes.Count - 1);
        return true;
    }
}
