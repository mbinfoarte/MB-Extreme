using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Qualidade da gravação vertical para redes sociais (Shorts, Reels, TikTok): destino, tamanho, quadros, codificador, taxa ou qualidade constante e o MP4 pronto para postar.</summary>
public sealed partial class StudioPage
{
    private static readonly int[] VerticalMbps = { 0, 8, 12, 16, 20, 25, 35, 50, 80 };

    private FrameworkElement BuildVerticalQuality()
    {
        var panel = new StackPanel();
        var head = new WrapPanel();
        head.Children.Add(new TextBlock { Text = "Qualidade da gravação vertical", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(Help("Vale para o vídeo vertical gravado (sozinho ou junto com o horizontal em \"Os dois\"). " +
            "Escolha para onde vai o vídeo e o programa acerta tamanho, quadros, codec e qualidade; depois dá para mudar item por item. " +
            "Com a live vertical ligada, o vertical segue os limites da plataforma da live (H.264 e taxa fixa)."));
        panel.Children.Add(head);

        void Custom() { _s.StudioVRecTarget = -1; _s.Save(); }

        // ---- destino (predefinições)
        var names = SocialVideo.All.Select(p => p.Name).Append("Personalizado").ToList();
        int target = _s.StudioVRecTarget is >= 0 and var t && t < SocialVideo.All.Count ? t : names.Count - 1;
        panel.Children.Add(Field("Para onde", Chips(names, target, i =>
        {
            if (i >= SocialVideo.All.Count) { Custom(); BuildVertical(); return; }
            var preset = SocialVideo.All[i];
            _s.StudioVRecTarget = i;
            _s.StudioVerticalFormat = preset.Format;
            _s.StudioVRecFps = preset.Fps;
            _s.StudioVRecMbps = preset.BitrateMbps;
            _s.StudioVRecQuality = preset.QualityLevel;
            _s.StudioVRecEncoder = SocialVideo.PickEncoder(_encoders, preset.Codec)?.FfmpegName ?? "";
            _s.Save();
            BuildVertical();
            UpdatePlan();
        }), 120));
        if (target < SocialVideo.All.Count) panel.Children.Add(Field("", Muted(SocialVideo.All[target].Note, 11), 120));

        // ---- quadros
        int[] fpsOptions = { 0, 30, 60 };
        panel.Children.Add(Field("Quadros por segundo", Chips(new[] { "Igual ao vídeo principal", "30", "60" }, Math.Max(0, Array.IndexOf(fpsOptions, _s.StudioVRecFps)), i =>
        {
            _s.StudioVRecFps = fpsOptions[i];
            Custom();
        }), 120));

        // ---- codificador (todos os que a placa tem: H.264, HEVC, AV1)
        if (_encoders.Count == 0)
        {
            panel.Children.Add(Field("Codificador", Muted("Detecte os codificadores da sua placa em Programa › Avançado para escolher H.264, HEVC ou AV1.", 11.5), 120));
        }
        else
        {
            var options = new List<string> { "Igual ao vídeo principal" };
            options.AddRange(_encoders.Select(e => e.Title));
            int current = _encoders.ToList().FindIndex(e => e.FfmpegName == _s.StudioVRecEncoder) + 1;
            var encoderChips = Chips(options, current, i =>
            {
                _s.StudioVRecEncoder = i == 0 ? "" : _encoders[i - 1].FfmpegName;
                Custom();
            });
            encoderChips.ToolTip = "H.264: aceito em toda rede social. HEVC: arquivo uns 40% menor com a mesma imagem (YouTube, Instagram e TikTok aceitam). AV1: o menor de todos, ótimo para o YouTube; só placas novas têm.";
            panel.Children.Add(Field("Codificador", encoderChips, 120));
        }

        // ---- qualidade: constante (a imagem manda) ou taxa fixa
        panel.Children.Add(Field("Qualidade", Chips(new[] { "Pela taxa (Mbps)", "Alta", "Muito alta", "Máxima" }, Math.Clamp(_s.StudioVRecQuality, 0, 3), i =>
        {
            _s.StudioVRecQuality = i;
            Custom();
            BuildVertical();
        }), 120));
        if (_s.StudioVRecQuality == 0)
        {
            panel.Children.Add(Field("Taxa de vídeo", Chips(VerticalMbps.Select(m => m == 0 ? "Automática" : m + " Mbps").ToList(), Math.Max(0, Array.IndexOf(VerticalMbps, _s.StudioVRecMbps)), i =>
            {
                _s.StudioVRecMbps = VerticalMbps[i];
                Custom();
            }), 120));
        }
        else
        {
            panel.Children.Add(Field("", Muted("Qualidade constante: o codificador usa o que precisar para manter a imagem (cenas paradas gastam pouco, cenas de ação gastam mais). É o melhor para gravar e postar depois.", 11), 120));
        }

        // ---- esforço
        panel.Children.Add(Field("Esforço", Chips(new[] { "Igual ao principal", "Rápido", "Equilibrado", "Qualidade" }, _s.StudioVRecEffort + 1, i =>
        {
            _s.StudioVRecEffort = i - 1;
            Custom();
        }), 120));

        // ---- arquivo pronto para postar
        var post = new CheckBox { Content = "Ao parar, criar um MP4 pronto para postar (\"-postar.mp4\", sem perder qualidade)", IsChecked = _s.StudioVRecPostMp4, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 6, 0, 0) };
        post.ToolTip = "Copia o vídeo e o áudio para um MP4 comum com o índice no começo: abre em qualquer celular e as redes começam a processar mais rápido. Leva poucos segundos e não recodifica.";
        post.Click += (_, _) => { _s.StudioVRecPostMp4 = post.IsChecked == true; _s.Save(); };
        panel.Children.Add(post);

        var card = CardOf(panel);
        card.Padding = new Thickness(12, 10, 12, 10);
        card.Margin = new Thickness(0, 10, 0, 6);
        card.CornerRadius = new CornerRadius(12);
        return card;
    }
}
