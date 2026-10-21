namespace NexoVideo.Core;

public enum AutomaticVerdict { NotComparable, Clear, Alert }
public enum HumanVerdict { NotEvaluated, NoProblem, Problem, Inconclusive }
public enum Satisfaction { NotEvaluated, Satisfactory, Partial, Unsatisfactory }

public sealed record DiagnosticMessage(double ElapsedMilliseconds, string Text, double? LastDecodedTimestamp);
public sealed record ProcessExecution(string Executable, IReadOnlyList<string> Arguments,
    DateTimeOffset StartedUtc, double ElapsedMilliseconds, int ExitCode, bool TimedOut, bool Cancelled,
    bool OutputTruncated, bool LogTruncated, int DiagnosticLines, long? PeakMemoryBytes, string LogPath,
    IReadOnlyList<DiagnosticMessage> Diagnostics, string LogSha256 = "", string Stage = "", int? SelectedStream = null);

public sealed record AnalysisEvidence(string AnalysisId, string Sha256, long Bytes, long ModifiedUtcTicks,
    DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, string AppVersion, string OperatingSystem,
    string FfprobeSha256, AnalysisOptions Options, string Termination,
    IReadOnlyList<ProcessExecution> Executions, string EngineSha256 = "", string Runtime = "", string Architecture = "");

// A avaliação pertence a uma execução e aos bytes verificados; nome/caminho não bastam.
public sealed record ManualReview(string AnalysisId, string Sha256, HumanVerdict Verdict,
    Satisfaction Satisfaction, bool WatchedWholeFile, string Reviewer, string Notes, DateTimeOffset ReviewedUtc, string Player = "", bool CouldNotOpen = false);

public sealed record ComparisonCase(string Format, AnalysisMode Mode, AutomaticVerdict Automatic,
    ManualReview? Manual, string? AnalysisId, string? Sha256);

public sealed record ComparisonMetrics(long Total, long ManuallyEvaluated, long Comparable,
    long TruePositive, long TrueNegative, long FalsePositive, long FalseNegative,
    long AutomaticInconclusive, long MissingOrIneligibleReview, double? Precision, double? Recall,
    double? Accuracy, double? Specificity, double? F1, double? ComparisonCoverage);

public sealed class AnalysisCancelledException(AnalysisResult result, CancellationToken token)
    : OperationCanceledException("Análise cancelada; evidências preservadas.", token)
{ public AnalysisResult Result { get; } = result; }

public sealed class AnalysisFailedException(AnalysisResult result, Exception cause)
    : IOException(cause.Message, cause)
{ public AnalysisResult Result { get; } = result; }
