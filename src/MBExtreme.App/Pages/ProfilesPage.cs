using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Tweaks;

namespace MBExtreme.UI.Pages;

/// <summary>Perfis prontos de ajustes (Jogo, Competitivo, PC fraco) e restauração de tudo.</summary>
public sealed class ProfilesPage : ToolPageBase
{
    public ProfilesPage() : base("Perfis",
        "Conjuntos prontos de ajustes de risco baixo. Cada ajuste guarda o valor original e pode ser desfeito a qualquer momento. Depois de aplicar, use Antes e depois para medir o efeito no seu PC.")
    {
        AddButton("Restaurar tudo ao original", async (_, _) => await RestoreAllAsync(), false);
        Loaded += (_, _) => Build();
    }

    private void Build()
    {
        Body.Children.Clear();
        foreach (var profile in TweakProfiles.Build(AppServices.Tweaks))
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = profile.Name, FontSize = 16, FontWeight = FontWeights.SemiBold });
            text.Children.Add(Muted(profile.Description, 13));
            text.Children.Add(Muted($"{profile.Tweaks.Count} ajustes: " + string.Join(" · ", profile.Tweaks.Select(t => t.Title)), 11));
            text.Children.Add(Muted(profile.Tweaks.Any(t => t.RequiresRestart) ? "Alguns ajustes só valem depois de reiniciar o PC." : "", 11));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(text);
            var button = SmallButton("Aplicar", async (_, _) => await ApplyAsync(profile), true);
            button.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
            Body.Children.Add(CardOf(grid));
        }
    }

    private async Task ApplyAsync(TweakProfile profile)
    {
        var answer = MessageBox.Show(
            $"Aplicar o perfil \"{profile.Name}\" ({profile.Tweaks.Count} ajustes)?\n\nO valor original de cada ajuste é guardado e você pode restaurar depois.",
            "Aplicar perfil", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await RunAsync($"Aplicando o perfil {profile.Name}", profile.Tweaks.Select(t => new TweakWork(t, true)).ToList());
    }

    private async Task RestoreAllAsync()
    {
        var withBackup = AppServices.Tweaks.Where(t => t.HasBackup).ToList();
        if (withBackup.Count == 0)
        {
            SetStatus("Nenhum ajuste foi aplicado ainda: não há nada para restaurar.");
            return;
        }

        var answer = MessageBox.Show($"Restaurar o valor original de {withBackup.Count} ajuste(s)?", "Restaurar tudo",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await RunAsync("Restaurando os ajustes", withBackup.Select(t => new TweakWork(t, false)).ToList());
    }

    private async Task RunAsync(string title, IReadOnlyList<TweakWork> work)
    {
        ShowProgress(title, "Ajustes");
        var token = Cts!.Token;
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var outcomes = await Task.Run(() => TweakRunner.Run(work, progress, token));
            int failed = outcomes.Count(o => !o.Success);
            Progress.Finish(failed == 0 ? $"{outcomes.Count} ajuste(s) concluído(s)" : $"{outcomes.Count - failed} concluído(s), {failed} com falha",
                failed == 0, string.Join("\n", outcomes.Where(o => !o.Success).Take(4).Select(o => $"{o.Tweak.Title}: {o.Error}")));
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido. O que já foi aplicado continua e pode ser restaurado.", false);
        }
    }
}
