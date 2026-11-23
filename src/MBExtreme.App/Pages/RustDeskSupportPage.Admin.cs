using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Remote;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Painel "Suporte ao vivo" dentro do programa, só para a conta de administrador (o servidor confere): a mesma lista do painel do site,
/// com quem está online, quem pediu ajuda, o plano, o PC e os botões Conectar e Arquivos. Conectar abre o RustDesk no ID do cliente
/// sem senha, então o cliente precisa aceitar na tela dele. Atualiza sozinho a cada 30 segundos.
/// </summary>
public sealed partial class RustDeskSupportPage
{
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00), Green = Color.FromRgb(0x3D, 0xDC, 0x97), Red = Color.FromRgb(0xE5, 0x48, 0x4D);
    private readonly Border _adminCard = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _adminList = new();
    private readonly WrapPanel _adminFilters = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _adminKpis = new() { FontSize = 12.5, Margin = new Thickness(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _adminSearch = new() { Width = 260, FontSize = 13, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 8, 0) };
    private readonly DispatcherTimer _adminTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private SupportList? _adminData;
    private string _adminFilter = "online";
    private bool _adminLoading;

    private void BuildAdmin()
    {
        var root = new StackPanel();
        var head = new DockPanel();
        var refresh = SmallButton("Atualizar", async (_, _) => await LoadAdminAsync(), false);
        DockPanel.SetDock(refresh, Dock.Right);
        head.Children.Add(refresh);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = "Suporte ao vivo", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(Tag("ADMINISTRADOR"));
        title.Children.Add(new TextBlock { Text = "atualiza sozinho a cada 30 s", FontSize = 11.5, Foreground = Brush("MutedBrush"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(title);
        root.Children.Add(head);
        root.Children.Add(_adminKpis);

        var bar = _adminFilters;
        System.Windows.Automation.AutomationProperties.SetName(_adminSearch, "Buscar por nome, e-mail, PC ou ID");
        _adminSearch.ToolTip = "Buscar por nome, e-mail, PC ou ID";
        _adminSearch.TextChanged += (_, _) => RenderAdmin();
        bar.Children.Add(_adminSearch);
        foreach (var (key, text) in new[] { ("online", "Online"), ("ajuda", "Pediram ajuda"), ("todos", "Todos") })
        {
            var b = SmallButton(text, (_, _) => { _adminFilter = key; RenderAdmin(); }, key == _adminFilter);
            b.Tag = key;
            b.Margin = new Thickness(0, 0, 6, 0);
            bar.Children.Add(b);
        }
        root.Children.Add(bar);
        root.Children.Add(_adminList);
        root.Children.Add(Muted("Conectar abre o acesso remoto deste PC no ID do cliente e ele vê o pedido na tela: sem o aceite dele, nada acontece. Só aparecem PCs que ligaram o suporte rápido.", 11));
        _adminCard.Child = root;
        _adminCard.Style = (Style)FindResource("Card");
        _adminCard.Padding = new Thickness(16, 12, 16, 12);
        _adminCard.Margin = new Thickness(0, 0, 0, 12);
        _adminTimer.Tick += async (_, _) => await LoadAdminAsync();
    }

    /// <summary>Pede a lista ao servidor. Conta que não é de administrador: o painel fica escondido e o relógio para.</summary>
    private async Task LoadAdminAsync()
    {
        if (_adminLoading || !InviteApi.Ready) return;
        _adminLoading = true;
        try
        {
            _adminData = await InviteApi.Client.AdminSupportListAsync(InviteApi.License, InviteApi.Device);
            if (_adminData is null)
            {
                ShowAdminColumn(false);
                _adminTimer.Stop();
                return;
            }
            ShowAdminColumn(true);
            if (!_adminTimer.IsEnabled) _adminTimer.Start();
            RenderAdmin();
        }
        catch (LicenseNetworkException ex)
        {
            if (_adminCard.Visibility == Visibility.Visible) _adminKpis.Text = "Não foi possível atualizar: " + ex.Message;
        }
        finally { _adminLoading = false; }
    }

    /// <summary>Conta de administrador: a tela vira duas colunas (o suporte deste PC à esquerda, a lista do Suporte ao vivo à direita).</summary>
    private void ShowAdminColumn(bool show)
    {
        _adminCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _columns.ColumnDefinitions[1].Width = new GridLength(show ? 14 : 0);
        _columns.ColumnDefinitions[2].Width = show ? new GridLength(1.1, GridUnitType.Star) : new GridLength(0);
    }

    private void RenderAdmin()
    {
        if (_adminData is not { } data) return;
        long now = data.Now;
        var all = data.Items;
        var online = all.Where(p => p.Online).ToList();
        var help = online.Where(p => p.HelpAt is not null).ToList();
        _adminKpis.Text = $"Online agora: {online.Count}   ·   Pediram ajuda: {help.Count}   ·   Em atendimento: {all.Count(p => p.InSession)}   ·   Atendimentos hoje: {all.Count(p => p.SessionToday)}   ·   Autorizaram: {all.Count}";
        _adminKpis.Foreground = help.Count > 0 ? new SolidColorBrush(Yellow) : Brush("TextBrush");

        foreach (var child in _adminFilters.Children.OfType<Button>())
            if (child.Tag is string key)
            {
                child.Style = (Style)FindResource(key == _adminFilter ? "PrimaryButton" : "GhostButton");
                child.Content = key switch { "online" => $"Online ({online.Count})", "ajuda" => $"Pediram ajuda ({help.Count})", _ => $"Todos ({all.Count})" };
            }

        var list = _adminFilter == "online" ? online : _adminFilter == "ajuda" ? help : all;
        string q = new string(_adminSearch.Text.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        if (q.Length > 0)
            list = list.Where(p => new string((p.Name + p.Email + p.PcName + p.RustdeskId).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant().Contains(q)).ToList();
        // quem pediu ajuda primeiro, depois quem está online, depois o mais recente
        // quem pediu ajuda primeiro, na ordem do pedido (o mais antigo antes); os outros em ordem alfabética
        var pt = StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), ignoreCase: true);
        list = list.OrderByDescending(p => p.Online && p.HelpAt is not null)
            .ThenBy(p => p.Online && p.HelpAt is not null ? p.HelpAt!.Value : 0)
            .ThenBy(p => p.Name.Length > 0 ? p.Name : p.Email.Length > 0 ? p.Email : p.PcName, pt).ToList();

        _adminList.Children.Clear();
        if (list.Count == 0)
        {
            _adminList.Children.Add(Muted(_adminFilter == "online" ? "Nenhum cliente com o MB Extreme aberto agora." : "Nada por aqui.", 12.5));
            return;
        }
        foreach (var p in list) _adminList.Children.Add(AdminRow(p, now));
    }

    private Border AdminRow(SupportListItem p, long now)
    {
        bool asking = p.Online && p.HelpAt is not null;
        var dot = new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            Background = p.InSession ? new SolidColorBrush(Red) : asking ? new SolidColorBrush(Yellow) : p.Online ? new SolidColorBrush(Green) : Brush("MutedBrush"),
            ToolTip = p.InSession ? "Em atendimento" : asking ? "Pediu ajuda" : p.Online ? "Online" : "Offline",
        };

        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(new TextBlock { Text = p.Name.Length > 0 ? p.Name : "Sem nome", FontSize = 13.5, FontWeight = FontWeights.SemiBold });
        nameLine.Children.Add(PlanTag(p));
        who.Children.Add(nameLine);
        string extra = asking ? $" · pediu ajuda {InviteClient.Ago(p.HelpAt!.Value, now)}" : p.InSession ? " · em atendimento" : "";
        who.Children.Add(new TextBlock
        {
            Text = p.Email + extra, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = asking ? new SolidColorBrush(Yellow) : p.InSession ? new SolidColorBrush(Red) : Brush("MutedBrush"),
        });

        var pc = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        pc.Children.Add(new TextBlock { Text = p.PcName, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
        pc.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { p.Os, p.Cpu, p.Gpu }.Where(s => s.Length > 0)), FontSize = 11, Foreground = Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });

        var idCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        idCol.Children.Add(new TextBlock { Text = RustDesk.FormatId(p.RustdeskId), FontFamily = new FontFamily("Consolas"), FontSize = 15, FontWeight = FontWeights.Bold });
        idCol.Children.Add(new TextBlock { Text = p.Online ? "online " + InviteClient.Ago(p.OnlineSince, now) : "visto " + InviteClient.Ago(p.LastSeen, now), FontSize = 11, Foreground = Brush("MutedBrush") });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var connect = SmallButton("Conectar", (_, _) => AdminConnect(p, false), true);
        var files = SmallButton("Arquivos", (_, _) => AdminConnect(p, true), false);
        var copy = SmallButton("Copiar ID", (_, _) => { if (TryCopy(RustDesk.FormatId(p.RustdeskId))) SetStatus("ID copiado.", "OkBrush"); }, false);
        foreach (var b in new[] { connect, files, copy }) { b.Margin = new Thickness(0, 0, 6, 0); buttons.Children.Add(b); }
        connect.IsEnabled = files.IsEnabled = p.Online;
        if (!p.Online) connect.ToolTip = files.ToolTip = "O cliente está offline: o MB Extreme dele está fechado.";

        // duas linhas, para caber na coluna: em cima quem é e o ID; embaixo o PC e os botões
        var top = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(idCol, Dock.Right);
        idCol.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(dot);
        top.Children.Add(idCol);
        top.Children.Add(who);
        var bottom = new DockPanel { Margin = new Thickness(20, 6, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        pc.Margin = new Thickness(0, 0, 8, 0);
        bottom.Children.Add(buttons);
        bottom.Children.Add(pc);
        var grid = new StackPanel();
        grid.Children.Add(top);
        grid.Children.Add(bottom);

        return new Border
        {
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(10), Child = grid,
            Background = asking ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xC4, 0x00)) : Brush("Surface2Brush"),
            BorderBrush = asking ? new SolidColorBrush(Yellow) : Brush("BorderBrush"), BorderThickness = new Thickness(1),
        };
    }

    private static Border PlanTag(SupportListItem p)
    {
        string text = p.PlanStatus == "trialing" ? "Teste 3 dias" : p.Plan switch
        {
            "essencial" => "Essencial", "gamer" => "Gamer", "creator" => "Creator", "tecnico" => "Técnico", "free" => "Grátis", _ => p.Plan,
        };
        bool tech = p.Plan == "tecnico" && p.PlanStatus != "trialing";
        return new Border
        {
            CornerRadius = new CornerRadius(999), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = tech ? new SolidColorBrush(Yellow) : new SolidColorBrush(Color.FromArgb(0x30, 0x3D, 0xDC, 0x97)),
            Child = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = tech ? Brushes.Black : new SolidColorBrush(Green) },
        };
    }

    /// <summary>Abre o acesso no ID do cliente sem senha: o cliente aceita na tela dele.</summary>
    private void AdminConnect(SupportListItem p, bool files)
    {
        if (!RustDeskHost.IsInstalled) { SetStatus("Ative o acesso remoto neste PC primeiro (botão Ativar acesso remoto).", "WarnBrush"); return; }
        try
        {
            RustDeskHost.Connect(p.RustdeskId, null, files);
            SetStatus($"Pedido de acesso enviado para {(p.Name.Length > 0 ? p.Name : p.PcName)}. Aguarde o cliente aceitar na tela dele.", "OkBrush");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível abrir a conexão: " + ex.Message, "BadBrush");
        }
    }
}
