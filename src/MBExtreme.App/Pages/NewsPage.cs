using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core;
using MBExtreme.Core.Update;

namespace MBExtreme.UI.Pages;

/// <summary>O que há de novo: as versões publicadas (texto de cada Release do GitHub), da mais nova para a mais velha.</summary>
public sealed class NewsPage : ToolPageBase
{
    public NewsPage() : base("O que há de novo", $"As novidades de cada versão do MB Extreme. Você está na compilação {AppUpdates.CurrentBuild()}.")
    {
        AddButton("Verificar se há atualização", (_, _) => MainWindow.Current?.CheckForUpdates());
        Loaded += async (_, _) => { if (Body.Children.Count == 0) await LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        SetStatus("Buscando as versões...");
        var releases = new List<(int Build, string Title, DateTimeOffset At, string Notes)>();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{AppInfo.UpdateRepository}/releases?per_page=20");
            request.Headers.UserAgent.ParseAdd("MBExtreme");
            using var response = await AppServices.Web.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                foreach (var r in doc.RootElement.EnumerateArray())
                {
                    string tag = r.GetProperty("tag_name").GetString() ?? "";
                    string body = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                    DateTimeOffset.TryParse(r.TryGetProperty("published_at", out var p) ? p.GetString() : null, out var at);
                    releases.Add((GitHubReleases.BuildOf(tag), r.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag, at, GitHubReleases.CleanNotes(body)));
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { }

        Body.Children.Clear();
        if (releases.Count == 0)
        {
            SetStatus("Não deu para buscar a lista agora (sem internet ou as versões ainda não estão públicas). Veja as novidades desta versão:", "MutedBrush");
            foreach (var item in WhatsNew.Items) Body.Children.Add(Muted("•  " + item, 12.5));
            return;
        }
        SetStatus($"{releases.Count} versão(ões).", "OkBrush");
        foreach (var r in releases.OrderByDescending(r => r.Build))
        {
            bool mine = r.Build == AppUpdates.CurrentBuild();
            string head = $"Compilação {r.Build}" + (mine ? "  ·  a sua" : r.Build > AppUpdates.CurrentBuild() ? "  ·  nova" : "") + (r.At > DateTimeOffset.MinValue ? $"  ·  {r.At.LocalDateTime:dd/MM/yyyy}" : "");
            Body.Children.Add(Row(head, r.Notes.Length > 0 ? r.Notes : r.Title, null, mine ? "OkBrush" : "MutedBrush"));
        }
        Motion.Cascade(Body, 25);
    }
}
