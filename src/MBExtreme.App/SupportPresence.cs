using System.IO;
using System.Net.Http;
using MBExtreme.UI.Controls;
using System.Management;
using System.Windows;
using System.Windows.Threading;
using MBExtreme.Core.Remote;

namespace MBExtreme.UI;

/// <summary>
/// Suporte rápido da MB InfoArt: com a autorização do cliente (perguntada uma vez, depois de entrar na conta), instala o módulo de
/// acesso remoto em segundo plano e avisa o painel "Suporte ao vivo" a cada 2 minutos que este PC está online (ID de acesso, nome do
/// PC e peças; nunca a senha). O técnico conecta pedindo aceite: o cliente sempre decide na tela.
/// </summary>
internal static class SupportPresence
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMinutes(2) };
    private static string? _id;
    private static (string Os, string Cpu, string Gpu)? _specs;
    private static bool _started, _sending;

    public static string PrefsFile => SupportPrefs.FileIn(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    public static SupportPrefs Prefs => SupportPrefs.Load(PrefsFile);

    /// <summary>Chamado quando a janela abre: pergunta uma vez (só com conta) e liga o aviso se autorizado.</summary>
    public static void Start(Window owner)
    {
        if (!InviteApi.Ready) return;   // sem conta: não há para quem aparecer no painel
        var prefs = Prefs;
        if (prefs.QuickSupport is null)
        {
            bool yes = Ask(owner);
            prefs = prefs with { QuickSupport = yes };
            Save(prefs);
        }
        if (prefs.QuickSupport == true) Begin();
    }

    public static void SetEnabled(bool on)
    {
        Save(Prefs with { QuickSupport = on, HelpSince = on ? Prefs.HelpSince : null });
        if (on) Begin();
        else
        {
            Timer.Stop();
            _started = false;
            _ = Task.Run(async () =>
            {
                try { await InviteApi.Client.SupportOptOutAsync(InviteApi.License, InviteApi.Device); }
                catch (Exception ex) when (ex is MBExtreme.Core.Licensing.LicenseNetworkException or HttpRequestException or TaskCanceledException) { }
            });
        }
    }

    /// <summary>"Pedir ajuda": aparece em destaque no painel do suporte por 1 hora (ou até cancelar).</summary>
    public static async Task RequestHelpAsync(bool on)
    {
        Save(Prefs with { HelpSince = on ? DateTime.Now : null });
        await SendAsync();
    }

    private static void Begin()
    {
        if (_started) return;
        _started = true;
        Timer.Tick -= OnTick;
        Timer.Tick += OnTick;
        Timer.Start();
        _ = SendAsync();
    }

    private static async void OnTick(object? sender, EventArgs e) => await SendAsync();

    private static async Task SendAsync()
    {
        if (_sending || !InviteApi.Ready || Prefs.QuickSupport != true) return;
        _sending = true;
        try
        {
            if (!RustDeskHost.IsInstalled)
            {
                await RustDeskHost.InstallAsync(new Progress<string>(_ => { }), CancellationToken.None);
                await RustDeskHost.ApplyOptionsAsync(Prefs.Unattended, CancellationToken.None);
            }
            _id ??= await RustDeskHost.GetIdAsync(CancellationToken.None);
            if (_id is null) return;
            _specs ??= await Task.Run(ReadSpecs);
            var prefs = Prefs;
            var beat = new SupportBeat(_id, SupportPrefs.Clip(Environment.MachineName, 60), _specs.Value.Os, _specs.Value.Cpu, _specs.Value.Gpu,
                prefs.HelpActive(DateTime.Now), await Task.Run(RustDeskHost.SessionOpen));
            if (beat.InSession && prefs.HelpSince is not null) Save(prefs with { HelpSince = null });   // já está sendo atendido
            await InviteApi.Client.SupportHeartbeatAsync(InviteApi.License, InviteApi.Device, beat);
        }
        catch (Exception ex) when (ex is MBExtreme.Core.Licensing.LicenseNetworkException or HttpRequestException or TaskCanceledException
                                       or IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // sem internet ou RustDesk ocupado: tenta de novo no próximo ciclo
        }
        finally { _sending = false; }
    }

    private static (string Os, string Cpu, string Gpu) ReadSpecs()
    {
        string os = SupportPrefs.Clip(MBExtreme.Core.Windows.SystemSummary.WindowsName(), 80);
        return (os, SupportPrefs.Clip(First("SELECT Name FROM Win32_Processor"), 80), SupportPrefs.Clip(First("SELECT Name FROM Win32_VideoController"), 80));
    }

    private static string First(string query)
    {
        try
        {
            using var s = new ManagementObjectSearcher(query);
            return s.Get().Cast<ManagementObject>().Select(o => o["Name"] as string).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "";
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return ""; }
    }

    private static void Save(SupportPrefs prefs)
    {
        try { prefs.Save(PrefsFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool Ask(Window owner)
    {
        var dialog = new Pages.FormDialog(owner, "Suporte rápido da MB InfoArt",
            "Quer que o suporte da MB InfoArt consiga te ajudar sem você precisar passar ID e senha?", "Permitir suporte rápido");
        dialog.Note("• O MB avisa ao nosso painel quando este PC está ligado com o programa aberto (nome do PC, peças, plano e o ID de acesso). Nunca a senha.");
        dialog.Note("• Quando precisarmos, pedimos acesso e você decide na tela: Aceitar ou Recusar. Sem o seu clique, ninguém entra.");
        dialog.Note("• Durante o acesso, uma faixa mostra que alguém está conectado, com o botão Encerrar.");
        dialog.Note("• Instala o módulo de acesso remoto (RustDesk, código aberto, 25 MB). Dá para desligar quando quiser em Sobre › Suporte.");
        return dialog.ShowDialog() == true;
    }
}
