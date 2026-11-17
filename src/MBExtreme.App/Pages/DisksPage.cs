using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Sensors;
using MBExtreme.Core.Storage;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Discos: modelo, velocidade de leitura e gravação ao vivo e espaço de cada unidade (verde = livre, laranja ou vermelho = usado).</summary>
public sealed class DisksPage : ToolPageBase
{
    private const double Gb = 1024.0 * 1024 * 1024;

    private sealed class DiskCard
    {
        public required Border View { get; init; }
        public required TextBlock Title { get; init; }
        public required TextBlock Speed { get; init; }
        public required LiveChart Chart { get; init; }
        public required StackPanel Volumes { get; init; }
        public List<double> Read { get; } = new();
        public List<double> Write { get; } = new();
        public string Model { get; init; } = "";
        public string Key { get; init; } = "";
    }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, DiskCard> _cards = new();
    private readonly StackPanel _cardHost = new();
    private IReadOnlyList<DriveEntry> _drives = Array.Empty<DriveEntry>();
    private bool _busy;
    private int _tick;

    public DisksPage() : base("Discos",
        "HDs e SSDs: modelo, velocidade de leitura e gravação em tempo real e espaço de cada unidade. Na barra, verde é o espaço livre e a parte laranja ou vermelha é o espaço usado.")
    {
        AddButton("Atualizar", async (_, _) => await ReloadDrivesAsync(true), false);
        AddButton("Saúde dos discos", (_, _) => MainWindow.Current?.Go("storage"), false);
        Body.Children.Add(_cardHost);

        _timer.Tick += async (_, _) => await TickAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await ReloadDrivesAsync();
            await TickAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task ReloadDrivesAsync(bool force = false)
    {
        try
        {
            _drives = await Task.Run(() => DriveMap.List(force));
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.IO.IOException)
        {
            SetStatus("Não foi possível ler as unidades: " + ex.Message, "WarnBrush");
        }

        RefreshVolumes();
    }

    /// <summary>Cada unidade vai para um único cartão: nome igual primeiro, e só depois nome parecido se houver um só candidato.</summary>
    private void RefreshVolumes()
    {
        var owner = new Dictionary<string, string>();
        foreach (var drive in _drives)
        {
            string a = Normalize(drive.Model);
            var exact = _cards.Values.FirstOrDefault(c => a.Length > 0 && Normalize(c.Model) == a);
            if (exact is not null) { owner[drive.Letter] = exact.Key; continue; }
            var similar = _cards.Values.Where(c => a.Length > 0 && Normalize(c.Model).Length > 0 && (Normalize(c.Model).Contains(a) || a.Contains(Normalize(c.Model)))).ToList();
            if (similar.Count == 1) owner[drive.Letter] = similar[0].Key;
        }

        foreach (var card in _cards.Values)
        {
            card.Volumes.Children.Clear();
            var mine = _drives.Where(d => owner.TryGetValue(d.Letter, out var key) && key == card.Key).ToList();
            if (mine.Count == 0) card.Volumes.Children.Add(Muted("Nenhuma letra de unidade encontrada neste disco."));
            foreach (var d in mine) card.Volumes.Children.Add(VolumeRow(d));
        }

        ShowOrphans(_drives.Where(d => !owner.ContainsKey(d.Letter)).ToList());
    }

    private async Task TickAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var monitor = await AppServices.Monitor;
            var groups = await Task.Run(monitor.ReadAll);
            bool created = false;
            var seen = new Dictionary<string, int>();
            foreach (var group in groups.Where(g => g.Type == "Storage"))
            {
                int n = seen.TryGetValue(group.Name, out var count) ? count : 0;
                seen[group.Name] = n + 1;
                created |= Update(group, $"{group.Name}#{n}");
            }

            if (created) RefreshVolumes();
            else if (++_tick % 30 == 0) await ReloadDrivesAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            SetStatus("Não foi possível ler a velocidade dos discos: " + ex.Message, "WarnBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    private bool Update(SensorGroup group, string key)
    {
        bool created = false;
        if (!_cards.TryGetValue(key, out var card))
        {
            card = Build(group.Name, key);
            _cards[key] = card;
            _cardHost.Children.Add(card.View);
            created = true;
        }

        double? Value(string name) => group.Sensors.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        double read = Value("Read Rate") ?? 0, write = Value("Write Rate") ?? 0;
        card.Read.Add(read / 1_048_576);
        card.Write.Add(write / 1_048_576);
        if (card.Read.Count > 60) { card.Read.RemoveAt(0); card.Write.RemoveAt(0); }
        card.Chart.Update(card.Read.ToList(), card.Write.ToList());

        var parts = new List<string> { $"Leitura {read / 1_048_576:0.0} MB/s", $"Gravação {write / 1_048_576:0.0} MB/s" };
        if (Value("Total Activity") is double activity) parts.Add($"Atividade {activity:0}%");
        if (Value("Temperature") is double temp) parts.Add($"{temp:0} °C");
        card.Speed.Text = string.Join("  ·  ", parts);
        return created;
    }

    private DiskCard Build(string model, string key)
    {
        var title = new TextBlock { Text = model, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var speed = new TextBlock { FontSize = 12, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        var chart = new LiveChart(60, Color.FromRgb(0xFF, 0xC4, 0x00), Color.FromRgb(0xE8, 0xE8, 0xEC))
        {
            Height = 70,
            Margin = new Thickness(0, 8, 0, 8),
            FormatMax = v => $"{v:0} MB/s",
        };
        var volumes = new StackPanel();

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(speed);
        stack.Children.Add(chart);
        stack.Children.Add(Muted("● Leitura (amarelo)   ● Gravação (branco)", 11));
        stack.Children.Add(volumes);
        var view = CardOf(stack);
        view.Margin = new Thickness(0, 0, 0, 14);
        return new DiskCard { View = view, Title = title, Speed = speed, Chart = chart, Volumes = volumes, Model = model, Key = key };
    }

    private static string Normalize(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private StackPanel? _orphans;

    private Border? _orphanCard;

    private void ShowOrphans(IReadOnlyList<DriveEntry> orphans)
    {
        if (_orphanCard is not null) _cardHost.Children.Remove(_orphanCard);
        _orphanCard = null;
        _orphans = null;
        if (orphans.Count == 0) return;

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Outras unidades", FontSize = 15, FontWeight = FontWeights.SemiBold });
        foreach (var d in orphans) stack.Children.Add(VolumeRow(d));
        _orphans = stack;
        _orphanCard = CardOf(stack);
        _cardHost.Children.Add(_orphanCard);
    }

    private UIElement VolumeRow(DriveEntry d)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        string name = string.IsNullOrWhiteSpace(d.Label) ? d.Name : $"{d.Name} {d.Label}";
        string kind = d.Media is { Length: > 0 } m ? $" · {m}" : "";
        panel.Children.Add(new TextBlock { Text = $"{name} · {d.Format}{kind}", FontSize = 13, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(UsageBar.Create(d.UsedPercent));
        panel.Children.Add(new TextBlock
        {
            Text = $"{d.FreeBytes / Gb:0.#} GB livres de {d.TotalBytes / Gb:0.#} GB · {d.UsedPercent:0}% usado",
            FontSize = 12,
            Foreground = Brush(d.UsedPercent >= 90 ? "BadBrush" : "MutedBrush"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        return panel;
    }
}
