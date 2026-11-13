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

        // Benchmark: o FSR roda num processo separado (código nativo da AMD). Sem janela do programa, sem trava de cópia única.
        if (e.Args.Length > 0 && e.Args[0] == MBExtreme.Core.Benchmark.FsrBench.Argument)
        {
            int code;
            try { code = MBExtreme.Core.Benchmark.FsrBench.ChildMain(e.Args); }
            catch (Exception) { code = 3; }
            Shutdown(code);
            return;
        }

        // ação vinda do botão direito do Windows (MBExtreme.exe --acao chave [caminho])
        var action = MBExtreme.Core.Windows.ShellActions.Parse(e.Args);

        // uma janela só: duas cópias abertas poderiam rodar dois reparos ou duas atualizações ao mesmo tempo
        _instance = new System.Threading.Mutex(true, @"Local\MBExtreme-Instancia", out bool first);
        if (!first)
        {
            if (action is { } a) QueueAction(a.Action.Key, a.Path);
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
        PendingAction = action;
        StartInTray = e.Args.Any(a => a.Equals(MBExtreme.Core.Setup.StartupTask.TrayArgument, StringComparison.OrdinalIgnoreCase));
        ApplyFirstRunOptions();
        SingleInstance.ListenForOthers(() => Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.ActivateFromOther())));

        Accessibility.Install();
        // animações desligadas (opção do programa ou do Windows): efeitos de passar o mouse instantâneos
        Motion.ApplyToTheme(Resources.MergedDictionaries.Count > 0 ? Resources.MergedDictionaries[0] : Resources);
        base.OnStartup(e);
    }

    private System.Threading.Mutex? _instance;

    /// <summary>Aberto pelo Windows ao entrar (--tray): fica só na bandeja, sem mostrar a janela.</summary>
    internal static bool StartInTray { get; private set; }

    /// <summary>Escolhas do instalador (barra flutuante, bandeja com o Windows), aplicadas uma vez.</summary>
    private static void ApplyFirstRunOptions()
    {
        string file = Path.Combine(AppContext.BaseDirectory, MBExtreme.Core.Setup.FirstRunOptions.FileName);
        try
        {
            if (!File.Exists(file)) return;
            if (MBExtreme.Core.Setup.FirstRunOptions.Parse(File.ReadAllText(file)) is { } o)
            {
                AppServices.Settings.WidgetEnabled = o.Widget;
                AppServices.Settings.StartWithWindows = o.TrayAtStartup;
                if (o.TrayAtStartup) AppServices.Settings.MinimizeToTray = true;
                AppServices.Settings.Save();
            }
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Ação pedida ao abrir o programa (feita quando a janela principal estiver pronta).</summary>
    internal static (MBExtreme.Core.Windows.ShellAction Action, string? Path)? PendingAction { get; set; }

    private static string QueueFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MBExtreme", "acao-pendente.txt");

    /// <summary>A segunda cópia deixa a ação num arquivo para a primeira pegar (junto com o aviso para aparecer).</summary>
    private static void QueueAction(string key, string? path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(QueueFile)!);
            File.WriteAllText(QueueFile, key + "\n" + (path ?? ""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Pega a ação deixada por outra cópia (só se for recente: menos de 1 minuto).</summary>
    internal static (MBExtreme.Core.Windows.ShellAction Action, string? Path)? TakeQueuedAction()
    {
        try
        {
            var info = new FileInfo(QueueFile);
            if (!info.Exists) return null;
            bool fresh = DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromMinutes(1);
            var lines = File.ReadAllText(QueueFile).Split('\n');
            File.Delete(QueueFile);
            if (!fresh || MBExtreme.Core.Windows.ShellActions.Find(lines[0].Trim()) is not { } found) return null;
            string? path = lines.Length > 1 && lines[1].Trim().Length > 0 ? lines[1].Trim() : null;
            return (found, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

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
        MBExtreme.Core.Setup.StartupTask.Disable();

        if (!InstallInfo.IsSafeInstallDirectory(directory))
        {
            MessageBox.Show("Não foi possível confirmar a pasta de instalação. Nada foi removido.", "Desinstalar MB Extreme (Beta 01)",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            InstallInfo.RemoveIntegration();
            try { MBExtreme.Core.Windows.ShellActions.Uninstall(); }   // itens do botão direito do Windows
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }

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
