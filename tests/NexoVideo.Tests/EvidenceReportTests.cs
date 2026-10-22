using System.Security.Cryptography;
using System.Text.Json;
using NexoVideo.Core;
using Xunit;

namespace NexoVideo.Tests;

public sealed class EvidenceReportTests
{
    private static readonly string Hash = new('A', 64);

    [Fact]
    public void ExistingDatabaseMigratesWithoutReinterpretingLegacyDiagnosis()
    {
        var root = EnvironmentPaths.NewDirectory(); var path = Path.Combine(root, "legacy.db");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE jobs(id INTEGER PRIMARY KEY,source TEXT NOT NULL,options TEXT NOT NULL,created TEXT NOT NULL);
                CREATE TABLE files(id INTEGER PRIMARY KEY,job_id INTEGER NOT NULL REFERENCES jobs(id),path TEXT NOT NULL COLLATE NOCASE,
                    size INTEGER NOT NULL,modified INTEGER NOT NULL,state TEXT NOT NULL,conclusion TEXT NOT NULL DEFAULT '',note TEXT NOT NULL DEFAULT '',result TEXT,UNIQUE(job_id,path));
                CREATE TABLE events(id INTEGER PRIMARY KEY,job_id INTEGER NOT NULL REFERENCES jobs(id),message TEXT NOT NULL,created TEXT NOT NULL);
                INSERT INTO jobs VALUES(1,$source,$options,'old');
                INSERT INTO files VALUES(1,1,'old.mp4',10,1,'Completed','old','',$result);
                """;
            command.Parameters.AddWithValue("$source", root);
            command.Parameters.AddWithValue("$options", JsonSerializer.Serialize(new AnalysisOptions(), QueueStore.Json));
            command.Parameters.AddWithValue("$result", JsonSerializer.Serialize(Diagnosis("old.mp4", "") with { Evidence = null }, QueueStore.Json));
            command.ExecuteNonQuery();
        }
        var store = new QueueStore(path); store.Initialize(); store.Initialize();
        Assert.Equal(1, store.Counts(1).Completed);
        Assert.Equal(AutomaticVerdict.NotComparable, EvidenceReportService.Verdict(store.Detail(1)));
        new EvidenceReportService(store).ExportJson(1, Path.Combine(root, "legacy-report.json"));
    }
    private static AnalysisResult Diagnosis(string path, string log, bool warning = false)
    {
        var options = new AnalysisOptions { Mode = AnalysisMode.Full };
        var run = new ProcessExecution("ffprobe", ["-show_frames", path], DateTimeOffset.UtcNow, 30, 0, false, false,
            false, false, warning ? 1 : 0, 100, log, warning ? [new(12, "decoder warning", 2.5)] : []);
        var evidence = new AnalysisEvidence("analysis-1", Hash, 10, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            "test", "Windows", Hash, options, "Completed", [run]);
        return new(path, AnalysisMode.Full, "test", "mp4", 3, [],
            [new(0, null, null, 0, 3, 90, 0, 0, warning ? 1 : 0, false, log, "test")], "", log, "ffprobe test", false, evidence);
    }

    [Fact]
    public void VerdictSeparatesAlertsLimitsLegacyAndQuickMetadata()
    {
        var clear = Diagnosis("test.mp4", "test.log");
        Assert.Equal(AutomaticVerdict.Clear, EvidenceReportService.Verdict(clear));
        Assert.Equal(AutomaticVerdict.Alert, EvidenceReportService.Verdict(Diagnosis("test.mp4", "test.log", true)));
        Assert.Equal(AutomaticVerdict.NotComparable, EvidenceReportService.Verdict(clear with { Evidence = null }));
        Assert.Equal(AutomaticVerdict.NotComparable, EvidenceReportService.Verdict(clear with { Mode = AnalysisMode.Quick }));
        var timed = clear.Evidence!.Executions[0] with { TimedOut = true };
        Assert.Equal(AutomaticVerdict.NotComparable, EvidenceReportService.Verdict(clear with { Evidence = clear.Evidence with { Executions = [timed] } }));
        var structure = clear.Evidence.Executions[0] with { ExitCode = 1, Arguments = ["-show_error"] };
        Assert.Equal(AutomaticVerdict.Alert, EvidenceReportService.Verdict(clear with { Tests = [], Inconclusive = true, Evidence = clear.Evidence with { Executions = [structure] } }));
        Assert.Equal(AutomaticVerdict.NotComparable, EvidenceReportService.Verdict(clear with { Inconclusive = true }));
    }

    [Fact]
    public void ExportIsPortableRetainsHistoryAndDoesNotOverwriteReports()
    {
        var root = EnvironmentPaths.NewDirectory(); var log = Path.Combine(root, "decode.log"); File.WriteAllText(log, "original diagnostic");
        var store = new QueueStore(Path.Combine(root, "test.db")); store.Initialize();
        var job = store.CreateJob(root, new() { Mode = AnalysisMode.Full });
        store.AddFile(job.Id, new(Path.Combine(root, "a.mp4"), 10, 1)); var item = store.ClaimNext(job.Id)!;
        var result = Diagnosis(item.Path, log, true);
        var capturedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(log)));
        result = result with { Evidence = result.Evidence! with { Executions = [result.Evidence!.Executions[0] with { LogSha256 = capturedHash }] } };
        store.Save(item, result, QueueState.Completed);
        // Simular registros de alpha.1/alpha.2, sem recriar a funcionalidade de gravação no produto.
        var legacy = JsonSerializer.Serialize(new { AnalysisId = "analysis-1", Sha256 = Hash, Verdict = 2,
            Satisfaction = 1, WatchedWholeFile = true, Reviewer = "LEGACY TEST", Notes = "Preservar sem usar como referência" });
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE reviews(id INTEGER PRIMARY KEY,file_id INTEGER NOT NULL,review TEXT NOT NULL); INSERT INTO reviews(file_id,review) VALUES($id,$json);";
            command.Parameters.AddWithValue("$id", item.Id); command.Parameters.AddWithValue("$json", legacy); command.ExecuteNonQuery();
        }
        store.Save(item, result with { Evidence = result.Evidence! with { AnalysisId = "analysis-2" } }, QueueState.Completed);
        store.Event(job.Id, "inventário de controle");
        var destination = Path.Combine(root, "report.json"); new EvidenceReportService(store).ExportJson(job.Id, destination);
        File.WriteAllText(log, "modified after capture");
        var tamperedPath = Path.Combine(root, "tampered-log.json"); new EvidenceReportService(store).ExportJson(job.Id, tamperedPath);
        using (var tampered = JsonDocument.Parse(File.ReadAllText(tamperedPath)))
            Assert.False(tampered.RootElement.GetProperty("files")[0].GetProperty("logs")[0].GetProperty("matches_capture").GetBoolean());
        File.Delete(log);
        using var report = JsonDocument.Parse(File.ReadAllText(destination)); var row = report.RootElement.GetProperty("files")[0];
        Assert.Equal("nexovideo.evidence/2", report.RootElement.GetProperty("schema").GetString());
        Assert.Equal("original diagnostic", row.GetProperty("logs")[0].GetProperty("text").GetString());
        Assert.False(row.GetProperty("logs")[0].GetProperty("missing").GetBoolean());
        Assert.True(row.GetProperty("logs")[0].GetProperty("matches_capture").GetBoolean());
        Assert.Equal(2, row.GetProperty("analysis_history").GetArrayLength());
        Assert.Single(row.GetProperty("legacy_review_history").EnumerateArray());
        Assert.Equal("LEGACY TEST", row.GetProperty("legacy_review_history")[0].GetProperty("Reviewer").GetString());
        Assert.False(row.TryGetProperty("manual", out _)); Assert.False(row.TryGetProperty("comparison_outcome", out _));
        Assert.False(report.RootElement.TryGetProperty("comparison", out _));
        Assert.Equal("not_performed", report.RootElement.GetProperty("reference_comparison").GetString());
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT review FROM reviews;";
            Assert.Equal(legacy, command.ExecuteScalar());
        }
        var original = File.ReadAllBytes(destination);
        Assert.Throws<IOException>(() => new EvidenceReportService(store).ExportJson(job.Id, destination));
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(root, "*.tmp")); Assert.Empty(Directory.GetFiles(root, "*.export-*"));
        new EvidenceReportService(store).ExportJson(job.Id, Path.Combine(root, "missing-log.json"));
        using var missing = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "missing-log.json")));
        Assert.True(missing.RootElement.GetProperty("files")[0].GetProperty("logs")[0].GetProperty("missing").GetBoolean());
    }

    [Fact]
    public void BatchReportExportsAll205FilesWithoutManualStepsOrPrecision()
    {
        var root = EnvironmentPaths.NewDirectory(); var store = new QueueStore(Path.Combine(root, "test.db")); store.Initialize();
        var job = store.CreateJob(root, new() { Mode = AnalysisMode.Full });
        for (var i = 0; i < 205; i++) store.AddFile(job.Id, new(Path.Combine(root, $"{i:000}.mp4"), 10, 1));
        QueueItem? item;
        while ((item = store.ClaimNext(job.Id)) is not null)
            store.Save(item, Diagnosis(item.Path, "") with { Evidence = Diagnosis(item.Path, "").Evidence! with { AnalysisId = "batch-" + item.Id } }, QueueState.Completed);
        var destination = Path.Combine(root, "batch.json"); new EvidenceReportService(store).ExportJson(job.Id, destination);
        using var report = JsonDocument.Parse(File.ReadAllText(destination));
        Assert.Equal(205, report.RootElement.GetProperty("files").GetArrayLength());
        Assert.Equal("not_performed", report.RootElement.GetProperty("reference_comparison").GetString());
        Assert.False(report.RootElement.TryGetProperty("comparison", out _));
        foreach (var row in report.RootElement.GetProperty("files").EnumerateArray())
        {
            Assert.Equal("Clear", row.GetProperty("automatic_verdict").GetString());
            Assert.Equal(64, row.GetProperty("diagnosis").GetProperty("Evidence").GetProperty("Sha256").GetString()!.Length);
            Assert.False(row.TryGetProperty("manual", out _)); Assert.False(row.TryGetProperty("legacy_review_history", out _));
        }
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='reviews';";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [MediaFact]
    public async Task RealDecoderCollectsIdentityIntervalsCommandsAndDiagnosticContext()
    {
        var root = EnvironmentPaths.NewDirectory(); var path = EnvironmentPaths.Fixture("payload_damaged.mp4");
        var tools = new ToolPaths(EnvironmentPaths.Probe!, Path.Combine(Path.GetDirectoryName(EnvironmentPaths.Probe)!, "ffmpeg.exe"));
        var result = await new MediaAnalyzer(tools, new(Path.Combine(root, "logs"))).AnalyzeAsync(path, new() { Mode = AnalysisMode.Full }, default);
        var evidence = Assert.IsType<AnalysisEvidence>(result.Evidence);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), evidence.Sha256);
        Assert.Equal(64, evidence.FfprobeSha256.Length); Assert.True(evidence.FinishedUtc >= evidence.StartedUtc);
        Assert.Equal(result.Tests.Count + 2, evidence.Executions.Count);
        Assert.Contains(evidence.Executions, run => run.Diagnostics.Count > 0 && run.DiagnosticLines >= run.Diagnostics.Count);
        Assert.All(evidence.Executions, run => { Assert.True(File.Exists(run.LogPath)); Assert.True(run.ElapsedMilliseconds >= 0); Assert.Equal(64, run.LogSha256.Length); });
        Assert.All(result.Tests, test => Assert.Contains(evidence.Executions, run => run.LogPath == test.LogPath && run.Arguments.Contains(test.StreamIndex.ToString())));
        Assert.Equal(AutomaticVerdict.Alert, EvidenceReportService.Verdict(result));
    }

    [MediaFact]
    public async Task QueueKeepsEvidenceForCancelledFileWithoutCallingItCorruption()
    {
        var root = EnvironmentPaths.NewDirectory(); var input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
        File.Copy(EnvironmentPaths.Fixture("av.mp4"), Path.Combine(input, "a.mp4"));
        var store = new QueueStore(Path.Combine(root, "test.db")); store.Initialize();
        var tools = new ToolPaths(EnvironmentPaths.Probe!, Path.Combine(Path.GetDirectoryName(EnvironmentPaths.Probe)!, "ffmpeg.exe"));
        using var cancellation = new CancellationTokenSource();
        var progress = new DirectProgress(p => { if (p.Stage.StartsWith("Decodificando")) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new QueueCoordinator(store, new(tools, new(Path.Combine(root, "logs"))))
            .StartAsync(input, new() { Mode = AnalysisMode.Full }, progress, cancellation.Token));
        var job = store.LatestJob()!; var item = Assert.Single(store.Page(job.Id, 0)); Assert.Equal(QueueState.Cancelled, item.State);
        var result = store.Detail(item.Id)!; Assert.Equal("Cancelled", result.Evidence!.Termination);
        Assert.Equal(64, result.Evidence.Sha256.Length); Assert.NotEmpty(result.Evidence.Executions);
        Assert.Equal(AutomaticVerdict.NotComparable, EvidenceReportService.Verdict(result));
    }

    [Fact]
    public async Task DiagnosticsAreBoundedButCountersAndTimeoutEvidenceContinue()
    {
        var root = EnvironmentPaths.NewDirectory(); ProcessExecution? evidence = null;
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var result = await new WindowsProcessRunner(root).RunAsync(shell,
            ["-NoProfile", "-Command", "1..230 | ForEach-Object { [Console]::Error.WriteLine('diagnostic') }; Start-Sleep -Seconds 30"],
            3, 512, default, completed: run => evidence = run, mediaTimestamp: () => 4.5);
        Assert.True(result.TimedOut); Assert.NotNull(evidence); Assert.True(evidence.TimedOut);
        Assert.Equal(230, evidence.DiagnosticLines); Assert.Equal(200, evidence.Diagnostics.Count);
        Assert.All(evidence.Diagnostics, message => Assert.Equal(4.5, message.LastDecodedTimestamp));
    }

    private sealed class DirectProgress(Action<QueueProgress> report) : IProgress<QueueProgress>
    { public void Report(QueueProgress value) => report(value); }
}
