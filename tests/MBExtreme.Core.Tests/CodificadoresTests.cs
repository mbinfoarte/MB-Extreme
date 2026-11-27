using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Ajustes dos codificadores na live e na gravação, 10 bits e o alívio quando a placa não dá conta.</summary>
public class CodificadoresTests
{
    private static readonly EncoderOption NvH264 = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly EncoderOption NvAv1 = new(VideoCodec.Av1, EncoderKind.Nvenc, "av1_nvenc");
    private static readonly EncoderOption AmdH264 = new(VideoCodec.H264, EncoderKind.Amf, "h264_amf");
    private static readonly EncoderOption AmdHevc = new(VideoCodec.Hevc, EncoderKind.Amf, "hevc_amf");
    private static readonly EncoderOption IntelHevc = new(VideoCodec.Hevc, EncoderKind.Qsv, "hevc_qsv");

    [Fact]
    public void Live_na_NVIDIA_tem_passada_dupla_quadros_B_e_sem_quadro_chave_por_cena()
    {
        var a = StudioCommand.LiveTuning(NvAv1);
        Assert.Contains("qres", a);
        Assert.Contains("-bf", a);
        Assert.Contains("-no-scenecut", a);
        Assert.Contains("cbr", a);
    }

    [Fact]
    public void Live_na_AMD_usa_baixa_latencia_VBAQ_e_pre_analise()
    {
        var a = StudioCommand.LiveTuning(AmdH264);
        Assert.Contains("lowlatency", a);
        Assert.Contains("-vbaq", a);
        Assert.Contains("-preanalysis", a);
    }

    [Fact]
    public void Gravacao_com_qualidade_constante_na_AMD_define_os_quadros_B_e_nao_usa_VBAQ()
    {
        Assert.Contains("-qp_b", StudioCommand.ConstantQualityArgs(AmdHevc, 2));
        Assert.Empty(StudioCommand.RecordTuning(AmdHevc, constantQuality: true));
        Assert.Contains("-vbaq", StudioCommand.RecordTuning(AmdHevc, constantQuality: false));
        Assert.Contains("-spatial-aq", StudioCommand.RecordTuning(NvH264, constantQuality: true));
    }

    [Fact]
    public void Dez_bits_so_no_HEVC_e_AV1()
    {
        Assert.Contains("main10", StudioCommand.TenBitArgs(IntelHevc));
        Assert.Contains("p010le", StudioCommand.TenBitArgs(NvAv1));
        Assert.Empty(StudioCommand.TenBitArgs(NvH264));
    }

    [Fact]
    public void Quadro_chave_a_cada_2_segundos_na_live() =>
        Assert.Equal(new[] { "-force_key_frames", "expr:gte(t,n_forced*2)" }, StudioCommand.LiveKeyframes);

    [Fact]
    public void Sobrecarga_so_depois_de_10_segundos_lentos_e_com_intervalo()
    {
        var load = new EncoderLoad();
        var t = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(load.Feed(0.8, t));
        Assert.False(load.Feed(0.8, t.AddSeconds(5)));
        Assert.True(load.Feed(0.8, t.AddSeconds(10)));
        Assert.False(load.Feed(0.8, t.AddSeconds(30)));   // dentro dos 2 minutos
        Assert.False(load.Feed(1.0, t.AddSeconds(40)));   // voltou ao normal: zera
        Assert.False(load.Feed(0.0, t.AddSeconds(41)));   // sem medida
        Assert.True(load.Feed(0.85, t.AddMinutes(3)) || load.Feed(0.85, t.AddMinutes(3).AddSeconds(10)));
    }

    [Fact]
    public void Esforco_desce_um_degrau_por_vez()
    {
        Assert.Equal(EncoderQuality.Balanced, EncoderLoad.StepDown(EncoderQuality.Quality));
        Assert.Equal(EncoderQuality.Fast, EncoderLoad.StepDown(EncoderQuality.Balanced));
        Assert.Null(EncoderLoad.StepDown(EncoderQuality.Fast));
    }
}
