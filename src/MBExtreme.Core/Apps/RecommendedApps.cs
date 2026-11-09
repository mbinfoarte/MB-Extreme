namespace MBExtreme.Core.Apps;

public sealed record RecommendedApp(string Id, string Title, string Description, string Why, string Url);

/// <summary>Programas gratuitos e confiáveis que combinam com o MB Extreme, instalados pelo winget (instalador oficial da Microsoft). Sem anúncios nem programas de terceiros embutidos.</summary>
public static class RecommendedApps
{
    public static IReadOnlyList<RecommendedApp> All { get; } = new RecommendedApp[]
    {
        new("Gyan.FFmpeg", "FFmpeg", "Motor de captura e codificação por placa de vídeo.", "Necessário para gravar, transmitir e usar o replay em Gravação e transmissão.", "https://ffmpeg.org"),
        new("namazso.PawnIO", "PawnIO", "Driver que libera a leitura de temperatura e tensões da CPU.", "Ativa os sensores da CPU no painel e no monitor em tempo real.", "https://github.com/namazso/PawnIO.Setup"),
        new("REALiX.HWiNFO", "HWiNFO", "Sensores e informações detalhadas de hardware.", "Conferir temperaturas, tensões e clocks com uma segunda ferramenta.", "https://www.hwinfo.com"),
        new("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", "Saúde e SMART de HDs e SSDs.", "Dados completos de desgaste e setores ruins.", "https://crystalmark.info"),
        new("CPUID.CPU-Z", "CPU-Z", "Informações detalhadas da CPU, memória e placa-mãe.", "Confirmar velocidade e timings da memória.", "https://www.cpuid.com"),
        new("TechPowerUp.GPU-Z", "GPU-Z", "Informações e sensores da placa de vídeo.", "Confirmar o PCIe e o Resizable BAR.", "https://www.techpowerup.com/gpuz"),
        new("Guru3D.Afterburner", "MSI Afterburner", "Curva de clock e ventoinhas da placa de vídeo, com overlay.", "Ajustar a placa de vídeo com cuidado e medir.", "https://www.msi.com/Landing/afterburner"),
        new("Wagnardsoft.DisplayDriverUninstaller", "Display Driver Uninstaller", "Remove por completo o driver de vídeo antigo.", "Instalação limpa do driver quando há travamentos ou tela preta.", "https://www.wagnardsoft.com"),
        new("OBSProject.OBSStudio", "OBS Studio", "Gravação e transmissão com cenas e webcam.", "Quando você quiser cenas, webcam e sobreposições.", "https://obsproject.com"),
        new("AntibodySoftware.WizTree", "WizTree", "Mostra o que ocupa espaço no disco em segundos.", "Achar o que encheu o SSD.", "https://diskanalyzer.com"),
        new("voidtools.Everything", "Everything", "Busca instantânea de arquivos.", "Encontrar qualquer arquivo na hora.", "https://www.voidtools.com"),
        new("7zip.7zip", "7-Zip", "Compactador de arquivos livre.", "Abrir qualquer arquivo compactado.", "https://www.7-zip.org"),
    };
}
