using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection;

namespace NexoVideo.Core;

public sealed class MediaAnalyzer(ToolPaths tools, WindowsProcessRunner runner)
{
    private string? _version, _versionToolHash;
    private static readonly Lazy<string> EngineHash = new(() =>
    {
        var location = typeof(MediaAnalyzer).Assembly.Location;
        if (string.IsNullOrEmpty(location)) return "";
        using var stream = File.OpenRead(location);
        return Convert.ToHexString(SHA256.HashData(stream));
    });
    private static double? Number(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number) ? number : null;
    }
    private static string Text(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.ToString() : "";
    private static string Decimal(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public async Task<AnalysisResult> AnalyzeAsync(string path, AnalysisOptions options, CancellationToken cancellation,
        Action<string, double?>? progress = null)
    {
        options.Validate(); tools.Validate(); path = Path.GetFullPath(path);
        var started = DateTimeOffset.UtcNow;
        var runs = new List<ProcessExecution>();
        var id = Guid.NewGuid().ToString("N");
        var hash = ""; var toolHash = ""; long bytes = 0, modified = 0;
        AnalysisResult Finish(AnalysisResult result, string termination) => result with
        {
            Evidence = new(id, hash, bytes, modified, started, DateTimeOffset.UtcNow,
                typeof(MediaAnalyzer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
                System.Runtime.InteropServices.RuntimeInformation.OSDescription, toolHash, options, termination, runs.ToArray(), EngineHash.Value, Environment.Version.ToString(),
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString())
        };
        try
        {
            // Manter o handle aberto impede alterações/exclusão durante hash e decodificação no Windows.
            await using var stable = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            bytes = stable.Length; modified = File.GetLastWriteTimeUtc(path).Ticks;
            progress?.Invoke("Calculando identidade SHA-256 do arquivo", null);
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stable, cancellation));
            await using var tool = new FileStream(tools.Ffprobe, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            toolHash = Convert.ToHexString(await SHA256.HashDataAsync(tool, cancellation));
            if (_versionToolHash != toolHash) { _version = null; _versionToolHash = toolHash; }
            return Finish(await AnalyzeContentAsync(path, options, cancellation, progress, runs), "Completed");
        }
        catch (OperationCanceledException)
        {
            throw new AnalysisCancelledException(Finish(new(path, options.Mode, "Análise cancelada", "", null,
                [], [], "Cancelamento não é diagnóstico de corrupção.", "", _version ?? "", true), "Cancelled"), cancellation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException)
        {
            throw new AnalysisFailedException(Finish(new(path, options.Mode, "Falha de execução", "", null,
                [], [], ex.GetType().Name + ": " + ex.Message, "", _version ?? "", true), "Failed"), ex);
        }
    }

    private async Task<AnalysisResult> AnalyzeContentAsync(string path, AnalysisOptions options, CancellationToken cancellation,
        Action<string, double?>? progress, List<ProcessExecution> runs)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Arquivo indisponível.", path);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Links não são analisados nesta versão.");
        await using (var signature = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
        {
            var header = new byte[4]; var read = await signature.ReadAsync(header, cancellation);
            if (read == 4 && header.AsSpan().SequenceEqual(new byte[] { 0, 5, 22, 7 }))
                return new(path, options.Mode, "Arquivo auxiliar AppleDouble", "AppleDouble", null, [], [],
                    "Não contém um segundo vídeo recuperável.", "", "", true);
        }
        if (_version is null)
        {
            var version = await runner.RunAsync(tools.Ffprobe, ["-version"], 15, options.MemoryLimitMb, cancellation, captureOutput: true, completed: run => runs.Add(run with { Stage = "Version" }));
            if (version.ExitCode != 0 || version.TimedOut) throw new IOException("FFprobe não respondeu à consulta de versão.");
            _version = version.Output.Split('\n')[0].Trim();
        }
        progress?.Invoke("Identificando contêiner e trilhas", null);
        var probe = await runner.RunAsync(tools.Ffprobe,
            ["-v", "warning", "-protocol_whitelist", "file", "-show_error", "-show_format", "-show_streams", "-of", "json", path],
            options.TimeoutSeconds, options.MemoryLimitMb, cancellation, captureOutput: true, completed: run => runs.Add(run with { Stage = "Probe" }));
        if (probe.TimedOut || probe.OutputTruncated)
            return new(path, options.Mode, "Inconclusivo", "", null, [], [], "Limite de tempo/saída atingido na identificação.", probe.LogPath, _version, true);
        if (probe.ExitCode != 0)
            return new(path, options.Mode, "Falha de estrutura ou identificação", "", null, [], [],
                "FFprobe não conseguiu identificar a mídia. Consulte o log; isso não prova ausência de payload recuperável.", probe.LogPath, _version, true);
        using var document = JsonDocument.Parse(probe.Output);
        var root = document.RootElement;
        var duration = root.TryGetProperty("format", out var format) ? Number(format, "duration") : null;
        var start = format.ValueKind == JsonValueKind.Object ? Number(format, "start_time") ?? 0 : 0;
        var container = format.ValueKind == JsonValueKind.Object ? Text(format, "format_name") : "";
        var streams = new List<MediaStream>();
        if (root.TryGetProperty("streams", out var streamArray))
        {
            if (streamArray.GetArrayLength() > 128) throw new IOException("Mais de 128 trilhas: limite de recursos da versão de desenvolvimento.");
            foreach (var stream in streamArray.EnumerateArray())
            {
                var kind = Text(stream, "codec_type");
                if (kind is not ("video" or "audio")) continue;
                // Capa estática não deve ser contabilizada como vídeo principal.
                if (stream.TryGetProperty("disposition", out var disposition) && Number(disposition, "attached_pic") == 1) continue;
                streams.Add(new(stream.GetProperty("index").GetInt32(), kind, Text(stream, "codec_name"), Number(stream, "duration"),
                    (int?)Number(stream, "width"), (int?)Number(stream, "height"), (int?)Number(stream, "channels"),
                    Text(stream, "avg_frame_rate"), Number(stream, "start_time")));
            }
        }
        var audioNote = streams.All(x => x.Kind != "audio") ? "Áudio não declarado; pode ser uma característica legítima da gravação. " : "";
        if (options.Mode == AnalysisMode.Quick)
            return new(path, options.Mode, streams.Count > 0 ? "Estrutura identificada" : "Nenhuma trilha A/V identificada", container,
                duration, streams, [], audioNote + "Modo rápido não comprova integridade do conteúdo." +
                (probe.DiagnosticLines > 0 ? " A identificação registrou avisos." : ""), probe.LogPath, _version, streams.Count == 0);

        var selected = streams.Where(x => x.Kind == "video" ? options.Video : options.Audio).ToArray();
        var tests = new List<DecodeTest>();
        var partial = selected.Length == 0;
        foreach (var stream in selected)
        {
            var streamDuration = stream.Duration ?? duration;
            IReadOnlyList<double?> positions;
            if (options.Mode == AnalysisMode.Full) positions = new double?[] { null };
            else if (streamDuration is > 0)
            {
                var sampling = SamplePositions(streamDuration.Value, info.Length, options);
                positions = sampling.Select(x => (double?)x).ToArray();
                if (options.Sampling == SamplingMode.ApproximateMegabytes && Math.Ceiling(info.Length / (options.Megabytes * 1_000_000)) > 200)
                    partial = true;
            }
            else { positions = new double?[] { 0 }; partial = true; }
            foreach (var position in positions)
            {
                progress?.Invoke($"Decodificando trilha {stream.Index} ({stream.Kind})", position is not null && streamDuration > 0 ? position / streamDuration : null);
                var arguments = new List<string> { "-v", "warning", "-protocol_whitelist", "file", "-threads", options.DecoderThreads.ToString(CultureInfo.InvariantCulture),
                    "-select_streams", stream.Index.ToString(CultureInfo.InvariantCulture), "-show_frames", "-show_entries",
                    "frame=stream_index,best_effort_timestamp_time,pkt_dts_time,duration_time,pkt_duration_time,nb_samples", "-of", "compact=p=0:nk=0" };
                if (position is not null)
                {
                    // Não fazer seek em zero: alguns FLVs íntegros retornam uma janela vazia.
                    var target = (stream.StartTime ?? start) + position.Value;
                    arguments.AddRange(["-read_intervals", position == 0 ? $"%+{Decimal(options.SampleSeconds)}" : $"{Decimal(target)}%{Decimal(target + options.SampleSeconds)}"]);
                }
                arguments.Add(path);
                long frames = 0, samples = 0; double? first = null, last = null;
                var lastProgress = Stopwatch.GetTimestamp();
                double contextTimestamp = double.NaN;
                var result = await runner.RunAsync(tools.Ffprobe, arguments, options.TimeoutSeconds, options.MemoryLimitMb, cancellation, line =>
                {
                    var fields = line.Split('|'); double? timestamp = null, frameDuration = null; long frameSamples = 0;
                    var isFrame = false;
                    foreach (var field in fields)
                    {
                        var pair = field.Split('=', 2); if (pair.Length != 2) continue;
                        if (pair[0] == "stream_index") isFrame = true;
                        if (pair[0] == "best_effort_timestamp_time" && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && double.IsFinite(t)) timestamp = t;
                        if (pair[0] is "duration_time" or "pkt_duration_time" && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)) frameDuration = d;
                        if (pair[0] == "nb_samples") long.TryParse(pair[1], out frameSamples);
                    }
                    if (!isFrame) return;
                    frames++; samples += frameSamples;
                    if (timestamp is not null) { Volatile.Write(ref contextTimestamp, timestamp.Value); first = first is null ? timestamp : Math.Min(first.Value, timestamp.Value); last = Math.Max(last ?? double.MinValue, timestamp.Value + (frameDuration ?? 0)); }
                    if (Stopwatch.GetElapsedTime(lastProgress).TotalMilliseconds >= 500 && timestamp is not null)
                    {
                        lastProgress = Stopwatch.GetTimestamp();
                        progress?.Invoke($"Decodificando trilha {stream.Index} ({stream.Kind})", streamDuration > 0 ?
                            Math.Clamp((timestamp.Value - (stream.StartTime ?? start)) / streamDuration.Value, 0, 1) : null);
                    }
                }, completed: run => runs.Add(run with { Stage = "Decode", SelectedStream = stream.Index }), mediaTimestamp: () => double.IsNaN(Volatile.Read(ref contextTimestamp)) ? null : Volatile.Read(ref contextTimestamp));
                var inconclusive = result.TimedOut || result.OutputTruncated || frames == 0 || first is null;
                if (position is not null && streamDuration is > 0)
                {
                    var requestedEnd = (stream.StartTime ?? start) + Math.Min(streamDuration.Value, position.Value + options.SampleSeconds);
                    if (last is null || last < requestedEnd - 0.25) inconclusive = true;
                }
                partial |= inconclusive;
                tests.Add(new(stream.Index, position is null ? null : (stream.StartTime ?? start) + position,
                    position is null ? null : options.SampleSeconds, first, last, frames, samples, result.ExitCode,
                    result.DiagnosticLines, result.TimedOut, result.LogPath,
                    inconclusive ? "Inconclusivo" : result.ExitCode != 0 || result.DiagnosticLines > 0 ? "Com avisos/erros" : "Sem erros observados"));
            }
        }
        var errors = tests.Any(x => x.ExitCode != 0 || x.Warnings > 0);
        var conclusion = partial ? "Análise parcial / inconclusiva" : errors ? "Decodificação com avisos/erros" :
            options.Mode == AnalysisMode.Full ? "Decodificação completa sem erros observados" : "Amostras sem erros observados";
        var note = audioNote + (options.Mode == AnalysisMode.Sampled ? "Trechos não amostrados podem conter defeitos. " : "Decodificação sem erros não garante qualidade visual/acústica. ") +
            (options.Sampling == SamplingMode.ApproximateMegabytes && options.Mode == AnalysisMode.Sampled ? "Posições por MB são estimativas temporais, limitadas a 200 janelas. " : "") +
            (selected.Length == 0 ? "Nenhuma trilha corresponde à seleção de áudio/vídeo. " : "") +
            (probe.DiagnosticLines > 0 ? "A identificação também registrou avisos. " : "");
        return new(path, options.Mode, conclusion, container, duration, streams, tests, note, probe.LogPath, _version, partial);
    }

    public static IReadOnlyList<double> SamplePositions(double duration, long bytes, AnalysisOptions options)
    {
        if (!double.IsFinite(duration) || duration <= 0) return [0];
        var count = options.Sampling == SamplingMode.Count ? options.Samples :
            (int)Math.Clamp(Math.Ceiling(bytes / (options.Megabytes * 1_000_000)) + 1, 2, 200);
        var maximum = Math.Max(0, duration - options.SampleSeconds);
        if (maximum == 0) return [0];
        var positions = Enumerable.Range(0, count).Select(i => maximum * i / (count - 1)).ToArray();
        return positions.Distinct().ToArray();
    }
}
