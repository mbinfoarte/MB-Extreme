using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Rgb;

namespace MBExtreme.UI.Pages;

/// <summary>Iluminação RGB e ARGB (placa-mãe, memória, placa de vídeo, coolers, fitas e ventoinhas) pelo OpenRGB.</summary>
public sealed class RgbPage : ToolPageBase
{
    private static readonly (string Name, string Hex)[] Swatches =
    {
        ("Branco", "FFFFFF"), ("Vermelho", "FF0000"), ("Laranja", "FF6A00"), ("Amarelo", "FFC400"), ("Verde", "00E060"),
        ("Ciano", "00D8FF"), ("Azul", "1A4DFF"), ("Roxo", "8A2BE2"), ("Rosa", "FF2DA0"),
    };

    private readonly StackPanel _devices = new();
    private readonly TextBox _hex = new() { Text = "FFC400", Width = 100, MaxLength = 6, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Border _preview = new() { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 0) };
    private readonly DispatcherTimer _tempTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly Dictionary<int, string> _modeChoice = new();
    private IReadOnlyList<RgbDevice> _list = Array.Empty<RgbDevice>();
    private int _brightness = 100;
    private string? _lastTempColor;
    private bool _busy, _tempSync;
    private Button? _tempButton;

    public RgbPage() : base("Iluminação RGB",
        "Controle as cores dos LEDs RGB e ARGB do PC: placa-mãe, memória, placa de vídeo, coolers, bomba de líquido e ventoinhas. Usa o OpenRGB (gratuito, de código aberto), que conversa com centenas de dispositivos. O MB Extreme não inclui o OpenRGB: baixe do site oficial.")
    {
        AddButton("Procurar dispositivos", async (_, _) => await LoadAsync());
        AddButton("Escolher OpenRGB.exe...", (_, _) => Pick(), false);

        Body.Children.Add(Heading("Cor para todos os dispositivos"));
        var swatchRow = new WrapPanel();
        foreach (var (name, hex) in Swatches)
        {
            var color = (Color)ColorConverter.ConvertFromString("#" + hex);
            var b = new Button
            {
                Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 8), ToolTip = name, Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(0),
                Content = new Border { Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(6), Width = 22, Height = 22 },
            };
            b.Click += (_, _) => { _hex.Text = hex; UpdatePreview(); };
            swatchRow.Children.Add(b);
        }
        Body.Children.Add(swatchRow);

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        line.Children.Add(_preview);
        line.Children.Add(Muted("#", 14));
        line.Children.Add(_hex);
        var applyAll = SmallButton("Aplicar a todos", async (_, _) => await ApplyAsync(_list, _hex.Text, null), true);
        applyAll.Margin = new Thickness(0, 0, 8, 0);
        line.Children.Add(applyAll);
        line.Children.Add(SmallButton("Apagar os LEDs", async (_, _) => await ApplyAsync(_list, "000000", null)));
        _tempButton = SmallButton("Cor pela temperatura: desligada", (_, _) => ToggleTemperature());
        line.Children.Add(_tempButton);
        Body.Children.Add(line);
        _hex.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        Body.Children.Add(Muted("Brilho:", 11));
        var chips = new WrapPanel();
        foreach (int percent in new[] { 25, 50, 75, 100 })
        {
            int p = percent;
            var b = SmallButton(p + "%", (_, _) => { _brightness = p; PaintBrightness(chips); });
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Padding = new Thickness(11, 4, 11, 4);
            b.FontSize = 12;
            chips.Children.Add(b);
        }
        PaintBrightness(chips);
        Body.Children.Add(chips);

        Body.Children.Add(Heading("Dispositivos encontrados"));
        Body.Children.Add(_devices);
        Body.Children.Add(Muted("Se um dispositivo não aparece, feche o programa do fabricante (iCUE, Armoury Crate, Mystic Light, Fusion, CAM) e procure de novo: dois programas não podem controlar o mesmo LED. Ligar o OpenRGB uma vez como administrador ajuda a achar memória e placa-mãe.", 11));

        _tempTimer.Tick += async (_, _) => await TemperatureTickAsync();
        Loaded += async (_, _) => await LoadAsync();
        Unloaded += (_, _) => _tempTimer.Stop();
    }

    private void PaintBrightness(WrapPanel chips)
    {
        int i = 0;
        foreach (Button b in chips.Children)
        {
            b.Style = (Style)FindResource(new[] { 25, 50, 75, 100 }[i++] == _brightness ? "PrimaryButton" : "GhostButton");
            b.Padding = new Thickness(11, 4, 11, 4);
            b.FontSize = 12;
        }
    }

    private void UpdatePreview()
    {
        _preview.Background = OpenRgb.IsValidHex(_hex.Text) ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + _hex.Text)) : Brushes.DimGray;
    }

    private string? Exe() => OpenRgb.Locate(AppServices.Settings.OpenRgbPath);

    private void Pick()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "OpenRGB (OpenRGB.exe)|OpenRGB.exe|Programas (*.exe)|*.exe", Title = "Escolha o OpenRGB.exe" };
        if (dialog.ShowDialog() != true) return;
        AppServices.Settings.OpenRgbPath = dialog.FileName;
        AppServices.Settings.Save();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _devices.Children.Clear();
        string? exe = Exe();
        if (exe is null)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "OpenRGB não encontrado", FontSize = 14, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(Muted("Baixe o OpenRGB (gratuito) no site oficial, extraia ou instale, e clique em \"Procurar dispositivos\". Se ele estiver numa pasta diferente, use \"Escolher OpenRGB.exe\"."));
            var open = SmallButton("Abrir o site do OpenRGB", (_, _) => Open(OpenRgb.OfficialPage), true);
            open.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(open);
            _devices.Children.Add(CardOf(panel));
            return;
        }

        SetStatus("Procurando dispositivos (pode levar alguns segundos)...");
        try
        {
            _list = await Task.Run(() => OpenRgb.List(exe));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _devices.Children.Add(Row("Não foi possível listar", ex.Message, null, "BadBrush"));
            SetStatus("Falha ao procurar.", "BadBrush");
            return;
        }

        SetStatus(_list.Count == 0 ? "Nenhum dispositivo RGB encontrado." : $"{_list.Count} dispositivo(s) encontrado(s).", _list.Count == 0 ? "WarnBrush" : "OkBrush");
        foreach (var device in _list) _devices.Children.Add(BuildDevice(device));
    }

    private UIElement BuildDevice(RgbDevice device)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = device.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Muted($"{device.Type} · modo atual: {device.ActiveMode ?? "—"}", 11));

        if (device.Modes.Count > 0)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var buttons = new List<Button>();
            _modeChoice.TryAdd(device.Index, OpenRgb.StaticMode(device) ?? device.Modes[0]);
            void Paint()
            {
                foreach (var b in buttons)
                {
                    b.Style = (Style)FindResource((string)b.Content == _modeChoice[device.Index] ? "PrimaryButton" : "GhostButton");
                    b.Padding = new Thickness(10, 3, 10, 3);
                    b.FontSize = 11;
                }
            }
            foreach (string mode in device.Modes)
            {
                var b = new Button { Content = mode, Margin = new Thickness(0, 0, 6, 6) };
                b.Click += (_, _) => { _modeChoice[device.Index] = mode; Paint(); };
                buttons.Add(b);
                wrap.Children.Add(b);
            }
            Paint();
            panel.Children.Add(wrap);
        }

        var apply = SmallButton("Aplicar modo e cor neste dispositivo", async (_, _) => await ApplyAsync(new[] { device }, _hex.Text, _modeChoice.GetValueOrDefault(device.Index)), true);
        apply.Margin = new Thickness(0, 2, 0, 0);
        apply.Padding = new Thickness(11, 4, 11, 4);
        apply.FontSize = 12;
        panel.Children.Add(apply);
        return CardOf(panel);
    }

    private async Task ApplyAsync(IReadOnlyList<RgbDevice> devices, string hex, string? mode)
    {
        string? exe = Exe();
        if (exe is null) { SetStatus("OpenRGB não encontrado.", "WarnBrush"); return; }
        if (devices.Count == 0) { SetStatus("Nenhum dispositivo para aplicar.", "WarnBrush"); return; }
        if (!OpenRgb.IsValidHex(hex)) { SetStatus("Cor inválida: use 6 dígitos (ex.: FFC400).", "WarnBrush"); return; }
        if (_busy) return;
        _busy = true;
        try
        {
            SetStatus("Aplicando...");
            string scaled = OpenRgb.WithBrightness(hex, _brightness);
            var args = OpenRgb.ColorArguments(devices.Select(d => (d, scaled, mode is not null && d.Modes.Contains(mode, StringComparer.OrdinalIgnoreCase) ? mode : null)));
            var (code, output) = await Task.Run(() => OpenRgb.Run(exe, args));
            SetStatus(code == 0 ? "Cor aplicada." : "O OpenRGB respondeu com erro: " + output, code == 0 ? "OkBrush" : "BadBrush");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível aplicar: " + ex.Message, "BadBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- cor pela temperatura do processador

    private void ToggleTemperature()
    {
        _tempSync = !_tempSync;
        _lastTempColor = null;
        if (_tempSync) _tempTimer.Start();
        else _tempTimer.Stop();
        if (_tempButton is not null) _tempButton.Content = _tempSync ? "Cor pela temperatura: ligada" : "Cor pela temperatura: desligada";
        SetStatus(_tempSync ? "A cor dos LEDs acompanha a temperatura do processador (azul = frio, vermelho = quente)." : "Cor pela temperatura desligada.", "OkBrush");
    }

    private async Task TemperatureTickAsync()
    {
        if (_busy || _list.Count == 0) return;
        try
        {
            var sensors = await AppServices.Sensors;
            var snapshot = await Task.Run(sensors.Read);
            if (snapshot.CpuTemperature is not { } temp) return;
            string hex = OpenRgb.TemperatureColor(temp);
            if (hex == _lastTempColor) return;
            _lastTempColor = hex;
            await ApplyAsync(_list, hex, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException) { }
    }
}
