using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Licensing;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Atendimento técnico (só a conta de administrador): iniciar e finalizar o atendimento deste PC, o cadastro das empresas e o
/// histórico das visitas com a cobrança. Os dados ficam no servidor e aparecem também no painel do site.
/// </summary>
public sealed class ServicePage : ToolPageBase
{
    private static readonly CultureInfo Br = new("pt-BR");
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00), Green = Color.FromRgb(0x3D, 0xDC, 0x97), Orange = Color.FromRgb(0xFF, 0xB0, 0x20), Red = Color.FromRgb(0xFF, 0x7B, 0x7B);

    private readonly StackPanel _top = new();
    private readonly TextBlock _kpis = new() { FontSize = 13, Margin = new Thickness(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _companies = new();
    private readonly StackPanel _detail = new();
    private readonly TextBox _search = new() { FontSize = 13, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 8) };
    private ServiceCompanies? _data;
    private string? _selected;
    private string _payment = "a_receber";

    public ServicePage() : base("Atendimento técnico", "Manutenção nas empresas sem assinatura: entre com a sua conta de administrador, otimize, finalize e as otimizações ficam no PC. Empresas, visitas e cobrança num lugar só (também no painel do site).")
    {
        Body.Children.Add(_top);
        Body.Children.Add(_kpis);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var add = SmallButton("+ Nova empresa", (_, _) => EditCompany(null), true);
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);
        head.Children.Add(new TextBlock { Text = "Empresas", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(head);
        System.Windows.Automation.AutomationProperties.SetName(_search, "Buscar empresa");
        _search.ToolTip = "Buscar empresa";
        _search.TextChanged += (_, _) => RenderCompanies();
        left.Children.Add(_search);
        left.Children.Add(_companies);
        grid.Children.Add(CardOf(left));
        var right = CardOf(_detail);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        Body.Children.Add(grid);
        Loaded += async (_, _) => await LoadAsync();
        ServiceMode.Changed += () => Dispatcher.BeginInvoke(RenderTop);
    }

    private static string Money(double v) => v.ToString("C", Br);
    private static string Date(long? ms) => ms is { } m ? DateTimeOffset.FromUnixTimeMilliseconds(m).ToLocalTime().ToString("dd/MM/yyyy", Br) : "—";

    private async Task LoadAsync()
    {
        if (ServiceMode.Client is not { } client) { ShowLocked("Entre com a sua conta de administrador para usar o atendimento técnico."); return; }
        try
        {
            _data = await client.CompaniesAsync();
            if (_data is null) { ShowLocked("Só a conta de administrador faz atendimento técnico."); return; }
            var t = _data.Totals;
            _kpis.Text = $"Atendimentos no mês: {t.MonthCount}   ·   Recebido no mês: {Money(t.MonthPaid)}   ·   A receber: {Money(t.Due)}   ·   Sem visita há mais de 30 dias: {t.Stale}";
            RenderTop();
            RenderCompanies();
            if (_selected is not null) await ShowCompanyAsync(_selected);
            else _detail.Children.Clear();
        }
        catch (LicenseNetworkException ex) { SetStatus("Não foi possível carregar: " + ex.Message, "BadBrush"); }
    }

    private void ShowLocked(string text)
    {
        Body.Children.Clear();
        Body.Children.Add(CardOf(Muted(text, 13.5)));
    }

    // ---------- atendimento deste PC ----------

    private void RenderTop()
    {
        _top.Children.Clear();
        if (ServiceMode.Active is { } v) { _top.Children.Add(CardOf(BuildActive(v))); return; }
        if (_data is null) return;
        _top.Children.Add(CardOf(BuildStart()));
    }

    private StackPanel BuildStart()
    {
        var p = new StackPanel();
        p.Children.Add(Head("INICIAR ATENDIMENTO NESTE PC"));
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var company = new ComboBox { Width = 260, FontSize = 13, Margin = new Thickness(0, 0, 10, 6) };
        foreach (var c in _data!.Items) company.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c });
        System.Windows.Automation.AutomationProperties.SetName(company, "Empresa");
        var pc = new TextBox { Width = 200, Text = Environment.MachineName, FontSize = 13, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 10, 6), ToolTip = "Nome deste PC na empresa (ex.: PC do caixa)" };
        System.Windows.Automation.AutomationProperties.SetName(pc, "Nome deste PC na empresa");
        var price = new TextBox { Width = 110, Text = "0,00", FontSize = 13, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 10, 6), ToolTip = "Valor do atendimento (R$)" };
        System.Windows.Automation.AutomationProperties.SetName(price, "Valor do atendimento em reais");
        company.SelectionChanged += (_, _) => { if (company.SelectedItem is ComboBoxItem { Tag: ServiceCompany c }) price.Text = c.Price.ToString("0.00", Br); };
        if (_selected is not null)
            foreach (ComboBoxItem i in company.Items) if (i.Tag is ServiceCompany c && c.Id == _selected) company.SelectedItem = i;
        row.Children.Add(Labeled("Empresa", company));
        row.Children.Add(Labeled("Nome deste PC", pc));
        row.Children.Add(Labeled("Valor (R$)", price));
        var start = SmallButton("Iniciar atendimento", async (_, _) =>
        {
            if (company.SelectedItem is not ComboBoxItem { Tag: ServiceCompany c }) { SetStatus("Escolha a empresa (ou cadastre uma nova).", "WarnBrush"); return; }
            if (!double.TryParse(price.Text.Replace("R$", "").Trim(), NumberStyles.Number, Br, out double value)) value = 0;
            try
            {
                string id = await ServiceMode.Client!.StartAsync(c.Id, pc.Text.Trim().Length > 0 ? pc.Text.Trim() : Environment.MachineName, value);
                ServiceMode.Begin(new ServiceVisitState { VisitId = id, CompanyId = c.Id, CompanyName = c.Name, PcName = pc.Text.Trim(), Price = value, StartedAt = DateTime.Now });
                SetStatus("Atendimento iniciado. O que você aplicar vai sendo anotado sozinho.", "OkBrush");
            }
            catch (LicenseNetworkException ex) { SetStatus(ex.Message, "BadBrush"); }
        }, true);
        start.VerticalAlignment = VerticalAlignment.Bottom;
        start.Margin = new Thickness(0, 0, 0, 6);
        row.Children.Add(start);
        p.Children.Add(row);
        if (ServiceMode.Sealed && ServiceSeal.Info(AppServices.License.Config.PublicKey, AppServices.License.DeviceId) is { } info)
            p.Children.Add(Muted($"Este PC já tem selo de atendimento ({info.Company}, {info.When.ToLocalTime():dd/MM/yyyy}).", 11.5));
        return p;
    }

    private StackPanel BuildActive(ServiceVisitState v)
    {
        var p = new StackPanel();
        var head = Head($"ATENDIMENTO EM ANDAMENTO · {v.CompanyName} · {v.PcName} · desde {v.StartedAt:HH:mm}");
        p.Children.Add(head);
        p.Children.Add(Muted("Cada otimização aplicada é anotada sozinha. Anote também o que fez à mão.", 12));
        var items = new StackPanel { Margin = new Thickness(0, 8, 0, 6) };
        var report = v.ReportItems();
        if (report.Count == 0) items.Children.Add(Muted("Nada anotado ainda.", 12));
        foreach (string i in report) items.Children.Add(new TextBlock { Text = "● " + i, FontSize = 12.5, Margin = new Thickness(0, 1, 0, 1), Foreground = new SolidColorBrush(Green) });
        p.Children.Add(items);

        var noteRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var note = new TextBox { FontSize = 13, Padding = new Thickness(6, 4, 6, 4) };
        System.Windows.Automation.AutomationProperties.SetName(note, "Anotar algo feito à mão");
        note.ToolTip = "Ex.: troca da pasta térmica, limpeza física, troca de SSD";
        var addNote = SmallButton("Anotar", (_, _) => { ServiceMode.Note(note.Text); note.Clear(); }, false);
        addNote.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(addNote, Dock.Right);
        noteRow.Children.Add(addNote);
        noteRow.Children.Add(note);
        p.Children.Add(noteRow);

        var finish = new WrapPanel();
        var price = new TextBox { Width = 110, Text = v.Price.ToString("0.00", Br), FontSize = 13, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 10, 6) };
        System.Windows.Automation.AutomationProperties.SetName(price, "Valor em reais");
        finish.Children.Add(Labeled("Valor (R$)", price));
        var pay = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 6) };
        foreach (var (key, text) in new[] { ("a_receber", "A receber"), ("pago", "Pago"), ("pix_enviado", "Pix enviado") })
        {
            var b = SmallButton(text, (_, _) => { _payment = key; RenderTop(); }, key == _payment);
            b.Margin = new Thickness(0, 0, 6, 0);
            pay.Children.Add(b);
        }
        finish.Children.Add(Labeled("Cobrança", pay));
        p.Children.Add(finish);

        var seal = new CheckBox { Content = "Manter as otimizações neste PC (selo de atendimento)", IsChecked = true, Margin = new Thickness(0, 4, 0, 2) };
        var signOut = new CheckBox { Content = "Sair da minha conta de administrador", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
        var whats = new CheckBox { Content = "Abrir o WhatsApp da empresa com o relatório", IsChecked = true, Margin = new Thickness(0, 2, 0, 8) };
        p.Children.Add(seal);
        p.Children.Add(signOut);
        p.Children.Add(whats);

        var buttons = new WrapPanel();
        var done = SmallButton("Finalizar atendimento", async (_, _) => await FinishAsync(v, price.Text, seal.IsChecked == true, signOut.IsChecked == true, whats.IsChecked == true), true);
        var cancel = SmallButton("Cancelar atendimento", (_, _) =>
        {
            if (MessageBox.Show("Cancelar este atendimento? O que foi aplicado continua no PC, mas não vai para o histórico nem ganha selo.", "Atendimento técnico", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                ServiceMode.End();
        }, false);
        done.Margin = cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(done);
        buttons.Children.Add(cancel);
        p.Children.Add(buttons);
        return p;
    }

    private async Task FinishAsync(ServiceVisitState v, string priceText, bool seal, bool signOut, bool whatsApp)
    {
        if (ServiceMode.Client is not { } client) { SetStatus("Sem conexão com a conta.", "BadBrush"); return; }
        if (double.TryParse(priceText.Replace("R$", "").Trim(), NumberStyles.Number, Br, out double value)) v.Price = value;
        try
        {
            SetStatus("Finalizando...", "WarnBrush");
            string? token = await client.FinishAsync(v, _payment, seal);
            if (token is not null) ServiceSeal.Save(token);
            string summary = ServiceClient.Summary(v, DateTime.Now);
            string? phone = _data?.Items.FirstOrDefault(c => c.Id == v.CompanyId)?.Whatsapp;
            ServiceMode.End();
            _payment = "a_receber";
            if (whatsApp)
            {
                string digits = new((phone ?? "").Where(char.IsDigit).ToArray());
                if (digits.Length is 10 or 11) digits = "55" + digits;
                Open((digits.Length >= 12 ? $"https://wa.me/{digits}" : "https://wa.me/") + "?text=" + Uri.EscapeDataString(summary));
            }
            TryCopy(summary);
            SetStatus("Atendimento finalizado" + (token is not null ? " e selo gravado neste PC" : "") + ". O relatório foi copiado.", "OkBrush");
            if (signOut)
            {
                await AppServices.License.SignOutAsync();
                MessageBox.Show("Você saiu da sua conta neste PC." + (token is not null ? " As otimizações ficam aplicadas." : ""), "Atendimento técnico", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
                return;
            }
            await LoadAsync();
        }
        catch (Exception ex) when (ex is LicenseNetworkException or System.IO.IOException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível finalizar: " + ex.Message, "BadBrush");
        }
    }

    // ---------- empresas ----------

    private void RenderCompanies()
    {
        _companies.Children.Clear();
        if (_data is null) return;
        string q = _search.Text.Trim().ToLowerInvariant();
        var list = _data.Items.Where(c => q.Length == 0 || (c.Name + c.Cnpj + c.Contact).ToLowerInvariant().Contains(q)).ToList();
        if (list.Count == 0) { _companies.Children.Add(Muted(_data.Items.Count == 0 ? "Nenhuma empresa ainda. Clique em + Nova empresa." : "Nada encontrado.", 12.5)); return; }
        foreach (var c in list)
        {
            bool late = c.LastVisit is { } lv && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lv > 30L * 86_400_000;
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = c.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            string line = $"{c.Pcs.Count} PC(s) · " + (c.LastVisit is null ? "sem visitas" : "última visita " + Date(c.LastVisit)) + (c.Due > 0 ? " · a receber " + Money(c.Due) : "");
            st.Children.Add(new TextBlock { Text = line, FontSize = 11, Foreground = late ? new SolidColorBrush(Red) : c.Due > 0 ? new SolidColorBrush(Orange) : Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Button
            {
                Content = st, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 4),
                Style = (Style)FindResource(c.Id == _selected ? "PrimaryButton" : "GhostButton"),
            };
            var company = c;
            b.Click += async (_, _) => { _selected = company.Id; RenderCompanies(); RenderTop(); await ShowCompanyAsync(company.Id); };
            _companies.Children.Add(b);
        }
    }

    private async Task ShowCompanyAsync(string id)
    {
        if (_data?.Items.FirstOrDefault(c => c.Id == id) is not { } c) return;
        _detail.Children.Clear();
        var head = new DockPanel();
        var edit = SmallButton("Editar", (_, _) => EditCompany(c), false);
        DockPanel.SetDock(edit, Dock.Right);
        head.Children.Add(edit);
        if (c.Whatsapp.Length > 0)
        {
            var wa = SmallButton("WhatsApp", (_, _) =>
            {
                string d = new(c.Whatsapp.Where(char.IsDigit).ToArray());
                Open("https://wa.me/" + (d.Length is 10 or 11 ? "55" + d : d));
            }, false);
            wa.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(wa, Dock.Right);
            head.Children.Add(wa);
        }
        head.Children.Add(new TextBlock { Text = c.Name, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _detail.Children.Add(head);
        var info = string.Join(" · ", new[] { c.Cnpj.Length > 0 ? "CNPJ " + c.Cnpj : "", c.Contact.Length > 0 ? "Contato: " + c.Contact : "", c.Whatsapp, c.Address }.Where(s => s.Length > 0));
        if (info.Length > 0) _detail.Children.Add(Muted(info, 12));
        if (c.Price > 0) _detail.Children.Add(Muted("Valor por visita: " + Money(c.Price), 12));
        if (c.Notes.Length > 0) _detail.Children.Add(Muted(c.Notes, 12));

        _detail.Children.Add(Spaced(Head("COMPUTADORES")));
        if (c.Pcs.Count == 0) _detail.Children.Add(Muted("Nenhum PC atendido ainda.", 12));
        var pcs = new WrapPanel();
        foreach (var pc in c.Pcs)
            pcs.Children.Add(new Border
            {
                Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(8), Background = Brush("Surface2Brush"),
                BorderBrush = pc.Sealed ? new SolidColorBrush(Green) : Brush("BorderBrush"), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = pc.PcName + " · " + Date(pc.LastVisit) + (pc.Sealed ? " · selo" : ""), FontSize = 12 },
            });
        _detail.Children.Add(pcs);

        _detail.Children.Add(Spaced(Head("HISTÓRICO DE VISITAS")));
        List<ServiceVisitInfo> visits;
        try { visits = await ServiceMode.Client!.VisitsAsync(id); }
        catch (LicenseNetworkException ex) { _detail.Children.Add(Muted("Não foi possível carregar: " + ex.Message, 12)); return; }
        if (visits.Count == 0) { _detail.Children.Add(Muted("Nenhuma visita ainda.", 12)); return; }
        foreach (var v in visits)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var status = new TextBlock
            {
                Text = ServiceClient.PaymentText(v.Payment), FontSize = 11.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
                Foreground = new SolidColorBrush(v.Payment == "pago" ? Green : Orange),
            };
            DockPanel.SetDock(status, Dock.Right);
            row.Children.Add(status);
            if (v.Payment != "pago")
            {
                var visit = v;
                var paid = SmallButton("Marcar pago", async (_, _) =>
                {
                    try { await ServiceMode.Client!.SetPaymentAsync(visit.Id, "pago"); await LoadAsync(); }
                    catch (LicenseNetworkException ex) { SetStatus(ex.Message, "BadBrush"); }
                }, false);
                paid.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(paid, Dock.Right);
                row.Children.Add(paid);
            }
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{Date(v.FinishedAt)} · {v.PcName} · {Money(v.Price)}", FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = string.Join(", ", v.Items) + (v.Notes.Length > 0 ? " · " + v.Notes : ""), FontSize = 11.5, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(text);
            _detail.Children.Add(row);
        }
    }

    private void EditCompany(ServiceCompany? c)
    {
        _detail.Children.Clear();
        _detail.Children.Add(new TextBlock { Text = c is null ? "Nova empresa" : "Editar " + c.Name, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        TextBox Field(string label, string value, double width = 320)
        {
            var t = new TextBox { Text = value, Width = width, FontSize = 13, Padding = new Thickness(6, 4, 6, 4), HorizontalAlignment = HorizontalAlignment.Left };
            System.Windows.Automation.AutomationProperties.SetName(t, label);
            _detail.Children.Add(Labeled(label, t));
            return t;
        }
        var name = Field("Nome da empresa *", c?.Name ?? "");
        var cnpj = Field("CNPJ", c?.Cnpj ?? "", 200);
        var contact = Field("Contato (nome)", c?.Contact ?? "");
        var whats = Field("WhatsApp", c?.Whatsapp ?? "", 200);
        var address = Field("Endereço", c?.Address ?? "", 420);
        var price = Field("Valor por visita (R$)", (c?.Price ?? 0).ToString("0.00", Br), 120);
        var notes = Field("Observações (contrato, frequência...)", c?.Notes ?? "", 420);
        notes.AcceptsReturn = true;
        notes.Height = 60;
        var save = SmallButton("Salvar", async (_, _) =>
        {
            if (name.Text.Trim().Length < 2) { SetStatus("Informe o nome da empresa.", "WarnBrush"); return; }
            double.TryParse(price.Text.Replace("R$", "").Trim(), NumberStyles.Number, Br, out double value);
            try
            {
                _selected = await ServiceMode.Client!.SaveCompanyAsync(new ServiceCompanyInput(c?.Id, name.Text.Trim(), cnpj.Text.Trim(), contact.Text.Trim(), whats.Text.Trim(), address.Text.Trim(), notes.Text.Trim(), value));
                SetStatus("Empresa salva.", "OkBrush");
                await LoadAsync();
            }
            catch (LicenseNetworkException ex) { SetStatus(ex.Message, "BadBrush"); }
        }, true);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Margin = new Thickness(0, 6, 0, 0);
        _detail.Children.Add(save);
    }

    private static TextBlock Head(string text) => new() { Text = text, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Yellow), TextWrapping = TextWrapping.Wrap };

    private static FrameworkElement Spaced(FrameworkElement e) { e.Margin = new Thickness(0, 12, 0, 6); return e; }

    private StackPanel Labeled(string label, FrameworkElement control)
    {
        var st = new StackPanel { Margin = new Thickness(0, 0, 10, 6) };
        st.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 0, 0, 3) });
        control.Margin = new Thickness(0);
        st.Children.Add(control);
        return st;
    }
}
