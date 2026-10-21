using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Storage;

namespace MBExtreme.UI.Pages;

/// <summary>Espaço, saúde e otimização de SSD e HD.</summary>
public sealed class StoragePage : ToolPageBase
{
    public StoragePage() : base("Armazenamento",
        "Espaço usado, saúde dos discos e otimização com a ferramenta do Windows (TRIM em SSD, desfragmentação em HD). Temperatura e desgaste dependem do que o disco informa.")
    {
        AddButton("Atualizar", async (_, _) => await LoadAsync(), false);
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SetStatus("Lendo os discos...");
        Body.Children.Clear();
        IReadOnlyList<VolumeInfo> volumes;
        IReadOnlyList<DiskInfo> disks = Array.Empty<DiskInfo>();
        string? diskProblem = null;
        try
        {
            volumes = await Task.Run(StorageService.Volumes);
            try { disks = await Task.Run(StorageService.PhysicalDisks); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
            {
                diskProblem = ex.Message;
            }
        }
        catch (IOException ex)
        {
            SetStatus("Não foi possível ler os volumes: " + ex.Message, "BadBrush");
            return;
        }

        SetStatus("");
        Body.Children.Add(Heading("Unidades"));
        foreach (var v in volumes)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = $"{v.Name} {v.Label}".Trim(),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
            });
            content.Children.Add(new ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = v.UsedPercent, Height = 8, Margin = new Thickness(0, 6, 0, 6),
                Foreground = Brush(v.UsedPercent > 90 ? "BadBrush" : "ButtonBrush"),
                Background = Brush("BorderBrush"),
                BorderThickness = new Thickness(0),
            });
            content.Children.Add(Muted($"{(v.TotalBytes - v.FreeBytes) / 1e9:0.0} GB usados de {v.TotalBytes / 1e9:0.0} GB · livre {v.FreeBytes / 1e9:0.0} GB · {v.Format}"));
            string name = v.Name;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(content);
            var button = SmallButton("Otimizar", async (_, _) => await OptimizeAsync(name), true);
            Grid.SetColumn(button, 1);
            button.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(button);
            Body.Children.Add(CardOf(grid));
        }

        Body.Children.Add(Heading("Discos físicos"));
        if (diskProblem is not null) Body.Children.Add(Muted("Não foi possível ler a saúde dos discos: " + diskProblem));
        foreach (var d in disks)
        {
            var parts = new List<string> { $"{d.Media} · {d.Bus} · {d.SizeBytes / 1e9:0} GB" };
            if (d.Temperature is { } t && t > 0) parts.Add($"{t:0} °C");
            if (d.WearPercent is { } w) parts.Add($"desgaste {w:0}%");
            if (d.PowerOnHours is { } h) parts.Add($"{h:N0} h ligado");
            if (d.ReadErrors is > 0 || d.WriteErrors is > 0) parts.Add($"erros: leitura {d.ReadErrors ?? 0}, gravação {d.WriteErrors ?? 0}");
            bool healthy = d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase);
            Body.Children.Add(Row($"{d.Name} — {(healthy ? "Saudável" : d.Health)}", string.Join(" · ", parts), null,
                healthy ? "MutedBrush" : "BadBrush"));
        }

        if (disks.Count == 0 && diskProblem is null) Body.Children.Add(Muted("Nenhum disco físico informado pelo Windows."));
    }

    private async Task OptimizeAsync(string drive)
    {
        ShowProgress("Otimizando " + drive, "Otimização");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        progress.Report(new ProgressEvent("Otimização", 10, "O Windows está otimizando a unidade (pode demorar em HD)"));
        try
        {
            var (ok, message) = await Task.Run(() => StorageService.Optimize(drive));
            progress.Report(new ProgressEvent("Otimização", 100, "Concluído"));
            Progress.Finish(ok ? "Unidade otimizada" : "Não foi possível otimizar", ok, message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Progress.Finish("Não foi possível otimizar", false, ex.Message);
        }
    }

    protected override async void OnProgressClosed() => await LoadAsync();
}
