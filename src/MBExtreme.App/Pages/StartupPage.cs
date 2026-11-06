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

    private async void Reload()
    {
        Body.Children.Clear();
        var bootCard = new StackPanel();
        bootCard.Children.Add(new TextBlock { Text = "TEMPO PARA LIGAR", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Brush("MutedBrush") });
        var bootText = new TextBlock { Text = "Lendo...", FontSize = 20, FontWeight = FontWeights.SemiBold };
        var bootDetail = Muted("");
        bootCard.Children.Add(bootText);
        bootCard.Children.Add(bootDetail);
        Body.Children.Add(CardOf(bootCard));
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

        var described = entries.Select(e => (Entry: e, Info: StartupAdvisor.Describe(e.Name, e.Command))).ToList();
        int safe = described.Count(d => d.Entry.Enabled && d.Info.Advice is StartupAdvice.CanDisable or StartupAdvice.DisableIfUnused);
        SetStatus(entries.Count == 0 ? "Nenhum programa na inicialização." : $"{entries.Count(e => e.Enabled)} de {entries.Count} abrem junto com o Windows · {safe} ligado(s) costuma(m) ser seguro(s) de desligar.");
        foreach (var (entry, info) in described)
        {
            var e = entry;
            var box = new CheckBox { IsChecked = e.Enabled, Content = "Abre na inicialização", Foreground = Brush("TextBrush"), VerticalAlignment = VerticalAlignment.Center };
            box.Click += (_, _) =>
            {
                try
                {
                    StartupService.SetEnabled(e, box.IsChecked == true);
                    MBExtreme.Core.Operations.OperationJournal.Shared.RecordSetting("startup." + e.Name, (box.IsChecked == true ? "Abrir na inicialização: " : "Tirar da inicialização: ") + e.Name, true,
                        "Para desfazer: marque ou desmarque de novo em Inicialização.");
                    SetStatus(box.IsChecked == true ? $"{e.Name}: vai abrir na inicialização." : $"{e.Name}: não abre mais na inicialização.", "OkBrush");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
                {
                    box.IsChecked = !box.IsChecked;
                    SetStatus("Não foi possível alterar: " + ex.Message, "BadBrush");
                }
            };
            string brush = info.Advice switch { StartupAdvice.CanDisable => "OkBrush", StartupAdvice.DisableIfUnused => "WarnBrush", StartupAdvice.KeepOn => "BadBrush", _ => "MutedBrush" };
            var row = Row($"{e.Name}   ·   {StartupAdvisor.AdviceText(info.Advice)}", $"O que é: {info.WhatIs}\n{e.SourceText} · {e.Command}", box);
            if (row.Child is Grid g && g.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault() is { } title)
            {
                title.Inlines.Clear();
                title.Inlines.Add(new System.Windows.Documents.Run(e.Name));
                title.Inlines.Add(new System.Windows.Documents.Run("   " + StartupAdvisor.AdviceText(info.Advice)) { Foreground = Brush(brush), FontSize = 11.5 });
            }
            Body.Children.Add(row);
        }

        try
        {
            var boots = await Task.Run(StartupAdvisor.ReadBootTimes);
            if (boots.Count == 0)
            {
                bootText.Text = "Ainda sem medição";
                bootDetail.Text = "O Windows ainda não registrou quanto tempo leva para ligar. Reinicie o PC (Iniciar → Energia → Reiniciar), espere alguns minutos e clique em Atualizar.";
            }
            else
            {
                var last = boots[0];
                bootText.Text = $"{last.Boot.TotalSeconds:0.0} s";
                bootDetail.Text = $"Última inicialização: {last.At:dd/MM HH:mm}" + (last.MainPath is { } m ? $" · até a área de trabalho: {m.TotalSeconds:0.0} s" : "")
                    + (boots.Count > 1 ? " · antes: " + string.Join(", ", boots.Skip(1).Select(b => $"{b.Boot.TotalSeconds:0} s")) : "")
                    + ". Desligue o que não usa e compare depois de reiniciar.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            bootText.Text = "Não deu para ler";
            bootDetail.Text = ex.Message;
        }
    }
}
