using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Drivers;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Remoção completa do driver de vídeo (NVIDIA, AMD ou Intel) para reinstalar limpo.</summary>
public sealed class DriverRemovalPage : ToolPageBase
{
    private readonly StackPanel _list = new();
    private readonly StackPanel _result = new();
    private readonly Dictionary<CheckBox, DisplayDriverPackage> _boxes = new();
    private CheckBox? _leftovers, _pauseUpdate;

    public DriverRemovalPage() : base("Remover driver de vídeo",
        "Tira por completo o driver da placa de vídeo (NVIDIA, AMD ou Intel) para você instalar uma versão limpa: resolve travadas, tela preta, erros depois de trocar de placa ou de uma atualização que deu errado. A tela pode piscar e ficar com a resolução básica até você instalar o driver novo.")
    {
        AddButton("Procurar drivers instalados", async (_, _) => await LoadAsync());
        AddButton("Remover selecionados", async (_, _) => await RemoveAsync(), false);

        Body.Children.Add(Heading("Drivers de vídeo instalados"));
        Body.Children.Add(_list);

        Body.Children.Add(Heading("Opções"));
        _leftovers = new CheckBox { Content = "Apagar também as pastas que o instalador deixa (ex.: C:\\NVIDIA, cache do painel)", IsChecked = true, Foreground = Brush("TextBrush") };
        _pauseUpdate = new CheckBox { Content = "Impedir o Windows Update de reinstalar um driver de vídeo sozinho (recomendado)", IsChecked = true, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 4, 0, 0) };
        Body.Children.Add(_leftovers);
        Body.Children.Add(_pauseUpdate);

        Body.Children.Add(Heading("Depois de remover"));
        Body.Children.Add(Muted("Reinicie o PC e instale o driver novo pela página oficial do fabricante (ou pela página Drivers do MB Extreme). Um ponto de restauração do Windows é criado antes."));
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var nvidia = SmallButton("Baixar NVIDIA", (_, _) => Open(DriverService.OfficialPage(GpuVendor.Nvidia)), true);
        nvidia.Margin = new Thickness(0);
        links.Children.Add(nvidia);
        links.Children.Add(SmallButton("Baixar AMD", (_, _) => Open(DriverService.OfficialPage(GpuVendor.Amd))));
        links.Children.Add(SmallButton("Baixar Intel", (_, _) => Open(DriverService.OfficialPage(GpuVendor.Intel))));
        links.Children.Add(SmallButton("Reiniciar o PC", (_, _) => Restart()));
        Body.Children.Add(links);

        Body.Children.Add(Heading("Limpeza ainda mais profunda"));
        Body.Children.Add(Muted("O MB Extreme usa as ferramentas do próprio Windows, o que já resolve a maioria dos casos. Para apagar também as entradas do registro, existe o DDU (Display Driver Uninstaller), um programa gratuito de terceiros que roda melhor no Modo Seguro. Ele não vem com o MB Extreme; baixe do site oficial se precisar."));
        var ddu = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var open = SmallButton("Abrir o site oficial do DDU", (_, _) => Open(DriverRemoval.DduPage));
        open.Margin = new Thickness(0);
        ddu.Children.Add(open);
        Body.Children.Add(ddu);

        Body.Children.Add(_result);
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _list.Children.Clear();
        _boxes.Clear();
        _list.Children.Add(Muted("Procurando..."));
        IReadOnlyList<DisplayDriverPackage> packages;
        try
        {
            packages = await Task.Run(DriverRemoval.ListPackages);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _list.Children.Clear();
            _list.Children.Add(Row("Não foi possível listar os drivers", ex.Message + " (execute o MB Extreme como administrador)", null, "BadBrush"));
            return;
        }

        _list.Children.Clear();
        if (packages.Count == 0)
        {
            _list.Children.Add(Muted("Nenhum driver de vídeo instalado encontrado (ou o MB Extreme não está como administrador)."));
            return;
        }

        foreach (var pkg in packages.OrderBy(p => p.Vendor).ThenByDescending(p => p.Version))
        {
            string maker = pkg.Vendor switch { GpuVendor.Nvidia => "NVIDIA", GpuVendor.Amd => "AMD", GpuVendor.Intel => "Intel", _ => pkg.Provider };
            var box = new CheckBox
            {
                IsChecked = false,
                Foreground = Brush("TextBrush"),
                Content = new StackPanel
                {
                    Margin = new Thickness(8, 0, 0, 0),
                    Children =
                    {
                        new TextBlock { Text = $"{maker} · versão {pkg.Version}", FontSize = 14, FontWeight = FontWeights.SemiBold },
                        Muted($"{pkg.Provider} · {pkg.PublishedName}" + (pkg.Date.Length > 0 ? $" · {pkg.Date}" : "")),
                    },
                },
            };
            _boxes[box] = pkg;
            _list.Children.Add(CardOf(box));
        }
        _list.Children.Add(Muted("Marque os que quer remover. Se você trocou de marca de placa, marque os da marca antiga."));
    }

    private async Task RemoveAsync()
    {
        var chosen = _boxes.Where(kv => kv.Key.IsChecked == true).Select(kv => kv.Value).ToList();
        if (chosen.Count == 0) { SetStatus("Marque ao menos um driver.", "WarnBrush"); return; }
        if (MessageBox.Show("O driver de vídeo marcado será removido agora.\n\nA tela pode piscar e ficar em resolução básica até você instalar o driver novo. Salve o que estiver fazendo e feche jogos e o painel do fabricante.\n\nContinuar?",
                "Remover driver de vídeo", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        SetStatus("Criando o ponto de restauração...");
        var (ok, error) = await Task.Run(() => RestorePointService.TryCreate("MB Extreme: antes de remover o driver de vídeo"));
        if (!ok && MessageBox.Show("Não foi possível criar o ponto de restauração:\n" + error + "\n\nContinuar mesmo assim?", "Remover driver de vídeo",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            SetStatus("Cancelado.", "WarnBrush");
            return;
        }

        SetStatus("Removendo o driver...");
        var vendor = chosen.GroupBy(c => c.Vendor).OrderByDescending(g => g.Count()).First().Key;
        bool leftovers = _leftovers?.IsChecked == true, pause = _pauseUpdate?.IsChecked == true;
        var steps = await Task.Run(() => DriverRemoval.Remove(chosen, leftovers, vendor, pause));

        _result.Children.Clear();
        _result.Children.Add(Heading("Resultado"));
        foreach (var step in steps) _result.Children.Add(Row((step.Ok ? "✔ " : "✖ ") + step.Title, step.Detail, null, step.Ok ? "OkBrush" : "BadBrush"));
        _result.Children.Add(Muted("Reinicie o PC e instale o driver novo."));
        SetStatus(steps.All(s => s.Ok) ? "Driver removido. Reinicie o PC e instale o novo." : "Concluído com avisos: veja o resultado abaixo.", steps.All(s => s.Ok) ? "OkBrush" : "WarnBrush");
        await LoadAsync();
    }

    private void Restart()
    {
        if (MessageBox.Show("Reiniciar o PC agora? Salve seus arquivos antes.", "Reiniciar", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /t 10") { UseShellExecute = false, CreateNoWindow = true }); }
        catch (System.ComponentModel.Win32Exception ex) { SetStatus("Não foi possível reiniciar: " + ex.Message, "BadBrush"); }
    }
}
