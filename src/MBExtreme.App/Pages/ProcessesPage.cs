using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Tools;

namespace MBExtreme.UI.Pages;

/// <summary>Gerenciador de processos: quem usa mais memória e processador, com encerramento protegido.</summary>
public sealed class ProcessesPage : ToolPageBase
{
    private readonly ProcessList _list = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly StackPanel _rows = new();
    private int _sort;
    private bool _busy;

    public ProcessesPage() : base("Processos",
        "Os programas que mais usam memória e processador agora. Você pode encerrar um programa que travou; os processos essenciais do Windows ficam protegidos.")
    {
        AddButton("Atualizar", async (_, _) => await RefreshAsync(), false);
        Body.Children.Add(Muted("Ordenar por:"));
        Body.Children.Add(Choice(new[] { "Memória", "Processador" }, _sort, i => { _sort = i; _ = RefreshAsync(); }));
        Body.Children.Add(_rows);

        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var all = await Task.Run(_list.Sample);
            var top = (_sort == 0 ? all.OrderByDescending(p => p.WorkingSetBytes) : all.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes))
                .Take(30).ToList();

            _rows.Children.Clear();
            foreach (var p in top)
            {
                var process = p;
                string title = process.Title is null ? "" : $" · {process.Title}";
                string detail = $"Memória {process.WorkingSetBytes / 1_048_576.0:0} MB · Processador {process.CpuPercent:0.0}% · PID {process.Id}{title}";
                UIElement? button = ProcessList.IsProtectedName(process.Name)
                    ? new TextBlock { Text = "Essencial", Foreground = Brush("MutedBrush"), FontSize = 12 }
                    : SmallButton("Encerrar", (_, _) => Kill(process));
                _rows.Children.Add(Row(process.Name, detail, button));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            SetStatus("Não foi possível listar os processos: " + ex.Message, "WarnBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    private void Kill(ProcessEntry process)
    {
        if (MessageBox.Show($"Encerrar {process.Name}?\n\nDados não salvos neste programa serão perdidos.", "Encerrar programa",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var (ok, message) = ProcessList.Kill(process.Id);
        SetStatus(message, ok ? "OkBrush" : "WarnBrush");
        _ = RefreshAsync();
    }
}
