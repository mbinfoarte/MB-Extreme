using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Repair;

/// <summary>Leitura da saída dos utilitários de reparo: codificação, linhas e porcentagem.</summary>
public static class RepairOutput
{
    private static readonly Regex Percent = new(@"(?<!\d)(?<n>\d{1,3})(?:[.,]\d+)?\s*%", RegexOptions.Compiled);

    /// <summary>
    /// Porcentagem escrita na linha ("45% concluída", "[=====  10,0% ]"), ou null.
    /// </summary>
    public static double? TryPercent(string line)
    {
        var match = Percent.Match(line);
        if (!match.Success) return null;
        string whole = match.Value.Replace("%", "").Trim().Replace(',', '.');
        return double.TryParse(whole, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value is >= 0 and <= 100
            ? value
            : null;
    }

    /// <summary>Remove caracteres de controle e barras de progresso desenhadas só com "=" e espaços.</summary>
    public static string Clean(string line)
    {
        var builder = new StringBuilder(line.Length);
        foreach (char c in line)
            if (c != '\0' && (c >= ' ' || c == '\t')) builder.Append(c);
        return builder.ToString().Trim();
    }

    /// <summary>Remove acentos e passa para minúsculas, para comparar textos em português e inglês.</summary>
    public static string Fold(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }
}

/// <summary>
/// Converte bytes em texto. O sfc escreve em UTF-16 quando a saída é redirecionada e o DISM e o chkdsk usam a página
/// de código do console; a codificação é descoberta no primeiro bloco.
/// </summary>
public sealed class OutputDecoder
{
    private Decoder? _decoder;
    private readonly Encoding _fallback;

    public OutputDecoder(Encoding? fallback = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _fallback = fallback ?? DefaultOem();
    }

    private static Encoding DefaultOem()
    {
        try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Encoding.UTF8; }
    }

    public static bool LooksUtf16(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return true;
        int pairs = Math.Min(bytes.Length / 2, 64);
        if (pairs < 2) return false;
        int zeros = 0;
        for (int i = 0; i < pairs; i++)
            if (bytes[i * 2 + 1] == 0 && bytes[i * 2] != 0) zeros++;
        return zeros * 2 >= pairs;
    }

    public string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return "";
        _decoder ??= (LooksUtf16(bytes) ? Encoding.Unicode : _fallback).GetDecoder();
        var chars = new char[_decoder.GetCharCount(bytes, false)];
        int count = _decoder.GetChars(bytes, chars, false);
        return new string(chars, 0, count).Replace("﻿", "");
    }
}

/// <summary>Junta pedaços de texto e entrega linhas inteiras (separadas por \r ou \n).</summary>
public sealed class LineSplitter
{
    private readonly StringBuilder _pending = new();

    public IEnumerable<string> Feed(string chunk)
    {
        var lines = new List<string>();
        foreach (char c in chunk)
        {
            if (c is '\r' or '\n')
            {
                if (_pending.Length > 0) lines.Add(_pending.ToString());
                _pending.Clear();
            }
            else _pending.Append(c);
        }
        return lines;
    }

    public string? Flush()
    {
        if (_pending.Length == 0) return null;
        string rest = _pending.ToString();
        _pending.Clear();
        return rest;
    }
}
