using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MBExtreme.UI;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    /// <summary>Grava o erro em %AppData%\MBExtreme\erro.log e mostra uma mensagem em vez de fechar sem avisar.</summary>
    private static void Report(Exception? exception)
    {
        string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{exception}\n\n";
        string where = "";
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "erro.log");
            File.AppendAllText(file, text);
            where = $"\n\nO erro foi gravado em:\n{file}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem onde gravar: só mostrar a mensagem.
        }

        MessageBox.Show(
            "O MB Extreme encontrou um erro e precisa fechar.\n\n" + (exception?.Message ?? "Erro desconhecido") + where,
            "MB Extreme", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
