using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Games;

namespace MBExtreme.UI.Pages;

/// <summary>Biblioteca de jogos: acha os jogos instalados (Steam, Epic, GOG e os que você adicionar), liga o Modo Jogo e abre o jogo daqui.</summary>
public sealed class GameLibraryPage : ToolPageBase
{
    private readonly StackPanel _list = new();
    private readonly TextBlock _tier = new() { TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<InstalledGame> _games = Array.Empty<InstalledGame>();
    private bool _busy;

    public GameLibraryPage() : base("Biblioteca de jogos",
        "Seus jogos instalados num só lugar. Ao abrir um jogo por aqui, o MB Extreme liga o Modo Jogo (plano de energia, programas de fundo, prioridade) e desfaz tudo quando você fecha o jogo. Também soma o tempo que você joga.")
    {
        AddButton("Procurar jogos", async (_, _) => await ScanAsync());
        AddButton("Adicionar jogo...", (_, _) => AddManual(), false);

        Body.Children.Add(CardOf(_tier));
        Body.Children.Add(Heading("Seus jogos"));
        Body.Children.Add(_list);
        Body.Children.Add(Muted("A Steam, a Epic e o GOG são lidos só para listar os jogos; nenhum arquivo deles é alterado. \"Prioridade alta\" usa o recurso do próprio Windows (Image File Execution Options) e é removida ao desligar."));
        Loaded += async (_, _) =>
        {
            await LoadTierAsync();
            await ScanAsync();
        };
    }

    private async Task LoadTierAsync()
    {
        try
        {
            var hw = await AppServices.Hardware();
            var advice = GameAdvisor.Recommend(hw);
            _tier.Text = $"Seu PC: {advice.Tier}. {advice.Headline}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            _tier.Text = "Não foi possível ler o hardware agora.";
        }
    }

    private async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        SetStatus("Procurando jogos...");
        try
        {
            _games = await Task.Run(() => GameScanner.Scan(AppServices.GameLibrary));
            SetStatus(_games.Count == 0 ? "Nenhum jogo encontrado. Use Adicionar jogo para escolher o executável." : $"{_games.Count} jogo(s) encontrado(s).", _games.Count == 0 ? "WarnBrush" : "OkBrush");
            Render();
        }
        finally
        {
            _busy = false;
        }
    }

    private void Render()
    {
        _list.Children.Clear();
        if (_games.Count == 0)
        {
            _list.Children.Add(Muted("Nenhum jogo na lista."));
            return;
        }

        foreach (var game in _games)
        {
            var current = game;
            var settings = AppServices.GameLibrary.Get(current.Id);
            string played = settings.MinutesPlayed < 1 ? "ainda sem registro de tempo" : $"{settings.MinutesPlayed / 60:0.#} h jogadas" + (settings.LastPlayed is { } last ? $" · último: {last:dd/MM}" : "");
            string detail = $"{current.LauncherName} · {played}" + (current.ExeName is null ? " · executável não identificado" : $" · {current.ExeName}");

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            if (current.ExeName is not null)
            {
                bool applied = false;
                try { applied = AppServices.GamePriority.IsApplied(current.ExeName); } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
                buttons.Children.Add(SmallButton(applied ? "Prioridade alta: ligada" : "Prioridade alta", (_, _) => TogglePriority(current, applied), applied));
            }
            buttons.Children.Add(SmallButton("Jogar", (_, _) => Play(current), true));
            if (current.Launcher == GameLauncher.Manual)
                buttons.Children.Add(SmallButton("Remover", (_, _) => { AppServices.GameLibrary.RemoveManual(current.LaunchTarget ?? ""); _ = ScanAsync(); }));
            _list.Children.Add(Row(current.Name, detail, buttons));
        }
    }

    private void TogglePriority(InstalledGame game, bool applied)
    {
        try
        {
            if (applied) AppServices.GamePriority.Revert(game.ExeName!);
            else AppServices.GamePriority.Apply(game.ExeName!);
            AppServices.GameLibrary.Update(game.Id, s => { s.HighPriority = !applied; s.ExeName = game.ExeName; });
            SetStatus(applied ? $"Prioridade de {game.Name} voltou ao normal." : $"{game.Name} abrirá com prioridade alta de CPU.", "OkBrush");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            SetStatus("Não foi possível alterar (execute o MB Extreme como administrador): " + ex.Message, "BadBrush");
        }

        Render();
    }

    private void Play(InstalledGame game)
    {
        if (string.IsNullOrWhiteSpace(game.LaunchTarget))
        {
            SetStatus("Não sei como abrir este jogo. Abra pela loja dele.", "WarnBrush");
            return;
        }

        if (game.ExeName is { } exe)
        {
            string process = Path.GetFileNameWithoutExtension(exe);
            AppServices.GameLibrary.Update(game.Id, s => s.ExeName = exe);
            var settings = AppServices.Boost.Settings;
            if (!settings.GameProcesses.Contains(process, StringComparer.OrdinalIgnoreCase))
            {
                settings.GameProcesses.Add(process);
                settings.Save();
            }
            AppServices.Boost.RefreshGames();
            if (!settings.BoostAuto) AppServices.Boost.SetAuto(true);
        }

        try
        {
            Process.Start(new ProcessStartInfo(game.LaunchTarget) { UseShellExecute = true, WorkingDirectory = game.Launcher == GameLauncher.Manual ? game.InstallPath : "" });
            SetStatus(game.ExeName is null
                ? $"Abrindo {game.Name}..."
                : $"Abrindo {game.Name}. O Modo Jogo liga sozinho quando o jogo aparecer e desliga quando você fechar.", "OkBrush");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            SetStatus("Não foi possível abrir: " + ex.Message, "BadBrush");
        }
    }

    private void AddManual()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Programas (*.exe)|*.exe", Title = "Escolha o executável do jogo" };
        if (dialog.ShowDialog() != true) return;
        string name = Path.GetFileNameWithoutExtension(dialog.FileName);
        AppServices.GameLibrary.AddManual(name, dialog.FileName);
        _ = ScanAsync();
    }
}
