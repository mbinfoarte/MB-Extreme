using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Controls;

/// <summary>Backup das configurações na conta: salvar agora, automático e restaurar em qualquer PC da conta.</summary>
internal static class CloudBackupService
{
    private static string Build() => CloudBackup.Build(AppServices.Settings, AppSettings.FolderPath, MBExtreme.Core.AppInfo.Version);

    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.RegularExpressions.Regex.Replace(json, "\"createdUtc\":\"[^\"]*\"", ""))))[..16];

    /// <summary>Salva na conta. <paramref name="auto"/>: só se mudou algo desde o último.</summary>
    public static async Task<string> SaveAsync(bool auto)
    {
        if (!InviteApi.Ready) return "Entre com a sua conta para salvar o backup.";
        string json = Build();
        if (Encoding.UTF8.GetByteCount(json) > CloudBackup.MaxBytes) return "As configurações passaram do tamanho do backup.";
        string hash = Hash(json);
        var s = AppServices.Settings;
        if (auto && hash == s.LastBackupHash) { s.LastBackupUtc = DateTime.UtcNow; s.Save(); return "Nada mudou desde o último backup."; }
        await InviteApi.Client.SaveBackupAsync(InviteApi.License, InviteApi.Device, json, auto);
        s.LastBackupUtc = DateTime.UtcNow;
        s.LastBackupHash = hash;
        s.Save();
        return "Backup salvo na sua conta.";
    }

    /// <summary>Backup automático: uma vez por dia, em segundo plano, se estiver ligado.</summary>
    public static async Task AutoAsync()
    {
        var s = AppServices.Settings;
        if (!s.CloudBackupAuto || !InviteApi.Ready || s.LastBackupUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
        try { await SaveAsync(auto: true); }
        catch (Exception ex) when (ex is LicenseNetworkException or System.Net.Http.HttpRequestException or TaskCanceledException or System.IO.IOException) { }
    }

    /// <summary>Baixa e aplica um backup; o programa precisa reiniciar para usar tudo.</summary>
    public static async Task RestoreAsync(string id)
    {
        string json = await InviteApi.Client.GetBackupAsync(InviteApi.License, InviteApi.Device, id);
        var merged = CloudBackup.Restore(json, AppServices.Settings, AppSettings.FolderPath);
        AppSettings.SaveRestoredAndFreeze(merged);
    }
}

internal sealed class BackupCard : Border
{
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    public BackupCard()
    {
        Style = (Style)Application.Current.FindResource("Card");
        Padding = new Thickness(20, 16, 20, 16);
        Margin = new Thickness(0, 10, 0, 0);
        var root = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var save = InviteApi.Button("Salvar agora", true);
        save.Margin = new Thickness(0);
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try { Say(await CloudBackupService.SaveAsync(auto: false), "OkBrush"); await LoadAsync(); }
            catch (LicenseNetworkException ex) { Say(ex.Message, "WarnBrush"); }
            finally { save.IsEnabled = true; }
        };
        DockPanel.SetDock(save, Dock.Right);
        head.Children.Add(save);
        var badge = GlassFx.Badge("", InviteApi.Yellow, 34, breathe: true);
        badge.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Backup das suas configurações", FontSize = 15, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "Perfis de controle, Modo Jogo, contador na tela, ventoinhas, estúdio e suas escolhas no programa. Formatou ou trocou de PC? Restaure com um clique.", FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = InviteApi.Res("MutedBrush") });
        head.Children.Add(titles);
        root.Children.Add(head);
        var auto = new CheckBox { Content = "Backup automático uma vez por dia (só quando algo mudou)", IsChecked = AppServices.Settings.CloudBackupAuto, Foreground = InviteApi.Res("TextBrush"), FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
        auto.Click += (_, _) => { AppServices.Settings.CloudBackupAuto = auto.IsChecked == true; AppServices.Settings.Save(); };
        root.Children.Add(auto);
        root.Children.Add(_list);
        root.Children.Add(new TextBlock { Text = "Não vão para a nuvem: senhas, chaves de transmissão, tokens, a licença e os ajustes do Windows (cada PC aplica os seus).", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = InviteApi.Res("MutedBrush"), Margin = new Thickness(0, 6, 0, 0) });
        root.Children.Add(_status);
        Child = root;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _list.Children.Clear();
        if (!InviteApi.Ready) { _list.Children.Add(new TextBlock { Text = "Entre com a sua conta para usar o backup.", FontSize = 12, Foreground = InviteApi.Res("MutedBrush") }); return; }
        try
        {
            var items = await InviteApi.Client.ListBackupsAsync(InviteApi.License, InviteApi.Device);
            if (items.Count == 0) { _list.Children.Add(new TextBlock { Text = "Nenhum backup ainda. Clique em Salvar agora.", FontSize = 12, Foreground = InviteApi.Res("MutedBrush") }); return; }
            foreach (var b in items)
            {
                var row = new DockPanel();
                var restore = InviteApi.Button("Restaurar");
                restore.Margin = new Thickness(8, 0, 0, 0);
                restore.Padding = new Thickness(12, 4, 12, 4);
                restore.Click += async (_, _) => await RestoreAsync(b);
                DockPanel.SetDock(restore, Dock.Right);
                row.Children.Add(restore);
                string when = b.CreatedMs is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("dd/MM/yyyy HH:mm") : "";
                row.Children.Add(new TextBlock
                {
                    Text = $"{when} · {b.DeviceName}" + (b.Auto ? " · automático" : "") + $" · {b.SizeBytes / 1024.0:0} KB", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                });
                _list.Children.Add(new Border { Background = InviteApi.Res("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 6, 8, 6), Margin = new Thickness(0, 0, 0, 5), Child = row });
            }
        }
        catch (LicenseNetworkException ex) { Say("Não deu para listar os backups: " + ex.Message, "WarnBrush"); }
    }

    private async Task RestoreAsync(CloudBackupInfo b)
    {
        if (MessageBox.Show($"Restaurar o backup de {b.DeviceName}?\n\nAs configurações do programa, perfis de controle, contador, ventoinhas e predefinições do estúdio voltam como estavam no backup. Senhas e ajustes do Windows deste PC não mudam.\n\nO MB reinicia em seguida.",
                "Backup das configurações", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await CloudBackupService.RestoreAsync(b.Id);
            Say("Backup restaurado. Reiniciando o MB...", "OkBrush");
            string? exe = Environment.ProcessPath;
            if (exe is not null)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
            }
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is LicenseNetworkException or System.IO.InvalidDataException or System.Text.Json.JsonException or System.IO.IOException or UnauthorizedAccessException)
        {
            Say("Não deu para restaurar: " + ex.Message, "WarnBrush");
        }
    }

    private void Say(string text, string brush)
    {
        _status.Text = text;
        _status.Foreground = InviteApi.Res(brush);
    }
}
