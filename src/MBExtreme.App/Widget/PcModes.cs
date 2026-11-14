namespace MBExtreme.UI.Widget;

/// <summary>
/// Modos do PC no widget. Ideia central: placa de vídeo e processador econômicos sem jogo aberto; tudo no máximo só enquanto
/// o jogo roda (o Modo Jogo troca o plano de energia para Alto desempenho e volta ao sair).
/// </summary>
internal static class PcModes
{
    public enum Mode { Normal, Gamer, Turbo, Silencioso }

    private const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string PowerSaver = "a1841308-3334-4fc9-8ecd-be729a8c5c95";

    public static Mode Current => Enum.TryParse<Mode>(AppServices.Settings.PcMode, out var m) ? m : AppServices.Boost.Settings.BoostAuto ? Mode.Gamer : Mode.Normal;

    public static (string Title, string Sub) Describe(Mode m) => m switch
    {
        Mode.Normal => ("Normal", "econômico, nada pausado"),
        Mode.Gamer => ("Gamer", "liga sozinho quando o jogo abre"),
        Mode.Turbo => ("Turbo", "tudo no máximo agora"),
        _ => ("Silencioso", "menos ventoinha e consumo"),
    };

    public static string Status()
    {
        var boost = AppServices.Boost;
        return Current switch
        {
            Mode.Gamer when boost.IsActive => $"Modo Gamer · {(boost.ActiveGame is { } g && g != "manual" ? g : "jogo")} aberto: tudo no máximo",
            Mode.Gamer => "Modo Gamer · esperando o jogo (placa de vídeo econômica)",
            Mode.Turbo => "Modo Turbo · tudo no máximo",
            Mode.Silencioso => "Modo Silencioso · economia de energia",
            _ => "Modo Normal",
        };
    }

    public static async Task ApplyAsync(Mode mode)
    {
        var boost = AppServices.Boost;
        var s = boost.Settings;
        AppServices.Settings.PcMode = mode.ToString();
        AppServices.Settings.Save();
        switch (mode)
        {
            case Mode.Normal:
            case Mode.Silencioso:
                s.BoostCloseWindows = false;
                s.Save();
                if (boost.IsActive) await boost.DeactivateAsync();
                boost.SetAuto(false);
                await Task.Run(() => SetPlan(mode == Mode.Silencioso ? PowerSaver : Balanced));
                break;
            case Mode.Gamer:
                s.BoostCloseWindows = false;
                s.Save();
                if (boost.IsActive && boost.ActiveGame == "manual") await boost.DeactivateAsync();
                boost.SetAuto(true);
                await Task.Run(() => SetPlan(Balanced));   // sem jogo: econômico; o Modo Jogo sobe para o máximo quando o jogo abre
                break;
            case Mode.Turbo:
                s.BoostCloseWindows = true;
                s.Save();
                if (boost.IsActive) await boost.DeactivateAsync();
                await boost.ActivateNowAsync();
                break;
        }
    }

    private static void SetPlan(string guid)
    {
        if (!Powercfg(guid) && guid != Balanced) Powercfg(Balanced);   // "Economia de energia" não existe em todo PC

        static bool Powercfg(string scheme)
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powercfg.exe", "/setactive " + scheme) { CreateNoWindow = true, UseShellExecute = false });
                if (p is null) return false;
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
        }
    }
}
