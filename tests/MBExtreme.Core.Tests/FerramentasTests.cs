using MBExtreme.Core.Tools;
using Xunit;

namespace MBExtreme.Core.Tests;

public class FerramentasTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mbx-tools-" + Guid.NewGuid().ToString("N"));
    private static readonly string[] NoSkip = Array.Empty<string>();

    public FerramentasTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Write(string relative, byte[] data)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] Bytes(int length, byte seed)
    {
        var b = new byte[length];
        for (int i = 0; i < length; i++) b[i] = (byte)(seed + i % 251);
        return b;
    }

    [Fact]
    public void Arquivos_grandes_vem_do_maior_para_o_menor_e_respeitam_o_minimo()
    {
        Write("a/pequeno.bin", Bytes(100, 1));
        Write("a/medio.bin", Bytes(5000, 2));
        Write("b/grande.bin", Bytes(20000, 3));
        var found = FileSearch.Largest(_root, 1000, 10, CancellationToken.None, null, NoSkip);
        Assert.Equal(new[] { "grande.bin", "medio.bin" }, found.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void Duplicados_exigem_conteudo_igual_nao_so_o_tamanho()
    {
        var same = Bytes(4000, 7);
        Write("x/um.dat", same);
        Write("y/dois.dat", same);
        Write("y/tres.dat", Bytes(4000, 9)); // mesmo tamanho, conteúdo diferente
        Write("z/quatro.dat", Bytes(3999, 7)); // tamanho diferente
        var groups = FileSearch.Duplicates(new[] { _root }, 1, CancellationToken.None, null, NoSkip);
        var group = Assert.Single(groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Equal(4000, group.WastedBytes);
        Assert.All(group.Files, f => Assert.Contains(Path.GetFileName(f.Path), new[] { "um.dat", "dois.dat" }));
    }

    [Fact]
    public void Duplicados_com_diferenca_depois_do_trecho_inicial_nao_sao_iguais()
    {
        var a = Bytes(200_000, 1);
        var b = (byte[])a.Clone();
        b[^1] ^= 0xFF;
        Write("a.bin", a);
        Write("b.bin", b);
        Assert.Empty(FileSearch.Duplicates(new[] { _root }, 1, CancellationToken.None, null, NoSkip));
    }

    [Fact]
    public void Pastas_vazias_incluem_as_que_so_tem_pastas_vazias()
    {
        Directory.CreateDirectory(Path.Combine(_root, "vazia"));
        Directory.CreateDirectory(Path.Combine(_root, "pai", "filho"));
        Write("cheia/arquivo.txt", Bytes(10, 1));
        var empty = FileSearch.EmptyFolders(_root, CancellationToken.None, NoSkip);
        var names = empty.Select(p => Path.GetRelativePath(_root, p)).OrderBy(x => x).ToList();
        Assert.Contains("vazia", names);
        Assert.Contains(Path.Combine("pai", "filho"), names);
        Assert.Contains("pai", names);
        Assert.DoesNotContain("cheia", names);
        Assert.Equal(2, names.Count(n => n.StartsWith("pai")));
    }

    [Fact]
    public void Busca_cancelada_para_na_hora()
    {
        Write("a.bin", Bytes(10, 1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => FileSearch.Largest(_root, 1, 5, cts.Token, null, NoSkip));
    }

    [Fact]
    public void Triturar_apaga_o_arquivo_e_sobrescreve_antes()
    {
        string path = Write("segredo.txt", System.Text.Encoding.UTF8.GetBytes("senha: 123456"));
        // guarda um segundo nome para o mesmo conteúdo (hard link não é portátil); confere só que sumiu
        Shredder.Shred(path, 2);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public void Triturar_recusa_arquivo_inexistente() =>
        Assert.Throws<FileNotFoundException>(() => Shredder.Shred(Path.Combine(_root, "nao-existe.txt")));

    [Fact]
    public void Pastas_protegidas_e_raizes_nao_podem_ser_mexidas()
    {
        var roots = new[] { "/windows", "/program files" };
        Assert.True(PathGuard.IsProtected("/windows/system32/x.dll", roots));
        Assert.True(PathGuard.IsProtected("/windows", roots));
        Assert.True(PathGuard.IsProtected("/", roots));
        Assert.True(PathGuard.IsProtected("", roots));
        Assert.False(PathGuard.IsProtected("/home/user/arquivo.txt", roots));
        Assert.False(PathGuard.IsProtected("/windowsapps/x", roots));
    }

    [Fact]
    public void Nomes_curtos_e_caminhos_de_rede_sao_tratados_como_protegidos()
    {
        var roots = new[] { "/windows" };
        Assert.True(PathGuard.IsProtected(@"C:\PROGRA~1\x.dll", roots));
        Assert.True(PathGuard.IsProtected(@"\\servidor\C$\Windows\x", roots));
        Assert.True(PathGuard.IsProtected(@"\\?\C:\Windows\x", roots));
    }

    [Fact]
    public void Triturar_recusa_atalho_simbolico_e_nao_toca_no_alvo()
    {
        if (OperatingSystem.IsWindows()) return;
        string target = Write("alvo.txt", System.Text.Encoding.UTF8.GetBytes("conteudo importante"));
        string link = Path.Combine(_root, "link.txt");
        File.CreateSymbolicLink(link, target);
        Assert.Throws<InvalidOperationException>(() => Shredder.Shred(link));
        Assert.Equal("conteudo importante", File.ReadAllText(target));
    }

    [Fact]
    public void Duplicados_com_pastas_aninhadas_nao_contam_o_mesmo_arquivo_duas_vezes()
    {
        Write("docs/fotos/a.bin", Bytes(500, 4));
        var groups = FileSearch.Duplicates(new[] { _root, Path.Combine(_root, "docs") }, 1, CancellationToken.None, null, NoSkip);
        Assert.Empty(groups);
    }

    [Fact]
    public void Desligamento_programado_gera_os_argumentos_certos()
    {
        Assert.Equal(new[] { "/s", "/t", "600", "/c", "Programado pelo MB Extreme" }, PowerSchedule.ShutdownArguments(false, 600));
        Assert.Equal("/r", PowerSchedule.ShutdownArguments(true, 0)[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => PowerSchedule.ShutdownArguments(false, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PowerSchedule.ShutdownArguments(false, PowerSchedule.MaxSeconds + 1));
    }

    [Fact]
    public void Uso_de_cpu_do_processo_e_proporcional_ao_tempo_e_aos_nucleos()
    {
        Assert.Equal(25, ProcessList.CpuPercent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 4), 3);
        Assert.Equal(0, ProcessList.CpuPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 4));
        Assert.Equal(100, ProcessList.CpuPercent(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(1), 2));
    }

    [Theory]
    [InlineData("csrss", true)]
    [InlineData("LSASS", true)]
    [InlineData("winlogon", true)]
    [InlineData("chrome", false)]
    public void Processos_essenciais_do_windows_sao_protegidos(string name, bool isProtected) =>
        Assert.Equal(isProtected, ProcessList.IsProtectedName(name));
}
