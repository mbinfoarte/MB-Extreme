using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Programas recomendados: lista com o motivo de cada um e instalação com um clique pelo winget.</summary>
public sealed class RecommendedPage : ToolPageBase
{
    private readonly Dictionary<string, Button> _buttons = new();
    private readonly Dictionary<string, TextBlock> _states = new();
    private bool _busy;

    public RecommendedPage() : base("Programas recomendados",
        "Ferramentas gratuitas que combinam com o MB Extreme. Cada uma diz para que serve; a instalação usa o winget, o instalador oficial da Microsoft, e não traz anúncios nem programas extras.")
    {
        AddButton("Verificar o que já está instalado", async (_, _) => await CheckAsync(), false);
        AddButton("Programas e atualizações", (_, _) => MainWindow.Current?.Go("software"), false);

        foreach (var app in RecommendedApps.All)
        {
            var current = app;
            var state = new TextBlock { FontSize = 12, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) };
            _states[app.Id] = state;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(SmallButton("Detalhes", (_, _) => Open(current.Url)));
            var install = SmallButton("Instalar agora", async (_, _) => await InstallAsync(current), true);
            _buttons[app.Id] = install;
            buttons.Children.Add(install);

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = app.Title, FontSize = 15, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = app.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = "Para que serve aqui: " + app.Why, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedBrush") });
            text.Children.Add(state);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(text);
            Grid.SetColumn(buttons, 1);
            buttons.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(buttons);
            var card = CardOf(grid);
            card.Margin = new Thickness(0, 0, 0, 10);
            Body.Children.Add(card);
        }

        Loaded += async (_, _) => await CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (_busy) return;
        _busy = true;
        SetStatus("Verificando os programas instalados...");
        try
        {
            if (!await Task.Run(WingetService.IsAvailable))
            {
                SetStatus("O winget não foi encontrado neste Windows. Atualize o \"Instalador de Aplicativo\" na Microsoft Store.", "WarnBrush");
                return;
            }

            foreach (var app in RecommendedApps.All)
            {
                string id = app.Id;
                bool installed = await Task.Run(() => WingetService.IsInstalled(id));
                _states[id].Text = installed ? "✔ Já instalado" : "";
                _states[id].Foreground = Brush("OkBrush");
                _buttons[id].Content = installed ? "Reinstalar" : "Instalar agora";
            }

            SetStatus("");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task InstallAsync(RecommendedApp app)
    {
        if (_busy) return;
        _busy = true;
        _buttons[app.Id].IsEnabled = false;
        SetStatus($"Instalando {app.Title} (pode demorar e abrir janelas do instalador)...");
        try
        {
            var (ok, message) = await Task.Run(() => WingetService.Install(app.Id));
            SetStatus(ok ? $"{app.Title} instalado." : $"Não foi possível instalar {app.Title}: {message}", ok ? "OkBrush" : "BadBrush");
            if (ok)
            {
                _states[app.Id].Text = "✔ Já instalado";
                _states[app.Id].Foreground = Brush("OkBrush");
            }
        }
        finally
        {
            _buttons[app.Id].IsEnabled = true;
            _busy = false;
        }
    }
}
