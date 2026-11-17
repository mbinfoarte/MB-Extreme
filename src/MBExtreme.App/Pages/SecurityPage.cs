using System.Diagnostics;
using System.Windows;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Segurança: estado do antivírus, do firewall e do controle de contas, com atalhos para a Segurança do Windows.</summary>
public sealed class SecurityPage : ToolPageBase
{
    private readonly System.Windows.Controls.Button _scan;
    private readonly System.Windows.Controls.Button _update;
    private bool _busy;
    private SecurityStatus? _last;

    public SecurityPage() : base("Segurança",
        "Veja se o Windows está protegido: antivírus, definições de vírus, firewall e controle de contas. O MB Extreme só lê e mostra; para mudar, abra a Segurança do Windows. Nada de antivírus de terceiros embutido.")
    {
        AddButton("Atualizar", async (_, _) => await LoadAsync(), false);
        _scan = AddButton("Verificação rápida", async (_, _) => await RunAsync("Verificando o computador (pode levar alguns minutos)...", SecurityService.QuickScan), false);
        AddButton("Corrigir problemas", async (_, _) => await FixAsync());
        _update = AddButton("Atualizar definições", async (_, _) => await RunAsync("Baixando as definições de vírus...", SecurityService.UpdateSignatures), false);
        AddButton("Abrir Segurança do Windows", (_, _) => OpenSecurity(), false);
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Body.Children.Clear();
        SetStatus("Lendo a proteção do Windows...");
        try
        {
            var status = await Task.Run(SecurityService.Read);
            _last = status;
            var items = SecurityService.Evaluate(status, DateTime.Now);
            Body.Children.Clear();
            foreach (var item in items)
            {
                (string mark, string brush) = item.Level switch
                {
                    SecurityLevel.Ok => ("✔", "OkBrush"),
                    SecurityLevel.Warning => ("●", "WarnBrush"),
                    _ => ("✖", "BadBrush"),
                };
                Body.Children.Add(Row($"{mark}  {item.Title}", item.Detail, null, brush));
            }

            int problems = items.Count(i => i.Level != SecurityLevel.Ok);
            SetStatus(problems == 0 ? "Tudo certo com a proteção do Windows." : $"{problems} {(problems == 1 ? "item precisa" : "itens precisam")} de atenção.",
                problems == 0 ? "OkBrush" : "WarnBrush");
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível ler a proteção do Windows: " + ex.Message, "BadBrush");
        }
    }

    private async Task FixAsync()
    {
        var plan = _last is null ? Array.Empty<SecurityFix>() : SecurityFixer.Plan(_last);
        if (plan.Count == 0) { SetStatus("Não há nada que o MB Extreme possa corrigir sozinho agora.", "OkBrush"); return; }
        if (MessageBox.Show("O MB Extreme vai fazer o seguinte:\n\n" + string.Join("\n", plan.Select(p => $"• {p.Title}\n   {p.Detail}")) + "\n\nContinuar?",
                "Corrigir segurança", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        SetStatus("Corrigindo...");
        var results = await Task.Run(() => SecurityFixer.Apply(plan));
        await LoadAsync();
        SetStatus(string.Join(" · ", results.Select(r => $"{(r.Ok ? "✔" : "✖")} {r.Fix.Title}: {r.Message}")), results.All(r => r.Ok) ? "OkBrush" : "WarnBrush");
    }

    private async Task RunAsync(string working, Func<(bool Ok, string Message)> action)
    {
        if (_busy) return;
        _busy = true;
        _scan.IsEnabled = _update.IsEnabled = false;
        SetStatus(working);
        try
        {
            var (ok, message) = await Task.Run(action);
            SetStatus(message, ok ? "OkBrush" : "WarnBrush");
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível executar: " + ex.Message, "BadBrush");
        }
        finally
        {
            _busy = false;
            _scan.IsEnabled = _update.IsEnabled = true;
        }

        await LoadAsync();
    }

    private void OpenSecurity()
    {
        try
        {
            Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível abrir a Segurança do Windows.", "WarnBrush");
        }
    }
}
