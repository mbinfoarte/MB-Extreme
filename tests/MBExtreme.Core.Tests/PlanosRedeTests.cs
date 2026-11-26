using System.Security.Cryptography;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.NetDiag;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Planos: Teste de internet e Visão geral no Grátis, Central de Rede parcial nos pagos e completa no Técnico.</summary>
public class PlanosRedeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static LicenseStatus Status(string? plan)
    {
        if (plan is null) return new LicenseStatus(LicenseState.None, null, "sem licença", null);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = new LicensePayload
        {
            Subject = "u1", Email = "a@b.com", Plan = plan, Device = "dev1", IssuedAt = Now.AddDays(-1), ExpiresAt = Now.AddDays(30), Status = "active", MaxDevices = 2,
        };
        return LicenseVerifier.Evaluate(LicenseToken.Sign(payload, key), LicenseToken.ExportPublicKey(key), "dev1", Now);
    }

    [Theory]
    [InlineData("netcenter")]
    [InlineData("speedtest")]
    public void Teste_de_internet_e_Central_de_Rede_abrem_no_gratis(string page)
    {
        Assert.Null(FeaturePolicy.RequiredPlan(page));
        Assert.True(FeaturePolicy.IsAllowed(page, Status(null), enforce: true));
    }

    [Fact]
    public void Analisador_de_rede_fica_no_tecnico() => Assert.Equal(FeaturePolicy.Tecnico, FeaturePolicy.RequiredPlan("netanalyzer"));

    [Theory]
    [InlineData(null, "overview", true)]
    [InlineData(null, "dns", false)]
    [InlineData(null, "wifi", false)]
    [InlineData(null, "repair", false)]
    [InlineData("essencial", "dns", true)]
    [InlineData("essencial", "wifi", true)]
    [InlineData("essencial", "repair", true)]
    [InlineData("essencial", "route", false)]
    [InlineData("gamer", "lan", false)]
    [InlineData("creator", "reports", false)]
    [InlineData("tecnico", "route", true)]
    [InlineData("tecnico", "lan", true)]
    [InlineData("tecnico", "reports", true)]
    public void Secoes_por_plano(string? plan, string section, bool allowed) =>
        Assert.Equal(allowed, NetCenterAccess.Allows(plan, NetCenterAccess.SectionPlan(section)));

    [Theory]
    [InlineData("essencial", RepairKind.FlushDns, true)]
    [InlineData("essencial", RepairKind.RenewDhcp, true)]
    [InlineData("creator", RepairKind.ResetWinsock, false)]
    [InlineData("creator", RepairKind.ResetTcpIp, false)]
    [InlineData("tecnico", RepairKind.ResetWinsock, true)]
    [InlineData("tecnico", RepairKind.ResetTcpIp, true)]
    [InlineData(null, RepairKind.FlushDns, false)]
    public void Reparacao_por_plano(string? plan, RepairKind kind, bool allowed) =>
        Assert.Equal(allowed, NetCenterAccess.Allows(plan, NetCenterAccess.RepairPlan(kind)));

    [Fact]
    public void Wifi_completo_so_no_tecnico()
    {
        Assert.False(NetCenterAccess.Allows(FeaturePolicy.Creator, NetCenterAccess.WifiFullPlan));
        Assert.True(NetCenterAccess.Allows(FeaturePolicy.Tecnico, NetCenterAccess.WifiFullPlan));
    }

    [Fact]
    public void Plano_efetivo()
    {
        Assert.Equal(FeaturePolicy.Tecnico, FeaturePolicy.EffectivePlan(Status(null), enforce: false));   // beta aberto
        Assert.Equal(FeaturePolicy.Tecnico, FeaturePolicy.EffectivePlan(Status(null), enforce: true, trialActive: true));
        Assert.Null(FeaturePolicy.EffectivePlan(Status(null), enforce: true));
        Assert.Equal(FeaturePolicy.Gamer, FeaturePolicy.EffectivePlan(Status("gamer"), enforce: true));
    }
}
