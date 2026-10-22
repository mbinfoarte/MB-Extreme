using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Storage;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Pendrives, HDs e SSDs externos e cartões: espaço e remoção segura.</summary>
public sealed class RemovablePage : ToolPageBase
{
    private const double Gb = 1024.0 * 1024 * 1024;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly StackPanel _list = new();
    private string _signature = "";
    private bool _busy;

    public RemovablePage() : base("Pendrives e HDs externos",
        "Veja os dispositivos externos conectados e remova com segurança: o MB Extreme fecha o acesso, grava o que estiver na memória e solta a unidade para você desconectar sem perder arquivos.")
    {
        AddButton("Atualizar", async (_, _) => await RefreshAsync(true), false);
        Body.Children.Add(_list);

        _timer.Tick += async (_, _) => await RefreshAsync(false);
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync(true);
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task RefreshAsync(bool force)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var all = await Task.Run(DriveMap.List);
            var external = all.Where(d => d.IsExternal && !d.IsSystem).ToList();
            string signature = string.Join("|", external.Select(d => d.Letter + d.FreeBytes / (1024 * 1024)));
            if (!force && signature == _signature) return;
            _signature = signature;
            Show(external);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.IO.IOException)
        {
            SetStatus("Não foi possível ler as unidades: " + ex.Message, "WarnBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    private void Show(IReadOnlyList<DriveEntry> drives)
    {
        _list.Children.Clear();
        if (drives.Count == 0)
        {
            _list.Children.Add(CardOf(new TextBlock
            {
                Text = "Nenhum pendrive, HD externo ou cartão conectado. Conecte um dispositivo e ele aparece aqui sozinho.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("MutedBrush"),
            }));
            return;
        }

        foreach (var d in drives)
        {
            var drive = d;
            var panel = new StackPanel();
            string name = string.IsNullOrWhiteSpace(drive.Label) ? $"Disco local ({drive.Name})" : $"{drive.Label} ({drive.Name})";
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = name, FontSize = 15, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = $"{(string.IsNullOrWhiteSpace(drive.Model) ? "Dispositivo USB" : drive.Model)} · {drive.Format} · {drive.TotalBytes / Gb:0.#} GB",
                FontSize = 12,
                Foreground = Brush("MutedBrush"),
            });
            grid.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(SmallButton("Abrir", (_, _) => Open(drive.Name + "\\")));
            buttons.Children.Add(SmallButton("Remover com segurança", async (_, _) => await EjectAsync(drive), true));
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);

            panel.Children.Add(grid);
            panel.Children.Add(UsageBar.Create(drive.UsedPercent));
            panel.Children.Add(new TextBlock
            {
                Text = $"{drive.FreeBytes / Gb:0.#} GB livres · {drive.UsedPercent:0}% usado",
                FontSize = 12,
                Foreground = Brush("MutedBrush"),
                Margin = new Thickness(0, 4, 0, 0),
            });
            var card = CardOf(panel);
            card.Margin = new Thickness(0, 0, 0, 12);
            _list.Children.Add(card);
        }
    }

    private async Task EjectAsync(DriveEntry drive)
    {
        if (MessageBox.Show($"Remover {drive.Name} com segurança?\n\nFeche antes os arquivos e programas que estiverem usando a unidade.",
                "Remover com segurança", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        SetStatus($"Preparando {drive.Name} para remoção...");
        var (ok, message) = await Task.Run(() => DriveMap.Eject(drive));
        SetStatus(message, ok ? "OkBrush" : "WarnBrush");
        await RefreshAsync(true);
    }
}
