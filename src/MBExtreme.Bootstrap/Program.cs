using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MBExtreme.Bootstrap
{
    internal static class Program
    {
        public const string Repository = "mbinfoarte/MB-Extreme-Download";

        /// <summary>Reserva: enquanto as versões ainda saem no repositório principal.</summary>
        public const string FallbackRepository = "mbinfoarte/MB-Extreme";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases";

        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BootstrapForm());
        }
    }

    /// <summary>Janela do instalador online: procura, baixa, confere e abre o instalador completo.</summary>
    internal sealed class BootstrapForm : Form
    {
        private static readonly Color Back = Color.FromArgb(14, 14, 16);
        private static readonly Color Surface = Color.FromArgb(32, 32, 36);
        private static readonly Color Accent = Color.FromArgb(255, 196, 0);
        private static readonly Color Muted = Color.FromArgb(160, 160, 168);

        private readonly Label _status = new Label();
        private readonly Label _detail = new Label();
        private readonly Panel _bar = new Panel();
        private readonly Button _action = new Button();
        private readonly Button _site = new Button();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private double _progress;
        private bool _working;

        public BootstrapForm()
        {
            Text = "Instalar o MB Extreme";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 250);
            BackColor = Back;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }

            var logo = new PictureBox { Location = new Point(24, 22), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Back };
            using (var stream = typeof(BootstrapForm).Assembly.GetManifestResourceStream("logo.png"))
                if (stream != null) logo.Image = Image.FromStream(new MemoryStream(ReadAll(stream)));
            var title = new Label { Text = "MB Extreme", Location = new Point(84, 22), AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), ForeColor = Color.White };
            var subtitle = new Label { Text = "Instalador online · baixa sempre a versão mais nova", Location = new Point(86, 54), AutoSize = true, ForeColor = Muted };

            _status.SetBounds(24, 100, 432, 22);
            _status.Font = new Font("Segoe UI Semibold", 10.5f);
            _bar.SetBounds(24, 128, 432, 10);
            _bar.BackColor = Surface;
            _bar.Paint += PaintBar;
            _detail.SetBounds(24, 146, 432, 40);
            _detail.ForeColor = Muted;

            StyleButton(_action, primary: true);
            _action.SetBounds(336, 198, 120, 32);
            _action.Click += (s, e) => { if (_working) _cts.Cancel(); else Start(); };
            StyleButton(_site, primary: false);
            _site.SetBounds(188, 198, 140, 32);
            _site.Text = "Baixar pelo site";
            _site.Visible = false;
            _site.Click += (s, e) => OpenUrl(Program.ReleasesPage);

            Controls.AddRange(new Control[] { logo, title, subtitle, _status, _bar, _detail, _site, _action });
            Shown += (s, e) => Start();
            FormClosing += (s, e) => _cts.Cancel();
        }

        private static byte[] ReadAll(Stream s)
        {
            using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
        }

        private static void StyleButton(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 76);
            b.BackColor = primary ? Accent : Back;
            b.ForeColor = primary ? Color.Black : Color.White;
            b.Font = new Font("Segoe UI Semibold", 9.5f);
            b.Cursor = Cursors.Hand;
        }

        private void PaintBar(object sender, PaintEventArgs e)
        {
            int w = (int)Math.Round(_bar.Width * Math.Max(0, Math.Min(1, _progress)));
            if (w <= 0) return;
            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, _bar.Width, _bar.Height), Color.FromArgb(255, 150, 0), Accent, LinearGradientMode.Horizontal))
                e.Graphics.FillRectangle(brush, 0, 0, w, _bar.Height);
        }

        private void SetStatus(string status, string detail, double progress)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(status, detail, progress))); return; }
            _status.Text = status;
            _detail.Text = detail;
            _progress = progress;
            _bar.Invalidate();
        }

        private async void Start()
        {
            _working = true;
            _site.Visible = false;
            _action.Text = "Cancelar";
            try
            {
                string file = await Task.Run(() => DownloadAsync(_cts.Token));
                SetStatus("Abrindo o instalador...", "Confirme a permissão de administrador para instalar.", 1);
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                Close();
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) Fail("Download cancelado.", "Nada foi instalado.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Fail("A instalação não foi autorizada.", "Clique em Tentar de novo e aceite o pedido de administrador do Windows.");
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is InvalidDataException || ex is WebException || ex is UnauthorizedAccessException)
            {
                Fail("Não foi possível baixar o MB Extreme.", ex.Message);
            }
            catch (Exception ex)
            {
                Fail("Algo deu errado.", ex.Message);
            }
        }

        private void Fail(string status, string detail)
        {
            _working = false;
            SetStatus(status, detail, 0);
            _action.Text = "Tentar de novo";
            _site.Visible = true;
        }

        private async Task<string> DownloadAsync(CancellationToken token)
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MBExtreme-Instalar/1.0");
                SetStatus("Procurando a versão mais nova...", "Conectando ao servidor de downloads.", 0);
                // os dois repositórios (downloads e o principal): vale a maior compilação
                var all = new List<object>();
                foreach (string repository in new[] { Program.Repository, Program.FallbackRepository })
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + repository + "/releases?per_page=100");
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    var response = await http.SendAsync(request, token);
                    if (!response.IsSuccessStatusCode) continue;
                    var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(await response.Content.ReadAsStringAsync());
                    if (json is IEnumerable items) foreach (var item in items) all.Add(item);
                }
                if (all.Count == 0)
                    throw new InvalidDataException("O download ainda não está público. Baixe o instalador completo pelo site.");
                var choice = ReleasePicker.Pick(all);
                if (choice == null) throw new InvalidDataException("Nenhuma versão disponível para baixar agora.");

                string sha = choice.Sha256;
                if (!ReleasePicker.IsSha256(sha))
                    sha = ReleasePicker.ParseShaFile(await http.GetStringAsync(choice.Sha256Url))
                          ?? throw new InvalidDataException("Não foi possível conferir o arquivo (impressão digital ausente).");

                string folder = Path.Combine(Path.GetTempPath(), "MBExtreme-Instalar");
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, ReleasePicker.SetupAsset);
                string partial = target + ".baixando";

                using (var download = await http.GetAsync(choice.Url, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    download.EnsureSuccessStatusCode();
                    long total = download.Content.Headers.ContentLength ?? choice.Size;
                    using (var input = await download.Content.ReadAsStreamAsync())
                    using (var output = File.Create(partial))
                    using (var hash = SHA256.Create())
                    {
                        var buffer = new byte[81920];
                        long done = 0;
                        var clock = Stopwatch.StartNew();
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, token);
                            hash.TransformBlock(buffer, 0, read, null, 0);
                            done += read;
                            double speed = done / Math.Max(clock.Elapsed.TotalSeconds, 0.2);
                            string eta = total > 0 && speed > 0 ? $" · faltam {Math.Max(1, (int)((total - done) / speed))} s" : "";
                            SetStatus($"Baixando o MB Extreme {choice.Version}...",
                                $"{done / 1048576.0:0.0} de {(total > 0 ? (total / 1048576.0).ToString("0.0") : "?")} MB · {speed / 1048576.0:0.0} MB/s{eta}",
                                total > 0 ? 0.95 * done / total : 0.5);
                        }
                        hash.TransformFinalBlock(new byte[0], 0, 0);
                        SetStatus("Conferindo o arquivo...", "Comparando a impressão digital (SHA-256) com a publicada.", 0.97);
                        string got = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                        if (got != sha)
                        {
                            output.Close();
                            File.Delete(partial);
                            throw new InvalidDataException("O arquivo baixado não confere com o original e foi apagado. Tente de novo.");
                        }
                    }
                }
                if (File.Exists(target)) File.Delete(target);
                File.Move(partial, target);
                return target;
            }
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
