using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Operations;
using MBExtreme.Core.Tweaks;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Pós-formatação: um assistente para o Windows recém-instalado. Mostra o sistema, sugere um perfil (Leve, Equilibrado ou Extremo),
/// deixa marcar e desmarcar cada item (apps para remover, ajustes e programas), roda tudo pela fila com ponto de restauração antes,
/// oferece o Windows Update e a análise final, e salva um relatório.
/// </summary>
public sealed class PostInstallPage : ToolPageBase
{
    private PostInstallProfile _profile = PostInstallProfile.Balanced;
    private readonly List<(CheckBox Box, Func<IOperation> Make)> _items = new();
    private IReadOnlyList<BloatApp> _installed = Array.Empty<BloatApp>();
    private IReadOnlyList<Tweak> _tweaks = Array.Empty<Tweak>();
    private readonly List<string> _report = new();
    private bool _busy;
    private CancellationTokenSource? _cts;

    public PostInstallPage() : base("Pós-formatação",
        "Para deixar um Windows recém-instalado pronto: escolha um perfil, revise cada item e clique em \"Preparar o computador\". " +
        "Nada é feito sem você conferir; um ponto de restauração é criado antes e os ajustes têm volta pela Central Desfazer.")
    {
        AddButton("Preparar o computador", async (_, _) => await RunAsync());
        AddButton("Parar", (_, _) => _cts?.Cancel(), false);
        AddButton("Abrir o Windows Update", (_, _) => Open("ms-settings:windowsupdate-action"), false);
        Loaded += async (_, _) => { if (_items.Count == 0) await LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        SetStatus("Lendo o sistema...");
        var names = await Task.Run(AppRemovalService.InstalledPackageNames);
        _installed = BloatCatalog.All.Where(a => AppRemovalService.IsInstalled(a, names)).ToList();
        var available = AppServices.AllowedTweaks();
        _tweaks = await Task.Run(() => available.Where(t => t.IsAvailable && t.IsApplied() == false).ToList());
        Build();
        SetStatus("");
    }

    private static string SystemText()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = key?.GetValue("ProductName") as string ?? "Windows";
            int build = Environment.OSVersion.Version.Build;
            if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");   // o registro ainda diz "10" no 11
            string version = key?.GetValue("DisplayVersion") as string ?? "";
            return $"{product} {version} (build {build}) · {Environment.MachineName} · {Environment.ProcessorCount} núcleos";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return Environment.OSVersion.VersionString; }
    }

    private void Build()
    {
        Body.Children.Clear();
        _items.Clear();

        Body.Children.Add(Heading("1. Sistema"));
        Body.Children.Add(Row(SystemText(), "Os itens abaixo já levam em conta esta versão do Windows: o que não existe nela fica de fora."));

        Body.Children.Add(Heading("2. Perfil"));
        Body.Children.Add(Choice(new[] { "Leve", "Equilibrado", "Extremo" }, (int)_profile, i => { _profile = (PostInstallProfile)i; Build(); }));
        Body.Children.Add(Muted(PostInstallPlan.Description(_profile)));

        var apps = PostInstallPlan.Apps(_profile, _installed).ToHashSet();
        Body.Children.Add(Heading($"3. Aplicativos para remover ({_installed.Count} encontrados)"));
        Body.Children.Add(Muted("Sem volta pelo programa: para ter de volta, instale pela Microsoft Store.", 11.5));
        foreach (var app in _installed.OrderBy(a => a.Level))
        {
            var box = Check(app.Title, $"{LevelText(app.Level)} · {app.Note}");
            box.IsChecked = apps.Contains(app);
            _items.Add((box, () => new AppRemovalOperation(app)));
            Body.Children.Add(CardOf(box));
        }
        if (_installed.Count == 0) Body.Children.Add(Muted("Nenhum dos aplicativos da lista está instalado."));

        var tweaks = PostInstallPlan.Tweaks(_profile, _tweaks).ToHashSet();
        Body.Children.Add(Heading($"4. Ajustes do Windows ({_tweaks.Count} ainda não aplicados)"));
        foreach (var group in _tweaks.GroupBy(t => t.Category).OrderBy(g => g.Key))
        {
            foreach (var tweak in group.OrderBy(t => t.Risk))
            {
                string detail = $"{RiskText(tweak.Risk)} · {tweak.Description}" + (tweak.Warning.Length > 0 ? "\nAtenção: " + tweak.Warning : "");
                var box = Check(tweak.Title, detail);
                box.IsChecked = tweaks.Contains(tweak);
                _items.Add((box, () => new TweakOperation(tweak, apply: true)));
                Body.Children.Add(CardOf(box));
            }
        }

        Body.Children.Add(Heading("5. Programas para instalar"));
        Body.Children.Add(Muted("Instalados pelo winget, o instalador oficial da Microsoft, um de cada vez.", 11.5));
        foreach (var app in WingetService.Catalog)
        {
            var box = Check(app.Title, app.Description);
            _items.Add((box, () => new WingetOperation(app.Id, app.Title, upgrade: false)));
            Body.Children.Add(CardOf(box));
        }

        Body.Children.Add(Heading("6. Windows Update (opcional)"));
        Body.Children.Add(Muted("Depois de preparar, clique em \"Abrir o Windows Update\" no topo para procurar atualizações. O programa não muda as configurações do Windows Update.", 12));
        Body.Children.Add(Heading("7. Diagnóstico final"));
        Body.Children.Add(Muted("No fim, o Painel inteligente faz uma análise para conferir como ficou. O relatório é salvo em Documentos.", 12));
    }

    private static string LevelText(AppRemovalLevel l) => l switch { AppRemovalLevel.Safe => "pode remover", AppRemovalLevel.Caution => "cuidado", _ => "não recomendado" };

    private static string RiskText(TweakRisk r) => r switch { TweakRisk.Low => "risco baixo", TweakRisk.Medium => "risco médio", _ => "risco alto" };

    private async Task RunAsync()
    {
        if (_busy) return;
        var chosen = _items.Where(i => i.Box.IsChecked == true).ToList();
        if (chosen.Count == 0) { SetStatus("Marque pelo menos um item.", "WarnBrush"); return; }
        if (MessageBox.Show($"Preparar o computador com {chosen.Count} item(ns), um de cada vez?\n\nAntes, o programa cria um ponto de restauração.",
                "Pós-formatação", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _busy = true;
        _cts = new CancellationTokenSource();
        var operations = chosen.Select(c => c.Make()).ToList();
        Body.Children.Clear();
        Body.Children.Add(Heading("Preparando"));
        var lines = operations.ToDictionary(o => o, o =>
        {
            var t = new TextBlock { Text = "◻  " + o.Info.Title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), Foreground = Brush("MutedBrush") };
            Body.Children.Add(t);
            return t;
        });
        void OnChanged(QueueItem item) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!lines.TryGetValue(item.Operation, out var line)) return;
            string mark = item.State switch { OperationState.Running => "⏳", OperationState.Succeeded or OperationState.RebootRequired => "✔", OperationState.Pending => "◻", OperationState.Skipped => "–", _ => "✖" };
            line.Text = $"{mark}  {item.Operation.Info.Title} — {item.Message}";
            line.Foreground = Brush(item.State switch { OperationState.Succeeded => "OkBrush", OperationState.Failed or OperationState.Cancelled => "BadBrush", OperationState.Running => "TextBrush", _ => "MutedBrush" });
        }));

        _report.Clear();
        _report.Add($"MB Extreme — pós-formatação em {DateTime.Now:dd/MM/yyyy HH:mm}");
        _report.Add(SystemText());
        _report.Add($"Perfil: {PostInstallPlan.Title(_profile)}");
        OperationQueue.Shared.Changed += OnChanged;
        try
        {
            SetStatus("Criando um ponto de restauração...");
            var (ok, error) = await Task.Run(() => RestorePointService.TryCreate("MB Extreme - antes da pós-formatação"));
            _report.Add(ok ? "Ponto de restauração criado." : "Ponto de restauração não criado: " + error);
            SetStatus("Preparando, um item de cada vez...");
            var token = _cts.Token;
            var results = await Task.Run(() => OperationQueue.Shared.Run(operations, null, token));
            foreach (var r in results) _report.Add($"- {r.Operation.Info.Title}: {r.State} — {r.Message}");
            int failed = results.Count(r => r.State == OperationState.Failed);
            bool reboot = results.Any(r => r.State == OperationState.RebootRequired);
            string path = SaveReport();
            SetStatus($"Pronto: {results.Count(r => r.State is OperationState.Succeeded or OperationState.RebootRequired)} feito(s), {failed} com erro." +
                      (reboot ? " Reinicie o computador para terminar." : "") + (path.Length > 0 ? " Relatório em " + path : ""), failed == 0 ? "OkBrush" : "WarnBrush");
            var next = SmallButton("Fazer o diagnóstico final", (_, _) => MainWindow.Current?.Go("diagnose", autoStart: true), true);
            next.HorizontalAlignment = HorizontalAlignment.Left;
            next.Margin = new Thickness(0, 12, 0, 0);
            Body.Children.Add(next);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, "WarnBrush");
        }
        finally
        {
            OperationQueue.Shared.Changed -= OnChanged;
            _busy = false;
        }
    }

    private string SaveReport()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"MB Extreme - pós-formatação {DateTime.Now:yyyy-MM-dd HHmm}.txt");
            File.WriteAllLines(path, _report);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    private void Open(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { SetStatus("Não foi possível abrir: " + ex.Message, "BadBrush"); }
    }
}
