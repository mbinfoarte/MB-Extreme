using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Network;

namespace MBExtreme.UI.Pages;

/// <summary>Diagnóstico do Wi-Fi: sinal, banda, canal e velocidade, com dicas para jogar online.</summary>
public sealed class WifiPage : ToolPageBase
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _busy;

    public WifiPage() : base("Diagnóstico de Wi-Fi",
        "Mostra a qualidade do seu Wi-Fi agora e o que fazer para melhorar a conexão nos jogos. A melhor conexão para jogos online continua sendo o cabo.")
    {
        AddButton("Atualizar", async (_, _) => await RefreshAsync(), false);
        AddButton("Analisador de rede", (_, _) => MainWindow.Current?.Go("netanalyzer"), false);
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
            var status = await Task.Run(WifiService.Read);
            Body.Children.Clear();
            if (status is null)
            {
                SetStatus("Este computador não tem Wi-Fi ou o serviço de Wi-Fi está desligado.");
                return;
            }

            SetStatus("");
            var card = new StackPanel();
            card.Children.Add(new TextBlock
            {
                Text = status.Connected ? $"Conectado a {status.Ssid}" : "Desconectado",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
            });
            if (status.Connected)
            {
                card.Children.Add(Muted($"Sinal {status.SignalPercent}% · banda {status.Band} · canal {status.Channel} · {status.RadioType}"));
                card.Children.Add(Muted($"Velocidade do enlace: recepção {status.ReceiveMbps:0} Mbps · envio {status.TransmitMbps:0} Mbps"));
            }

            Body.Children.Add(CardOf(card));
            Body.Children.Add(Heading("Dicas"));
            foreach (string tip in WifiService.Advice(status))
                Body.Children.Add(CardOf(new TextBlock { Text = "•  " + tip, TextWrapping = TextWrapping.Wrap }));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível ler o Wi-Fi: " + ex.Message, "BadBrush");
        }
        finally
        {
            _busy = false;
        }
    }
}
