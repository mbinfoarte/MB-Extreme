using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Pontos de restauração: lista, cria com nome, apaga e volta o Windows para uma data em que tudo funcionava.</summary>
public sealed class RestorePointsPage : ToolPageBase
{
    private readonly TextBox _name = new() { Padding = new Thickness(8, 5, 8, 5), MinWidth = 360, Margin = new Thickness(0, 0, 8, 0) };
    private bool _loaded;

    public RestorePointsPage() : base("Pontos de restauração",
        "Veja os pontos de restauração do PC, crie um novo antes de mexer em algo e volte o Windows para uma data em que tudo funcionava. Seus arquivos pessoais não são mexidos.")
    {
        AddButton("Atualizar", async (_, _) => await LoadAsync(), false);
        AddButton("Configurar no Windows", (_, _) => Start("SystemPropertiesProtection.exe"), false);
        AddButton("Assistente do Windows", (_, _) => Start("rstrui.exe"), false);
        System.Windows.Automation.AutomationProperties.SetName(_name, "Nome do ponto de restauração");
        Loaded += async (_, _) => { if (!_loaded) await LoadAsync(); };
    }

    private static void Start(string exe)
    {
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private async Task LoadAsync()
    {
        _loaded = true;
        Body.Children.Clear();
        SetStatus("Lendo os pontos de restauração...");
        IReadOnlyList<RestorePoint> points;
        bool? on;
        ShadowStorage? storage;
        try
        {
            (points, on, storage) = await Task.Run(() => (RestorePoints.List(), RestorePoints.ProtectionOn(), RestorePoints.ReadShadowStorage()));
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            SetStatus("Não foi possível ler os pontos: " + ex.Message, "BadBrush");
            return;
        }

        var status = new StackPanel();
        status.Children.Add(new TextBlock
        {
            Text = on switch { true => "✔  Proteção do Sistema ligada no disco do Windows", false => "✖  Proteção do Sistema desligada", _ => "Proteção do Sistema: não deu para ler" },
            FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush(on == false ? "BadBrush" : on == true ? "OkBrush" : "MutedBrush"),
        });
        if (on == false) status.Children.Add(Muted("Sem a proteção ligada o Windows não cria pontos. Ligue em \"Configurar no Windows\" (Configurar → Ativar proteção do sistema)."));
        if (storage is { Max: > 0 } s)
        {
            status.Children.Add(Muted($"Espaço usado pelos pontos: {Size(s.Used)} de {Size(s.Max)} no máximo"));
            status.Children.Add(new ProgressBar { Maximum = s.Max, Value = s.Used, Height = 6, Margin = new Thickness(0, 6, 0, 0) });
        }
        Body.Children.Add(CardOf(status));

        var create = new StackPanel { Orientation = Orientation.Horizontal };
        create.Children.Add(UiTree.Free(_name));
        create.Children.Add(SmallButton("Criar ponto agora", async (_, _) => await CreateAsync(), true));
        var createCard = new StackPanel();
        createCard.Children.Add(new TextBlock { Text = "Criar um ponto agora", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        createCard.Children.Add(create);
        createCard.Children.Add(Muted("Dê um nome (ex.: Antes de instalar o driver da impressora). Leva menos de um minuto.", 11.5));
        Body.Children.Add(CardOf(createCard));

        Body.Children.Add(Heading($"Pontos neste PC ({points.Count})"));
        if (points.Count == 0) Body.Children.Add(Muted("Nenhum ponto de restauração."));
        bool first = true;
        foreach (var p in points)
        {
            var point = p;
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(SmallButton("Voltar para este ponto", async (_, _) => await RestoreAsync(point)));
            right.Children.Add(SmallButton("Apagar", async (_, _) => await DeleteAsync(point)));
            Body.Children.Add(Row(p.Description + (first ? "   · mais recente" : ""), $"{p.Created:dd/MM/yyyy HH:mm} · {p.TypeText}", right));
            first = false;
        }
        SetStatus(points.Count == 0 ? "Nenhum ponto ainda." : $"{points.Count} ponto(s). O mais recente é de {points[0].Created:dd/MM/yyyy HH:mm}.");
    }

    private static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GB" : $"{bytes / (double)(1L << 20):0} MB";

    private async Task CreateAsync()
    {
        string name = _name.Text.Trim();
        SetStatus("Criando o ponto de restauração...");
        var (ok, error) = await Task.Run(() => RestorePoints.CreateNow(name.Length > 0 ? name : "Ponto criado no MB Extreme"));
        SetStatus(ok ? "Ponto criado." : "Não foi possível criar: " + error, ok ? "OkBrush" : "BadBrush");
        if (ok) { _name.Text = ""; await LoadAsync(); }
    }

    private async Task DeleteAsync(RestorePoint point)
    {
        if (MessageBox.Show($"Apagar o ponto \"{point.Description}\" de {point.Created:dd/MM/yyyy HH:mm}?\n\nNão tem volta.", "Apagar ponto",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        bool ok = await Task.Run(() => RestorePoints.Delete(point.Sequence));
        SetStatus(ok ? "Ponto apagado." : "O Windows não apagou o ponto.", ok ? "OkBrush" : "BadBrush");
        await LoadAsync();
    }

    private async Task RestoreAsync(RestorePoint point)
    {
        if (MessageBox.Show($"Voltar o Windows para \"{point.Description}\" ({point.Created:dd/MM/yyyy HH:mm})?\n\n" +
                            "• Seus arquivos pessoais (fotos, documentos) não são mexidos.\n" +
                            "• Programas e drivers instalados depois dessa data somem.\n" +
                            "• O PC reinicia e leva alguns minutos. Salve o que estiver aberto.\n\nContinuar?",
                "Voltar para o ponto", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var (ok, message) = await Task.Run(() => RestorePoints.Restore(point.Sequence));
        SetStatus(message, ok ? "OkBrush" : "BadBrush");
        if (ok && MessageBox.Show(message + "\n\nReiniciar agora?", "Voltar para o ponto", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Start2("shutdown.exe", "/r /t 5");
    }

    private static void Start2(string exe, string args)
    {
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
