using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MBExtreme.Core.Setup;

namespace MBExtreme.UI;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { MBExtreme.UI.AppServices.Cameras.CloseAll(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }   // não deixa câmera presa aberta
        AppServices.Shutdown();
        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // "Aplicativos instalados" do Windows chama o programa com /uninstall.
        if (e.Args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstall();
            Shutdown(0);
            return;
        }

        // Tarefa agendada da limpeza semanal: roda sem janela e sai.
        if (e.Args.Any(a => a.Equals(MBExtreme.Core.Cleanup.WeeklyRoutine.Argument, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                MBExtreme.Core.Cleanup.CleanupService.Excluded = MBExtreme.Core.Settings.AppSettings.Load().CleanupExcluded;
                string text = MBExtreme.Core.Cleanup.WeeklyRoutine.RunClean(DateTime.UtcNow);
                Directory.CreateDirectory(Path.GetDirectoryName(MBExtreme.Core.Cleanup.WeeklyRoutine.LogPath)!);
                File.AppendAllText(MBExtreme.Core.Cleanup.WeeklyRoutine.LogPath, text + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Shutdown(0);
            return;
        }

        // uma janela só: duas cópias abertas poderiam rodar dois reparos ou duas atualizações ao mesmo tempo
        _instance = new System.Threading.Mutex(true, @"Local\MBExtreme-Instancia", out bool first);
        if (!first)
        {
            SingleInstance.SignalFirst();
            // a outra cópia pode estar fechando (atualização, reinício): espera um pouco antes de desistir
            try { first = _instance.WaitOne(TimeSpan.FromSeconds(3)); }
            catch (System.Threading.AbandonedMutexException) { first = true; }   // a outra fechou sem soltar: agora é esta
            if (!first)
            {
                Shutdown(0);
                return;
            }
        }
        SingleInstance.ListenForOthers(() => Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.ActivateFromOther())));

        Accessibility.Install();
        base.OnStartup(e);
    }

    private System.Threading.Mutex? _instance;

    private static void RunUninstall()
    {
        string? exe = Environment.ProcessPath;
        string directory = exe is null ? "" : Path.GetDirectoryName(exe) ?? "";

        var answer = MessageBox.Show(
            "Remover o MB Extreme (Beta 01) deste computador?\n\n" +
            "Os ajustes do Windows que você aplicou NÃO são desfeitos ao desinstalar. " +
            "Para desfazê-los, cancele, abra o programa e use \"Restaurar originais\" em cada página.\n\n" +
            "Os backups dos valores originais ficam em %AppData%\\MBExtreme.",
            "Desinstalar MB Extreme (Beta 01)", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        if (!InstallInfo.IsSafeInstallDirectory(directory))
        {
            MessageBox.Show("Não foi possível confirmar a pasta de instalação. Nada foi removido.", "Desinstalar MB Extreme (Beta 01)",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            InstallInfo.RemoveIntegration();

            // O programa em execução não consegue apagar a própria pasta: um comando separado apaga depois que ele fechar.
            var cleanup = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            cleanup.ArgumentList.Add("/c");
            cleanup.ArgumentList.Add($"ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{directory}\"");
            Process.Start(cleanup);

            MessageBox.Show("MB Extreme (Beta 01) removido.", "Desinstalar MB Extreme (Beta 01)", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                       or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show("Não foi possível concluir a remoção: " + ex.Message, "Desinstalar MB Extreme (Beta 01)",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (e.Exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Falta de permissão em uma ação isolada não deve fechar o programa: grava o erro e avisa.
            LogOnly(e.Exception);
            MessageBox.Show("Uma ação não teve permissão do Windows e foi ignorada:\n\n" + e.Exception.Message +
                "\n\nSe precisar dela, feche e abra o MB Extreme como administrador. O detalhe foi gravado em erro.log.",
                "MB Extreme (Beta 01)", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Report(e.Exception);
        Shutdown(1);
    }

    private static void LogOnly(Exception exception)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "erro.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} (não fatal)\n{exception}\n\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
            "O MB Extreme (Beta 01) encontrou um erro e precisa fechar.\n\n" + (exception?.Message ?? "Erro desconhecido") + where,
            "MB Extreme (Beta 01)", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
