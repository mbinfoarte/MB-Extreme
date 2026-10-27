using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Pages;

/// <summary>Monta, a partir das cenas guardadas, o que o FFmpeg precisa: a tela ou janela de cada cena e, com várias cenas, todas juntas para trocar sem reiniciar.</summary>
public sealed partial class StudioPage
{
    /// <summary>Uma cena antes de virar índices: a captura (índice em Captures), as câmeras (índices em CameraSettings), o Pix e as camadas pelo tipo.</summary>
    private sealed record SceneDraft(string Name, int Capture, List<int> Cameras, bool Pix, List<OverlayKind> Layers);

    private sealed record LivePlan(List<CaptureSource> Captures, List<SceneDraft> Scenes, List<StudioCameraSetting> CameraSettings, bool Pix, bool Intro, bool Lower, bool Logo, int Active)
    {
        /// <summary>As cenas com as camadas trocadas pelo índice delas na lista final (entrada, faixa, logo).</summary>
        public List<SceneLayout> Layouts(IReadOnlyList<OverlayLayer> layers) => Scenes
            .Select(s => new SceneLayout(s.Name, s.Capture, s.Cameras, s.Pix,
                s.Layers.Select(kind => layers.ToList().FindIndex(l => l.Kind == kind)).Where(i => i >= 0).ToList()))
            .ToList();
    }

    private (DateTime At, IReadOnlyList<WindowInfo> List) _windowCache = (DateTime.MinValue, Array.Empty<WindowInfo>());

    /// <summary>Janelas abertas, lidas no máximo uma vez por segundo (a prévia pergunta várias vezes por segundo).</summary>
    private IReadOnlyList<WindowInfo> OpenWindows()
    {
        if (DateTime.UtcNow - _windowCache.At > TimeSpan.FromSeconds(1)) _windowCache = (DateTime.UtcNow, CaptureCatalog.Windows());
        return _windowCache.List;
    }

    private static StudioSceneItem? SceneCaptureItem(StudioSceneSetting scene) => scene.Items.FirstOrDefault(i => i.Kind == StudioSceneItemKind.Capture);

    /// <summary>A captura da cena: a escolha de baixo da prévia, uma tela ou uma janela (achada pelo programa).</summary>
    private CaptureSource? ResolveCapture(StudioSceneItem? item, CaptureSource fallback, IReadOnlyList<DisplayInfo> displays, out string? problem, string? sceneName = null)
    {
        problem = null;
        string where = sceneName is null ? "" : $" (cena \"{sceneName}\")";
        switch (item?.CaptureMode ?? 0)
        {
            case 1:
                if (item!.Display < 0 || item.Display >= displays.Count) { problem = $"A Tela {item.Display + 1}{where} não está ligada a este PC agora."; return null; }
                return CaptureRules.ForDisplay(displays[item.Display]);
            case 2:
            case 3:
                var window = StudioScenes.FindWindow(item!, OpenWindows());
                if (window is null) { problem = $"A janela de \"{item!.WindowProcess}\"{where} não está aberta. Abra o programa ou troque a captura da cena."; return null; }
                var source = CaptureRules.ForWindow(window, displays, gdi: item!.CaptureMode == 3);
                if (source is null) problem = $"A janela de \"{item.WindowProcess}\"{where} está fora da área visível das telas.";
                return source;
            default:
                return fallback;
        }
    }

    /// <summary>Todas as cenas juntas: capturas sem repetir, câmeras usadas em alguma cena (até o limite), Pix e camadas.</summary>
    private (LivePlan? Plan, string? Problem) PlanLiveScenes(CaptureSource fallback, IReadOnlyList<DisplayInfo> displays)
    {
        var captures = new List<CaptureSource>();
        var cameras = new List<StudioCameraSetting>();
        var drafts = new List<SceneDraft>();
        foreach (var scene in _s.StudioScenes)
        {
            var capture = ResolveCapture(SceneCaptureItem(scene), fallback, displays, out string? problem, scene.Name);
            if (capture is null) return (null, problem);
            int captureIndex = captures.IndexOf(capture);
            if (captureIndex < 0) { captures.Add(capture); captureIndex = captures.Count - 1; }

            var cams = new List<int>();
            foreach (var item in scene.Items.Where(i => i.Kind == StudioSceneItemKind.Camera && !i.Hidden))
            {
                var setting = _s.StudioCameras.FirstOrDefault(c => c.Name == item.Camera);
                if (setting is null) continue;   // câmera tirada da lista: a cena segue sem ela
                int camIndex = cameras.IndexOf(setting);
                if (camIndex < 0)
                {
                    if (cameras.Count >= StudioCommand.MaxCameras)
                        return (null, $"As cenas usam mais de {StudioCommand.MaxCameras} câmeras diferentes. Para trocar de cena sem reiniciar, use até {StudioCommand.MaxCameras} no total (ou desligue a troca sem reiniciar em Programa › Avançado).");
                    cameras.Add(setting);
                    camIndex = cameras.Count - 1;
                }
                if (!cams.Contains(camIndex)) cams.Add(camIndex);
            }

            bool Shows(StudioSceneItemKind kind) => StudioScenes.Shows(scene, kind);
            var layers = new List<OverlayKind>();
            if (Shows(StudioSceneItemKind.Intro)) layers.Add(OverlayKind.Intro);
            if (Shows(StudioSceneItemKind.LowerThird)) layers.Add(OverlayKind.LowerThird);
            if (Shows(StudioSceneItemKind.Logo)) layers.Add(OverlayKind.Logo);
            drafts.Add(new SceneDraft(scene.Name, captureIndex, cams, Shows(StudioSceneItemKind.Pix), layers));
        }

        // a cena no ar: a que já está no ar (se o estúdio está ligado) ou a escolhida
        int active = _studio.IsRunning && _airScene is not null && _s.StudioScenes.IndexOf(_airScene) is var air and >= 0 ? air : _s.StudioSceneIndex;
        var plan = new LivePlan(captures, drafts, cameras,
            drafts.Any(d => d.Pix), drafts.Any(d => d.Layers.Contains(OverlayKind.Intro)), drafts.Any(d => d.Layers.Contains(OverlayKind.LowerThird)), drafts.Any(d => d.Layers.Contains(OverlayKind.Logo)),
            Math.Clamp(active, 0, drafts.Count - 1));
        return (plan, null);
    }

    /// <summary>O que a prévia mostra quando a cena escolhida tem a sua própria tela ou janela; null = a escolha de baixo da prévia.</summary>
    private (PixelRect Area, IntPtr? Gdi, string? Problem)? SceneGrabTarget()
    {
        var item = SceneCaptureItem(CurrentScene);
        if (item is null || item.CaptureMode == 0) return null;
        if (item.CaptureMode == 1)
        {
            var displays = _displayList;
            if (item.Display < 0 || item.Display >= displays.Count) return (new PixelRect(0, 0, 0, 0), null, $"A Tela {item.Display + 1} desta cena não está ligada agora.");
            return (displays[item.Display].Bounds, null, null);
        }

        var window = StudioScenes.FindWindow(item, OpenWindows());
        if (window is null) return (new PixelRect(0, 0, 0, 0), null, $"A janela de \"{item.WindowProcess}\" desta cena não está aberta.");
        return (window.Bounds, item.CaptureMode == 3 ? window.Handle : null, null);
    }
}
