namespace MBExtreme.Core.Repair;

/// <summary>Traduz o código de saída e o texto dos utilitários de reparo em uma frase clara.</summary>
public static class RepairInterpreter
{
    public const string CbsLog = @"%windir%\Logs\CBS\CBS.log";
    public const string DismLog = @"%windir%\Logs\DISM\dism.log";

    public static (bool Ok, string Message) Interpret(StepKind kind, int exitCode, string output)
    {
        string text = RepairOutput.Fold(output);
        return kind switch
        {
            StepKind.Sfc => Sfc(exitCode, text),
            StepKind.Dism => Dism(exitCode),
            StepKind.DismCheck => DismCheck(exitCode, text),
            StepKind.Chkdsk => Chkdsk(exitCode),
            StepKind.ChkdskSchedule => ChkdskSchedule(text),
            _ => exitCode == 0 ? (true, "Concluído.") : (false, $"Terminou com o código {exitCode}."),
        };
    }

    private static (bool, string) Sfc(int exitCode, string text)
    {
        if (text.Contains("nao pode corrigir") || text.Contains("nao conseguiu corrigir") || text.Contains("unable to fix"))
            return (false, $"O SFC encontrou arquivos corrompidos que não conseguiu corrigir. Rode \"Reparar a imagem do Windows (DISM)\" e depois o SFC de novo. Detalhes: {CbsLog}");
        if (text.Contains("nenhuma violacao de integridade") || text.Contains("did not find any integrity violations"))
            return (true, "Nenhum problema encontrado nos arquivos do sistema.");
        if (text.Contains("com exito") || text.Contains("com sucesso") || text.Contains("successfully repaired"))
            return (true, "Havia arquivos corrompidos e foram reparados. Reinicie o PC e, se quiser, rode o SFC mais uma vez.");
        if (text.Contains("violacoes de integridade") || text.Contains("found integrity violations"))
            return (false, $"Foram encontradas violações de integridade. Rode \"Verificar e reparar arquivos do sistema (SFC)\" depois do reparo da imagem (DISM). Detalhes: {CbsLog}");
        if (text.Contains("reinicializacao") || text.Contains("reiniciar") || text.Contains("pending") || text.Contains("reboot"))
            return (false, "Há um reparo pendente que exige reiniciar o PC. Reinicie e rode o SFC de novo.");
        return exitCode == 0
            ? (true, "Concluído.")
            : (false, $"O SFC terminou com o código {exitCode}. Detalhes: {CbsLog}");
    }

    private static (bool, string) DismCheck(int exitCode, string text)
    {
        if (exitCode != 0) return Dism(exitCode);
        if (text.Contains("nao e reparavel") || text.Contains("not repairable"))
            return (false, "A imagem do Windows está danificada e não pode ser reparada por aqui. Considere \"Reiniciar este PC\" mantendo seus arquivos.");
        if (text.Contains("reparavel") || text.Contains("repairable"))
            return (false, "Foi encontrada corrupção que pode ser reparada. Rode \"Reparar a imagem do Windows (DISM)\".");
        if (text.Contains("nenhuma corrupcao") || text.Contains("no component store corruption"))
            return (true, "A imagem do Windows está íntegra.");
        return (true, "Concluído.");
    }

    private static (bool, string) Dism(int exitCode)
    {
        if (exitCode == 0) return (true, "Concluído.");
        string message = unchecked((uint)exitCode) switch
        {
            740 => "O DISM precisa ser executado como administrador.",
            87 => "O DISM recusou os parâmetros do comando.",
            0x800F081F or 0x800F0906 or 0x800F0950 or 0x800F0907 =>
                "O DISM não achou os arquivos de origem. Conecte-se à internet (ele usa o Windows Update) e tente de novo, ou use uma imagem do Windows com /Source.",
            0x8007000D => "Os dados lidos estão inválidos: o repositório de componentes está muito danificado.",
            0x800F0922 => "A atualização não pôde ser concluída: falta espaço na partição reservada do sistema ou há uma atualização pendente. Reinicie e tente de novo.",
            0x800705B4 => "A operação demorou demais. Tente de novo com o PC ocioso.",
            _ => $"O DISM terminou com o código 0x{unchecked((uint)exitCode):X8}.",
        };
        return (false, $"{message} Detalhes: {DismLog}");
    }

    private static (bool, string) Chkdsk(int exitCode) => exitCode switch
    {
        0 => (true, "Nenhum erro encontrado no disco."),
        1 => (true, "Foram encontrados erros e corrigidos."),
        2 => (true, "Limpeza realizada. Nenhum erro crítico."),
        3 => (false, "Não foi possível terminar a verificação ou há erros que não foram corrigidos. Use \"Reparo profundo do disco\" para verificar no próximo reinício."),
        _ => (false, $"O CHKDSK terminou com o código {exitCode}."),
    };

    private static (bool, string) ChkdskSchedule(string text) =>
        text.Contains("sera verificado") || text.Contains("will be checked") || text.Contains("scheduled to be checked")
            ? (true, "Verificação agendada. Ela roda na próxima vez que o PC reiniciar; não desligue durante a verificação.")
            : (false, "Não foi possível confirmar o agendamento. Abra o terminal como administrador e rode o comando mostrado abaixo.");

    /// <summary>Texto final de uma tarefa a partir dos passos executados.</summary>
    public static string Summarize(RepairTask task, IReadOnlyList<StepResult> results, bool cancelled)
    {
        if (cancelled) return "Reparo interrompido.";
        var problems = results.Where(r => !r.Ok).ToList();
        string text = problems.Count == 0
            ? $"{task.Title}: concluído sem problemas."
            : $"{task.Title}: {problems.Count} {(problems.Count == 1 ? "passo precisa" : "passos precisam")} de atenção.";
        if (task.NeedsRestart) text += " Reinicie o PC para finalizar.";
        return text;
    }
}
