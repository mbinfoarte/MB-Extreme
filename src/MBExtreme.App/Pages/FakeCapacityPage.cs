using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Storage;

namespace MBExtreme.UI.Pages;

/// <summary>Pendrive original? Escreve e confere todo o espaço livre para pegar pendrive e cartão com capacidade falsa.</summary>
public sealed class FakeCapacityPage : ToolPageBase
{
    private readonly ComboBox _drives = new() { MinWidth = 320, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ProgressBar _bar = new() { Height = 8, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private bool _busy;

    public FakeCapacityPage() : base("Pendrive original?",
        "Descobre se um pendrive ou cartão de memória tem a capacidade falsa (diz ter 128 GB, mas só guarda 8). O teste enche o espaço livre com arquivos de teste, lê tudo de volta e confere. Não apaga o que já está no pendrive; os arquivos de teste são apagados no fim.")
    {
        AddButton("Começar o teste", async (_, _) => await RunAsync());
        AddButton("Parar", (_, _) => Cts?.Cancel(), false);
        AddButton("Atualizar a lista", (_, _) => Fill(), false);
        System.Windows.Automation.AutomationProperties.SetName(_drives, "Pendrive ou cartão para testar");
        var pick = new StackPanel { Orientation = Orientation.Horizontal };
        pick.Children.Add(new TextBlock { Text = "Pendrive ou cartão:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        pick.Children.Add(_drives);
        var card = new StackPanel();
        card.Children.Add(pick);
        card.Children.Add(_bar);
        card.Children.Add(Muted("Quanto mais espaço livre, mais demora: um pendrive de 32 GB leva de 20 minutos a 1 hora. Não tire o pendrive durante o teste.", 11.5));
        Body.Children.Add(CardOf(card));
        Loaded += (_, _) => Fill();
    }

    private void Fill()
    {
        _drives.Items.Clear();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType != DriveType.Removable) continue;
                _drives.Items.Add(new ComboBoxItem { Content = $"{d.Name}  {d.VolumeLabel}  ·  {d.TotalSize / 1e9:0.#} GB ({d.AvailableFreeSpace / 1e9:0.#} GB livres)", Tag = d.RootDirectory.FullName });
            }
            catch (IOException) { }
        }
        if (_drives.Items.Count > 0) _drives.SelectedIndex = 0;
        SetStatus(_drives.Items.Count == 0 ? "Conecte um pendrive ou cartão de memória." : "Escolha o pendrive e clique em Começar o teste.");
    }

    private async Task RunAsync()
    {
        if (_busy || _drives.SelectedItem is not ComboBoxItem { Tag: string root }) { if (!_busy) SetStatus("Escolha um pendrive.", "WarnBrush"); return; }
        if (MessageBox.Show($"Testar {root}?\n\nO espaço livre vai ser preenchido com arquivos de teste e conferido. O que já está no pendrive não é apagado. Pode levar bastante tempo.",
                "Pendrive original?", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true;
        Cts = new CancellationTokenSource();
        _bar.Visibility = Visibility.Visible;
        var progress = new Progress<CapacityProgress>(p =>
        {
            _bar.Maximum = Math.Max(1, p.Total);
            _bar.Value = p.Done;
            SetStatus($"{p.Stage}: {p.Done / 1e9:0.0} de {p.Total / 1e9:0.0} GB · {p.MBps:0} MB/s");
        });
        try
        {
            var token = Cts.Token;
            var r = await Task.Run(() => FakeCapacity.Run(root, progress, token));
            Body.Children.Add(CardOf(Result(r)));
            SetStatus(r.Cancelled ? "Teste parado." : r.Genuine ? "Pendrive original: todo o espaço testado guardou certo." : "Pendrive com problema: parte do espaço não guarda os dados.",
                r.Cancelled ? "MutedBrush" : r.Genuine ? "OkBrush" : "BadBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("O teste não terminou: " + ex.Message, "BadBrush");
        }
        finally
        {
            _busy = false;
            _bar.Visibility = Visibility.Collapsed;
        }
    }

    private StackPanel Result(CapacityResult r)
    {
        var panel = new StackPanel();
        string head = r.Cancelled ? "Teste parado antes do fim" : r.Genuine ? "✔  Capacidade verdadeira" : "✖  Capacidade falsa ou com defeito";
        panel.Children.Add(new TextBlock { Text = head, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brush(r.Cancelled ? "MutedBrush" : r.Genuine ? "OkBrush" : "BadBrush") });
        panel.Children.Add(Muted($"Escrito: {r.Written / 1e9:0.00} GB · conferido certo: {r.Verified / 1e9:0.00} GB · com erro: {r.Corrupt / 1e9:0.00} GB"));
        panel.Children.Add(Muted($"Velocidade: escrita {r.WriteMBps:0.0} MB/s · leitura {r.ReadMBps:0.0} MB/s"));
        if (!r.Genuine && !r.Cancelled)
            panel.Children.Add(Muted($"Só cerca de {r.Verified / 1e9:0.#} GB guardam os dados de verdade. Não use para coisas importantes; peça a troca ou o reembolso.", 12));
        return panel;
    }
}
