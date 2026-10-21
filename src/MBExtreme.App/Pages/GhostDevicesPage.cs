using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Devices;
using MBExtreme.Core.Progress;

namespace MBExtreme.UI.Pages;

/// <summary>Limpa o "cache" de dispositivos USB que já não estão conectados (entradas fantasmas do Windows).</summary>
public sealed class GhostDevicesPage : ToolPageBase
{
    private readonly Dictionary<DeviceInfo, CheckBox> _boxes = new();

    public GhostDevicesPage() : base("Cache de portas USB",
        "O Windows guarda um registro de cada dispositivo USB já conectado. Com o tempo isso acumula entradas que não existem mais. Aqui você remove só as que estão desconectadas; se plugar o dispositivo de novo, o Windows o reconhece normalmente.")
    {
        AddButton("Procurar", async (_, _) => await ScanAsync());
        AddButton("Remover selecionados", async (_, _) => await RemoveAsync());
        AddButton("Selecionar todos", (_, _) => { foreach (var b in _boxes.Values) b.IsChecked = true; }, false);
        AddButton("Limpar seleção", (_, _) => { foreach (var b in _boxes.Values) b.IsChecked = false; }, false);
        Loaded += async (_, _) => { if (_boxes.Count == 0) await ScanAsync(); };
    }

    private async Task ScanAsync()
    {
        SetStatus("Procurando dispositivos desconectados...");
        Body.Children.Clear();
        _boxes.Clear();
        IReadOnlyList<DeviceInfo> ghosts;
        try
        {
            ghosts = await Task.Run(DeviceService.ListGhostDevices);
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível procurar: " + ex.Message, "BadBrush");
            return;
        }

        SetStatus(ghosts.Count == 0 ? "Nenhuma entrada antiga encontrada. Está limpo." : $"{ghosts.Count} entrada(s) de dispositivos desconectados.",
            ghosts.Count == 0 ? "OkBrush" : "MutedBrush");
        foreach (var d in ghosts.OrderBy(x => x.Name))
        {
            var box = Check(d.Name, string.Join(" · ", new[] { d.Class, d.InstanceId }.Where(s => !string.IsNullOrWhiteSpace(s))));
            _boxes[d] = box;
            Body.Children.Add(CardOf(box));
        }
    }

    private async Task RemoveAsync()
    {
        var selected = _boxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Selecione ao menos uma entrada.");
            return;
        }

        var answer = MessageBox.Show(
            $"Remover {selected.Count} registro(s) de dispositivos desconectados?\n\nNenhum dispositivo conectado agora é afetado.",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        ShowProgress("Limpando cache USB", "Remoção");
        var token = Cts!.Token;
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var failures = await Task.Run(() =>
            {
                var errors = new List<string>();
                for (int i = 0; i < selected.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    progress.Report(new ProgressEvent("Remoção", 100.0 * i / selected.Count, "Removendo " + selected[i].Name));
                    try { DeviceService.RemoveGhost(selected[i].InstanceId); }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { errors.Add($"{selected[i].Name}: {ex.Message}"); }
                }
                progress.Report(new ProgressEvent("Remoção", 100, "Concluído"));
                return errors;
            });
            Progress.Finish(failures.Count == 0 ? $"{selected.Count} registro(s) removido(s)" : $"{selected.Count - failures.Count} removido(s), {failures.Count} com falha",
                failures.Count == 0, string.Join("\n", failures.Take(4)));
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido.", false);
        }
    }

    protected override async void OnProgressClosed() => await ScanAsync();
}
