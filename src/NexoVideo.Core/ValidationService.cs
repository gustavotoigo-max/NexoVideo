using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexoVideo.Core;

public sealed record ComparisonReport(string Policy, ComparisonMetrics Overall,
    IReadOnlyDictionary<string, ComparisonMetrics> ByMode, IReadOnlyDictionary<string, ComparisonMetrics> ByFormat);

public sealed class ValidationService(QueueStore store)
{
    public const string Policy = "alerts-v1";
    private static readonly JsonSerializerOptions ExportOptions = new(QueueStore.Json)
    { Converters = { new JsonStringEnumConverter() } };

    // Positivo = alerta do decoder. Aviso não é prova de corrupção: pode ser falso positivo.
    public static AutomaticVerdict Verdict(AnalysisResult? result)
    {
        if (result?.Evidence is not { Termination: "Completed", Sha256.Length: 64 } evidence
            || result.Mode == AnalysisMode.Quick) return AutomaticVerdict.NotComparable;
        var runs = evidence.Executions.Where(e => !e.Arguments.Contains("-version")).ToArray();
        if (runs.Any(e => e.TimedOut || e.Cancelled || e.OutputTruncated)) return AutomaticVerdict.NotComparable;
        if (result.Tests.Any(t => t.ExitCode != 0 || t.Warnings > 0) || runs.Any(e => e.ExitCode != 0 || e.DiagnosticLines > 0))
            return AutomaticVerdict.Alert;
        return result.Inconclusive || result.Tests.Count == 0 ? AutomaticVerdict.NotComparable : AutomaticVerdict.Clear;
    }

    public static bool Current(ComparisonCase item) => item.Manual is { } review
        && review.AnalysisId == item.AnalysisId && review.Sha256 == item.Sha256 && item.Sha256?.Length == 64;

    public static string PairOutcome(ComparisonCase item)
    {
        if (!Current(item) || item.Manual is not { Verdict: HumanVerdict.NoProblem or HumanVerdict.Problem }
            || !(item.Manual.WatchedWholeFile || (item.Manual.CouldNotOpen && item.Manual.Verdict == HumanVerdict.Problem)))
            return "ReviewMissingOrIneligible";
        if (item.Automatic == AutomaticVerdict.NotComparable) return "AutomaticInconclusive";
        return (item.Automatic == AutomaticVerdict.Alert, item.Manual.Verdict == HumanVerdict.Problem) switch
        { (true, true) => "TP", (false, false) => "TN", (true, false) => "FP", _ => "FN" };
    }

    private sealed class Counter
    {
        private long total, reviewed, tp, tn, fp, fn, inconclusive, missing;
        public void Add(ComparisonCase item)
        {
            total++;
            if (Current(item) && item.Manual!.Verdict != HumanVerdict.NotEvaluated) reviewed++;
            if (item.Automatic == AutomaticVerdict.NotComparable) inconclusive++;
            switch (PairOutcome(item))
            {
                case "TP": tp++; break; case "TN": tn++; break; case "FP": fp++; break; case "FN": fn++; break;
                case "ReviewMissingOrIneligible": missing++; break;
            }
        }
        private static double? Ratio(long n, long d) => d == 0 ? null : (double)n / d;
        public ComparisonMetrics Finish() => new(total, reviewed, tp + tn + fp + fn, tp, tn, fp, fn, inconclusive, missing,
            Ratio(tp, tp + fp), Ratio(tp, tp + fn), Ratio(tp + tn, tp + tn + fp + fn), Ratio(tn, tn + fp),
            Ratio(2 * tp, 2 * tp + fp + fn), Ratio(tp + tn + fp + fn, total));
    }

    public static ComparisonMetrics Compare(IEnumerable<ComparisonCase> items)
    { var counter = new Counter(); foreach (var item in items) counter.Add(item); return counter.Finish(); }

    private IEnumerable<(QueueItem Item, AnalysisResult? Result, ManualReview? Review)> Rows(long jobId)
    {
        for (var page = 0; ; page++)
        {
            var items = store.Page(jobId, page); if (items.Count == 0) yield break;
            foreach (var item in items)
            {
                var result = store.Detail(item.Id);
                var review = store.ReviewHistory(item.Id).LastOrDefault(r => r.AnalysisId == result?.Evidence?.AnalysisId);
                yield return (item, result, review);
            }
        }
    }

    private static ComparisonCase Case(QueueItem item, AnalysisResult? result, ManualReview? review) => new(
        Path.GetExtension(item.Path).ToLowerInvariant(), result?.Mode ?? AnalysisMode.Quick,
        item.State == QueueState.Completed ? Verdict(result) : AutomaticVerdict.NotComparable,
        review, result?.Evidence?.AnalysisId, result?.Evidence?.Sha256);

    public ComparisonReport Summary(long jobId)
    {
        var all = new Counter(); var modes = new Dictionary<string, Counter>(); var formats = new Dictionary<string, Counter>();
        var mode = store.GetJob(jobId).Options.Mode;
        foreach (var row in Rows(jobId))
        {
            var item = Case(row.Item, row.Result, row.Review) with { Mode = row.Result?.Mode ?? mode };
            all.Add(item);
            if (!modes.TryGetValue(item.Mode.ToString(), out var mc)) modes[item.Mode.ToString()] = mc = new();
            if (!formats.TryGetValue(item.Format, out var fc)) formats[item.Format] = fc = new();
            mc.Add(item); fc.Add(item);
        }
        return new(Policy, all.Finish(), modes.ToDictionary(x => x.Key, x => x.Value.Finish()), formats.ToDictionary(x => x.Key, x => x.Value.Finish()));
    }

    public async Task SaveReviewAsync(long fileId, ManualReview review, CancellationToken cancellation = default)
    {
        var result = store.Detail(fileId) ?? throw new InvalidOperationException("Arquivo sem diagnóstico.");
        if (result.Evidence?.Sha256.Length != 64) throw new InvalidOperationException("Reanalise o arquivo para coletar sua identidade antes de avaliar.");
        // Revalidar bytes, mesmo se alguém restaurou tamanho e data originais após editar o arquivo.
        await using var source = new FileStream(result.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellation));
        if (hash != review.Sha256) throw new InvalidOperationException("Conteúdo alterado desde a análise. Faça uma nova análise.");
        cancellation.ThrowIfCancellationRequested(); store.SaveReview(fileId, review);
    }

    public void ExportJson(long jobId, string destination)
    {
        // O backup online do SQLite fixa uma única visão, mesmo se outra instância alterar o banco.
        var snapshot = store.DatabasePath + ".export-" + Guid.NewGuid().ToString("N") + ".db";
        try { new ValidationService(store.Snapshot(snapshot)).ExportSnapshot(jobId, destination); }
        finally
        {
            foreach (var path in new[] { snapshot + "-shm", snapshot + "-wal", snapshot })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    private void ExportSnapshot(long jobId, string destination)
    {
        if (store.Counts(jobId).Running > 0) throw new InvalidOperationException("Aguarde pausar ou concluir a análise.");
        var job = store.GetJob(jobId);
        var temporary = Path.GetFullPath(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
            {
                void Property<T>(string name, T value) { writer.WritePropertyName(name); JsonSerializer.Serialize(writer, value, ExportOptions); }
                writer.WriteStartObject(); writer.WriteString("schema", "nexovideo.validation/1");
                writer.WriteString("generated_utc", DateTimeOffset.UtcNow); Property("session", job);
                Property("counts", store.Counts(jobId)); Property("comparison", Summary(jobId));
                Property("interpretation", new[] {
                    "Positivo = alerta (avisos/retorno não zero, inclusive identificação); negativo = decode sem alertas no escopo selecionado. Alerta não prova corrupção.",
                    "Modo rápido, legado sem evidência, execução falha, cancelamento, limites e análise parcial sem alertas não entram na matriz.",
                    "Só compara revisão conclusiva do mesmo SHA-256 e AnalysisId, com reprodução humana inteira ou impossibilidade confirmada de abrir (problema).",
                    "Áudio/vídeo desmarcados e trechos não amostrados ficam fora da cobertura automática; confira Options e Tests.",
                    "A trilha selecionada é o contexto da execução; o demuxer pode emitir mensagens sobre outras trilhas. Não atribuir todas as mensagens ao índice selecionado.",
                    "LastDecodedTimestamp é contexto aproximado dos pipes assíncronos, não o instante exato do erro. Null = localização desconhecida.",
                    "Máximo 200 mensagens estruturadas de 2.048 caracteres por processo; DiagnosticLines conta todas. Logs limitados a 1 MiB de texto.",
                    "Taxas entre 0 e 1; null = denominador zero. Os grupos por formato usam a extensão; Container e Streams registram codecs reais.",
                    "Satisfaction registra qualidade percebida separadamente; a matriz usa Verdict. Resultados sintéticos não estimam precisão no acervo real."
                });
                writer.WritePropertyName("events"); store.WriteEvents(writer, jobId);
                writer.WritePropertyName("files"); writer.WriteStartArray();
                foreach (var row in Rows(jobId))
                {
                    writer.WriteStartObject(); Property("queue", row.Item); Property("diagnosis", row.Result);
                    writer.WritePropertyName("analysis_history"); var historyLogs = store.WriteAnalysisHistory(writer, row.Item.Id, ExportOptions);
                    Property("manual", row.Review); Property("review_history", store.ReviewHistory(row.Item.Id));
                    var item = Case(row.Item, row.Result, row.Review);
                    Property("automatic_verdict", item.Automatic); Property("comparison_outcome", PairOutcome(item));
                    writer.WritePropertyName("logs"); writer.WriteStartArray();
                    var paths = (row.Result?.Evidence?.Executions.Select(e => e.LogPath) ?? [])
                        .Concat(historyLogs.Keys).Concat(row.Result?.Tests.Select(t => t.LogPath) ?? []).Append(row.Result?.ProbeLog ?? "")
                        .Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
                    foreach (var path in paths)
                    {
                        writer.WriteStartObject(); writer.WriteString("path", path);
                        try
                        {
                            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                            var actualHash = Convert.ToHexString(SHA256.HashData(stream)); stream.Position = 0;
                            writer.WriteString("sha256", actualHash);
                            var expectedHash = historyLogs.GetValueOrDefault(path, "");
                            writer.WriteString("captured_sha256", expectedHash);
                            if (expectedHash.Length == 64) writer.WriteBoolean("matches_capture", actualHash == expectedHash);
                            else writer.WriteNull("matches_capture");
                            using var reader = new StreamReader(stream);
                            var buffer = new char[1024 * 1024 + 32768]; var length = reader.ReadBlock(buffer, 0, buffer.Length);
                            writer.WriteString("text", new string(buffer, 0, length)); writer.WriteBoolean("missing", false);
                            writer.WriteBoolean("export_truncated", !reader.EndOfStream);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { writer.WriteBoolean("missing", true); writer.WriteString("reason", ex.Message); }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
