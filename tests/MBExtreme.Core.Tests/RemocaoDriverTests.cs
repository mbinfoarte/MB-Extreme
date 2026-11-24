using MBExtreme.Core.Drivers;
using Xunit;

namespace MBExtreme.Core.Tests;

public class RemocaoDriverTests
{
    private const string Json = """
        [{"Driver":"oem12.inf","ClassName":"Display","ProviderName":"NVIDIA","Version":"32.0.15.6094","Date":"2024-10-02"},
         {"Driver":"oem7.inf","ClassName":"Display","ProviderName":"Advanced Micro Devices, Inc.","Version":"31.0.21.1","Date":"2024-01-05"},
         {"Driver":"oem3.inf","ClassName":"Net","ProviderName":"Intel","Version":"1.0","Date":""},
         {"Driver":"..\\evil.inf","ClassName":"Display","ProviderName":"NVIDIA","Version":"1","Date":""},
         {"Driver":"oem99.inf","ClassName":"Display","ProviderName":"Intel Corporation","Version":"31.0.101.5","Date":""}]
        """;

    [Fact]
    public void LeSoOsDriversDeVideoComNomeValido()
    {
        var packages = DriverRemoval.ParsePackages(Json);
        Assert.Equal(new[] { "oem12.inf", "oem7.inf", "oem99.inf" }, packages.Select(p => p.PublishedName).ToArray());
        Assert.Equal(new[] { GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel }, packages.Select(p => p.Vendor).ToArray());
    }

    [Fact]
    public void AceitaUmUnicoObjetoELixo()
    {
        Assert.Single(DriverRemoval.ParsePackages("""{"Driver":"oem1.inf","ClassName":"Display","ProviderName":"NVIDIA","Version":"1","Date":""}"""));
        Assert.Empty(DriverRemoval.ParsePackages(""));
        Assert.Empty(DriverRemoval.ParsePackages("não é json"));
    }

    [Theory]
    [InlineData("oem1.inf", true)]
    [InlineData("OEM123.INF", true)]
    [InlineData("oem.inf", false)]
    [InlineData("oem1.inf & calc", false)]
    [InlineData("..\\oem1.inf", false)]
    [InlineData("oemX1.inf", false)]
    [InlineData(null, false)]
    public void NomeDoPacoteNuncaVirouComando(string? name, bool valid) => Assert.Equal(valid, DriverRemoval.IsValidPublishedName(name));

    [Theory]
    [InlineData("NVIDIA Corporation", GpuVendor.Nvidia)]
    [InlineData("Advanced Micro Devices, Inc.", GpuVendor.Amd)]
    [InlineData("ATI Technologies Inc.", GpuVendor.Amd)]
    [InlineData("Intel Corporation", GpuVendor.Intel)]
    [InlineData("Microsoft", GpuVendor.Other)]
    [InlineData(null, GpuVendor.Other)]
    public void FabricantePeloNomeDoProvedor(string? provider, GpuVendor expected) => Assert.Equal(expected, DriverRemoval.VendorOfProvider(provider));

    [Fact]
    public void PastasSobrandoSaoSoDoFabricanteESeguras()
    {
        foreach (var vendor in new[] { GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel })
        {
            var folders = DriverRemoval.LeftoverFolders(vendor, "C:", @"C:\ProgramData", @"C:\Users\u\AppData\Local", @"C:\Program Files");
            Assert.NotEmpty(folders);
            Assert.All(folders, f => Assert.True(DriverRemoval.IsSafeToDelete(f.Replace('\\', Path.DirectorySeparatorChar)), f));
        }
        Assert.Empty(DriverRemoval.LeftoverFolders(GpuVendor.Other, "C:", "p", "l", "f"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Program Files")]
    [InlineData("/home/user/Documents")]
    [InlineData("/tmp/qualquercoisa")]
    public void NuncaApagaPastasQueNaoSaoDoFabricante(string folder) => Assert.False(DriverRemoval.IsSafeToDelete(folder));
}
