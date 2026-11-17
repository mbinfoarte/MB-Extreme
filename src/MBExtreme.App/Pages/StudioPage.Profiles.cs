using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Perfis do Estúdio: salva tudo o que foi configurado (vídeo, live, gravação, câmeras, cenas, áudio, sobreposições) com um nome
/// e volta a ele num clique — por exemplo "Live de jogo", "Gravação de aula", "Live do celular". Chaves e senhas nunca entram no perfil.
/// </summary>
public sealed partial class StudioPage
{
    private readonly Button _profilesButton = new() { Content = "Perfis", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };

    private void SetupProfilesButton()
    {
        _profilesButton.Style = (Style)FindResource("GhostButton");
        _profilesButton.ToolTip = "Salvar e carregar perfis: tudo da live, da gravação, das câmeras e das cenas com um nome";
        _profilesButton.Click += (_, _) => ShowProfiles();
    }

    private void ShowProfiles()
    {
        var panel = new StackPanel();
        panel.Children.Add(Muted("Guarde a configuração atual com um nome e volte a ela quando quiser. O perfil leva vídeo, live, gravação, codificador, câmeras, " +
                                 "cenas, áudio, filtros, Pix, legendas e vertical. A chave de transmissão e os logins ficam fora (continuam só neste PC, protegidos).", 12));

        var saveRow = new DockPanel { Margin = new Thickness(0, 12, 0, 4) };
        var name = new TextBox { MaxLength = StudioProfiles.MaxNameLength, MinWidth = 260, Padding = new Thickness(6, 4, 6, 4), ToolTip = "Nome do perfil (ex.: Live de jogo)" };
        System.Windows.Automation.AutomationProperties.SetName(name, "Nome do perfil");
        var save = new Button { Content = "Salvar a configuração atual", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(save, Dock.Right);
        saveRow.Children.Add(save);
        saveRow.Children.Add(name);
        panel.Children.Add(saveRow);

        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(Heading("Perfis salvos"));
        panel.Children.Add(list);
        var note = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(note);

        void Refill()
        {
            list.Children.Clear();
            var profiles = StudioProfiles.List();
            if (profiles.Count == 0) list.Children.Add(Muted("Nenhum perfil salvo ainda.", 12));
            foreach (var profile in profiles)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var remove = SmallButton("Excluir", (_, _) =>
                {
                    if (MessageBox.Show($"Excluir o perfil \"{profile.Name}\"?", "Perfis do Estúdio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    StudioProfiles.Delete(profile.Name);
                    Refill();
                });
                remove.Margin = new Thickness(6, 0, 0, 0);
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var load = SmallButton("Carregar", (_, _) => LoadProfile(profile.Name, note));
                load.Margin = new Thickness(6, 0, 0, 0);
                DockPanel.SetDock(load, Dock.Right);
                row.Children.Add(load);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = profile.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
                text.Children.Add(new TextBlock { Text = "Salvo em " + profile.SavedAt.ToString("dd/MM/yyyy HH:mm"), FontSize = 11, Foreground = Brush("MutedBrush") });
                row.Children.Add(text);
                list.Children.Add(CardOf(row));
            }
        }

        save.Click += (_, _) =>
        {
            if (StudioProfiles.CleanName(name.Text) is not { } clean)
            {
                note.Text = "Dê um nome ao perfil (letras e números).";
                note.Foreground = Brush("WarnBrush");
                return;
            }
            if (StudioProfiles.List().Any(p => string.Equals(p.Name, clean, StringComparison.CurrentCultureIgnoreCase))
                && MessageBox.Show($"Já existe um perfil \"{clean}\". Substituir pelo que está configurado agora?", "Perfis do Estúdio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                // o que está só na tela (microfone e saída de som) entra nas configurações antes de copiar; a chave não é tocada
                _s.StudioOutputId = _outputs.Selected > 0 && _outputs.Selected - 1 < _outList.Count ? _outList[_outputs.Selected - 1].Id : null;
                _s.StudioMicId = _mics.Selected > 0 && _mics.Selected - 1 < _micList.Count ? _micList[_mics.Selected - 1].Id : null;
                _s.Save();
                StudioProfiles.Save(_s, clean);
                note.Text = $"Perfil \"{clean}\" salvo.";
                note.Foreground = Brush("OkBrush");
                name.Text = "";
                Refill();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                note.Text = "Não foi possível salvar o perfil: " + ex.Message;
                note.Foreground = Brush("BadBrush");
            }
        };

        Refill();
        ShowEditor("Perfis do Estúdio", panel, width: 640);
    }

    private void LoadProfile(string name, TextBlock note)
    {
        if (_studio.IsRunning || AppServices.Recorder.IsRecording)
        {
            note.Text = "Pare a gravação ou a live antes de trocar de perfil.";
            note.Foreground = Brush("WarnBrush");
            return;
        }
        // fecha a janela antes: ao fechar ela guarda o estado da tela, o que apagaria o perfil recém-carregado
        Window.GetWindow(note)?.Close();
        int applied = StudioProfiles.Load(_s, name);
        if (applied < 0)
        {
            SetStatus($"Não foi possível abrir o perfil \"{name}\" (arquivo apagado ou estragado).", "BadBrush");
            return;
        }
        _s.Save();
        CameraSource.Modes.Clear();   // a qualidade das câmeras pode ter mudado
        StopAllFeeds();
        StopScreenFeed();
        MainWindow.Current?.Reopen("record", $"Perfil \"{name}\" carregado.");
    }
}
