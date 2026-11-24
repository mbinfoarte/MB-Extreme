using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Info;

namespace MBExtreme.UI.Pages;

/// <summary>Informações de hardware e sistema (estilo CPU-Z, GPU-Z e AIDA).</summary>
public sealed class SystemInfoPage : ToolPageBase
{
    private IReadOnlyList<InfoSection> _sections = Array.Empty<InfoSection>();

    public SystemInfoPage() : base("Informações do sistema",
        "Processador, placa-mãe, memória, placa de vídeo, discos, rede e Windows em um só lugar. Só leitura: nada é alterado.")
    {
        AddButton("Atualizar", async (_, _) => await RunOnce(LoadAsync));
        AddButton("Copiar relatório", (_, _) =>
        {
            if (_sections.Count == 0) return;
            if (TryCopy(SystemInfoService.ToText(_sections))) SetStatus("Relatório copiado.", "OkBrush");
        }, false);
        AddButton("Salvar relatório", (_, _) => Save(), false);
        Loaded += async (_, _) => { if (_sections.Count == 0) await RunOnce(LoadAsync); };
    }

    private async Task LoadAsync()
    {
        SetStatus("Lendo o hardware...");
        Body.Children.Clear();
        try
        {
            _sections = await Task.Run(SystemInfoService.Collect);
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível ler: " + ex.Message, "BadBrush");
            return;
        }

        SetStatus("");
        foreach (var section in _sections)
        {
            Body.Children.Add(Heading(section.Title));
            var panel = new StackPanel();
            foreach (var row in section.Rows)
            {
                var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.Children.Add(Muted(row.Label, 13));
                var value = new TextBlock { Text = row.Value, FontSize = 13, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(value, 1);
                grid.Children.Add(value);
                panel.Children.Add(grid);
            }
            Body.Children.Add(CardOf(panel));
        }
    }

    private void Save()
    {
        if (_sections.Count == 0) return;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MB Extreme");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"sistema-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, SystemInfoService.ToText(_sections));
            SetStatus("Salvo em " + file, "OkBrush");
        }
        catch (IOException ex)
        {
            SetStatus("Não foi possível salvar: " + ex.Message, "BadBrush");
        }
    }
}
