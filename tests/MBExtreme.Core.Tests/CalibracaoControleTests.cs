using System.Text;
using MBExtreme.Core.Gamepads;
using Xunit;

namespace MBExtreme.Core.Tests;

public class CalibracaoControleTests
{
    /// <summary>Controle de mentira: guarda os comandos e responde como um original.</summary>
    private sealed class FakePad : IHidChannel
    {
        public readonly List<(byte Id, byte[] Payload)> Sent = new();
        public Func<byte, byte[]?, byte[]>? Answer;
        public int InputLength => 64;

        public byte[] GetFeature(byte reportId)
        {
            var last = Sent.Count > 0 ? Sent[^1].Payload : null;
            var r = Answer?.Invoke(reportId, last) ?? new byte[64];
            var full = new byte[64];
            r.CopyTo(full, 0);
            full[0] = reportId;
            return full;
        }

        public void SetFeature(byte reportId, ReadOnlySpan<byte> payload) => Sent.Add((reportId, payload.ToArray()));
        public byte[]? ReadInput(int timeoutMs) => null;
        public void Dispose() { }
    }

    private static byte[] Bytes(params int[] values) => values.Select(v => (byte)v).ToArray();

    /// <summary>Resposta 0x83 do DualSense ao último comando de calibração (passo, 1, tipo).</summary>
    private static byte[] Ds5Calib(byte[]? last)
    {
        int step = last![0], kind = last[2];
        int tail = step == 2 ? 2 : 1;
        return Bytes(0x83, 1, kind, tail);
    }

    [Fact]
    public void Ids_DosControles()
    {
        Assert.Equal(PadModel.DualSense, PadIds.Identify(0x054C, 0x0CE6));
        Assert.Equal(PadModel.DualSenseEdge, PadIds.Identify(0x054C, 0x0DF2));
        Assert.Equal(PadModel.DualShock4, PadIds.Identify(0x054C, 0x05C4));
        Assert.Equal(PadModel.DualShock4, PadIds.Identify(0x054C, 0x09CC));
        Assert.Null(PadIds.Identify(0x045E, 0x0B13)); // Xbox
    }

    [Fact]
    public void DualSense_CalibraCentroEAlcance()
    {
        var hid = new FakePad { Answer = (id, last) => id == 0x83 ? Ds5Calib(last) : new byte[64] };
        var pad = new SonyPad(hid, PadModel.DualSense) { Sleep = _ => { } };
        var progress = new List<double>();
        pad.CalibrateCenter(progress.Add);
        Assert.Equal(new byte[] { 1, 1, 1 }, hid.Sent[0].Payload);
        Assert.Equal(0x82, hid.Sent[0].Id);
        Assert.Equal(5, hid.Sent.Count(s => s.Payload.SequenceEqual(new byte[] { 3, 1, 1 })));
        Assert.Equal(new byte[] { 2, 1, 1 }, hid.Sent[^1].Payload);
        Assert.Equal(1, progress[^1]);

        hid.Sent.Clear();
        pad.RangeBegin();
        pad.RangeEnd();
        Assert.Equal(new[] { new byte[] { 1, 1, 2 }, new byte[] { 2, 1, 2 } }, hid.Sent.Select(s => s.Payload));
    }

    [Fact]
    public void DualSense_RespostaErradaViraErro()
    {
        var hid = new FakePad { Answer = (id, _) => Bytes(0x83, 9, 9, 9) };
        var pad = new SonyPad(hid, PadModel.DualSense) { Sleep = _ => { } };
        var ex = Assert.Throws<PadCommandException>(pad.CenterBegin);
        Assert.Contains("0x83090909", ex.Message);
    }

    [Fact]
    public void DualSense_GravaNaMemoriaPermanente()
    {
        var hid = new FakePad();
        var pad = new SonyPad(hid, PadModel.DualSense);
        pad.SaveToController();
        Assert.Equal(new[] { new byte[] { 3, 2, 101, 50, 64, 12 }, new byte[] { 3, 1 } }, hid.Sent.Select(s => s.Payload));
        Assert.All(hid.Sent, s => Assert.Equal(0x80, s.Id));
        Assert.Throws<PadCommandException>(() => new SonyPad(new FakePad(), PadModel.DualSenseEdge).SaveToController());
    }

    [Fact]
    public void DualSense_EstadoDaMemoria()
    {
        NvStatus With(uint v) => new SonyPad(new FakePad { Answer = (_, _) => Bytes(0x81, (int)(v >> 24), (int)(v >> 16) & 0xFF, (int)(v >> 8) & 0xFF, (int)v & 0xFF) }, PadModel.DualSense).QueryNv();
        Assert.Equal(NvStatus.Temporary, With(0x03030201));
        Assert.Equal(NvStatus.Permanent, With(0x03030200));
        Assert.Equal(NvStatus.PendingReboot, With(0x15010100));
        Assert.Equal(NvStatus.Unknown, With(0x12345678));
    }

    [Fact]
    public void DualSense_InformacoesECopiaDaCalibracao()
    {
        var hid = new FakePad
        {
            Answer = (id, last) =>
            {
                if (id == 0x20)
                {
                    var r = new byte[64];
                    Encoding.ASCII.GetBytes("Jul  4 2025").CopyTo(r, 1);
                    Encoding.ASCII.GetBytes("10:10:32").CopyTo(r, 12);
                    BitConverter.GetBytes(0x00000800u).CopyTo(r, 24); // placa BDM-050
                    BitConverter.GetBytes(0x01000630u).CopyTo(r, 28);
                    return r;
                }
                if (id == 0x81 && last is [1, 19, ..])
                {
                    var r = Bytes(0x81, 1, 19, 2);
                    return r.Concat(Encoding.ASCII.GetBytes("V558Z3TGL10256116")).ToArray();
                }
                if (id == 0x81 && last is [12, 2])
                {
                    var r = new List<byte> { 0x81, 12, 2, 2 };
                    for (int i = 0; i < 12; i++) r.AddRange(BitConverter.GetBytes((ushort)(1000 + i)));
                    return r.ToArray();
                }
                if (id == 0x81 && last is [9, 2]) return Bytes(0x81, 9, 2, 0, 0xD5, 0x25, 0xEB, 0x9C, 0xFA, 0xA0);
                if (id == 0x81 && last is [3, 3]) return Bytes(0x81, 3, 3, 2, 1);
                return new byte[64];
            },
        };
        var pad = new SonyPad(hid, PadModel.DualSense) { Sleep = _ => { } };
        var info = pad.ReadInfo();
        Assert.Equal("V558Z3TGL10256116", info.Serial);
        Assert.Equal("BDM-050", info.Board);
        Assert.Equal("Astro Bot", info.Color);
        Assert.Equal("A0:FA:9C:EB:25:D5", info.BluetoothAddress);
        Assert.False(info.LooksClone);
        Assert.Equal(NvStatus.Temporary, info.Memory);
        Assert.Equal("Jul  4 2025 10:10:32", info.BuildDate);

        var tune = pad.ReadFinetune();
        Assert.Equal(1000, tune![0]);
        Assert.Equal(1011, tune[11]);
        pad.WriteFinetune(tune);
        var write = hid.Sent[^1].Payload;
        Assert.Equal(new byte[] { 12, 1, 0xE8, 0x03 }, write[..4]);
    }

    [Fact]
    public void DualShock4_ComandosComDuasRespostas()
    {
        var hid = new FakePad
        {
            Answer = (id, last) =>
            {
                int step = last![0], kind = last[2];
                return id == 0x91
                    ? Bytes(0x91, 1, kind, step == 2 ? 2 : 1)
                    : Bytes(0x92, 1, kind, step == 2 ? 1 : 0xFF);
            },
        };
        var pad = new SonyPad(hid, PadModel.DualShock4) { Sleep = _ => { } };
        pad.CalibrateCenter();
        pad.RangeBegin();
        pad.RangeEnd();
        Assert.All(hid.Sent, s => Assert.Equal(0x90, s.Id));
        Assert.Equal(new byte[] { 2, 1, 2 }, hid.Sent[^1].Payload);

        var save = new FakePad();
        new SonyPad(save, PadModel.DualShock4).SaveToController();
        Assert.Equal(new[] { new byte[] { 10, 2, 0x3E, 0x71, 0x7F, 0x89 }, new byte[] { 10, 1, 0 } }, save.Sent.Select(s => s.Payload));
        Assert.All(save.Sent, s => Assert.Equal(0xA0, s.Id));
    }

    [Fact]
    public void Placas_ECores()
    {
        Assert.Equal("BDM-010", SonyPad.BoardDs5(0x0300));
        Assert.Equal("BDM-060M", SonyPad.BoardDs5(0x1100));
        Assert.Equal("JDM-055", SonyPad.BoardDs4(0xB400));
        Assert.Equal("JDM-040", SonyPad.BoardDs4(0x6400));
        Assert.Equal("Azul (Starlight Blue)", SonyPad.ColorDs5("A12305xxx"));
    }

    [Fact]
    public void Leitura_DualSenseUsb()
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[1] = 255; r[2] = 128; r[3] = 0; r[4] = 127; // esquerdo todo à direita, direito todo à esquerda
        r[5] = 255; r[6] = 0;                          // L2 apertado
        r[8] = 0x20 | 0x02;                            // X + direcional para a direita
        r[9] = 0x01 | 0x40;                            // L1 + L3
        r[10] = 0x01;                                  // PS
        BitConverter.GetBytes((short)8192).CopyTo(r, 16 + 6); // 1 g no acelerômetro X
        r[53] = 0x25;                                  // carregando, ~55%
        var s = PadInput.Parse(PadModel.DualSense, r)!;
        Assert.Equal(1.0, s.LX, 2);
        Assert.Equal(-1.0, s.RX, 2);
        Assert.Equal(1.0, s.L2, 3);
        Assert.Equal(PadButtons.Cross | PadButtons.Right | PadButtons.L1 | PadButtons.L3 | PadButtons.PS, s.Buttons);
        Assert.Equal(1.0, s.AccelX, 3);
        Assert.Equal(100, s.Battery); // status 2 = carregado
        Assert.True(s.Cable);
        Assert.Null(PadInput.Parse(PadModel.DualSense, new byte[10]));
    }

    [Fact]
    public void Leitura_DualShock4Usb()
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[1] = 128; r[2] = 128; r[3] = 128; r[4] = 128;
        r[5] = 0x08 | 0x80; // sem direcional + triângulo
        r[6] = 0x02;        // R1
        r[9] = 128;         // R2 pela metade
        r[30] = 0x10 | 11;  // no cabo, carregado
        var s = PadInput.Parse(PadModel.DualShock4, r)!;
        Assert.Equal(PadButtons.Triangle | PadButtons.R1, s.Buttons);
        Assert.Equal(0.5, s.R2, 2);
        Assert.Equal(100, s.Battery);
        Assert.False(s.Charging);
        Assert.InRange(s.LX, -0.01, 0.01);
    }

    [Fact]
    public void Circularidade_EDrift()
    {
        var perfect = new CircularityMap();
        for (int i = 0; i < 480; i++) { double a = i * Math.PI * 2 / 480; perfect.Add(Math.Cos(a), Math.Sin(a)); }
        Assert.True(perfect.Error < 0.5);
        Assert.Equal(1.0, perfect.Coverage);

        var square = new CircularityMap();
        for (int i = 0; i < 480; i++)
        {
            double a = i * Math.PI * 2 / 480;
            double k = 1 / Math.Max(Math.Abs(Math.Cos(a)), Math.Abs(Math.Sin(a)));
            square.Add(Math.Cos(a) * k, Math.Sin(a) * k); // analógico "quadrado": passa do círculo nas diagonais
        }
        Assert.InRange(square.Error, 15, 30);

        var drift = new DriftMeter();
        for (int i = 0; i < 100; i++) drift.Add(0.06, -0.02);
        Assert.Equal(0.063, drift.Offset, 3);
        Assert.Equal("Drift leve", DriftMeter.Verdict(drift.Peak));
        Assert.Equal("Sem drift", DriftMeter.Verdict(0.0078));
        Assert.Equal("Drift forte", DriftMeter.Verdict(0.3));
        Assert.False(DriftMeter.VisibleInGames(0.06));
    }

    [Fact]
    public void Historico_BackupELaudo()
    {
        string folder = Path.Combine(Path.GetTempPath(), "mbx-pad-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new PadRepairLog(folder);
            var values = Enumerable.Range(0, 12).Select(i => (ushort)(i * 100)).ToArray();
            Assert.True(log.SaveBackup("V558", values));
            Assert.False(log.SaveBackup("V558", new ushort[12])); // a primeira cópia fica
            Assert.Equal(values, log.LoadBackup("V558"));
            Assert.Null(log.LoadBackup("outro"));

            var repair = new PadRepair(new DateTime(2026, 11, 8, 10, 0, 0), "DualSense", "V558", "BDM-050", "Astro Bot", "João <Silva>", "(74) 9999-0000",
                new StickMeasure(0.12, 0.1, 9.5), new StickMeasure(0.01, 0, 6), new StickMeasure(0.008, 0, 4.1), new StickMeasure(0.008, 0, 3.9), true);
            log.Add(repair);
            Assert.Single(log.For("V558"));
            string html = PadRepairLog.ReportHtml(repair);
            Assert.Contains("&lt;Silva&gt;", html); // texto do cliente sai escapado
            Assert.Contains("12.0%", html.Replace(',', '.'));
            Assert.Contains("Drift", html);
            Assert.Contains("permanente", html);
            string file = PadRepairLog.WriteReport(repair, folder);
            Assert.True(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
