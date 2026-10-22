using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Tools;

namespace MBExtreme.UI.Pages;

/// <summary>Destruir arquivos: sobrescreve o conteúdo antes de apagar, sem passar pela Lixeira.</summary>
public sealed class ShredderPage : ToolPageBase
{
    private readonly List<string> _files = new();
    private readonly StackPanel _list = new();
    private int _passes;
    private bool _busy;

    public ShredderPage() : base("Destruir arquivos",
        "Apaga arquivos de forma definitiva: o conteúdo é sobrescrito com dados aleatórios antes de remover, e o arquivo não vai para a Lixeira. Não dá para recuperar depois.")
    {
        AddButton("Escolher arquivos...", (_, _) => Pick(), false);
        AddButton("Destruir definitivamente", async (_, _) => await ShredAsync());

        Body.Children.Add(Heading("Passagens de sobrescrita"));
        Body.Children.Add(Choice(new[] { "1 (suficiente)", "3 (mais lenta)" }, _passes, i => _passes = i));
        Body.Children.Add(Muted("Em SSD e em pendrives, o disco decide onde grava, então a sobrescrita não garante que todo vestígio some; para dados realmente sensíveis, use a criptografia do disco (BitLocker). Em HD comum, o método é eficaz."));
        Body.Children.Add(Heading("Arquivos escolhidos"));
        Body.Children.Add(_list);
        Render();
    }

    private void Pick()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Escolha os arquivos a destruir" };
        if (dialog.ShowDialog() != true) return;
        foreach (string file in dialog.FileNames)
        {
            if (PathGuard.IsProtected(file)) { SetStatus($"{Path.GetFileName(file)} está em uma pasta protegida do Windows e foi ignorado.", "WarnBrush"); continue; }
            if (!_files.Contains(file)) _files.Add(file);
        }

        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        if (_files.Count == 0) { _list.Children.Add(Muted("Nenhum arquivo escolhido.")); return; }
        foreach (string file in _files.ToList())
        {
            string path = file;
            _list.Children.Add(Row(Path.GetFileName(path), Path.GetDirectoryName(path) ?? "", SmallButton("Tirar", (_, _) => { if (!_busy) { _files.Remove(path); Render(); } })));
        }
    }

    private async Task ShredAsync()
    {
        if (_busy || _files.Count == 0) return;
        if (MessageBox.Show($"Destruir {_files.Count} arquivo(s) para sempre?\n\nNão há como recuperar.", "Destruir arquivos",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show("Tem certeza? Esta é a última confirmação.", "Destruir arquivos", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes) return;

        _busy = true;
        try
        {
            int passes = _passes == 0 ? 1 : 3;
            var done = new List<string>();
            var failed = new List<string>();
            foreach (string file in _files.ToList())
            {
                if (!_files.Contains(file)) continue;
                SetStatus($"Destruindo {Path.GetFileName(file)}...");
                try
                {
                    await Task.Run(() => Shredder.Shred(file, passes));
                    done.Add(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                    or NotSupportedException or ArgumentException or System.Security.SecurityException)
                {
                    failed.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }

            foreach (string file in done) _files.Remove(file);
            Render();
            SetStatus(failed.Count == 0 ? $"{done.Count} arquivo(s) destruído(s)." : $"{done.Count} destruído(s); falhou: {string.Join("; ", failed)}",
                failed.Count == 0 ? "OkBrush" : "WarnBrush");
        }
        finally
        {
            _busy = false;
        }
    }
}
