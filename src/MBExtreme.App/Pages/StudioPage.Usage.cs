using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Quanto o estúdio está pesando agora: processador, placa de vídeo (3D e o chip de vídeo que codifica) e memória.</summary>
public sealed partial class StudioPage
{
    private ResourceMonitor? _usage;
    private bool _usageBusy;
    private readonly TextBlock _usageCpu = UsageText();
    private readonly TextBlock _usageGpu = UsageText();
    private readonly TextBlock _usageEnc = UsageText();
    private readonly TextBlock _usageRam = UsageText();

    private static TextBlock UsageText() => new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Text = "—", VerticalAlignment = VerticalAlignment.Center };

    private FrameworkElement BuildUsageBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(UsageChip("CPU", _usageCpu, "Processador usado pelo MB Extreme e pelo motor de vídeo (em % do PC inteiro). Entre parênteses: o PC todo, com o jogo."));
        row.Children.Add(UsageChip("GPU", _usageGpu, "Placa de vídeo (3D) usada pelo estúdio. Entre parênteses: a placa toda, com o jogo."));
        row.Children.Add(UsageChip("Codificador", _usageEnc, "O chip de vídeo da placa (NVENC, AMD VCN ou Quick Sync) que comprime a gravação e a live. Ele é separado do 3D: usar ele quase não tira FPS do jogo."));
        row.Children.Add(UsageChip("RAM", _usageRam, "Memória usada pelo MB Extreme e pelo motor de vídeo. Entre parênteses: o PC todo."));
        return row;
    }

    private Border UsageChip(string label, TextBlock value, string tip)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = label + " ", FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(value);
        return new Border
        {
            Child = panel, ToolTip = tip, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0),
            Background = Frozen(Color.FromRgb(0x1C, 0x1C, 0x20)), BorderBrush = Frozen(Color.FromRgb(0x2E, 0x2E, 0x34)), BorderThickness = new Thickness(1),
        };
    }

    /// <summary>Lê os contadores fora da tela (a cada 2 s) e mostra.</summary>
    private async Task SampleUsageAsync()
    {
        if (_usageBusy || !IsLoaded) return;
        _usageBusy = true;
        try
        {
            var pids = new List<int> { Environment.ProcessId };
            if (_studio.Session.ProcessId is { } ffmpeg) pids.Add(ffmpeg);
            var snapshot = await Task.Run(() => (_usage ??= new ResourceMonitor()).Sample(pids));
            ShowUsage(snapshot);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        finally { _usageBusy = false; }
    }

    private void ShowUsage(UsageSnapshot u)
    {
        _usageCpu.Text = $"{u.AppCpu:0}% ({u.SystemCpu:0}%)";
        _usageGpu.Text = $"{u.AppGpu:0}% ({u.SystemGpu:0}%)";
        _usageEnc.Text = _studio.IsRunning ? $"{u.AppEncoder:0}%" : "parado";
        _usageRam.Text = u.SystemRamTotal > 0 ? $"{UsageMath.Bytes(u.AppRamBytes)} ({u.SystemRamUsed * 100 / u.SystemRamTotal}%)" : UsageMath.Bytes(u.AppRamBytes);
        _usageCpu.Foreground = u.SystemCpu >= 90 ? Brush("WarnBrush") : Brush("TextBrush");
        _usageGpu.Foreground = u.SystemGpu >= 95 ? Brush("WarnBrush") : Brush("TextBrush");
    }
}
