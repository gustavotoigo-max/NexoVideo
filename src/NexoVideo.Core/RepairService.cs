using System.Text.Json;

namespace NexoVideo.Core;

public sealed class RepairService(ToolPaths tools, WindowsProcessRunner runner, MediaAnalyzer analyzer)
{
    public async Task<RepairResult> GenerateAsync(string sourceRoot, string source, string outputRoot,
        AnalysisOptions options, string? reference, CancellationToken cancellation)
    {
        sourceRoot = Path.GetFullPath(sourceRoot); source = Path.GetFullPath(source); outputRoot = Path.GetFullPath(outputRoot);
        if (!Inventory.IsInside(source, sourceRoot)) throw new ArgumentException("O arquivo deve pertencer à origem da análise.");
        if (string.Equals(outputRoot.TrimEnd('\\'), sourceRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Escolha uma pasta separada para os candidatos.");
        CheckDirectory(outputRoot);
        var relative = Path.GetRelativePath(sourceRoot, source);
        var directory = Path.Combine(outputRoot, Path.GetDirectoryName(relative) ?? "");
        CheckDirectory(directory);
        Directory.CreateDirectory(directory);
        var work = Path.Combine(directory, ".nexovideo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string staged; ProcessResult process;
        if (reference is null)
        {
            staged = Path.Combine(work, "candidate.mkv");
            process = await runner.RunAsync(tools.Ffmpeg, ["-hide_banner", "-nostdin", "-v", "warning", "-n", "-protocol_whitelist", "file",
                "-i", source, "-map", "0:v?", "-map", "0:a?", "-c", "copy", staged], options.TimeoutSeconds, options.MemoryLimitMb, cancellation);
        }
        else
        {
            if (tools.Untrunc is null || !File.Exists(tools.Untrunc)) throw new FileNotFoundException("Configure o executável Untrunc para usar referência.");
            if (!new[] { ".mp4", ".mov", ".m4v", ".3gp" }.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase))
                throw new NotSupportedException("Untrunc experimental: apenas MP4/MOV/M4V/3GP candidatos nesta etapa.");
            var copy = Path.Combine(work, "damaged" + Path.GetExtension(source));
            var healthy = Path.Combine(work, "reference" + Path.GetExtension(reference));
            await CopyAsync(source, copy, cancellation); await CopyAsync(reference, healthy, cancellation);
            process = await runner.RunAsync(Path.GetFullPath(tools.Untrunc), ["-n", healthy, copy], options.TimeoutSeconds, options.MemoryLimitMb, cancellation);
            staged = Directory.EnumerateFiles(work, "*fixed*").FirstOrDefault() ?? "";
        }
        if (process.ExitCode != 0 || process.TimedOut || !File.Exists(staged) || new FileInfo(staged).Length == 0)
            throw new IOException($"A ferramenta não gerou candidato utilizável. Log: {process.LogPath}. Área de trabalho preservada: {work}");
        var diagnosis = await analyzer.AnalyzeAsync(staged, options with { Mode = AnalysisMode.Full, Video = true, Audio = true }, cancellation);
        var candidate = Path.Combine(directory, Path.GetFileNameWithoutExtension(source) + ".candidato-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(staged));
        File.Move(staged, candidate, overwrite: false);
        diagnosis = diagnosis with { Path = candidate };
        var result = new RepairResult(candidate, diagnosis, "Candidato experimental — revisão necessária",
            "Original preservado. Não há comparação automática suficiente para declarar reparo validado ou melhora. " +
            (reference is null ? "Remux A/V sem recodificação; outras trilhas/metadados podem não ser preservados." : "Referência usada pelo Untrunc; verificar continuidade temporal e sincronia A/V."));
        await using var report = new FileStream(candidate + ".nexovideo.json", FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(report, result, QueueStore.Json, cancellation);
        return result;
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellation)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        await input.CopyToAsync(output, cancellation);
    }
    private static void CheckDirectory(string directory)
    {
        for (DirectoryInfo? info = new(directory); info is not null; info = info.Parent)
            if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("O destino não pode atravessar links ou junctions nesta versão.");
    }
}
