using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Games;

namespace MBExtreme.UI.Pages;

/// <summary>Modo Jogo do MB Extreme: prepara o PC quando um jogo abre e devolve tudo quando ele fecha.</summary>
public sealed class GameBoostPage : ToolPageBase
{
    private readonly StackPanel _status = new();
    private readonly StackPanel _games = new();
    private readonly StackPanel _apps = new();
    private readonly StackPanel _running = new();
    private readonly Button _toggle;

    public GameBoostPage() : base("Modo Jogo",
        "Quando um jogo da sua lista abre, o MB Extreme troca para o plano de energia de alto desempenho, pede para fechar os programas pesados que você escolher (sem forçar), tira o jogo do modo de eficiência do Windows, sobe a prioridade dele e, se quiser, libera RAM. Ao fechar o jogo, o plano de energia volta ao que era.")
    {
        _toggle = AddButton("Ativar agora", (_, _) => Toggle());
        AddButton("Atualizar listas", (_, _) => Build(), false);

        Body.Children.Add(_status);
        Body.Children.Add(Heading("Opções"));
        Body.Children.Add(OptionBox("Ativar sozinho quando um jogo da lista abrir", AppServices.Boost.Settings.BoostAuto, v => AppServices.Boost.SetAuto(v)));
        Body.Children.Add(OptionBox("Trocar para o plano de energia de alto desempenho", AppServices.Boost.Settings.BoostSwitchPlan, v => Save(s => s.BoostSwitchPlan = v)));
        Body.Children.Add(OptionBox("Liberar memória RAM ao ativar", AppServices.Boost.Settings.BoostCleanMemory, v => Save(s => s.BoostCleanMemory = v)));

        Body.Children.Add(Heading("Seus jogos"));
        Body.Children.Add(_games);
        Body.Children.Add(Muted("Abra o jogo e clique no programa dele na lista abaixo para adicioná-lo."));
        Body.Children.Add(_running);

        Body.Children.Add(Heading("Programas para fechar ao jogar"));
        Body.Children.Add(Muted("O MB Extreme pede para o programa fechar (como se você clicasse no X). Nada é encerrado à força, então você não perde dados."));
        Body.Children.Add(_apps);

        AppServices.Boost.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(RenderStatus));
        Loaded += (_, _) => Build();
    }

    private CheckBox OptionBox(string text, bool value, Action<bool> onChange)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 3, 0, 3) };
        box.Click += (_, _) => onChange(box.IsChecked == true);
        return box;
    }

    private static void Save(Action<MBExtreme.Core.Settings.AppSettings> change)
    {
        change(AppServices.Boost.Settings);
        AppServices.Boost.Settings.Save();
    }

    private void Toggle()
    {
        try
        {
            if (AppServices.Boost.IsActive) AppServices.Boost.Deactivate();
            else AppServices.Boost.ActivateNow();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível alterar o Modo Jogo: " + ex.Message, "BadBrush");
        }
    }

    private void Build()
    {
        RenderStatus();
        var settings = AppServices.Boost.Settings;

        _games.Children.Clear();
        if (settings.GameProcesses.Count == 0) _games.Children.Add(Muted("Nenhum jogo na lista ainda."));
        foreach (string game in settings.GameProcesses.ToList())
        {
            string g = game;
            _games.Children.Add(Row(g + ".exe", "Ativa o Modo Jogo quando este programa abre.",
                SmallButton("Remover", (_, _) =>
                {
                    settings.GameProcesses.Remove(g);
                    settings.Save();
                    Build();
                })));
        }

        _running.Children.Clear();
        var windows = new List<(string Name, string Title)>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle) && p.Id != Environment.ProcessId)
                            windows.Add((p.ProcessName, p.MainWindowTitle));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
            }
        }
        catch (InvalidOperationException) { }

        foreach (var w in windows.DistinctBy(w => w.Name).OrderBy(w => w.Name).Take(25))
        {
            if (settings.GameProcesses.Contains(w.Name, StringComparer.OrdinalIgnoreCase)) continue;
            string name = w.Name;
            _running.Children.Add(Row(name + ".exe", w.Title,
                SmallButton("Adicionar como jogo", (_, _) =>
                {
                    settings.GameProcesses.Add(name);
                    settings.Save();
                    Build();
                })));
        }

        _apps.Children.Clear();
        var candidates = GameBoostService.SuggestedCloseApps.Concat(settings.BoostCloseApps).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string app in candidates)
        {
            string a = app;
            bool running = Process.GetProcessesByName(a).Length > 0;
            var box = new CheckBox
            {
                IsChecked = settings.BoostCloseApps.Contains(a, StringComparer.OrdinalIgnoreCase),
                Content = a + (running ? "  (aberto agora)" : ""),
                Foreground = Brush("TextBrush"),
                Margin = new Thickness(0, 3, 0, 3),
            };
            box.Click += (_, _) =>
            {
                if (box.IsChecked == true) { if (!settings.BoostCloseApps.Contains(a, StringComparer.OrdinalIgnoreCase)) settings.BoostCloseApps.Add(a); }
                else settings.BoostCloseApps.RemoveAll(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
                settings.Save();
            };
            _apps.Children.Add(box);
        }
    }

    private void RenderStatus()
    {
        var boost = AppServices.Boost;
        _toggle.Content = boost.IsActive ? "Desativar" : "Ativar agora";
        _status.Children.Clear();
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = boost.IsActive ? "Modo Jogo ativo" + (boost.ActiveGame is { } g && g != "manual" ? $" ({g})" : "") : "Modo Jogo desligado",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(boost.IsActive ? "OkBrush" : "TextBrush"),
        });
        if (boost.IsActive && boost.LastReport is { } r)
        {
            text.Children.Add(Muted(
                (r.PowerPlan is { } p ? $"Plano de energia: {p}. " : "") +
                (r.Closed.Count > 0 ? $"Fechou: {string.Join(", ", r.Closed)}. " : "") +
                (r.PriorityRaised ? "Prioridade do jogo aumentada. " : "") +
                (r.MemoryFreedBytes > 0 ? $"RAM liberada: {r.MemoryFreedBytes / 1024.0 / 1024:0} MB." : "")));
        }
        else
        {
            text.Children.Add(Muted(boost.Settings.BoostAuto
                ? "Vigiando a lista de jogos. Ele liga sozinho quando um jogo abrir."
                : "Ligue \"Ativar sozinho\" e adicione seus jogos, ou use \"Ativar agora\"."));
        }

        _status.Children.Add(CardOf(text));
    }
}
