using System.Text.Json.Serialization;

namespace NexoVideo.Core;

public enum AnalysisMode { Quick, Sampled, Full }
public enum SamplingMode { Count, ApproximateMegabytes }
public enum QueueState { Pending, Running, Completed, Failed, Cancelled }

public sealed record AnalysisOptions
{
    public AnalysisMode Mode { get; init; } = AnalysisMode.Quick;
    public SamplingMode Sampling { get; init; } = SamplingMode.Count;
    public int Samples { get; init; } = 20;
    public double SampleSeconds { get; init; } = 3;
    public double Megabytes { get; init; } = 20;
    public bool Video { get; init; } = true;
    public bool Audio { get; init; } = true;
    public bool Recursive { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 300;
    public int DecoderThreads { get; init; } = 2;
    public int MemoryLimitMb { get; init; } = 2048;
    public string[] Extensions { get; init; } = [".mp4", ".mov", ".avi", ".mkv", ".ts", ".webm", ".mpg", ".flv", ".wmv"];
    public string? ExcludedDirectory { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Sampling)) throw new ArgumentException("Modo de análise/amostragem inválido.");
        if (!Video && !Audio) throw new ArgumentException("Selecione pelo menos vídeo ou áudio.");
        if (Samples is < 2 or > 200 || SampleSeconds is < 0.1 or > 3600 || !double.IsFinite(SampleSeconds)
            || Megabytes is < 1 or > 1_000_000 || !double.IsFinite(Megabytes))
            throw new ArgumentException("Amostragem inválida: 2–200 amostras, 0,1–3.600 s e intervalo a partir de 1 MB.");
        if (TimeoutSeconds is < 1 or > 86400 || DecoderThreads is < 1 or > 32 || MemoryLimitMb is < 128 or > 65536)
            throw new ArgumentException("Timeout, threads ou limite de memória inválido.");
        if (Extensions.Length == 0 || Extensions.Any(x => !x.StartsWith('.') || x.IndexOfAny(['/', '\\', ':']) >= 0))
            throw new ArgumentException("Selecione extensões válidas.");
    }
}

public sealed record ToolPaths(string Ffprobe, string Ffmpeg, string? Untrunc = null)
{
    public void Validate()
    {
        foreach (var path in new[] { Ffprobe, Ffmpeg })
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
                throw new FileNotFoundException("Configure os caminhos completos do FFprobe e FFmpeg.", path);
    }
}

public sealed record MediaStream(int Index, string Kind, string Codec, double? Duration,
    int? Width, int? Height, int? Channels, string? FrameRate, double? StartTime);

public sealed record DecodeTest(int StreamIndex, double? RequestedStart, double? RequestedSeconds,
    double? ActualStart, double? ActualEnd, long Frames, long AudioSamples,
    int ExitCode, int Warnings, bool TimedOut, string LogPath, string Status);

public sealed record AnalysisResult(string Path, AnalysisMode Mode, string Conclusion, string Container,
    double? Duration, IReadOnlyList<MediaStream> Streams, IReadOnlyList<DecodeTest> Tests,
    string Note, string ProbeLog, string ToolVersion, bool Inconclusive, AnalysisEvidence? Evidence = null);

public sealed record InventoryFile(string Path, long Size, long ModifiedTicks);
public sealed record QueueItem(long Id, long JobId, string Path, long Size, long ModifiedTicks,
    QueueState State, string Conclusion, string Note);
public sealed record Job(long Id, string Source, AnalysisOptions Options);
public sealed record QueueCounts(long Total, long Pending, long Running, long Completed, long Failed, long Cancelled);
public sealed record QueueProgress(long JobId, string Path, string Stage, QueueCounts Counts, double? Fraction = null);
public sealed record ProcessResult(int ExitCode, bool TimedOut, bool OutputTruncated, string Output, string LogPath,
    int DiagnosticLines, long PeakMemoryBytes);
public sealed record RepairResult(string Candidate, AnalysisResult Diagnosis, string Status, string Note);

public static class Formats
{
    public static readonly string[] All = [".mp4", ".mov", ".m4v", ".avi", ".mkv", ".wmv", ".mts", ".m2ts",
        ".mod", ".tod", ".mxf", ".dv", ".ts", ".flv", ".f4v", ".webm", ".ogv", ".mpg", ".mpeg", ".vob",
        ".3gp", ".3g2", ".asf", ".divx", ".r3d", ".braw", ".crm", ".cin", ".dav"];
    public static readonly HashSet<string> Synthetic = new([".mp4", ".mov", ".avi", ".mkv", ".ts", ".webm", ".mpg", ".flv", ".wmv"], StringComparer.OrdinalIgnoreCase);
}
