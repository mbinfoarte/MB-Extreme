using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Cleanup;

namespace MBExtreme.UI.Pages;

/// <summary>Limpeza semanal automática: uma tarefa agendada do Windows limpa os arquivos descartáveis seguros todo domingo.</summary>
public sealed class WeeklyPage : ToolPageBase
{
    private readonly TextBlock _state = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
    private readonly TextBlock _log = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12 };
    private Button? _toggle;

    public WeeklyPage() : base("Limpeza semanal",
        "Agenda uma limpeza automática todo domingo às 12:00 (se o PC estiver ligado). Ela apaga só o que está marcado por padrão na página Limpeza (temporários, cache de navegadores, relatórios de erro...), sem abrir janela, sem reiniciar e sem mexer em ajustes. O resultado fica registrado abaixo.")
    {
        _toggle = AddButton("Agendar", async (_, _) => await ToggleAsync());
        AddButton("Limpar agora (modo semanal)", async (_, _) => await RunNowAsync(), false);

        Body.Children.Add(Heading("Situação"));
        Body.Children.Add(CardOf(_state));
        Body.Children.Add(Muted("Reparo do sistema (SFC e DISM) não entra na rotina automática porque leva muito tempo; rode em Reparo do Windows quando houver sintomas."));
        Body.Children.Add(Heading("Últimas execuções"));
        Body.Children.Add(CardOf(_log));
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        bool scheduled = await Task.Run(WeeklyRoutine.IsScheduled);
        _state.Text = scheduled ? "Agendada: todo domingo às 12:00." : "Não agendada.";
        if (_toggle is not null) _toggle.Content = scheduled ? "Remover agendamento" : "Agendar";
        string text = "Nenhuma execução registrada ainda.";
        try
        {
            if (File.Exists(WeeklyRoutine.LogPath)) text = string.Join(Environment.NewLine, File.ReadAllLines(WeeklyRoutine.LogPath).TakeLast(20));
        }
        catch (IOException) { }
        _log.Text = text;
    }

    private async Task ToggleAsync()
    {
        bool scheduled = await Task.Run(WeeklyRoutine.IsScheduled);
        string? exe = Environment.ProcessPath;
        var (ok, message) = scheduled ? await Task.Run(WeeklyRoutine.Unschedule) : await Task.Run(() => WeeklyRoutine.Schedule(exe ?? ""));
        SetStatus(message, ok ? "OkBrush" : "BadBrush");
        await RefreshAsync();
    }

    private async Task RunNowAsync()
    {
        SetStatus("Limpando...");
        string text = await Task.Run(() => WeeklyRoutine.RunClean(DateTime.UtcNow));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WeeklyRoutine.LogPath)!);
            File.AppendAllText(WeeklyRoutine.LogPath, text + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        SetStatus(text.Split('\n')[0].Trim(), "OkBrush");
        await RefreshAsync();
    }
}
