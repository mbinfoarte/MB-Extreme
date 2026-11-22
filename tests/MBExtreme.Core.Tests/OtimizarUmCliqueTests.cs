using MBExtreme.Core.Startup;
using MBExtreme.Core.Tools;
using Xunit;

namespace MBExtreme.Core.Tests;

public class OtimizarUmCliqueTests
{
    [Fact]
    public void Inicializacao_desliga_so_o_que_e_seguro()
    {
        Assert.True(OptimizeAdvisor.StartupOneClick(StartupAdvisor.Describe("Spotify", "spotify.exe").Advice));
        Assert.True(OptimizeAdvisor.StartupOneClick(StartupAdvisor.Describe("OneDrive", "onedrive.exe").Advice));
        Assert.False(OptimizeAdvisor.StartupOneClick(StartupAdvisor.Describe("SecurityHealth", "SecurityHealthSystray.exe").Advice));
        Assert.False(OptimizeAdvisor.StartupOneClick(StartupAdvisor.Describe("Desconhecido", "c:\\x\\y.exe").Advice));
        Assert.Equal("Alto", OptimizeAdvisor.StartupImpact("Steam", "steam.exe -silent"));
        Assert.Equal("Médio", OptimizeAdvisor.StartupImpact("Java", "jusched updater"));
        Assert.Equal("Baixo", OptimizeAdvisor.StartupImpact("Algo", "algo.exe"));
    }

    [Fact]
    public void Processos_fecha_ajudantes_e_protege_o_sistema()
    {
        Assert.True(OptimizeAdvisor.ProcessOneClick("GoogleUpdate"));
        Assert.True(OptimizeAdvisor.ProcessOneClick("PhoneExperienceHost"));
        Assert.False(OptimizeAdvisor.ProcessOneClick("explorer"));
        Assert.False(OptimizeAdvisor.ProcessOneClick("Discord"));
        Assert.True(OptimizeAdvisor.ProcessProtected("lsass"));
        Assert.True(OptimizeAdvisor.ProcessProtected("EasyAntiCheat"));
        Assert.Equal("Aplicativo", OptimizeAdvisor.ProcessKind("chrome", hasWindow: true));
        Assert.Equal("Windows", OptimizeAdvisor.ProcessKind("svchost", hasWindow: false));
        Assert.Equal("Segundo plano", OptimizeAdvisor.ProcessKind("GoogleUpdate", hasWindow: false));
    }

    [Fact]
    public void Servicos_so_vao_para_manual_os_seguros_em_automatico()
    {
        Assert.Equal(OptimizeAdvice.Safe, OptimizeAdvisor.ServiceAdvice("DiagTrack", "Experiências do Usuário Conectado e Telemetria"));
        Assert.Equal(OptimizeAdvice.Safe, OptimizeAdvisor.ServiceAdvice("gupdate", "Google Update Service"));
        Assert.Equal(OptimizeAdvice.Keep, OptimizeAdvisor.ServiceAdvice("NVDisplay.ContainerLocalSystem", "NVIDIA Display Container LS"));
        Assert.Equal(OptimizeAdvice.Keep, OptimizeAdvisor.ServiceAdvice("Steam Client Service", "Steam Client Service"));
        Assert.Equal(OptimizeAdvice.Safe, OptimizeAdvisor.ServiceAdvice("FooUpdater", "Foo Updater"));
        Assert.Equal(OptimizeAdvice.Unknown, OptimizeAdvisor.ServiceAdvice("Xyz", "Serviço Xyz"));
        Assert.True(OptimizeAdvisor.ServiceOneClick(OptimizeAdvice.Safe, automatic: true));
        Assert.False(OptimizeAdvisor.ServiceOneClick(OptimizeAdvice.Safe, automatic: false));
        Assert.False(OptimizeAdvisor.ServiceOneClick(OptimizeAdvice.IfUnused, automatic: true));
        Assert.Equal(OptimizeAdvisor.Services.Count, OptimizeAdvisor.Services.Select(s => s.Name.ToLowerInvariant()).Distinct().Count());
    }
}
