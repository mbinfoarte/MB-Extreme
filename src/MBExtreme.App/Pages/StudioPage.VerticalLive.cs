using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Formato do estúdio (Horizontal, Vertical ou Os dois) e a live vertical: só na vertical (Instagram ou TikTok como plataforma principal)
/// ou ao mesmo tempo que a horizontal (por exemplo YouTube na horizontal e Instagram na vertical).
/// </summary>
public sealed partial class StudioPage
{
    private readonly Border _layoutChipsHost = new();
    private readonly TextBox _vliveUrl = new() { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly PasswordBox _vliveKey = new() { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
    private bool _vliveLoaded;

    /// <summary>0 = horizontal, 1 = só vertical, 2 = os dois (horizontal + cópia vertical).</summary>
    private int PreviewLayout => _s.StudioVerticalOn ? 1 : _s.StudioVerticalCopy ? 2 : 0;

    private bool VerticalLiveWanted => _s.StudioVLiveOn && _outStream;

    private string VerticalLiveKey() => _vliveKey.Password.Trim().Length > 0 ? _vliveKey.Password.Trim() : SecretStore.Unprotect(_s.StudioVLiveKeyProtected) ?? "";

    /// <summary>Os botões Horizontal / Vertical / Os dois da prévia: escolhem o formato do que é gravado e transmitido.</summary>
    private void RefreshLayoutChips()
    {
        var chips = Chips(new[] { "Horizontal", "Vertical", "Os dois" }, PreviewLayout, i =>
        {
            _s.StudioVerticalOn = i == 1;
            _s.StudioVerticalCopy = i == 2;
            _s.Save();
            BuildVertical();
            UpdatePlan();
            RefreshStudioLayout();
            UpdateVerticalLive();
            SetStatus(i switch
            {
                1 => "Formato vertical: grava e transmite em pé (Shorts, Reels, TikTok). A montagem fica em Gravar › Vertical.",
                2 => "Os dois: o vídeo normal e, junto, o vertical (arquivo próprio e, se ligada, a live vertical em Transmitir › Conexão).",
                _ => "Formato horizontal (normal).",
            });
        });
        chips.ToolTip = "Horizontal: vídeo normal. Vertical: tudo em pé, para Instagram, TikTok ou Shorts. Os dois: o normal e o vertical ao mesmo tempo (dá para fazer live nos dois).";
        _layoutChipsHost.Child = chips;
    }

    private FrameworkElement BuildVerticalLiveCard()
    {
        foreach (var box in new FrameworkElement[] { _vliveUrl, _vliveKey }) Detach(box);
        if (!_vliveLoaded)
        {
            _vliveLoaded = true;
            _vliveUrl.Text = _s.StudioVLiveUrl;
            if (SecretStore.Unprotect(_s.StudioVLiveKeyProtected) is { } key) _vliveKey.Password = key;
            _vliveUrl.TextChanged += (_, _) => _s.StudioVLiveUrl = _vliveUrl.Text.Trim();
            _vliveKey.PasswordChanged += (_, _) =>
            {
                try { _s.StudioVLiveKeyProtected = _vliveKey.Password.Length > 0 ? SecretStore.Protect(_vliveKey.Password.Trim()) : null; }
                catch (CryptographicException) { SetStatus("Não foi possível guardar a chave com segurança; ela vale só até fechar o programa.", "WarnBrush"); }
            };
        }

        var platform = (VerticalPlatform)Math.Clamp(_s.StudioVLivePlatform, 0, 3);
        var spec = VerticalLive.For(platform);
        var panel = new StackPanel();
        var head = new WrapPanel();
        head.Children.Add(new TextBlock { Text = "Live vertical ao mesmo tempo", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(Help("Transmite a horizontal na plataforma de cima (ex.: YouTube) e, ao mesmo tempo, a vertical no Instagram, TikTok ou num segundo canal do YouTube. " +
            "O vertical usa a montagem de Gravar › Vertical. Pesa mais: são dois vídeos codificados e duas lives saindo pela sua internet (some as duas taxas no teste de internet). " +
            "Para fazer live só na vertical, escolha Instagram (vertical) ou TikTok (vertical) na Plataforma acima."));
        panel.Children.Add(head);

        var on = new CheckBox { Content = "Ligar a live vertical (precisa de \"Os dois\" na prévia)", IsChecked = _s.StudioVLiveOn, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 2, 0, 6) };
        on.Click += (_, _) =>
        {
            _s.StudioVLiveOn = on.IsChecked == true;
            if (_s.StudioVLiveOn)
            {
                _s.StudioVerticalOn = false;   // a live vertical sai junto com a horizontal
                _s.StudioVerticalCopy = true;
                BuildVertical();
                RefreshLayoutChips();
                RefreshStudioLayout();
                UpdateVerticalLive();
            }
            _s.Save();
            BuildStream();
        };
        panel.Children.Add(on);

        if (_s.StudioVLiveOn)
        {
            panel.Children.Add(Field("Onde", Chips(VerticalLive.All.Select(p => p.Name).ToList(), (int)platform, i =>
            {
                _s.StudioVLivePlatform = i;
                if (string.IsNullOrWhiteSpace(_vliveUrl.Text) || VerticalLive.All.Any(p => p.DefaultUrl == _vliveUrl.Text.Trim()))
                    _vliveUrl.Text = VerticalLive.All[i].DefaultUrl;
                _s.Save();
                BuildStream();
            }), 120));
            if (string.IsNullOrWhiteSpace(_vliveUrl.Text)) _vliveUrl.Text = spec.DefaultUrl;
            panel.Children.Add(Field("Servidor (URL)", _vliveUrl, 120));
            panel.Children.Add(Field("Chave", _vliveKey, 120));
            panel.Children.Add(Field("", Muted(spec.HowToKey, 11), 120));
            panel.Children.Add(Field("", Muted($"Sai em {spec.Width} de largura, {spec.Fps} quadros por segundo, {spec.VideoKbps / 1000.0:0.#} Mbps (o que a {spec.Name} aceita). A chave fica protegida pelo Windows.", 11), 120));
        }

        var card = CardOf(panel);
        card.Padding = new Thickness(12, 10, 12, 10);
        card.Margin = new Thickness(0, 8, 0, 8);
        card.CornerRadius = new CornerRadius(12);
        return card;
    }

    /// <summary>Instagram ou TikTok como plataforma principal: live só na vertical.</summary>
    private void ChooseVerticalPlatform(int preset)
    {
        _s.StudioStreamPreset = preset;
        _s.StudioStreamService = (int)StreamService.Custom;
        var spec = VerticalLive.For(preset == 1 ? VerticalPlatform.Instagram : VerticalPlatform.TikTok);
        if (spec.DefaultUrl.Length > 0) _customUrl.Text = spec.DefaultUrl;
        else if (_customUrl.Text.Contains("instagram", StringComparison.OrdinalIgnoreCase)) _customUrl.Text = "";
        _s.StudioVerticalOn = true;
        _s.StudioVerticalCopy = false;
        _s.StudioFps = Math.Min(_s.StudioFps, spec.Fps);   // Instagram e TikTok recebem até 30 quadros por segundo
        _s.Save();
        BuildVideo();
        BuildVertical();
        RefreshLayoutChips();
        RefreshStudioLayout();
        UpdateVerticalLive();
        BuildStream();
        SetStatus($"Live só na vertical no {spec.Name}: o vídeo principal passou para Vertical. {spec.HowToKey} A chave vai em Programa › Contas e chaves.", "OkBrush");
    }
}
