using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Startup;
using MBExtreme.Core.Tools;
using MBExtreme.Core.Tweaks;
using MBExtreme.Core.Windows;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Otimizar processos e inicialização: examina o PC, mostra números claros e lista, com caixinhas, o que sai da inicialização,
/// os ajustes do Windows e os programas pesados abertos agora. O perfil (Gamer, Turbo, Trabalho, Notebook, Streamer) só pré-marca;
/// a pessoa decide item por item e aplica tudo num clique. Ponto de restauração antes; "Desfazer" volta o que o perfil mudou.
/// </summary>
public sealed class ProcessProfilesPage : ToolPageBase
{
    private readonly System.Windows.Controls.Primitives.UniformGrid _stats = new() { Columns = 4, Margin = new Thickness(0, 0, 0, 4) };
    private readonly System.Windows.Controls.Primitives.UniformGrid _profiles = new() { Columns = 5 };
    private readonly StackPanel _lists = new();
    private readonly TextBlock _forecast = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly Button _apply = new() { Padding = new Thickness(22, 10, 22, 10), FontSize = 14 };
    private readonly List<(CheckBox Box, StartupEntry Entry)> _startupBoxes = new();
    private readonly List<(CheckBox Box, Tweak Tweak)> _tweakBoxes = new();
    private readonly List<(CheckBox Box, ProcessEntry[] Group)> _processBoxes = new();
    private IReadOnlyList<StartupEntry> _startup = Array.Empty<StartupEntry>();
    private IReadOnlyList<ProcessEntry> _processes = Array.Empty<ProcessEntry>();
    private double _ramGb, _ramUsedGb;
    private bool _laptop, _scanning;
    private PcProfile _profile;

    /// <summary>Programas que nunca aparecem na lista de "fechar agora" (drivers, áudio, antivírus, o próprio MB).</summary>
    private static readonly HashSet<string> NeverClose = new(StringComparer.OrdinalIgnoreCase)
    {
        "MBExtreme", "nvcontainer", "NVDisplay.Container", "RtkAudUService64", "RtkAudioService64", "WavesSvc64", "atieclxx", "amdfendrsr",
        "RadeonSoftware", "igfxEM", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "RuntimeBroker",
        "SearchIndexer", "WmiPrvSE", "dllhost", "conhost", "smartscreen", "NisSrv", "MpDefenderCoreService", "SecurityHealthSystray",
        "LockApp", "ApplicationFrameHost", "SystemSettings", "WUDFHost", "Widgets", "msedgewebview2",
    };

    public ProcessProfilesPage() : base("Otimizar processos e inicialização",
        "O MB examina o PC e marca o que pode sair. Você confere item por item e aplica tudo num clique. Antivírus, áudio, rede e drivers nunca entram na lista.")
    {
        AddButton("Examinar de novo", async (_, _) => await ScanAsync(), false);
        AddButton("Desfazer", async (_, _) => await UndoAsync(), false);
        Body.Children.Add(_stats);
        Body.Children.Add(Heading("Como você usa o PC?"));
        Body.Children.Add(Muted("O perfil só marca as sugestões abaixo. Você pode marcar e desmarcar o que quiser.", 11.5));
        _profiles.Margin = new Thickness(0, 6, 0, 0);
        Body.Children.Add(_profiles);
        Body.Children.Add(_lists);

        var bottom = new DockPanel();
        _apply.Style = (Style)FindResource("PrimaryButton");
        _apply.Click += async (_, _) => await ApplyAsync();
        DockPanel.SetDock(_apply, Dock.Right);
        bottom.Children.Add(_apply);
        var badge = GlassFx.Badge("", Color.FromRgb(0xFF, 0xC4, 0x00), 34);
        badge.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(badge, Dock.Left);
        bottom.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "Ponto de restauração antes · \"Desfazer\" volta tudo", FontSize = 13, FontWeight = FontWeights.SemiBold });
        text.Children.Add(_forecast);
        bottom.Children.Add(text);
        var card = CardOf(bottom);
        card.Margin = new Thickness(0, 12, 0, 8);
        Body.Children.Add(card);
        RenderWaiting();
        Loaded += async (_, _) => { if (_startup.Count == 0 && _processes.Count == 0 && !_scanning) await ScanAsync(); };
    }

    private void RenderWaiting()
    {
        _stats.Children.Clear();
        foreach (var (title, glyph) in new[] { ("Abrem com o Windows", ""), ("Processos abertos", ""), ("Memória em uso", ""), ("Dá para otimizar", "") })
            _stats.Children.Add(Stat(glyph, "…", title));
        _lists.Children.Clear();
        _lists.Children.Add(Muted("Examinando o PC: o que abre com o Windows, os processos abertos e os ajustes já feitos...", 12));
        _apply.IsEnabled = false;
        _apply.Content = "Otimizar agora";
        _forecast.Text = "";
    }

    private async Task ScanAsync()
    {
        if (_scanning) return;
        _scanning = true;
        RenderWaiting();
        try
        {
            var (startup, processes) = await Task.Run(() =>
            {
                IReadOnlyList<StartupEntry> list;
                try { list = StartupService.List(); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException or InvalidOperationException) { list = Array.Empty<StartupEntry>(); }
                IReadOnlyList<ProcessEntry> procs;
                try { procs = new ProcessList().Sample(); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) { procs = Array.Empty<ProcessEntry>(); }
                return (list, procs);
            });
            _startup = startup;
            _processes = processes;
            _ramUsedGb = processes.Sum(p => (double)p.WorkingSetBytes) / 1024 / 1024 / 1024;
            try
            {
                var hw = await AppServices.Hardware();
                _ramGb = hw.Memory.Sum(m => (double)m.CapacityBytes) / 1024 / 1024 / 1024;
                _laptop = hw.IsLaptop;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
            _profile = AppServices.Settings.ProcessProfile is { } saved && Enum.TryParse<PcProfile>(saved, out var p) ? p : ProcessProfiles.Recommend(_laptop, _ramGb);
            SetStatus("");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or System.Management.ManagementException)
        {
            SetStatus("Não deu para examinar tudo: " + ex.Message, "WarnBrush");
        }
        finally { _scanning = false; }
        Render();
    }

    private void Render()
    {
        // perfis
        _profiles.Children.Clear();
        var recommended = ProcessProfiles.Recommend(_laptop, _ramGb);
        foreach (var info in ProcessProfiles.All)
        {
            var p = info;
            int n = Pending(p.Id).Count + _startup.Count(e => e.Enabled && ProcessProfiles.Classify(e, p.Id).Verdict == StartupVerdict.CanWait);
            var tile = PadUi.SelectTile(info.Glyph, info.Name + (info.Id == recommended ? " · recomendado" : ""), $"{n} sugestões", info.Id == _profile, () => { _profile = p.Id; Render(); });
            tile.ToolTip = info.Description;
            _profiles.Children.Add(tile);
        }

        _lists.Children.Clear();
        _startupBoxes.Clear();
        _tweakBoxes.Clear();
        _processBoxes.Clear();

        // 1. inicialização
        var advice = _startup.Where(e => e.Enabled).Select(e => ProcessProfiles.Classify(e, _profile))
            .OrderBy(a => a.Verdict == StartupVerdict.CanWait ? 0 : 1).ThenBy(a => a.Entry.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var startupGrid = CompactPick.Grid(3, 300);
        foreach (var a in advice)
        {
            bool can = a.Verdict == StartupVerdict.CanWait;
            var box = new CheckBox { IsChecked = can, IsEnabled = a.Verdict != StartupVerdict.Protected };
            box.Checked += (_, _) => Count();
            box.Unchecked += (_, _) => Count();
            var tag = CompactPick.Tag(a.Verdict switch { StartupVerdict.CanWait => "Pode esperar", StartupVerdict.Protected => "Protegido", _ => "Recomendo manter" },
                can ? "OkBrush" : "MutedBrush");
            startupGrid.Children.Add(CompactPick.Card(box, CompactPick.ProgramIcon(ExePathOf(a.Entry.Command), a.Entry.Name), a.Entry.Name, a.Why, tag));
            _startupBoxes.Add((box, a.Entry));
        }
        AddSection("1. Programas que abrem com o Windows", "Marcados deixam de abrir sozinhos. Continuam instalados e abrem quando você quiser.",
            advice.Count == 0 ? Muted("Nenhum programa abre junto com o Windows.", 12) : startupGrid);

        // 2. ajustes do Windows
        var pending = Pending();
        var tweakGrid = CompactPick.Grid(3, 300);
        foreach (var tw in pending)
        {
            var box = new CheckBox { IsChecked = true };
            box.Checked += (_, _) => Count();
            box.Unchecked += (_, _) => Count();
            var icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = Brush("AccentBrush"), TextAlignment = TextAlignment.Center };
            TweakCard(tweakGrid, box, icon, tw);
            _tweakBoxes.Add((box, tw));
        }
        AddSection("2. Serviços e ajustes do Windows", "Tarefas em segundo plano que o perfil desliga ou deixa mais leves. Cada um tem volta pela Central Desfazer.",
            pending.Count == 0 ? Muted("Tudo certo: os ajustes deste perfil já estão feitos.", 12) : tweakGrid);
        if (ProcessProfiles.UsesGameMode(_profile))
            _lists.Children.Add(Muted("Enquanto você joga, o Modo Jogo ainda pausa OneDrive, atualizadores, Widgets e a busca do Windows — e devolve tudo quando o jogo fecha.", 11.5));

        // 3. processos abertos agora
        int self = Environment.ProcessId;
        var groups = _processes.Where(p => p.Id != self && !ProcessList.IsProtectedName(p.Name) && !NeverClose.Contains(p.Name))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.ToArray())
            .Where(g => g.All(p => p.Title is null) && g.Sum(p => p.WorkingSetBytes) > 60L * 1024 * 1024)
            .OrderByDescending(g => g.Sum(p => p.WorkingSetBytes)).Take(12).ToList();
        var procGrid = CompactPick.Grid(3, 300);
        foreach (var g in groups)
        {
            var box = new CheckBox { IsChecked = false };
            box.Checked += (_, _) => Count();
            box.Unchecked += (_, _) => Count();
            double mb = g.Sum(p => (double)p.WorkingSetBytes) / 1024 / 1024;
            procGrid.Children.Add(CompactPick.Card(box, CompactPick.Initials(g[0].Name, g[0].Name), g[0].Name,
                g.Length > 1 ? $"{g.Length} processos em segundo plano" : "Em segundo plano, sem janela", CompactPick.Tag(mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0} MB")));
            _processBoxes.Add((box, g));
        }
        AddSection("3. Rodando agora em segundo plano", "Os que mais gastam memória sem janela aberta. Marque só os que você reconhece: fecham agora e voltam quando você abrir de novo.",
            groups.Count == 0 ? Muted("Nada pesado rodando escondido agora.", 12) : procGrid);

        var info2 = ProcessProfiles.All.First(i => i.Id == _profile);
        _forecast.Foreground = Brush("MutedBrush");
        Count();

        // números do topo
        _stats.Children.Clear();
        _stats.Children.Add(Stat("", _startup.Count(e => e.Enabled).ToString(), "Abrem com o Windows"));
        _stats.Children.Add(Stat("", _processes.Count.ToString(), "Processos abertos"));
        _stats.Children.Add(Stat("", _ramGb > 0 ? $"{_ramUsedGb:0.0} de {_ramGb:0} GB" : $"{_ramUsedGb:0.0} GB", "Memória em uso"));
        _stats.Children.Add(Stat("", (advice.Count(a => a.Verdict == StartupVerdict.CanWait) + pending.Count).ToString(), $"Dá para otimizar · {info2.Name.Split(' ')[0]}"));
    }

    private void TweakCard(Panel grid, CheckBox box, FrameworkElement icon, Tweak tw) =>
        grid.Children.Add(CompactPick.Card(box, icon, tw.Title, tw.Description, tw.RequiresRestart ? CompactPick.Tag("Reiniciar") : null));

    private void AddSection(string title, string hint, UIElement content)
    {
        var head = Heading(title);
        head.Margin = new Thickness(0, 16, 0, 2);
        _lists.Children.Add(head);
        _lists.Children.Add(Muted(hint, 11.5));
        if (content is FrameworkElement fe) fe.Margin = new Thickness(0, 8, 0, 0);
        _lists.Children.Add(content);
    }

    private Border Stat(string glyph, string value, string label)
    {
        var row = new DockPanel();
        var badge = GlassFx.Badge(glyph, Color.FromRgb(0xFF, 0xC4, 0x00), 30);
        badge.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = value, FontSize = 17, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
        t.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(t);
        var card = CardOf(row);
        card.Margin = new Thickness(0, 0, 8, 8);
        return card;
    }

    private void Count()
    {
        int s = _startupBoxes.Count(b => b.Box.IsChecked == true), t = _tweakBoxes.Count(b => b.Box.IsChecked == true), p = _processBoxes.Count(b => b.Box.IsChecked == true);
        int total = s + t + p;
        bool game = ProcessProfiles.UsesGameMode(_profile);
        _apply.IsEnabled = !_scanning && (total > 0 || game && !AppServices.Boost.Settings.BoostAuto);
        _apply.Content = total > 0 ? $"Otimizar agora ({total})" : "Otimizar agora";
        _forecast.Text = $"{s} fora da inicialização · {t} ajuste(s) do Windows · {p} programa(s) fechados agora" + (game ? " · Modo Jogo automático" : "");
    }

    /// <summary>Caminho do executável na linha de comando da inicialização (para mostrar o ícone do programa).</summary>
    internal static string? ExePathOf(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string c = Environment.ExpandEnvironmentVariables(command.Trim());
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? c[..(exe + 4)] : null;
    }

    private List<Tweak> Pending(PcProfile? profile = null)
    {
        var ids = ProcessProfiles.Tweaks(profile ?? _profile, _ramGb);
        return AppServices.Tweaks.Where(tw => ids.Contains(tw.Id) && tw.IsCompatible && SafeApplied(tw) == false).ToList();
    }

    private static bool? SafeApplied(Tweak tw)
    {
        try { return tw.IsApplied(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or System.IO.IOException) { return null; }
    }

    private async Task ApplyAsync()
    {
        var info = ProcessProfiles.All.First(i => i.Id == _profile);
        var tweaks = _tweakBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.Tweak).ToList();
        var startup = _startupBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.Entry).ToList();
        var close = _processBoxes.Where(b => b.Box.IsChecked == true).SelectMany(b => b.Group).ToList();
        bool game = ProcessProfiles.UsesGameMode(_profile);
        if (MessageBox.Show($"Otimizar com o perfil {info.Name}?\n\n• {startup.Count} programa(s) deixam de abrir com o Windows\n• {tweaks.Count} ajuste(s) do Windows\n" +
                (close.Count > 0 ? $"• {close.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()} programa(s) em segundo plano fecham agora\n" : "") +
                (game ? "• Modo Jogo automático (pausa o resto quando o jogo abre)\n" : "") +
                "\nAntes, o MB cria um ponto de restauração. \"Desfazer\" volta tudo.", "Otimizar processos e inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _apply.IsEnabled = false;
        SetStatus("Criando o ponto de restauração...");
        await Task.Run(() => RestorePoints.CreateNow("MB Extreme: antes do perfil " + info.Name));
        SetStatus("Aplicando...");
        var s = AppServices.Settings;
        int off = 0;
        foreach (var e in startup)
        {
            try
            {
                StartupService.SetEnabled(e, false);
                if (!s.ProcessProfileStartup.Contains(e.Name)) s.ProcessProfileStartup.Add(e.Name);
                off++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or System.IO.IOException) { }
        }
        var outcomes = await Task.Run(() => TweakRunner.Run(tweaks.Select(t => new TweakWork(t, true)).ToList()));
        foreach (var o in outcomes.Where(o => o.Success)) if (!s.ProcessProfileTweaks.Contains(o.Tweak.Id)) s.ProcessProfileTweaks.Add(o.Tweak.Id);
        int closed = await Task.Run(() => close.Count(p => ProcessList.Kill(p.Id, p.Name, p.StartTime).Ok));
        if (game && !AppServices.Boost.Settings.BoostAuto) AppServices.Boost.SetAuto(true);
        s.ProcessProfile = _profile.ToString();
        s.Save();
        int failed = outcomes.Count(o => !o.Success);
        bool restart = outcomes.Any(o => o.Success && o.Tweak.RequiresRestart);
        SetStatus($"Pronto: {off} programa(s) fora da inicialização, {outcomes.Count(o => o.Success)} ajuste(s)" + (close.Count > 0 ? $" e {closed} processo(s) fechados." : ".")
            + (failed > 0 ? $" {failed} precisam do MB aberto como administrador." : "") + (restart ? " Reinicie o PC para terminar." : ""), failed > 0 ? "WarnBrush" : "OkBrush");
        await ScanAsync();
    }

    private async Task UndoAsync()
    {
        var s = AppServices.Settings;
        if (s.ProcessProfileTweaks.Count == 0 && s.ProcessProfileStartup.Count == 0) { SetStatus("Nenhum perfil aplicado por aqui."); return; }
        if (MessageBox.Show("Voltar tudo o que o perfil mudou (programas da inicialização e ajustes do Windows)?", "Processos e inicialização",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var e in StartupService.List().Where(e => s.ProcessProfileStartup.Contains(e.Name)))
        {
            try { StartupService.SetEnabled(e, true); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or System.IO.IOException) { }
        }
        var tweaks = AppServices.Tweaks.Where(t => s.ProcessProfileTweaks.Contains(t.Id) && t.HasBackup).ToList();
        await Task.Run(() => TweakRunner.Run(tweaks.Select(t => new TweakWork(t, false)).ToList()));
        s.ProcessProfileStartup.Clear();
        s.ProcessProfileTweaks.Clear();
        s.ProcessProfile = null;
        s.Save();
        SetStatus("Perfil desfeito: tudo voltou como era.", "OkBrush");
        await ScanAsync();
    }
}
