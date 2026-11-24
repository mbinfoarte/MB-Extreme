using System.Security.Cryptography;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Tweaks;
using Xunit;

namespace MBExtreme.Core.Tests;

public class FimDoTesteTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static LicenseStatus Status(string? plan, int daysLeft = 3)
    {
        if (plan is null) return new LicenseStatus(LicenseState.None, null, "sem licença", null);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pub = LicenseToken.ExportPublicKey(key);
        var payload = new LicensePayload
        {
            Subject = "u1", Email = "a@b.com", Plan = plan, Device = "dev1", IssuedAt = Now.AddDays(-20),
            ExpiresAt = Now.AddDays(daysLeft), Status = "trialing", MaxDevices = 1,
        };
        return LicenseVerifier.Evaluate(LicenseToken.Sign(payload, key), pub, "dev1", Now);
    }

    [Fact]
    public void ProValido_NaoDesfaz() =>
        Assert.False(LicenseExpiry.ShouldRevert(enforce: true, configured: true, Status("pro")));

    [Fact]
    public void SemLicenca_ComAplicacaoLigada_Desfaz() =>
        Assert.True(LicenseExpiry.ShouldRevert(true, true, Status(null)));

    [Fact]
    public void PlanoGratuito_Desfaz() =>
        Assert.True(LicenseExpiry.ShouldRevert(true, true, Status("free")));

    [Fact]
    public void TesteVencido_Desfaz() =>
        Assert.True(LicenseExpiry.ShouldRevert(true, true, Status("pro", daysLeft: -10)));

    [Fact]
    public void BetaAberto_OuServidorNaoConfigurado_NuncaDesfaz()
    {
        Assert.False(LicenseExpiry.ShouldRevert(enforce: false, configured: true, Status(null)));
        Assert.False(LicenseExpiry.ShouldRevert(enforce: true, configured: false, Status(null)));
    }

    [Fact]
    public void AjustesProSaoDesempenhoJogosERede()
    {
        Assert.Contains(TweakCategory.Performance, LicenseExpiry.ProTweakCategories);
        Assert.Contains(TweakCategory.Gaming, LicenseExpiry.ProTweakCategories);
        Assert.Contains(TweakCategory.Network, LicenseExpiry.ProTweakCategories);
        Assert.DoesNotContain(TweakCategory.Privacy, LicenseExpiry.ProTweakCategories);
        Assert.DoesNotContain(TweakCategory.Storage, LicenseExpiry.ProTweakCategories);
    }

    [Fact]
    public void BibliotecaDeJogosEPerfisSaoPro()
    {
        Assert.True(FeaturePolicy.RequiresPro("gamelibrary"));
        Assert.True(FeaturePolicy.RequiresPro("profiles"));
        Assert.False(FeaturePolicy.RequiresPro("cleanup"));
    }
}
