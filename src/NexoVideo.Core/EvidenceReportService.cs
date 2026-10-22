using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexoVideo.Core;

public sealed class EvidenceReportService(QueueStore store)
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

    private IEnumerable<(QueueItem Item, AnalysisResult? Result)> Rows(long jobId)
    {
        for (var page = 0; ; page++)
        {
            var items = store.Page(jobId, page); if (items.Count == 0) yield break;
            foreach (var item in items) yield return (item, store.Detail(item.Id));
        }
    }

    public void ExportJson(long jobId, string destination)
    {
        // O backup online do SQLite fixa uma única visão, mesmo se outra instância alterar o banco.
        var snapshot = store.DatabasePath + ".export-" + Guid.NewGuid().ToString("N") + ".db";
        try { new EvidenceReportService(store.Snapshot(snapshot)).ExportSnapshot(jobId, destination); }
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
                writer.WriteStartObject(); writer.WriteString("schema", "nexovideo.evidence/2");
                writer.WriteString("generated_utc", DateTimeOffset.UtcNow); Property("session", job);
                Property("counts", store.Counts(jobId)); Property("automatic_policy", Policy);
                Property("reference_comparison", "not_performed");
                Property("interpretation", new[] {
                    "Positivo = alerta (avisos/retorno não zero, inclusive identificação); negativo = decode sem alertas no escopo selecionado. Alerta não prova corrupção.",
                    "Modo rápido, legado sem evidência, execução falha, cancelamento, limites e análise parcial sem alertas não são classificados como conteúdo íntegro.",
                    "Este relatório coleta evidências; nenhuma referência externa foi comparada e nenhuma precisão foi calculada.",
                    "Áudio/vídeo desmarcados e trechos não amostrados ficam fora da cobertura automática; confira Options e Tests.",
                    "A trilha selecionada é o contexto da execução; o demuxer pode emitir mensagens sobre outras trilhas. Não atribuir todas as mensagens ao índice selecionado.",
                    "LastDecodedTimestamp é contexto aproximado dos pipes assíncronos, não o instante exato do erro. Null = localização desconhecida.",
                    "Máximo 200 mensagens estruturadas de 2.048 caracteres por processo; DiagnosticLines conta todas. Logs limitados a 1 MiB de texto.",
                    "Container e Streams registram os formatos e codecs identificados; a extensão não garante compatibilidade.",
                    "Avaliações antigas, quando presentes em legacy_review_history, são apenas histórico e não são referência para comparação."
                });
                writer.WritePropertyName("events"); store.WriteEvents(writer, jobId);
                writer.WritePropertyName("files"); writer.WriteStartArray();
                foreach (var row in Rows(jobId))
                {
                    writer.WriteStartObject(); Property("queue", row.Item); Property("diagnosis", row.Result);
                    writer.WritePropertyName("analysis_history"); var historyLogs = store.WriteAnalysisHistory(writer, row.Item.Id, ExportOptions);
                    store.WriteLegacyReviewHistory(writer, row.Item.Id);
                    Property("automatic_verdict", row.Item.State == QueueState.Completed ? Verdict(row.Result) : AutomaticVerdict.NotComparable);
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
