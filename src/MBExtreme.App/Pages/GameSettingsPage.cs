using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Recursos do próprio Windows para jogos: Modo de Jogo, agendamento de GPU e plano de energia.</summary>
public sealed class GameSettingsPage : ToolPageBase
{
    private readonly GameSettingsService _service = new(AppServices.TweakStore);

    public GameSettingsPage() : base("Jogos e energia",
        "Recursos que o Windows já tem para jogar melhor. Cada mudança guarda o valor original e pode ser desfeita.")
    {
        AddButton("Atualizar", (_, _) => Reload(), false);
        AddButton("Restaurar originais", (_, _) =>
        {
            try
            {
                _service.RestoreAll();
                MBExtreme.Core.Operations.OperationJournal.Shared.MarkRolledBackWhere(e => e.OperationId.StartsWith("gamesettings.", StringComparison.Ordinal), DateTimeOffset.Now);
                SetStatus("Valores originais restaurados.", "OkBrush");
            }
            catch (Exception ex)
            {
                SetStatus("Não foi possível restaurar: " + ex.Message, "BadBrush");
            }
            Reload(keepStatus: true);
        }, false);
        AddButton("Mais ajustes para jogos", (_, _) => MainWindow.Current?.Go("tweaks-game"), false);
        Loaded += (_, _) => Reload();
    }

    private void Reload(bool keepStatus = false)
    {
        if (!keepStatus) SetStatus("");
        Body.Children.Clear();

        bool gameMode = _service.GameModeEnabled();
        Body.Children.Add(Row("Modo de Jogo",
            "Faz o Windows priorizar o jogo e evitar interrupções (como atualizações) durante a partida. Recomendado: ligado.",
            SmallButton(gameMode ? "Desligar" : "Ligar", (_, _) => Apply("gamesettings.gamemode", gameMode ? "Desligar o Modo de Jogo" : "Ligar o Modo de Jogo", () => _service.SetGameMode(!gameMode)))));

        bool? hags = _service.HagsEnabled();
        string hagsText = hags switch { true => "Ligado", false => "Desligado", null => "Padrão do Windows" };
        Body.Children.Add(Row($"Agendamento de GPU acelerado por hardware: {hagsText}",
            "A placa de vídeo gerencia a própria memória. Pode ajudar ou piorar conforme a placa e o driver (precisa de GPU recente e driver atual). Exige reiniciar o PC. Risco: médio.",
            SmallButton(hags == true ? "Desligar" : "Ligar", (_, _) => Apply("gamesettings.hags", hags == true ? "Desligar o agendamento de GPU (HAGS)" : "Ligar o agendamento de GPU (HAGS)", () => _service.SetHags(hags != true))), "WarnBrush"));

        Body.Children.Add(Heading("Plano de energia"));
        try
        {
            foreach (var plan in _service.PowerPlans())
            {
                var button = plan.Active
                    ? new TextBlock { Text = "Ativo", Foreground = Brush("OkBrush"), FontWeight = FontWeights.SemiBold }
                    : (UIElement)SmallButton("Ativar", (_, _) => Apply("gamesettings.plan", "Ativar o plano de energia " + plan.Name, () => _service.ActivatePlan(plan.Guid)));
                Body.Children.Add(Row(plan.Name, plan.Guid, button));
            }
        }
        catch (Exception ex)
        {
            Body.Children.Add(Muted("Não foi possível listar os planos: " + ex.Message));
        }

        Body.Children.Add(Muted("Notebook: o plano de alto desempenho gasta mais bateria e esquenta mais."));
    }

    private const string UndoHint = "Para desfazer: \"Restaurar originais\" em Jogos e energia.";

    private void Apply(string id, string title, Action change)
    {
        try
        {
            change();
            MBExtreme.Core.Operations.OperationJournal.Shared.RecordSetting(id, title, true, UndoHint);
            SetStatus("Aplicado. Alguns ajustes só valem depois de reiniciar.", "OkBrush");
        }
        catch (Exception ex)
        {
            MBExtreme.Core.Operations.OperationJournal.Shared.RecordSetting(id, title, false, UndoHint, ex.Message);
            SetStatus("Não foi possível aplicar: " + ex.Message, "BadBrush");
        }
        Reload(keepStatus: true);
    }
}
