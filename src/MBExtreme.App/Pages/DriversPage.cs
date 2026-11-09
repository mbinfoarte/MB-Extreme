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
    private static (string Gpu, NvidiaLatest? Latest, DateTime At)? _latestCache;

    public DriversPage() : base("Drivers",
        "Mostra a placa de vídeo e atualiza o driver sem você precisar abrir site nenhum. Também procura outros drivers pelo Windows Update.")
    {
        AddButton("Procurar drivers no Windows Update", async (_, _) => await SearchAsync());
        AddButton("Instalar selecionados", async (_, _) => await InstallAsync());
        AddSelectionButtons(() => _boxes.Values);
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
        _ = ShowMoreAsync();
    }

    private readonly StackPanel _more = new();
    private readonly TextBox _driverSearch = new() { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 4, 0, 6) };
    private readonly CheckBox _showGeneric = new() { Content = "Mostrar os genéricos do Windows", Margin = new Thickness(0, 0, 0, 6) };
    private readonly StackPanel _driverList = new();
    private IReadOnlyList<MBExtreme.Core.Drivers.InstalledDriver> _drivers = Array.Empty<MBExtreme.Core.Drivers.InstalledDriver>();

    /// <summary>Aparelhos com problema (no alto), placa-mãe, backup e a lista de todos os drivers.</summary>
    private async Task ShowMoreAsync()
    {
        if (!Body.Children.Contains(_more)) Body.Children.Add(_more);
        _more.Children.Clear();
        _more.Children.Add(Muted("Lendo os aparelhos e os drivers..."));
        IReadOnlyList<MBExtreme.Core.Drivers.ProblemDevice> problems;
        MBExtreme.Core.Drivers.Motherboard? board;
        try
        {
            (problems, board, _drivers) = await Task.Run(() => (MBExtreme.Core.Drivers.DeviceDrivers.Problems(), MBExtreme.Core.Drivers.DeviceDrivers.Board(), MBExtreme.Core.Drivers.DeviceDrivers.All()));
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _more.Children.Clear();
            _more.Children.Add(Muted("Não foi possível ler os drivers: " + ex.Message));
            return;
        }
        _more.Children.Clear();

        // aparelhos com problema vão para o topo da página
        _problems = problems;
        foreach (var old in Body.Children.OfType<FrameworkElement>().Where(e => e.Tag as string == "problem").ToList()) Body.Children.Remove(old);
        int at = 0;
        if (problems.Count > 0)
        {
            var title = Heading($"Aparelhos sem driver ou com problema ({problems.Count})");
            title.Tag = "problem";
            title.Foreground = Brush("BadBrush");
            Body.Children.Insert(at++, title);

            var intro = Muted("O MB Extreme procura o driver de cada aparelho direto no Windows Update (fonte oficial da Microsoft) e instala aqui mesmo, sem abrir o site do fabricante.");
            intro.Tag = "problem";
            Body.Children.Insert(at++, intro);

            var allButton = SmallButton("Baixar os drivers que faltam", async (_, _) => await FetchAllMissingAsync(), true);
            allButton.Tag = "problem";
            allButton.Margin = new Thickness(0, 4, 0, 8);
            Body.Children.Insert(at++, allButton);

            foreach (var p in problems)
            {
                var device = p;
                var right = new StackPanel { Orientation = Orientation.Horizontal };
                right.Children.Add(SmallButton("Baixar e instalar o driver", async (_, _) => await FetchDeviceAsync(device), true));
                right.Children.Add(SmallButton("Copiar ID", (_, _) => { if (TryCopy(device.DeviceId)) SetStatus("ID copiado.", "OkBrush"); }));
                right.Children.Add(SmallButton("Gerenciador de Dispositivos", (_, _) => StartTool("devmgmt.msc")));
                var row = Row($"⚠  {p.Name}" + (p.Class.Length > 0 ? $"  ·  {p.Class}" : ""), $"{p.Explanation}  (código {p.Code})", right, "WarnBrush");
                row.Tag = "problem";
                Body.Children.Insert(at++, row);
            }
        }

        if (board is not null)
        {
            _more.Children.Add(Heading("Placa-mãe"));
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = $"{board.Brand} {board.Model}", FontSize = 14, FontWeight = FontWeights.SemiBold });
            info.Children.Add(Muted($"Fabricante: {board.Manufacturer} · BIOS {board.Bios}" + (board.BiosDate is { } d ? $" ({d:dd/MM/yyyy})" : "")));
            info.Children.Add(Muted("Na página do fabricante ficam os drivers de chipset, som, rede e a BIOS do seu modelo. Se ela não abrir direto no modelo, copie o modelo acima e cole na busca do site.", 11.5));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            if (board.SupportUrl is { } url) buttons.Children.Add(SmallButton($"Abrir suporte da {board.Brand}", (_, _) => Open(url), true));
            buttons.Children.Add(SmallButton("Copiar modelo", (_, _) => TryCopy(board.Model)));
            info.Children.Add(buttons);
            _more.Children.Add(CardOf(info));
        }

        _more.Children.Add(Heading("Backup dos drivers"));
        var backup = new StackPanel();
        backup.Children.Add(Muted("Salva uma cópia dos drivers instalados (menos os que já vêm com o Windows) e das redes Wi-Fi num pendrive, para devolver tudo depois de formatar."));
        backup.Children.Add(SmallButton("Abrir Backup para formatar", (_, _) => MainWindow.Current?.Go("formatbackup"), true));
        _more.Children.Add(CardOf(backup));

        _more.Children.Add(Heading("Todos os drivers instalados"));
        System.Windows.Automation.AutomationProperties.SetName(_driverSearch, "Procurar driver por aparelho, fabricante ou versão");
        _driverSearch.TextChanged -= FilterChanged;
        _driverSearch.TextChanged += FilterChanged;
        _showGeneric.Foreground = Brush("TextBrush");
        _showGeneric.Click -= GenericChanged;
        _showGeneric.Click += GenericChanged;
        _more.Children.Add(_driverSearch);
        _more.Children.Add(_showGeneric);
        _more.Children.Add(_driverList);
        FillDrivers();
    }

    private void FilterChanged(object sender, TextChangedEventArgs e) => FillDrivers();
    private void GenericChanged(object sender, RoutedEventArgs e) => FillDrivers();

    private void FillDrivers()
    {
        _driverList.Children.Clear();
        string q = _driverSearch.Text.Trim();
        var list = _drivers.Where(d => _showGeneric.IsChecked == true || !d.IsWindowsGeneric)
            .Where(d => q.Length == 0 || d.Device.Contains(q, StringComparison.CurrentCultureIgnoreCase) || d.Manufacturer.Contains(q, StringComparison.CurrentCultureIgnoreCase) || d.Version.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _driverList.Children.Add(Muted($"{list.Count} de {_drivers.Count} drivers" + (_showGeneric.IsChecked == true ? "" : $" ({_drivers.Count(d => d.IsWindowsGeneric)} genéricos do Windows escondidos)"), 11.5));
        foreach (var d in list.Take(300))
            _driverList.Children.Add(Row(d.Device, $"{d.Manufacturer} · versão {d.Version}" + (d.Date is { } date ? $" · {date:dd/MM/yyyy}" : "") + (d.Signed ? " · assinado" : " · SEM assinatura")
                , null, d.Signed ? "MutedBrush" : "WarnBrush"));
    }

    private static void StartTool(string tool)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tool) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
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
            NvidiaLatest? latest;
            if (_latestCache is { } cache && cache.Gpu == gpu.Name && DateTime.Now - cache.At < TimeSpan.FromMinutes(30))
            {
                latest = cache.Latest;
            }
            else
            {
                latest = await NvidiaDriverUpdater.FindLatestAsync(gpu.Name, CancellationToken.None);
                _latestCache = (gpu.Name, latest, DateTime.Now);
            }

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

    private IReadOnlyList<ProblemDevice> _problems = Array.Empty<ProblemDevice>();
    private IReadOnlyList<PendingDriver>? _wuDrivers;
    private DateTime _wuAt;

    /// <summary>Procura os drivers no Windows Update uma vez só (guardado por alguns minutos, porque a busca é demorada).</summary>
    private IReadOnlyList<PendingDriver> SearchWindowsUpdate(IProgress<ProgressEvent> progress)
    {
        if (_wuDrivers is not null && DateTime.Now - _wuAt < TimeSpan.FromMinutes(5)) return _wuDrivers;
        progress.Report(new ProgressEvent("Procurar", 10, "Procurando os drivers no Windows Update (pode levar alguns minutos)"));
        _wuDrivers = DriverService.SearchWindowsUpdateDrivers();
        _wuAt = DateTime.Now;
        return _wuDrivers;
    }

    private (bool Ok, bool Reboot, string Message) InstallMatched(IReadOnlyList<string> ids, string restoreName, IProgress<ProgressEvent> progress)
    {
        progress.Report(new ProgressEvent("Ponto de restauração", 40, "Criando ponto de restauração"));
        RestorePointService.TryCreate(restoreName);
        progress.Report(new ProgressEvent("Instalar", 55, $"Baixando e instalando {ids.Count} driver(s) pelo Windows Update"));
        var r = DriverService.InstallWindowsUpdateDrivers(ids);
        progress.Report(new ProgressEvent("Instalar", 100, "Concluído"));
        return (r.Ok, r.RebootRequired, r.Message);
    }

    /// <summary>Procura e instala o driver de um aparelho específico pelo Windows Update (casando o ID de hardware).</summary>
    private async Task FetchDeviceAsync(ProblemDevice device)
    {
        ShowProgress($"Driver de {device.Name}", "Procurar", "Ponto de restauração", "Instalar");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var outcome = await Task.Run<(bool Ok, bool Reboot, string Message)>(() =>
            {
                var matched = DriverService.DriversForDevice(device, SearchWindowsUpdate(progress));
                if (matched.Count == 0)
                    return (false, false, "O Windows Update não tem o driver deste aparelho agora. Clique em \"Copiar ID\" e procure no Catálogo do Windows Update ou no site do fabricante.");
                return InstallMatched(matched.Select(m => m.Id).ToList(), "MB Extreme - driver " + device.Name, progress);
            });
            Progress.Finish(outcome.Ok ? (outcome.Reboot ? "Driver instalado. Reinicie o PC." : "Driver instalado.") : "Não foi possível instalar", outcome.Ok, outcome.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            Progress.Finish("Não foi possível instalar", false, ex.Message);
        }
    }

    /// <summary>Procura e instala, de uma vez, os drivers de todos os aparelhos sem driver ou com problema.</summary>
    private async Task FetchAllMissingAsync()
    {
        if (_problems.Count == 0) { SetStatus("Nenhum aparelho com problema."); return; }
        ShowProgress("Drivers que faltam", "Procurar", "Ponto de restauração", "Instalar");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var outcome = await Task.Run<(bool Ok, bool Reboot, string Message)>(() =>
            {
                var pending = SearchWindowsUpdate(progress);
                var ids = _problems.SelectMany(d => DriverService.DriversForDevice(d, pending)).Select(m => m.Id).Distinct().ToList();
                if (ids.Count == 0)
                    return (false, false, "O Windows Update não tem driver para os aparelhos com problema agora. Use \"Copiar ID\" em cada um e procure no Catálogo do Windows Update ou no site do fabricante.");
                return InstallMatched(ids, "MB Extreme - drivers que faltam", progress);
            });
            Progress.Finish(outcome.Ok ? (outcome.Reboot ? "Instalado. Reinicie o PC." : "Instalado.") : "Não foi possível instalar", outcome.Ok, outcome.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            Progress.Finish("Não foi possível instalar", false, ex.Message);
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
