using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Drivers;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Driver da placa de vídeo (NVIDIA, AMD, Intel) e drivers do Windows Update.</summary>
public sealed class DriversPage : ToolPageBase
{
    private readonly Dictionary<PendingDriver, CheckBox> _boxes = new();

    public DriversPage() : base("Drivers",
        "Mostra o driver da sua placa de vídeo e abre a página oficial do fabricante para baixar o mais novo. Também procura drivers pelo Windows Update.")
    {
        AddButton("Procurar drivers no Windows Update", async (_, _) => await SearchAsync());
        AddButton("Instalar selecionados", async (_, _) => await InstallAsync());
        Loaded += (_, _) => ShowGpu();
    }

    private void ShowGpu()
    {
        Body.Children.Clear();
        _boxes.Clear();
        Body.Children.Add(Heading("Placa de vídeo"));
        try
        {
            foreach (var gpu in DriverService.GpuDrivers())
            {
                string version = gpu.Vendor == GpuVendor.Nvidia
                    ? $"Versão {DriverService.NvidiaDisplayVersion(gpu.WindowsVersion)} (Windows: {gpu.WindowsVersion})"
                    : $"Versão {gpu.WindowsVersion}";
                string date = gpu.DriverDate is { } d ? " · " + d.ToString("dd/MM/yyyy") : "";
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                buttons.Children.Add(SmallButton("Página oficial", (_, _) => Open(DriverService.OfficialPage(gpu.Vendor)), true));
                if (gpu.Vendor == GpuVendor.Intel)
                    buttons.Children.Add(SmallButton("Instalar Intel DSA", async (_, _) => await WingetAsync("Intel.IntelDriverAndSupportAssistant")));
                Body.Children.Add(Row(gpu.Name, version + date, buttons));
            }
        }
        catch (Exception ex)
        {
            Body.Children.Add(Muted("Não foi possível ler a placa de vídeo: " + ex.Message));
        }

        Body.Children.Add(Muted("Para jogos, baixe o driver direto do fabricante (NVIDIA, AMD ou Intel). O MB Extreme não baixa drivers de sites desconhecidos."));
    }

    private async Task SearchAsync()
    {
        ShowGpu();
        SetStatus("Procurando no Windows Update (pode levar alguns minutos)...");
        IReadOnlyList<PendingDriver> found;
        try
        {
            found = await Task.Run(DriverService.SearchWindowsUpdateDrivers);
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível procurar: " + ex.Message, "BadBrush");
            return;
        }

        SetStatus(found.Count == 0 ? "Nenhum driver pendente. Está em dia." : $"{found.Count} driver(s) disponível(is).", found.Count == 0 ? "OkBrush" : "MutedBrush");
        if (found.Count > 0) Body.Children.Add(Heading("Disponíveis no Windows Update"));
        foreach (var driver in found)
        {
            var box = Check(driver.Title, string.Join(" · ", new[] { driver.Manufacturer, driver.Model, driver.Date }.Where(s => !string.IsNullOrWhiteSpace(s))));
            _boxes[driver] = box;
            Body.Children.Add(CardOf(box));
        }
    }

    private async Task InstallAsync()
    {
        var selected = _boxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Procure e selecione ao menos um driver.");
            return;
        }

        var answer = MessageBox.Show($"Instalar {selected.Count} driver(s)? A tela pode piscar durante a instalação.",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        ShowProgress("Instalando drivers", "Ponto de restauração", "Instalação");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var (ok, reboot, message) = await Task.Run(() =>
            {
                progress.Report(new ProgressEvent("Ponto de restauração", 5, "Criando ponto de restauração"));
                var (_, restoreError) = RestorePointService.TryCreate("MB Extreme - drivers");
                progress.Report(new ProgressEvent("Instalação", 30, "Baixando e instalando pelo Windows Update"));
                var result = DriverService.InstallWindowsUpdateDrivers(selected.Select(s => s.Id).ToList());
                progress.Report(new ProgressEvent("Instalação", 100, "Concluído"));
                return result;
            });
            Progress.Finish(ok ? (reboot ? "Instalado. Reinicie o PC para concluir." : "Instalado.") : "Não foi possível instalar.", ok, message);
        }
        catch (Exception ex)
        {
            Progress.Finish("Não foi possível instalar.", false, ex.Message);
        }
    }

    private async Task WingetAsync(string id)
    {
        ShowProgress("Instalando programa", "Instalação");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        var (ok, message) = await Task.Run(() =>
        {
            progress.Report(new ProgressEvent("Instalação", 20, "Instalando pelo winget"));
            return WingetService.Install(id);
        });
        Progress.Finish(ok ? "Instalado." : "Não foi possível instalar.", ok, message);
    }

    protected override void OnProgressClosed() => ShowGpu();
}
