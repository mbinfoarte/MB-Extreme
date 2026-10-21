using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Drivers;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Driver da placa de vídeo (NVIDIA, AMD, Intel), com atualização automática, e drivers do Windows Update.</summary>
public sealed class DriversPage : ToolPageBase
{
    private readonly Dictionary<PendingDriver, CheckBox> _boxes = new();

    public DriversPage() : base("Drivers",
        "Mostra a placa de vídeo e atualiza o driver sem você precisar abrir site nenhum. Também procura outros drivers pelo Windows Update.")
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

        IReadOnlyList<GpuDriverInfo> gpus;
        try
        {
            gpus = DriverService.GpuDrivers();
        }
        catch (Exception ex)
        {
            Body.Children.Add(Muted("Não foi possível ler a placa de vídeo: " + ex.Message));
            return;
        }

        foreach (var gpu in gpus) Body.Children.Add(GpuCard(gpu));

        Body.Children.Add(Muted("A NVIDIA é atualizada direto do servidor oficial dela, com conferência da assinatura digital. AMD e Intel usam o Windows Update; se ele não tiver a versão mais nova, abra a página oficial do fabricante."));
    }

    private Border GpuCard(GpuDriverInfo gpu)
    {
        string version = gpu.Vendor == GpuVendor.Nvidia
            ? $"Versão {gpu.DisplayVersion} (Windows: {gpu.WindowsVersion})"
            : $"Versão {gpu.WindowsVersion}";
        string date = gpu.DriverDate is { } d ? " · " + d.ToString("dd/MM/yyyy") : "";

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = gpu.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(Muted(version + date));
        var status = Muted(gpu.Vendor == GpuVendor.Nvidia ? "Verificando a versão mais nova..." : "");
        status.Margin = new Thickness(0, 4, 0, 0);
        text.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(SmallButton("Atualizar", async (_, _) => await UpdateGpuAsync(gpu), true));
        buttons.Children.Add(SmallButton("Página oficial", (_, _) => Open(DriverService.OfficialPage(gpu.Vendor))));
        if (gpu.Vendor == GpuVendor.Intel)
            buttons.Children.Add(SmallButton("Intel DSA", async (_, _) => await WingetAsync("Intel.IntelDriverAndSupportAssistant")));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        if (gpu.Vendor == GpuVendor.Nvidia) _ = CheckLatestAsync(gpu, status);
        return CardOf(grid);
    }

    private async Task CheckLatestAsync(GpuDriverInfo gpu, TextBlock status)
    {
        try
        {
            var latest = await NvidiaDriverUpdater.FindLatestAsync(gpu.Name, CancellationToken.None);
            if (latest is null)
            {
                status.Text = "Não achei esta placa no catálogo da NVIDIA. Use a página oficial.";
                status.Foreground = Brush("WarnBrush");
            }
            else if (NvidiaDriverUpdater.CompareVersions(latest.Version, gpu.DisplayVersion) > 0)
            {
                status.Text = $"Nova versão disponível: {latest.Version}";
                status.Foreground = Brush("OkBrush");
            }
            else
            {
                status.Text = "Seu driver está na versão mais nova.";
                status.Foreground = Brush("OkBrush");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            status.Text = "Não consegui verificar a versão mais nova (sem internet?).";
        }
    }

    private async Task UpdateGpuAsync(GpuDriverInfo gpu)
    {
        if (gpu.Vendor == GpuVendor.Nvidia) await UpdateNvidiaAsync(gpu);
        else if (gpu.Vendor is GpuVendor.Amd or GpuVendor.Intel) await UpdateViaWindowsUpdateAsync(gpu);
        else MessageBox.Show("Esta placa não é NVIDIA, AMD ou Intel. Use a página oficial do fabricante.");
    }

    private async Task UpdateNvidiaAsync(GpuDriverInfo gpu)
    {
        var answer = MessageBox.Show(
            "Baixar e instalar o driver mais novo da NVIDIA?\n\n" +
            "• O arquivo vem do servidor oficial da NVIDIA e a assinatura é conferida.\n" +
            "• Um ponto de restauração é criado antes.\n" +
            "• A tela pode piscar durante a instalação. Salve o que estiver aberto.\n" +
            "• Pode ser preciso reiniciar o PC no final.",
            "Atualizar driver NVIDIA", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        ShowProgress("Atualizando o driver NVIDIA", NvidiaDriverUpdater.Steps);
        var token = Cts!.Token;
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var result = await NvidiaDriverUpdater.UpdateAsync(gpu, progress, token);
            Progress.Finish(result.Ok ? (result.RebootRecommended ? "Driver instalado" : "Tudo certo") : "Não foi possível atualizar",
                result.Ok, result.Message);
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido antes de instalar.", false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or Win32Exception)
        {
            Progress.Finish("Não foi possível atualizar", false, ex.Message);
        }
    }

    private async Task UpdateViaWindowsUpdateAsync(GpuDriverInfo gpu)
    {
        ShowProgress("Atualizando o driver de vídeo", "Procurar", "Ponto de restauração", "Instalar");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var outcome = await Task.Run<(bool Ok, bool Reboot, string Message)>(() =>
            {
                progress.Report(new ProgressEvent("Procurar", 5, "Procurando no Windows Update (pode levar alguns minutos)"));
                var found = DriverService.FilterGpuDrivers(DriverService.SearchWindowsUpdateDrivers(), gpu.Vendor);
                if (found.Count == 0) return (true, false, "O Windows Update não tem driver de vídeo mais novo. Se você quer a versão mais recente do fabricante, abra a página oficial.");

                progress.Report(new ProgressEvent("Ponto de restauração", 35, "Criando ponto de restauração"));
                RestorePointService.TryCreate("MB Extreme - driver de vídeo");

                progress.Report(new ProgressEvent("Instalar", 50, "Baixando e instalando pelo Windows Update"));
                var install = DriverService.InstallWindowsUpdateDrivers(found.Select(f => f.Id).ToList());
                progress.Report(new ProgressEvent("Instalar", 100, "Concluído"));
                return (install.Ok, install.RebootRequired, install.Message);
            });
            Progress.Finish(outcome.Ok ? (outcome.Reboot ? "Instalado. Reinicie o PC." : "Concluído") : "Não foi possível atualizar", outcome.Ok, outcome.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            Progress.Finish("Não foi possível atualizar", false, ex.Message);
        }
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
                RestorePointService.TryCreate("MB Extreme - drivers");
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
