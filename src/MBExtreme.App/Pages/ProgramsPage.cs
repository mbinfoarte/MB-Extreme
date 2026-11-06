using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Operations;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Programas: atualizações e instalações pelo winget (fonte oficial da Microsoft), uma de cada vez pela fila, com aviso quando o programa
/// está aberto e mensagens de erro em português; e o inventário de tudo que está instalado (instalador comum e Microsoft Store), com desinstalação acompanhada.
/// </summary>
public sealed class ProgramsPage : ToolPageBase
{
    private readonly Dictionary<WingetApp, CheckBox> _upgrades = new();
    private readonly Dictionary<CatalogApp, CheckBox> _installs = new();
    private IReadOnlyList<InstalledProgram>? _inventory;
    private readonly TextBox _search = new() { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 10) };
    private readonly StackPanel _inventoryList = new();
    private IReadOnlyList<WingetApp> _lastUpgrades = Array.Empty<WingetApp>();
    private CancellationTokenSource? _cts;

    public ProgramsPage() : this(false) { }

    /// <summary>startWithInventory: abre já na lista do que está instalado (tela Desinstalador).</summary>
    public ProgramsPage(bool startWithInventory) : base(startWithInventory ? "Desinstalador" : "Programas",
        "Atualize seus programas e instale ferramentas úteis pelo winget, o gerenciador de pacotes da Microsoft. Tudo roda um de cada vez, e o programa avisa se o que vai ser atualizado está aberto.")
    {
        AddButton("Procurar atualizações", async (_, _) => await SearchAsync());
        AddButton("Atualizar selecionados", async (_, _) => await RunAsync(upgrade: true));
        AddButton("Instalar selecionados", async (_, _) => await RunAsync(upgrade: false), false);
        AddButton("Programas instalados", async (_, _) => await ShowInventoryAsync(), false);
        AddButton("Parar", (_, _) => _cts?.Cancel(), false);
        AddSelectionButtons(() => _upgrades.Values.Concat(_installs.Values));
        _search.TextChanged += (_, _) => FillInventory();
        System.Windows.Automation.AutomationProperties.SetName(_search, "Procurar programa instalado");
        Loaded += async (_, _) =>
        {
            if (Body.Children.Count > 0) return;
            if (startWithInventory) await ShowInventoryAsync();
            else Render(Array.Empty<WingetApp>());
        };
    }

    private async Task SearchAsync()
    {
        SetStatus("Procurando atualizações...");
        try
        {
            if (!await Task.Run(WingetService.IsAvailable))
            {
                SetStatus("O winget não foi encontrado. Instale o \"Instalador de Aplicativo\" pela Microsoft Store.", "WarnBrush");
                return;
            }

            var list = await Task.Run(WingetService.Upgradable);
            _lastUpgrades = list;
            SetStatus(list.Count == 0 ? "Tudo em dia." : $"{list.Count} programa(s) com atualização.", list.Count == 0 ? "OkBrush" : "MutedBrush");
            Render(list);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            SetStatus("Não foi possível procurar: " + ex.Message, "BadBrush");
        }
    }

    private void Render(IReadOnlyList<WingetApp> upgrades)
    {
        Body.Children.Clear();
        _upgrades.Clear();
        _installs.Clear();

        if (upgrades.Count > 0)
        {
            Body.Children.Add(Heading("Atualizações disponíveis"));
            foreach (var app in upgrades)
            {
                var box = Check(app.Name, $"{app.Version} → {app.Available}");
                _upgrades[app] = box;
                Body.Children.Add(CardOf(box));
            }
        }

        Body.Children.Add(Heading("Instalar programas úteis"));
        foreach (var app in WingetService.Catalog)
        {
            var box = Check(app.Title, app.Description);
            _installs[app] = box;
            Body.Children.Add(CardOf(box));
        }
    }

    private async Task RunAsync(bool upgrade)
    {
        var operations = new List<IOperation>();
        if (upgrade)
        {
            var chosen = _upgrades.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
            if (chosen.Count == 0) { SetStatus("Selecione ao menos um programa para atualizar.", "WarnBrush"); return; }
            // programa aberto: a atualização costuma falhar ou pedir para fechar no meio
            _inventory ??= await Task.Run(InstalledPrograms.List);
            var processes = await Task.Run(ProcessPaths);
            foreach (var app in chosen)
            {
                var program = InstalledPrograms.Match(_inventory, app.Name);
                var open = InstalledPrograms.RunningFrom(program?.InstallLocation, processes);
                if (open.Count > 0 && MessageBox.Show($"{app.Name} está aberto ({string.Join(", ", open)}).\n\nFeche o programa antes de atualizar. Atualizar mesmo assim?",
                        "Programa aberto", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) continue;
                operations.Add(new WingetOperation(app.Id, app.Name, upgrade: true));
            }
        }
        else
        {
            operations.AddRange(_installs.Where(p => p.Value.IsChecked == true).Select(p => (IOperation)new WingetOperation(p.Key.Id, p.Key.Title, upgrade: false)));
            if (operations.Count == 0) { SetStatus("Selecione ao menos um programa para instalar.", "WarnBrush"); return; }
        }
        if (operations.Count == 0) { SetStatus("Nada para fazer."); return; }
        await RunQueueAsync(operations, upgrade ? "Atualizando, um de cada vez" : "Instalando, um de cada vez");
        if (upgrade) await SearchAsync();
    }

    private async Task RunQueueAsync(IReadOnlyList<IOperation> operations, string title)
    {
        Body.Children.Clear();
        Body.Children.Add(Heading(title));
        var lines = operations.ToDictionary(o => o, o =>
        {
            var t = new TextBlock { Text = "◻  " + o.Info.Title + " — na fila", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), Foreground = Brush("MutedBrush") };
            Body.Children.Add(t);
            return t;
        });
        void OnChanged(QueueItem item) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!lines.TryGetValue(item.Operation, out var line)) return;
            string mark = item.State switch { OperationState.Running => "⏳", OperationState.Succeeded or OperationState.RebootRequired => "✔", OperationState.Pending => "◻", OperationState.Skipped => "–", _ => "✖" };
            line.Text = $"{mark}  {item.Operation.Info.Title} — {item.Message}";
            line.Foreground = Brush(item.State switch { OperationState.Succeeded => "OkBrush", OperationState.Failed or OperationState.Cancelled => "BadBrush", OperationState.Running => "TextBrush", _ => "MutedBrush" });
        }));

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        OperationQueue.Shared.Changed += OnChanged;
        try
        {
            SetStatus(title + "...");
            var results = await Task.Run(() => OperationQueue.Shared.Run(operations, null, token));
            int failed = results.Count(r => r.State == OperationState.Failed);
            bool reboot = results.Any(r => r.State == OperationState.RebootRequired);
            SetStatus($"{results.Count(r => r.State is OperationState.Succeeded or OperationState.RebootRequired)} concluído(s), {failed} com erro." + (reboot ? " Reinicie para terminar." : ""),
                failed == 0 ? "OkBrush" : "WarnBrush");
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, "WarnBrush");
        }
        finally
        {
            OperationQueue.Shared.Changed -= OnChanged;
        }
    }

    private static IReadOnlyList<(string Name, string? Path)> ProcessPaths()
    {
        var list = new List<(string, string?)>();
        foreach (var p in Process.GetProcesses())
        {
            try { list.Add((p.ProcessName, p.MainModule?.FileName)); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            finally { p.Dispose(); }
        }
        return list;
    }

    // ---------------------------------------------------------------- inventário

    private async Task ShowInventoryAsync(bool keepStatus = false)
    {
        if (!keepStatus) SetStatus("Lendo os programas instalados...");
        _inventory = await Task.Run(InstalledPrograms.List);
        Body.Children.Clear();
        Body.Children.Add(Heading($"Programas instalados ({_inventory.Count})"));
        Body.Children.Add(Muted("Lidos do próprio Windows (Aplicativos instalados) e da Microsoft Store. A última vez que cada um foi aberto não é mostrada: o Windows não guarda isso de forma confiável.", 11.5));
        Body.Children.Add(_search);
        Body.Children.Add(_inventoryList);
        FillInventory();
        if (!keepStatus) SetStatus("");
    }

    private void FillInventory()
    {
        if (_inventory is null) return;
        _inventoryList.Children.Clear();
        string query = _search.Text.Trim();
        var shown = _inventory.Where(p => query.Length == 0 || p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || p.Publisher.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(300).ToList();
        foreach (var program in shown)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Image { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(icon);
            if (program.IconPath is { } iconPath) _ = LoadIconAsync(icon, iconPath);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = program.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var details = new List<string>();
            if (program.Version.Length > 0) details.Add(program.Version);
            if (program.Publisher.Length > 0) details.Add(program.Publisher);
            if (program.SizeBytes is { } size) details.Add(MBExtreme.Core.Diagnostics.DiagnosticRules.Size(size));
            if (program.InstalledOn is { } date) details.Add("instalado em " + date.ToString("dd/MM/yyyy"));
            if (program.Source == ProgramSource.Store) details.Add("Microsoft Store");
            text.Children.Add(Muted(string.Join(" · ", details), 11.5));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var remove = SmallButton("Desinstalar", async (_, _) => await UninstallAsync(program));
            System.Windows.Automation.AutomationProperties.SetName(remove, "Desinstalar " + program.Name);
            remove.IsEnabled = program.Source == ProgramSource.Store || program.UninstallCommand is not null;
            Grid.SetColumn(remove, 2);
            remove.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(remove);
            _inventoryList.Children.Add(CardOf(grid));
        }
        if (shown.Count == 0) _inventoryList.Children.Add(Muted("Nenhum programa encontrado."));
    }

    private static async Task LoadIconAsync(Image target, string path)
    {
        var source = await Task.Run(() =>
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) return null;
                var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return (ImageSource)bitmap;
            }
            catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return null; }
        });
        if (source is not null) target.Source = source;
    }

    private async Task UninstallAsync(InstalledProgram program)
    {
        var open = InstalledPrograms.RunningFrom(program.InstallLocation, await Task.Run(ProcessPaths));
        string warning = open.Count > 0 ? $"\n\n{program.Name} está aberto ({string.Join(", ", open)}): feche antes." : "";
        if (MessageBox.Show($"Desinstalar {program.Name}?{warning}\n\nA janela do desinstalador do próprio programa vai aparecer. Não tem volta pelo MB Extreme.",
                "Desinstalar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunQueueAsync(new IOperation[] { new UninstallOperation(program) }, "Desinstalando");
        _inventory = null;
        await ShowInventoryAsync(keepStatus: true);
    }
}
