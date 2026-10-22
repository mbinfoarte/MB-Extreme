using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace MBExtreme.UI;

/// <summary>Envia arquivos e pastas para a Lixeira (dá para recuperar). Com todas as janelas ligadas, se o item não couber na Lixeira o Windows pergunta antes de apagar de vez.</summary>
internal static class RecycleBin
{
    public static void Delete(string path)
    {
        if (MBExtreme.Core.Tools.PathGuard.IsProtected(path)) throw new InvalidOperationException("Este item está em uma pasta protegida do Windows.");
        if (Directory.Exists(path)) FileSystem.DeleteDirectory(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
        else FileSystem.DeleteFile(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
    }
}
