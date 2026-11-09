namespace MBExtreme.Core.Repair;

/// <summary>Como interpretar o resultado de um comando.</summary>
public enum StepKind
{
    Generic,
    Sfc,
    Dism,
    DismCheck,
    Chkdsk,
    ChkdskSchedule,
}

/// <summary>Um comando executado durante um reparo.</summary>
public sealed record RepairStep(
    string Title,
    string File,
    string Arguments,
    StepKind Kind = StepKind.Generic,
    bool ContinueOnError = false,
    string? Input = null,
    bool AlwaysRun = false)
{
    /// <summary>O comando como seria digitado no terminal.</summary>
    public string CommandLine => Arguments.Length == 0 ? File : File + " " + Arguments;
}

/// <summary>Uma tarefa de reparo: um ou mais comandos com explicação.</summary>
public sealed record RepairTask(
    string Id,
    string Group,
    string Title,
    string Description,
    string Duration,
    IReadOnlyList<RepairStep> Steps,
    bool NeedsRestart = false,
    bool AdvisesRestorePoint = false,
    bool NeedsInternet = false,
    string? Caution = null)
{
    public string ManualCommands => string.Join(Environment.NewLine, Steps.Select(s => s.CommandLine));
}

/// <summary>Atualização enviada à interface durante a execução.</summary>
public sealed record RepairUpdate(string Message, double OverallPercent, bool IsLog);

public sealed record StepResult(string Title, int ExitCode, bool Ok, string Verdict);

public sealed record RepairResult(
    bool Success,
    bool Cancelled,
    bool NeedsRestart,
    string Summary,
    IReadOnlyList<StepResult> Steps);
