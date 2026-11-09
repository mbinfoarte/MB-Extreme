using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Repair;
using MBExtreme.Core.Storage;

namespace MBExtreme.UI.Pages;

/// <summary>Gerenciador de discos: veja as partições de cada disco e crie, formate, renomeie, troque a letra, reduza, estenda, divida ou exclua, com confirmação e o comando à mostra.</summary>
public sealed class DiskManagerPage : ToolPageBase
{
    private const long Gb = DiskOps.Gb;

    private readonly StackPanel _host = new();
    private IReadOnlyList<ManagedDisk> _disks = Array.Empty<ManagedDisk>();
    private int _selDisk = -1, _selPart = -1;
    private long _selFree = -1;
    private bool _busy;

    public DiskManagerPage() : base("Gerenciador de discos",
        "Veja os discos e as partições do PC e faça o que o Gerenciamento de Disco do Windows faz, de forma guiada: criar partições, formatar, renomear, trocar a letra, reduzir, estender, dividir e excluir. Antes de qualquer alteração o programa mostra o que vai acontecer e o comando que será executado. O disco do Windows fica protegido.")
    {
        AddButton("Atualizar", async (_, _) => await LoadAsync());
        AddButton("Preparar disco novo (assistente)", async (_, _) => await NewDiskWizardAsync(), false);
        AddButton("Abrir o Gerenciamento de Disco do Windows", (_, _) => OpenWindowsTool(), false);

        Body.Children.Add(Muted("Clique numa partição ou num espaço livre da barra para ver as opções. Faça backup do que for importante antes de mexer: formatar e excluir não têm como desfazer.", 12));
        Body.Children.Add(_host);
        Loaded += async (_, _) => { if (_disks.Count == 0) await LoadAsync(); };
    }

    // ---------------------------------------------------------------- leitura

    private async Task LoadAsync()
    {
        if (_busy) return;
        SetStatus("Lendo os discos...");
        try
        {
            _disks = await Task.Run(DiskInventory.Load);
            SetStatus(_disks.Count == 0 ? "Nenhum disco encontrado." : $"{_disks.Count} disco(s) encontrado(s).", _disks.Count == 0 ? "WarnBrush" : "OkBrush");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível ler os discos: " + ex.Message, "BadBrush");
        }

        Render();
    }

    private static string SystemDrive => RepairCatalog.SystemDrive;

    private static string AppDrive => Path.GetPathRoot(AppContext.BaseDirectory) is { Length: >= 2 } root ? root[..2] : "";

    private ManagedDisk? SelectedDisk => _disks.FirstOrDefault(d => d.Number == _selDisk);

    private ManagedPartition? SelectedPartition => SelectedDisk?.Partitions.FirstOrDefault(p => p.Number == _selPart);

    private IReadOnlyList<char> FreeLetters()
    {
        var used = _disks.SelectMany(d => d.Partitions).Where(p => p.Letter is not null).Select(p => p.Letter!.Value).ToList();
        try { used.AddRange(DriveInfo.GetDrives().Select(d => d.Name[0])); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return DiskOps.FreeLetters(used);
    }

    // ---------------------------------------------------------------- desenho

    private void Render()
    {
        _host.Children.Clear();
        foreach (var disk in _disks)
        {
            var card = new StackPanel();
            card.Children.Add(DiskHeader(disk));
            card.Children.Add(SegmentBar(disk));
            if (disk.Number == _selDisk) card.Children.Add(DetailPanel(disk));
            var border = CardOf(card);
            border.Margin = new Thickness(0, 10, 0, 0);
            _host.Children.Add(border);
        }
    }

    private UIElement DiskHeader(ManagedDisk disk)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var title = new WrapPanel();
        title.Children.Add(new TextBlock { Text = disk.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0) });
        if (disk.IsSystem || disk.IsBoot) title.Children.Add(Badge("Disco do Windows", "AccentBrush"));
        if (disk.IsOffline) title.Children.Add(Badge("Offline", "WarnBrush"));
        if (disk.IsReadOnly) title.Children.Add(Badge("Somente leitura", "WarnBrush"));
        if (disk.IsRaw) title.Children.Add(Badge("Disco novo (sem partições)", "WarnBrush"));
        panel.Children.Add(title);
        // mesmas informações do Gerenciamento de Disco: tipo, tamanho e estado
        panel.Children.Add(Muted(disk.SummaryText + (DiskText.Health(disk.Health) is { } h && h != "Íntegro" ? " · " + h : ""), 12));
        return panel;
    }

    private Border Badge(string text, string brush) => new()
    {
        BorderBrush = Brush(brush), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 1),
        Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = Brush(brush) },
    };

    /// <summary>Barra com um bloco por partição ou espaço livre, largura proporcional ao tamanho (com um mínimo para caber o nome).</summary>
    private UIElement SegmentBar(ManagedDisk disk)
    {
        // o Windows não desenha a partição reservada da Microsoft (MSR)
        var segments = DiskInventory.Segments(disk).Where(x => x.Partition is not { IsReserved: true }).ToList();
        var grid = new Grid { Height = 70 };
        if (segments.Count == 0)
        {
            grid.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = new TextBlock { Text = "Disco sem partições", Foreground = Brush("MutedBrush"), Margin = new Thickness(10) } });
            return grid;
        }

        double total = Math.Max(1, segments.Sum(s => (double)s.Size));
        for (int i = 0; i < segments.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.07, segments[i].Size / total), GridUnitType.Star) });
            var view = SegmentView(disk, segments[i]);
            Grid.SetColumn(view, i);
            grid.Children.Add(view);
        }

        return grid;
    }

    private UIElement SegmentView(ManagedDisk disk, DiskSegment segment)
    {
        var p = segment.Partition;
        bool selected = disk.Number == _selDisk && (p is null ? segment.Offset == _selFree : p.Number == _selPart);
        bool isProtected = p is not null && DiskSafety.IsProtectedPartition(p);

        string title, sub;
        if (p is null) { title = DiskText.Size(segment.Size); sub = "Não alocado"; }
        else if (p.Letter is null) { title = DiskText.Size(p.Size); sub = p.TypeText; }
        else
        {
            title = p.Title;
            sub = DiskText.Size(p.Size) + (string.IsNullOrEmpty(p.Volume?.FileSystem) ? "" : " " + p.Volume!.FileSystem);
        }

        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });

        var inner = new Grid();
        inner.RowDefinitions.Add(new RowDefinition());
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.Children.Add(texts);
        if (p?.Volume is { Size: > 0 } v)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 100.0 * (v.Size - v.Free) / v.Size, Height = 5, Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetRow(bar, 1);
            inner.Children.Add(bar);
        }

        Brush background = p is null ? Brush("BgBrush") : isProtected ? Brush("SurfaceBrush") : new SolidColorBrush(Color.FromArgb(46, 255, 196, 0));
        var border = new Border
        {
            Background = background,
            BorderBrush = Brush(selected ? "AccentBrush" : "BorderBrush"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = inner,
            ToolTip = p is null ? $"Não alocado: {DiskText.Size(segment.Size)}" : $"{p.Title} · {DiskText.Size(p.Size)}\n{p.StatusText}",
        };
        border.MouseLeftButtonUp += (_, _) =>
        {
            _selDisk = disk.Number;
            _selPart = p?.Number ?? -1;
            _selFree = p is null ? segment.Offset : -1;
            Render();
        };
        return border;
    }

    private UIElement DetailPanel(ManagedDisk disk)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var part = SelectedPartition;
        var free = DiskInventory.Segments(disk).FirstOrDefault(s => s.IsFree && s.Offset == _selFree);

        if (part is not null)
        {
            panel.Children.Add(new TextBlock { Text = part.Letter is null ? part.TypeText : part.Title, FontSize = 15, FontWeight = FontWeights.SemiBold });
            var v = part.Volume;
            string info = $"Partição {part.Number} · {DiskText.Size(part.Size)}";
            if (v is not null && v.FileSystem.Length > 0) info += $" · {v.FileSystem}";
            panel.Children.Add(Muted(info, 12));
            if (v is { Size: > 0 })
                panel.Children.Add(Muted($"Capacidade {DiskText.Size(v.Size)} · Espaço livre {DiskText.Size(v.Free)} ({part.FreePercentText})", 12));
            panel.Children.Add(Muted("Status: " + part.StatusText, 12));

            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            string? dataBlock = DiskSafety.CanChangeData(disk, part, SystemDrive, AppDrive);
            string? resizeBlock = DiskSafety.CanResize(disk, part);
            bool hasLetter = part.Letter is not null;

            buttons.Children.Add(Act("Abrir no Explorador", hasLetter ? null : "A partição não tem letra.", () => OpenInExplorer(part), true));
            buttons.Children.Add(Act("Renomear", !hasLetter ? "A partição não tem letra." : disk.IsReadOnly || disk.IsOffline ? "O disco não aceita alterações agora." : null, () => _ = RenameAsync(part)));
            buttons.Children.Add(Act("Trocar a letra", dataBlock ?? (hasLetter ? null : "A partição não tem letra."), () => _ = ChangeLetterAsync(disk, part)));
            buttons.Children.Add(Act("Formatar…", dataBlock ?? (hasLetter ? null : "A partição não tem letra."), () => _ = FormatAsync(disk, part)));
            buttons.Children.Add(Act("Reduzir…", resizeBlock, () => _ = ShrinkAsync(disk, part, split: false)));
            buttons.Children.Add(Act("Estender…", resizeBlock, () => _ = ExtendAsync(disk, part)));
            buttons.Children.Add(Act("Dividir em duas…", resizeBlock, () => _ = ShrinkAsync(disk, part, split: true)));
            buttons.Children.Add(Act("Excluir…", dataBlock, () => _ = DeleteAsync(disk, part), false, danger: true));
            buttons.Children.Add(Act("Reparar este disco…", hasLetter ? null : "A partição não tem letra.", () => RepairDisk(part)));
            panel.Children.Add(buttons);

            string? why = dataBlock ?? resizeBlock;
            if (why is not null) panel.Children.Add(Muted("Algumas opções estão desligadas: " + why, 11));
        }
        else if (free is not null)
        {
            panel.Children.Add(new TextBlock { Text = "Não alocado", FontSize = 15, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(Muted($"{DiskText.Size(free.Size)} não alocados (sem partição). Crie uma partição para poder guardar arquivos nele.", 12));
            string? block = DiskSafety.CanCreate(disk, free);
            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(Act("Criar partição…", block, () => _ = CreateAsync(disk, free), true));
            panel.Children.Add(buttons);
            if (block is not null) panel.Children.Add(Muted(block, 11));
        }
        else if (disk.IsRaw)
        {
            panel.Children.Add(new TextBlock { Text = "Disco novo", FontSize = 15, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(Muted("Este disco ainda não foi preparado. O assistente inicializa, cria a partição e formata.", 12));
            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(Act("Preparar este disco (assistente)…", DiskSafety.CanInitialize(disk), () => _ = NewDiskWizardAsync(disk.Number), true));
            panel.Children.Add(buttons);
        }
        else
        {
            panel.Children.Add(Muted("Clique numa partição ou num espaço livre da barra.", 12));
        }

        return panel;
    }

    private Button Act(string text, string? blocked, Action click, bool primary = false, bool danger = false)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"),
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(12, 5, 12, 5),
            FontSize = 12,
            IsEnabled = blocked is null && !_busy,
        };
        if (danger) button.Foreground = Brush("BadBrush");
        if (blocked is not null)
        {
            button.ToolTip = blocked;
            ToolTipService.SetShowOnDisabled(button, true);
        }

        button.Click += (_, _) => click();
        return button;
    }

    // ---------------------------------------------------------------- execução

    private async Task RunAsync(DiskCommand command, string done)
    {
        if (_busy) return;
        _busy = true;
        SetStatus($"Executando: {command.Title}...", "MutedBrush");
        Render();
        try
        {
            var (ok, output) = await Task.Run(() => DiskOps.Run(command));
            if (ok) SetStatus(done, "OkBrush");
            else SetStatus($"Não foi possível concluir ({command.Title}): {FirstLine(output)}", "BadBrush");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível executar: " + ex.Message, "BadBrush");
        }
        finally
        {
            _busy = false;
        }

        _selPart = -1;
        _selFree = -1;
        await LoadAsync();
    }

    private static string FirstLine(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "erro sem mensagem";

    private FormDialog Dialog(string title, string description, string ok = "Executar", bool danger = false) =>
        new(Window.GetWindow(this), title, description, ok, danger);

    private static bool TryGb(string text, out double gb) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out gb) && gb > 0 && !double.IsInfinity(gb);

    // ---------------------------------------------------------------- operações

    private void OpenInExplorer(ManagedPartition part)
    {
        if (part.Letter is not { } letter) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{letter}:\\\"") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { SetStatus("Não foi possível abrir: " + ex.Message, "BadBrush"); }
    }

    private static void OpenWindowsTool()
    {
        try { Process.Start(new ProcessStartInfo("diskmgmt.msc") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private void RepairDisk(ManagedPartition part)
    {
        if (part.Letter is not { } letter) return;
        RepairPage.PendingDrive = letter + ":";
        MainWindow.Current?.Go("repair");
    }

    private async Task RenameAsync(ManagedPartition part)
    {
        if (part.Letter is not { } letter) return;
        string fsName = part.Volume?.FileSystem ?? "NTFS";
        var fs = fsName.Equals("FAT32", StringComparison.OrdinalIgnoreCase) ? FileSystemKind.Fat32 : fsName.Equals("exFAT", StringComparison.OrdinalIgnoreCase) ? FileSystemKind.ExFat : FileSystemKind.Ntfs;
        var dialog = Dialog($"Renomear {letter}:", "Troca só o nome que aparece no Explorador. Os arquivos não são tocados.");
        var label = dialog.Text($"Novo nome (até {DiskOps.MaxLabelLength(fs)} caracteres)", part.Volume?.Label ?? "", DiskOps.MaxLabelLength(fs));
        dialog.Validator = () => DiskOps.ValidateLabel(label(), fs);
        dialog.Owner = Window.GetWindow(this);
        dialog.ShowDialog();
        if (!dialog.Confirmed) return;
        await RunAsync(DiskOps.Rename(letter, label()), $"Unidade {letter}: renomeada.");
    }

    private async Task ChangeLetterAsync(ManagedDisk disk, ManagedPartition part)
    {
        if (part.Letter is not { } current) return;
        var letters = FreeLetters();
        if (letters.Count == 0) { SetStatus("Não há letras livres.", "WarnBrush"); return; }
        var dialog = Dialog($"Trocar a letra de {current}:", "A unidade passa a aparecer com outra letra. Programas e atalhos que apontam para a letra antiga deixam de achar os arquivos; jogos instalados ali podem precisar ser localizados de novo.");
        var pick = dialog.Choice("Nova letra", letters.Select(l => l + ":").ToList(), 0);
        dialog.Validator = () => null;
        dialog.ShowDialog();
        if (!dialog.Confirmed) return;
        await RunAsync(DiskOps.ChangeLetter(disk.Number, part.Number, current, letters[pick()]), $"Letra alterada para {letters[pick()]}:.");
    }

    private async Task FormatAsync(ManagedDisk disk, ManagedPartition part)
    {
        if (part.Letter is not { } letter) return;
        var systems = new[] { FileSystemKind.Ntfs, FileSystemKind.ExFat, FileSystemKind.Fat32 };
        var dialog = Dialog($"Formatar {letter}:", $"TODOS os arquivos de {letter}: ({DiskOps.FormatBytes(part.Size)}) serão apagados e não dá para recuperar. Faça backup antes.", "Formatar", danger: true);
        var fs = dialog.Choice("Sistema de arquivos", new[] { "NTFS (Windows, jogos)", "exFAT (pendrive e HD externo)", "FAT32 (compatibilidade, até 32 GB)" }, 0);
        var label = dialog.Text("Nome da unidade", part.Volume?.Label ?? "", 32);
        var mode = dialog.Choice("Tipo de formatação", new[] { "Rápida (recomendada)", "Completa (lenta)" }, 0);
        dialog.RequireTyped(letter.ToString(), $"Para confirmar, digite a letra da unidade ({letter}):");

        DiskCommand? command = null;
        string? error = null;
        dialog.Validator = () =>
        {
            try { command = DiskOps.Format(letter, systems[fs()], label(), mode() == 1); error = DiskOps.ValidateFormat(systems[fs()], part.Size); }
            catch (ArgumentException ex) { error = ex.Message; }
            return error;
        };
        dialog.ShowDialog();
        if (!dialog.Confirmed || command is null) return;
        await RunAsync(command, $"Unidade {letter}: formatada.");
    }

    private async Task DeleteAsync(ManagedDisk disk, ManagedPartition part)
    {
        string name = part.Letter is { } l ? l + ":" : "partição " + part.Number;
        var dialog = Dialog($"Excluir {name}", $"A partição {name} ({DiskOps.FormatBytes(part.Size)}) e TUDO que há nela serão apagados e não dá para recuperar. O espaço fica livre, sem partição.", "Excluir", danger: true);
        string phrase = part.Letter?.ToString() ?? "EXCLUIR";
        dialog.RequireTyped(phrase, part.Letter is null ? "Para confirmar, digite EXCLUIR:" : $"Para confirmar, digite a letra da unidade ({part.Letter}):");
        dialog.Validator = () => null;
        dialog.ShowDialog();
        if (!dialog.Confirmed) return;
        await RunAsync(DiskOps.Delete(disk.Number, part.Number, part.Letter), $"{name} excluída.");
    }

    private async Task<(long Min, long Max)?> SupportedSizesAsync(ManagedDisk disk, ManagedPartition part)
    {
        SetStatus("Calculando o quanto dá para redimensionar...");
        var (ok, output) = await Task.Run(() => DiskOps.RunScript(DiskOps.SupportedSizesScript(disk.Number, part.Number)));
        if (ok && DiskOps.TryParseSupportedSizes(output, out long min, out long max)) return (min, max);
        SetStatus("Não foi possível calcular os limites: " + FirstLine(output), "BadBrush");
        return null;
    }

    private async Task ShrinkAsync(ManagedDisk disk, ManagedPartition part, bool split)
    {
        if (part.Letter is not { } letter) return;
        if (await SupportedSizesAsync(disk, part) is not { } limits) return;
        long maxShrink = part.Size - limits.Min;
        if (maxShrink < Gb)
        {
            SetStatus($"Não dá para reduzir {letter}: os arquivos ocupam quase todo o espaço (ou há arquivos imóveis no fim). Libere espaço ou use o Gerenciamento de Disco do Windows.", "WarnBrush");
            return;
        }

        long maxGb = maxShrink / Gb;
        string title = split ? $"Dividir {letter}: em duas" : $"Reduzir {letter}:";
        string description = split
            ? $"Reduz {letter}: e cria uma partição nova com o espaço liberado. Os arquivos de {letter}: são mantidos. Dá para liberar até {maxGb} GB."
            : $"O espaço liberado fica sem partição. Os arquivos de {letter}: são mantidos. Dá para liberar até {maxGb} GB.";
        var dialog = Dialog(title, description, split ? "Dividir" : "Reduzir");
        Action? refresh = null;
        var preview = (TextBlock?)null;
        Func<int>? letterPick = null, fsPick = null;
        Func<string>? label = null;
        var letters = FreeLetters().Take(12).ToList();
        var fsKinds = new[] { FileSystemKind.Ntfs, FileSystemKind.ExFat };

        Func<string> amount = dialog.Text(split ? "Tamanho da nova partição (GB)" : "Reduzir em (GB)", Math.Max(1, maxGb / 2).ToString(CultureInfo.InvariantCulture), 8, _ => refresh?.Invoke());
        if (split)
        {
            if (letters.Count == 0) { SetStatus("Não há letras livres.", "WarnBrush"); return; }
            letterPick = dialog.Choice("Letra da nova partição", letters.Select(c => c + ":").ToList(), 0, _ => refresh?.Invoke());
            fsPick = dialog.Choice("Sistema de arquivos da nova partição", new[] { "NTFS", "exFAT" }, 0, _ => refresh?.Invoke());
            label = dialog.Text("Nome da nova partição", "Dados", 32, _ => refresh?.Invoke());
        }

        dialog.Note("Comando que será executado:");
        preview = dialog.Command("");

        DiskCommand? Build(out string? error)
        {
            error = null;
            if (!TryGb(amount(), out double gb) || gb < 1 || gb > maxGb) { error = $"Digite um tamanho entre 1 e {maxGb} GB."; return null; }
            long bytes = (long)(gb * Gb);
            try
            {
                var shrink = DiskOps.Shrink(disk.Number, part.Number, letter, part.Size - bytes);
                if (!split) return shrink;
                var create = DiskOps.CreatePartition(disk.Number, 0, bytes, letters[letterPick!()], fsKinds[fsPick!()], label!());
                return DiskOps.Chain(title, shrink, create);
            }
            catch (ArgumentException ex) { error = ex.Message; return null; }
        }

        void Refresh() => preview!.Text = Build(out string? err)?.Script ?? err ?? "";
        refresh = Refresh;
        Refresh();
        dialog.Validator = () => { Build(out string? err); return err; };
        dialog.ShowDialog();
        if (!dialog.Confirmed) return;
        var command = Build(out _);
        if (command is not null) await RunAsync(command, split ? "Partição dividida." : $"{letter}: reduzida.");
    }

    private async Task ExtendAsync(ManagedDisk disk, ManagedPartition part)
    {
        if (part.Letter is not { } letter) return;
        if (await SupportedSizesAsync(disk, part) is not { } limits) return;
        long maxGrow = limits.Max - part.Size;
        if (maxGrow < Gb)
        {
            SetStatus($"Não dá para estender {letter}: o espaço livre precisa estar logo depois da partição, no mesmo disco. Exclua ou reduza a partição ao lado, ou use o Gerenciamento de Disco do Windows.", "WarnBrush");
            return;
        }

        long maxGb = maxGrow / Gb;
        var dialog = Dialog($"Estender {letter}:", $"Usa o espaço livre logo depois da partição para aumentá-la. Os arquivos são mantidos. Dá para aumentar até {maxGb} GB.", "Estender");
        Action? refresh = null;
        var amount = dialog.Text("Aumentar em (GB)", maxGb.ToString(CultureInfo.InvariantCulture), 8, _ => refresh?.Invoke());
        dialog.Note("Comando que será executado:");
        var preview = dialog.Command("");

        DiskCommand? Build(out string? error)
        {
            error = null;
            if (!TryGb(amount(), out double gb) || gb < 1 || gb > maxGb) { error = $"Digite um tamanho entre 1 e {maxGb} GB."; return null; }
            return DiskOps.Extend(disk.Number, part.Number, letter, part.Size + (long)(gb * Gb));
        }

        void Refresh() => preview.Text = Build(out string? err)?.Script ?? err ?? "";
        refresh = Refresh;
        Refresh();
        dialog.Validator = () => { Build(out string? err); return err; };
        dialog.ShowDialog();
        if (!dialog.Confirmed) return;
        var command = Build(out _);
        if (command is not null) await RunAsync(command, $"{letter}: estendida.");
    }

    private async Task CreateAsync(ManagedDisk disk, DiskSegment free)
    {
        var letters = FreeLetters().Take(12).ToList();
        if (letters.Count == 0) { SetStatus("Não há letras livres.", "WarnBrush"); return; }
        long maxGb = Math.Max(1, free.Size / Gb);
        var dialog = Dialog("Criar partição", $"Cria uma partição no espaço livre ({DiskOps.FormatBytes(free.Size)}) e já formata, pronta para uso.", "Criar");
        Action? refresh = null;
        var size = dialog.Text($"Tamanho em GB (deixe {maxGb} para usar tudo)", maxGb.ToString(CultureInfo.InvariantCulture), 8, _ => refresh?.Invoke());
        var letter = dialog.Choice("Letra", letters.Select(c => c + ":").ToList(), 0, _ => refresh?.Invoke());
        var kinds = new[] { FileSystemKind.Ntfs, FileSystemKind.ExFat, FileSystemKind.Fat32 };
        var fs = dialog.Choice("Sistema de arquivos", new[] { "NTFS (Windows, jogos)", "exFAT (pendrive e HD externo)", "FAT32 (até 32 GB)" }, 0, _ => refresh?.Invoke());
        var label = dialog.Text("Nome da unidade", "Dados", 32, _ => refresh?.Invoke());
        dialog.Note("Comando que será executado:");
        var preview = dialog.Command("");

        DiskCommand? Build(out string? error)
        {
            error = null;
            if (!TryGb(size(), out double gb) || gb < 0.1 || gb * Gb > free.Size) { error = $"Digite um tamanho entre 0,1 e {maxGb} GB."; return null; }
            long bytes = gb >= maxGb ? 0 : (long)(gb * Gb);
            try { return DiskOps.CreatePartition(disk.Number, free.Offset, bytes, letters[letter()], kinds[fs()], label()); }
            catch (ArgumentException ex) { error = ex.Message; return null; }
        }

        void Refresh() => preview.Text = Build(out string? err)?.Script ?? err ?? "";
        refresh = Refresh;
        Refresh();
        dialog.Validator = () => { Build(out string? err); return err; };
        dialog.ShowDialog();
        if (!dialog.Confirmed) return;
        var command = Build(out _);
        if (command is not null) await RunAsync(command, "Partição criada.");
    }

    /// <summary>Assistente de disco novo: inicializa, cria uma partição com todo o espaço e formata.</summary>
    private async Task NewDiskWizardAsync(int? preferred = null)
    {
        if (_disks.Count == 0) await LoadAsync();
        var eligible = _disks.Where(d => !d.IsSystem && !d.IsBoot && !d.IsOffline && !d.IsReadOnly && (d.IsRaw || d.Partitions.Count == 0)).ToList();
        if (eligible.Count == 0)
        {
            SetStatus("Não há disco novo (vazio) para preparar. Discos com partições não entram neste assistente; use as opções de cada partição.", "WarnBrush");
            return;
        }

        var letters = FreeLetters().Take(12).ToList();
        if (letters.Count == 0) { SetStatus("Não há letras livres.", "WarnBrush"); return; }
        var dialog = Dialog("Preparar disco novo", "Passo a passo: 1) inicializa o disco, 2) cria uma partição com todo o espaço, 3) formata. O disco precisa estar vazio: o que houver nele será perdido.", "Preparar", danger: true);
        Action? refresh = null;
        int start = Math.Max(0, eligible.FindIndex(d => d.Number == preferred));
        var disk = dialog.Choice("Disco", eligible.Select(d => $"Disco {d.Number} · {d.Name} · {DiskOps.FormatBytes(d.Size)}").ToList(), start, _ => refresh?.Invoke());
        var style = dialog.Choice("Tabela de partições", new[] { "GPT (recomendado)", "MBR (PCs antigos)" }, 0, _ => refresh?.Invoke());
        var kinds = new[] { FileSystemKind.Ntfs, FileSystemKind.ExFat };
        var fs = dialog.Choice("Sistema de arquivos", new[] { "NTFS (Windows, jogos)", "exFAT (também no Mac e em consoles)" }, 0, _ => refresh?.Invoke());
        var letter = dialog.Choice("Letra", letters.Select(c => c + ":").ToList(), 0, _ => refresh?.Invoke());
        var label = dialog.Text("Nome da unidade", "Dados", 32, _ => refresh?.Invoke());
        dialog.Note("Comando que será executado:");
        var preview = dialog.Command("");
        dialog.RequireTyped("PREPARAR", "Para confirmar, digite PREPARAR:");

        DiskCommand? Build(out string? error)
        {
            error = null;
            var chosen = eligible[disk()];
            try
            {
                var steps = new List<DiskCommand>();
                if (chosen.IsRaw) steps.Add(DiskOps.Initialize(chosen.Number, style() == 0));
                steps.Add(DiskOps.CreatePartition(chosen.Number, 0, 0, letters[letter()], kinds[fs()], label()));
                return DiskOps.Chain($"Preparar o disco {chosen.Number}", steps.ToArray());
            }
            catch (ArgumentException ex) { error = ex.Message; return null; }
        }

        void Refresh() => preview.Text = Build(out string? err)?.Script ?? err ?? "";
        refresh = Refresh;
        Refresh();
        dialog.Validator = () => { Build(out string? err); return err; };
        dialog.ShowDialog();
        if (!dialog.Confirmed) return;
        var command = Build(out _);
        if (command is not null) await RunAsync(command, "Disco preparado.");
    }
}
