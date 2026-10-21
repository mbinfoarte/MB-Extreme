using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Startup;

namespace MBExtreme.UI.Pages;

/// <summary>Programas que abrem junto com o Windows: liga e desliga sem apagar nada (igual ao Gerenciador de Tarefas).</summary>
public sealed class StartupPage : ToolPageBase
{
    public StartupPage() : base("Inicialização",
        "Programas que abrem sozinhos quando o Windows liga. Desligar não apaga nada: o programa só deixa de abrir na inicialização e você pode ligar de novo aqui. É o jeito mais simples de deixar o PC mais rápido para ligar e com mais RAM livre.")
    {
        AddButton("Atualizar", (_, _) => Reload(), false);
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        Body.Children.Clear();
        IReadOnlyList<StartupEntry> entries;
        try
        {
            entries = StartupService.List();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            SetStatus("Não foi possível ler a lista: " + ex.Message, "BadBrush");
            return;
        }

        SetStatus(entries.Count == 0 ? "Nenhum programa na inicialização." : $"{entries.Count(e => e.Enabled)} de {entries.Count} abrem junto com o Windows.");
        foreach (var entry in entries)
        {
            var e = entry;
            var box = new CheckBox { IsChecked = e.Enabled, Content = "Abre na inicialização", Foreground = Brush("TextBrush"), VerticalAlignment = VerticalAlignment.Center };
            box.Click += (_, _) =>
            {
                try
                {
                    StartupService.SetEnabled(e, box.IsChecked == true);
                    SetStatus(box.IsChecked == true ? $"{e.Name}: vai abrir na inicialização." : $"{e.Name}: não abre mais na inicialização.", "OkBrush");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
                {
                    box.IsChecked = !box.IsChecked;
                    SetStatus("Não foi possível alterar: " + ex.Message, "BadBrush");
                }
            };
            Body.Children.Add(Row(e.Name, $"{e.SourceText}\n{e.Command}", box));
        }
    }
}
