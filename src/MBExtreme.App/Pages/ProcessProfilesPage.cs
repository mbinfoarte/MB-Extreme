using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Startup;
using MBExtreme.Core.Tweaks;
using MBExtreme.Core.Windows;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Processos e inicialização: examina o PC sozinho e aplica um perfil (Gamer, Turbo, Trabalho, Notebook, Streamer) com um clique.
/// Cada perfil mostra o que desliga já, o que só pausa no jogo (Modo Jogo) e o que fica de fora. Ponto de restauração antes; Desfazer tudo depois.
/// </summary>
public sealed class ProcessProfilesPage : ToolPageBase
{
    private readonly StackPanel _scan = new();
    private readonly System.Windows.Controls.Primitives.UniformGrid _profiles = new() { Columns = 5 };
    private readonly Grid _columns = new();
    private readonly TextBlock _forecast = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly Button _apply = new() { Padding = new Thickness(22, 10, 22, 10), FontSize = 14 };
    private IReadOnlyList<StartupEntry> _startup = Array.Empty<StartupEntry>();
    private int _processCount;
    private double _ramGb;
    private bool _laptop;
    private PcProfile _profile;
    private string _hogs = "";

    public ProcessProfilesPage() : base("Processos e inicialização",
        "Um clique: o MB examina o PC e mostra o que dá para desligar, o que só pausa enquanto você joga e o que fica.")
    {
        AddButton("Examinar de novo", async (_, _) => await ScanAsync(), false);
        AddButton("Escolher item por item", (_, _) => MainWindow.Current?.Go("startup"), false);
        AddButton("Desfazer perfil", async (_, _) => await UndoAsync(), false);
        Body.Children.Add(CardOf(_scan));
        Body.Children.Add(Heading("1. Escolha o jeito que você usa o PC"));
        Body.Children.Add(_profiles);
        Body.Children.Add(Heading("2. O que o perfil vai fazer neste PC"));
        Body.Children.Add(_columns);
        var bottom = new DockPanel();
        _apply.Style = (Style)FindResource("PrimaryButton");
        _apply.Click += async (_, _) => await ApplyAsync();
        DockPanel.SetDock(_apply, Dock.Right);
        bottom.Children.Add(_apply);
        var badge = GlassFx.Badge("", Color.FromRgb(0x3D, 0xDC, 0x97), 34);
        badge.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(badge, Dock.Left);
        bottom.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "Ponto de restauração antes · Desfazer perfil volta tudo", FontSize = 13, FontWeight = FontWeights.SemiBold });
        text.Children.Add(_forecast);
        bottom.Children.Add(text);
        var card = CardOf(bottom);
        card.Margin = new Thickness(0, 12, 0, 8);
        Body.Children.Add(card);
        Loaded += async (_, _) => { if (_processCount == 0) await ScanAsync(); };
    }

    private async Task ScanAsync()
    {
        _scan.Children.Clear();
        _scan.Children.Add(new TextBlock { Text = "Examinando o PC...", FontSize = 15, FontWeight = FontWeights.SemiBold });
        _scan.Children.Add(Muted("Lendo o que abre com o Windows, os processos abertos e os serviços.", 12));
        try
        {
            var (startup, count, hogs) = await Task.Run(() =>
            {
                var list = StartupService.List();
                int n = Process.GetProcesses().Length;
                var top = CpuHogs.Top(2);
                return (list, n, top);
            });
            _startup = startup;
            _processCount = count;
            _hogs = hogs.Count > 0 && hogs.Sum(h => h.Percent) > 15 ? string.Join(" e ", hogs.Select(h => $"\"{h.Name}\" ({h.Percent:0}%)")) : "";
            var hw = await AppServices.Hardware();
            _ramGb = hw.Memory.Sum(m => (double)m.CapacityBytes) / 1024 / 1024 / 1024;
            _laptop = hw.IsLaptop;
            _profile = AppServices.Settings.ProcessProfile is { } saved && Enum.TryParse<PcProfile>(saved, out var p) ? p : ProcessProfiles.Recommend(_laptop, _ramGb);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or System.Management.ManagementException)
        {
            SetStatus("Não deu para examinar tudo: " + ex.Message, "WarnBrush");
        }
        Render();
    }

    private void Render()
    {
        int auto = _startup.Count(e => e.Enabled);
        int canWait = _startup.Count(e => e.Enabled && ProcessProfiles.Classify(e, _profile).Verdict == StartupVerdict.CanWait);
        _scan.Children.Clear();
        var head = new DockPanel();
        var badge = GlassFx.Badge("", Color.FromRgb(0xFF, 0xC4, 0x00), 40, breathe: true);
        badge.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = $"Exame concluído · {_processCount} processos abertos, {auto} abrem com o Windows", FontSize = 15, FontWeight = FontWeights.SemiBold });
        t.Children.Add(Muted($"Achei {canWait} programa(s) que podem esperar você abrir e {Pending().Count} ajuste(s) do Windows ainda não feitos."
            + (_hogs.Length > 0 ? $" Agora quem mais usa o processador: {_hogs}." : ""), 12));
        head.Children.Add(t);
        _scan.Children.Add(head);

        _profiles.Children.Clear();
        var recommended = ProcessProfiles.Recommend(_laptop, _ramGb);
        foreach (var info in ProcessProfiles.All)
        {
            var p = info;
            int n = Pending(p.Id).Count + _startup.Count(e => e.Enabled && ProcessProfiles.Classify(e, p.Id).Verdict == StartupVerdict.CanWait);
            var tile = PadUi.SelectTile(info.Glyph, info.Name + (info.Id == recommended ? "  · recomendado" : ""), $"{n} itens", info.Id == _profile, () => { _profile = p.Id; Render(); });
            tile.ToolTip = info.Description;
            _profiles.Children.Add(tile);
        }

        // três colunas: desligar já, pausar no jogo, fica de fora
        _columns.Children.Clear();
        _columns.ColumnDefinitions.Clear();
        var now = Pending().Select(tw => tw.Title).ToList();
        now.InsertRange(0, _startup.Where(e => e.Enabled).Select(e => ProcessProfiles.Classify(e, _profile)).Where(a => a.Verdict == StartupVerdict.CanWait).Select(a => $"{a.Entry.Name} ao iniciar · {a.Why}"));
        var pause = ProcessProfiles.UsesGameMode(_profile)
            ? new List<string> { "OneDrive, Google Drive e Dropbox (voltam quando o jogo fecha)", "Atualizadores da Adobe, Google e Edge", "Widgets, Phone Link e Teams", "Busca do Windows e downloads do Windows Update" }
            : new List<string> { "Nada: este perfil não usa o Modo Jogo" };
        var keep = _startup.Where(e => e.Enabled).Select(e => ProcessProfiles.Classify(e, _profile))
            .Where(a => a.Verdict is StartupVerdict.Protected or StartupVerdict.Keep).Select(a => $"{a.Entry.Name} · {a.Why}").ToList();
        if (keep.Count == 0) keep.Add("Antivírus, áudio, rede, drivers e Windows Update nunca são tocados");
        AddColumn(0, "Desligar agora (seguro)", Color.FromRgb(0x3D, 0xDC, 0x97), now);
        AddColumn(1, "Pausar enquanto joga", Color.FromRgb(0xFF, 0xA9, 0x4D), pause);
        AddColumn(2, "Fica de fora", Color.FromRgb(0x8E, 0x8E, 0x96), keep);

        var info2 = ProcessProfiles.All.First(i => i.Id == _profile);
        _apply.Content = $"Aplicar perfil {info2.Name.Split(' ')[0]}";
        _apply.IsEnabled = now.Count > 0 || ProcessProfiles.UsesGameMode(_profile);
        _forecast.Text = $"{info2.Description} Previsão: {now.Count} item(ns) a menos rodando sem você pedir.";
        _forecast.Foreground = Brush("MutedBrush");
    }

    private void AddColumn(int index, string title, Color color, IReadOnlyList<string> items)
    {
        if (index > 0) _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        _columns.ColumnDefinitions.Add(new ColumnDefinition());
        var st = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var count = new TextBlock { Text = items.Count.ToString(), Foreground = Brush("MutedBrush"), FontSize = 12 };
        DockPanel.SetDock(count, Dock.Right);
        head.Children.Add(count);
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(dot, Dock.Left);
        head.Children.Add(dot);
        head.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        st.Children.Add(head);
        foreach (string item in items.Take(14))
            st.Children.Add(new Border
            {
                Background = Brush("Surface2Brush"), CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 5),
                Child = new TextBlock { Text = item, FontSize = 12, TextWrapping = TextWrapping.Wrap },
            });
        if (items.Count > 14) st.Children.Add(Muted($"e mais {items.Count - 14}", 11));
        var card = CardOf(st);
        card.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(card, index * 2);
        _columns.Children.Add(card);
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
        var tweaks = Pending();
        var startup = _startup.Where(e => e.Enabled && ProcessProfiles.Classify(e, _profile).Verdict == StartupVerdict.CanWait).ToList();
        if (MessageBox.Show($"Aplicar o perfil {info.Name}?\n\n• {startup.Count} programa(s) deixam de abrir com o Windows\n• {tweaks.Count} ajuste(s) do Windows\n" +
                (ProcessProfiles.UsesGameMode(_profile) ? "• Modo Jogo automático (pausa o resto quando o jogo abre)\n" : "") +
                "\nAntes, o MB cria um ponto de restauração. \"Desfazer perfil\" volta tudo.", "Processos e inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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
        if (ProcessProfiles.UsesGameMode(_profile) && !AppServices.Boost.Settings.BoostAuto) AppServices.Boost.SetAuto(true);
        s.ProcessProfile = _profile.ToString();
        s.Save();
        int failed = outcomes.Count(o => !o.Success);
        bool restart = outcomes.Any(o => o.Success && o.Tweak.RequiresRestart);
        SetStatus($"Perfil {info.Name} aplicado: {off} programa(s) fora da inicialização e {outcomes.Count(o => o.Success)} ajuste(s)."
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
