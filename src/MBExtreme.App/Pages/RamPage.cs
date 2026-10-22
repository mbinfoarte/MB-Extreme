using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Memory;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Memória: tamanho, tipo, velocidade e modelo dos pentes, uso em tempo real e limpeza automática.</summary>
public sealed class RamPage : ToolPageBase
{
    private const double Gb = 1024.0 * 1024 * 1024;
    private static readonly int[] Thresholds = { 70, 80, 85, 90 };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel _modules = new();
    private readonly MetricTile _usage;
    private readonly TextBlock _auto = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    public RamPage() : base("Memória",
        "Tamanho, tipo, velocidade e modelo dos pentes, uso em tempo real e limpeza automática para a memória não virar gargalo.")
    {
        _usage = new MetricTile("Uso da memória", Color.FromRgb(0xFF, 0xC4, 0x00), 100, v => $"{v:0}%");
        AddButton("Limpar agora (completa)", (_, _) => MainWindow.Current?.Go("memory"), false);

        Body.Children.Add(_usage);
        Body.Children.Add(Heading("Seus pentes de memória"));
        Body.Children.Add(_modules);

        Body.Children.Add(Heading("Limpeza automática"));
        var enable = Check("Limpar a memória sozinho quando o uso ficar alto",
            "Libera a memória de programas em segundo plano. Nunca mexe no programa que você está usando, nos jogos da lista do Modo Jogo nem na lista de permissões da limpeza de memória. Respeita um intervalo de 3 minutos entre limpezas.");
        enable.IsChecked = AppServices.AutoMemory.Enabled;
        enable.Click += (_, _) =>
        {
            AppServices.AutoMemory.SetEnabled(enable.IsChecked == true);
            ShowAuto();
        };
        Body.Children.Add(enable);
        Body.Children.Add(Muted("Limpar quando o uso passar de:"));
        Body.Children.Add(Choice(Thresholds.Select(t => t + "%").ToList(),
            Math.Max(0, Array.IndexOf(Thresholds, AppServices.Settings.MemoryAutoCleanThreshold)),
            i => AppServices.AutoMemory.SetThreshold(Thresholds[i])));
        Body.Children.Add(_auto);
        Body.Children.Add(Muted("Quando a memória está cheia por causa de programas que você realmente usa, nenhuma limpeza resolve: o certo é fechar programas ou ter mais memória. A limpeza ajuda quando o uso está alto por programas parados em segundo plano."));

        _timer.Tick += (_, _) => UpdateUsage();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            AppServices.AutoMemory.Changed += OnAutoChanged;
            ShowAuto();
            UpdateUsage();
            await LoadModulesAsync();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            AppServices.AutoMemory.Changed -= OnAutoChanged;
        };
    }

    private void OnAutoChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(ShowAuto));

    private void ShowAuto()
    {
        var auto = AppServices.AutoMemory;
        if (!auto.Enabled)
        {
            _auto.Text = "Desligada.";
            _auto.Foreground = Brush("MutedBrush");
            return;
        }

        _auto.Foreground = Brush("OkBrush");
        _auto.Text = auto.LastClean is { } when
            ? $"Ligada. Última limpeza às {when:HH:mm:ss}: {auto.LastFreedBytes / (1024.0 * 1024):0} MB liberados ({auto.Cleanings} limpezas desde que o programa abriu)."
            : "Ligada. Ainda não foi preciso limpar.";
    }

    private void UpdateUsage()
    {
        var status = MemoryService.GetStatus();
        if (status.TotalBytes <= 0) return;
        double used = 100 - status.FreePercent;
        _usage.Push(used, $"{used:0}%", $"{status.UsedBytes / Gb:0.0} GB em uso de {status.TotalBytes / Gb:0.#} GB · {status.AvailableBytes / Gb:0.0} GB livres");
    }

    private async Task LoadModulesAsync()
    {
        _modules.Children.Clear();
        try
        {
            var hw = await AppServices.Hardware();
            if (hw.Memory.Count == 0)
            {
                _modules.Children.Add(Muted("O Windows não informou os pentes de memória."));
                return;
            }

            long total = hw.Memory.Sum(m => m.CapacityBytes);
            _modules.Children.Add(Muted($"Total instalado: {total / Gb:0.#} GB em {hw.Memory.Count} {(hw.Memory.Count == 1 ? "pente" : "pentes")}."));
            foreach (var m in hw.Memory)
            {
                string speed = m.ConfiguredMhz > 0 ? $"{m.ConfiguredMhz} MHz" : "velocidade desconhecida";
                bool slower = m.RatedMhz > 0 && m.ConfiguredMhz > 0 && m.ConfiguredMhz + 100 < m.RatedMhz;
                string detail = $"{m.CapacityBytes / Gb:0.#} GB {m.Type} · {speed}" + (m.RatedMhz > 0 ? $" (nominal {m.RatedMhz} MHz)" : "");
                if (slower) detail += "\nRodando abaixo da velocidade nominal. Ative o XMP/EXPO na BIOS (veja Meu hardware).";
                _modules.Children.Add(Row($"{m.Slot} · {m.Manufacturer} {m.PartNumber}".Trim(), detail, null, slower ? "WarnBrush" : "MutedBrush"));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            _modules.Children.Add(Muted("Não foi possível ler os pentes: " + ex.Message));
        }
    }
}
