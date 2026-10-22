namespace NexoVideo.Core;

public enum AutomaticVerdict { NotComparable, Clear, Alert }

public sealed record DiagnosticMessage(double ElapsedMilliseconds, string Text, double? LastDecodedTimestamp);
public sealed record ProcessExecution(string Executable, IReadOnlyList<string> Arguments,
    DateTimeOffset StartedUtc, double ElapsedMilliseconds, int ExitCode, bool TimedOut, bool Cancelled,
    bool OutputTruncated, bool LogTruncated, int DiagnosticLines, long? PeakMemoryBytes, string LogPath,
    IReadOnlyList<DiagnosticMessage> Diagnostics, string LogSha256 = "", string Stage = "", int? SelectedStream = null);

public sealed record AnalysisEvidence(string AnalysisId, string Sha256, long Bytes, long ModifiedUtcTicks,
    DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, string AppVersion, string OperatingSystem,
    string FfprobeSha256, AnalysisOptions Options, string Termination,
    IReadOnlyList<ProcessExecution> Executions, string EngineSha256 = "", string Runtime = "", string Architecture = "");

public sealed class AnalysisCancelledException(AnalysisResult result, CancellationToken token)
    : OperationCanceledException("Análise cancelada; evidências preservadas.", token)
{ public AnalysisResult Result { get; } = result; }

public sealed class AnalysisFailedException(AnalysisResult result, Exception cause)
    : IOException(cause.Message, cause)
{ public AnalysisResult Result { get; } = result; }
