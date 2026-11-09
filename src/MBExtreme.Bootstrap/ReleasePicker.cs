using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MBExtreme.Bootstrap
{
    /// <summary>O instalador completo escolhido numa Release: endereço, tamanho e SHA-256 (ou o endereço do .sha256).</summary>
    public sealed class SetupChoice
    {
        public int Build;
        public string Version = "";
        public string Url = "";
        public long Size;
        public string Sha256 = "";
        public string Sha256Url = "";
        public string PageUrl = "";
    }

    /// <summary>
    /// Escolhe a Release mais nova (maior número no fim da tag: beta-53 → 53) que tenha o MBExtreme-Beta-Setup.exe.
    /// Recebe o JSON do /releases já convertido em dicionários, para o mesmo código rodar no .NET Framework e nos testes.
    /// </summary>
    public static class ReleasePicker
    {
        public const string SetupAsset = "MBExtreme-Beta-Setup.exe";
        private static readonly Regex TrailingNumber = new Regex(@"(\d+)\s*$");
        private static readonly Regex Hex64 = new Regex(@"\b([0-9a-fA-F]{64})\b");

        public static int BuildOf(string tag)
        {
            var m = TrailingNumber.Match((tag ?? "").Trim());
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 0;
        }

        /// <summary>Só baixa por HTTPS de github.com (o GitHub redireciona para o próprio armazenamento dele).</summary>
        public static bool IsSafeUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

        public static string ParseShaFile(string text)
        {
            var m = Hex64.Match(text ?? "");
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }

        public static bool IsSha256(string text) => text != null && text.Length == 64 && text.All(Uri.IsHexDigit);

        public static SetupChoice Pick(IEnumerable releases)
        {
            SetupChoice best = null;
            foreach (var item in releases)
            {
                if (!(item is IDictionary<string, object> release)) continue;
                if (Bool(release, "draft")) continue;
                int build = BuildOf(Str(release, "tag_name"));
                if (build <= 0 || (best != null && build <= best.Build)) continue;
                if (!(Get(release, "assets") is IEnumerable assets)) continue;

                var choice = new SetupChoice { Build = build, Version = Str(release, "name"), PageUrl = Str(release, "html_url") };
                if (choice.Version.Length == 0) choice.Version = Str(release, "tag_name");
                foreach (var a in assets)
                {
                    if (!(a is IDictionary<string, object> asset)) continue;
                    string name = Str(asset, "name");
                    if (name.Equals(SetupAsset, StringComparison.OrdinalIgnoreCase))
                    {
                        choice.Url = Str(asset, "browser_download_url");
                        choice.Size = Get(asset, "size") is IConvertible c ? Convert.ToInt64(c) : 0;
                        string digest = Str(asset, "digest");
                        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) choice.Sha256 = digest.Substring(7).ToLowerInvariant();
                    }
                    else if (name.Equals(SetupAsset + ".sha256", StringComparison.OrdinalIgnoreCase))
                        choice.Sha256Url = Str(asset, "browser_download_url");
                }
                // sem instalador ou sem como conferir o arquivo: essa Release não serve
                if (!IsSafeUrl(choice.Url)) continue;
                if (!IsSha256(choice.Sha256) && !IsSafeUrl(choice.Sha256Url)) continue;
                best = choice;
            }
            return best;
        }

        private static object Get(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v : null;
        private static string Str(IDictionary<string, object> d, string key) => Get(d, key) as string ?? "";
        private static bool Bool(IDictionary<string, object> d, string key) => Get(d, key) is bool b && b;
    }
}
