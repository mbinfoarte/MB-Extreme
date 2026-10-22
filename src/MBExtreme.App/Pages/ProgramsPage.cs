using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Progress;

namespace MBExtreme.UI.Pages;

/// <summary>Atualização dos programas instalados e instalação de programas úteis, via winget (fonte oficial da Microsoft).</summary>
public sealed class ProgramsPage : ToolPageBase
{
    private readonly Dictionary<string, CheckBox> _upgrades = new();
    private readonly Dictionary<string, CheckBox> _installs = new();

    public ProgramsPage() : base("Programas",
        "Atualize seus programas e instale ferramentas úteis (CPU-Z, GPU-Z, HWiNFO, OCCT, MSI Afterburner e outros) pelo winget, o gerenciador de pacotes da Microsoft.")
    {
        AddButton("Procurar atualizações", async (_, _) => await SearchAsync());
        AddButton("Atualizar selecionados", async (_, _) => await RunAsync(_upgrades, "Atualizando programas", WingetService.Upgrade));
        AddButton("Instalar selecionados", async (_, _) => await RunAsync(_installs, "Instalando programas", WingetService.Install), false);
        AddSelectionButtons(() => _upgrades.Values.Concat(_installs.Values));
        Loaded += (_, _) => { if (Body.Children.Count == 0) Render(Array.Empty<WingetApp>()); };
    }

    private async Task SearchAsync()
    {
        SetStatus("Procurando atualizações...");
        try
        {
            if (!await Task.Run(WingetService.IsAvailable))
            {
                SetStatus("O winget não foi encontrado. Instale o \"App Installer\" pela Microsoft Store.", "WarnBrush");
                return;
            }

            var list = await Task.Run(WingetService.Upgradable);
            SetStatus(list.Count == 0 ? "Tudo em dia." : $"{list.Count} programa(s) com atualização.", list.Count == 0 ? "OkBrush" : "MutedBrush");
            Render(list);
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível procurar: " + ex.Message, "BadBrush");
        }
    }

    private void Render(IReadOnlyList<WingetApp> upgrades)
    {
        Body.Children.Clear();
        _upgrades.Clear();
        _installs.Clear();

        if (upgrades.Count > 0)
        {
            Body.Children.Add(Heading("Atualizações disponíveis"));
            foreach (var app in upgrades)
            {
                var box = Check(app.Name, $"{app.Version} → {app.Available}");
                _upgrades[app.Id] = box;
                Body.Children.Add(CardOf(box));
            }
        }

        Body.Children.Add(Heading("Instalar programas úteis"));
        foreach (var app in WingetService.Catalog)
        {
            var box = Check(app.Title, app.Description);
            _installs[app.Id] = box;
            Body.Children.Add(CardOf(box));
        }
    }

    private async Task RunAsync(Dictionary<string, CheckBox> boxes, string title, Func<string, (bool Ok, string Message)> action)
    {
        var ids = boxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        if (ids.Count == 0)
        {
            SetStatus("Selecione ao menos um programa.");
            return;
        }

        ShowProgress(title, "Programas");
        var token = Cts!.Token;
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var failures = await Task.Run(() =>
            {
                var errors = new List<string>();
                for (int i = 0; i < ids.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    progress.Report(new ProgressEvent("Programas", 100.0 * i / ids.Count, ids[i]));
                    var (ok, message) = action(ids[i]);
                    if (!ok) errors.Add($"{ids[i]}: {message}");
                }
                progress.Report(new ProgressEvent("Programas", 100, "Concluído"));
                return errors;
            });
            Progress.Finish(failures.Count == 0 ? "Concluído" : $"{failures.Count} com falha", failures.Count == 0, string.Join("\n", failures.Take(3)));
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido.", false);
        }
    }
}
