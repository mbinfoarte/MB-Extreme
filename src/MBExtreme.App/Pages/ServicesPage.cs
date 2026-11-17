using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Serviços de outros fabricantes que abrem com o Windows (os da Microsoft ficam de fora, como no "Ocultar serviços Microsoft" do msconfig).
/// Desmarcar muda o serviço para "Manual": ele não abre mais sozinho, mas o Windows ainda o inicia quando um programa precisar. Marcar de novo desfaz.
/// </summary>
internal sealed class ServicesPage : UserControl
{
    private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services";
    private readonly StackPanel _list = new();
    private readonly TextBlock _summary = new() { FontSize = 12.5, Margin = new Thickness(0, 10, 0, 6), TextWrapping = TextWrapping.Wrap };

    private sealed record ServiceEntry(string Name, string Display, string Path, int Start);

    public ServicesPage()
    {
        var root = new StackPanel { Margin = new Thickness(40, 18, 40, 32) };
        root.Children.Add(new TextBlock { Text = "Serviços que abrem com o Windows", FontSize = 22, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Serviços de outros fabricantes (os do Windows ficam de fora). Desmarcar deixa o serviço em Manual: ele para de abrir junto com o PC, " +
                   "mas ainda inicia quando um programa precisar. Para voltar, é só marcar de novo. Não desmarque o antivírus.",
            FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = Res("MutedBrush"),
        });
        var refresh = new Button { Content = "Atualizar lista", Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        refresh.Click += (_, _) => Load();
        root.Children.Add(refresh);
        root.Children.Add(_summary);
        root.Children.Add(_list);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Loaded += (_, _) => Load();
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private void Load()
    {
        _list.Children.Clear();
        var services = Read();
        int auto = services.Count(s => s.Start == 2);
        _summary.Text = services.Count == 0
            ? "Nenhum serviço de outro fabricante encontrado."
            : $"{auto} de {services.Count} serviços de outros fabricantes abrem com o Windows.";
        foreach (var service in services.OrderByDescending(s => s.Start == 2).ThenBy(s => s.Display, StringComparer.CurrentCultureIgnoreCase))
            _list.Children.Add(Row(service));
    }

    private UIElement Row(ServiceEntry service)
    {
        var box = new CheckBox { IsChecked = service.Start == 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Abre com o Windows" };
        System.Windows.Automation.AutomationProperties.SetName(box, service.Display);
        var state = new TextBlock { FontSize = 11.5, Foreground = Res("MutedBrush") };
        void Show(bool on) => state.Text = (on ? "Abre com o Windows" : "Manual (só quando precisar)") + " · " + service.Name;
        Show(service.Start == 2);
        box.Click += (_, _) =>
        {
            bool on = box.IsChecked == true;
            if (SetStart(service.Name, on ? 2 : 3)) Show(on);
            else
            {
                box.IsChecked = !on;
                MessageBox.Show("O Windows não deixou mudar este serviço (ele é protegido).", "Serviços", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = service.Display, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(state);
        var dock = new DockPanel();
        DockPanel.SetDock(box, Dock.Left);
        dock.Children.Add(box);
        dock.Children.Add(text);
        return new Border
        {
            Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 0, 0, 6), Child = dock,
            ToolTip = service.Path,
        };
    }

    private static List<ServiceEntry> Read()
    {
        var list = new List<ServiceEntry>();
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(ServicesPath);
            if (root is null) return list;
            foreach (string name in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key?.GetValue("Type") is not int type || (type & 0x30) == 0) continue;   // só serviços de programas (não drivers)
                    if (key.GetValue("Start") is not int start || start is not (2 or 3)) continue;
                    string path = Environment.ExpandEnvironmentVariables(key.GetValue("ImagePath") as string ?? "").Trim();
                    if (path.Length == 0 || path.Contains(windowsDir, StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("\\Windows\\", StringComparison.OrdinalIgnoreCase) || path.Contains("svchost", StringComparison.OrdinalIgnoreCase)) continue;
                    if (path.Contains("\\Microsoft\\", StringComparison.OrdinalIgnoreCase) || path.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)) continue;
                    string display = key.GetValue("DisplayName") as string ?? name;
                    if (display.StartsWith('@')) display = name;
                    list.Add(new ServiceEntry(name, display, path, start));
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return list;
    }

    private static bool SetStart(string name, int start)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServicesPath + "\\" + name, writable: true);
            if (key is null) return false;
            key.SetValue("Start", start, RegistryValueKind.DWord);
            if (start == 3 && key.GetValue("DelayedAutostart") is not null) key.SetValue("DelayedAutostart", 0, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }
}
