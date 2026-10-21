using System.Security.Cryptography;
using System.Text.Json;
using NexoVideo.Core;
using Xunit;

namespace NexoVideo.Tests;

public sealed class ValidationTests
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
        Assert.Equal(1, store.Counts(1).Completed); Assert.Empty(store.ReviewHistory(1));
        Assert.Equal(AutomaticVerdict.NotComparable, ValidationService.Verdict(store.Detail(1)));
        new ValidationService(store).ExportJson(1, Path.Combine(root, "legacy-report.json"));
    }
    private static ManualReview Review(HumanVerdict verdict = HumanVerdict.NoProblem) =>
        new("analysis-1", Hash, verdict, Satisfaction.Satisfactory, true, "Avaliador", "Observação independente", DateTimeOffset.UtcNow, "Player 1");
    private static ComparisonCase Pair(AutomaticVerdict automatic, HumanVerdict human) => new(".mp4", AnalysisMode.Full, automatic, Review(human), "analysis-1", Hash);

    [Fact]
    public void ConfusionMatrixUsesOnlyMatchingConclusiveWholeFileReviews()
    {
        var cases = new[] {
            Pair(AutomaticVerdict.Alert, HumanVerdict.Problem), Pair(AutomaticVerdict.Clear, HumanVerdict.NoProblem),
            Pair(AutomaticVerdict.Alert, HumanVerdict.NoProblem), Pair(AutomaticVerdict.Clear, HumanVerdict.Problem),
            Pair(AutomaticVerdict.NotComparable, HumanVerdict.Problem), Pair(AutomaticVerdict.Clear, HumanVerdict.Inconclusive),
            Pair(AutomaticVerdict.Clear, HumanVerdict.NoProblem) with { Manual = Review() with { WatchedWholeFile = false } },
            Pair(AutomaticVerdict.Clear, HumanVerdict.NoProblem) with { Sha256 = new string('B', 64) },
            Pair(AutomaticVerdict.Clear, HumanVerdict.NoProblem) with { AnalysisId = "old" },
            Pair(AutomaticVerdict.Clear, HumanVerdict.NoProblem) with { Manual = null }
        };
        var metrics = ValidationService.Compare(cases);
        Assert.Equal(10, metrics.Total); Assert.Equal(4, metrics.Comparable);
        Assert.Equal(1, metrics.TruePositive); Assert.Equal(1, metrics.TrueNegative);
        Assert.Equal(1, metrics.FalsePositive); Assert.Equal(1, metrics.FalseNegative);
        Assert.Equal(0.5, metrics.Precision); Assert.Equal(0.5, metrics.Recall); Assert.Equal(0.5, metrics.Accuracy);
        Assert.Equal(0.5, metrics.Specificity); Assert.Equal(0.5, metrics.F1); Assert.Equal(0.4, metrics.ComparisonCoverage);
        Assert.Equal(1, metrics.AutomaticInconclusive); Assert.Equal(5, metrics.MissingOrIneligibleReview);
        var cannotOpen = Pair(AutomaticVerdict.Alert, HumanVerdict.Problem) with
        { Manual = Review(HumanVerdict.Problem) with { WatchedWholeFile = false, CouldNotOpen = true } };
        Assert.Equal(1, ValidationService.Compare([cannotOpen]).TruePositive);
        var empty = ValidationService.Compare([]);
        Assert.Null(empty.Precision); Assert.Null(empty.Accuracy); Assert.Null(empty.ComparisonCoverage);
        Assert.Null(ValidationService.Compare([Pair(AutomaticVerdict.Clear, HumanVerdict.NoProblem)]).Precision);
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
        Assert.Equal(AutomaticVerdict.Clear, ValidationService.Verdict(clear));
        Assert.Equal(AutomaticVerdict.Alert, ValidationService.Verdict(Diagnosis("test.mp4", "test.log", true)));
        Assert.Equal(AutomaticVerdict.NotComparable, ValidationService.Verdict(clear with { Evidence = null }));
        Assert.Equal(AutomaticVerdict.NotComparable, ValidationService.Verdict(clear with { Mode = AnalysisMode.Quick }));
        var timed = clear.Evidence!.Executions[0] with { TimedOut = true };
        Assert.Equal(AutomaticVerdict.NotComparable, ValidationService.Verdict(clear with { Evidence = clear.Evidence with { Executions = [timed] } }));
        var structure = clear.Evidence.Executions[0] with { ExitCode = 1, Arguments = ["-show_error"] };
        Assert.Equal(AutomaticVerdict.Alert, ValidationService.Verdict(clear with { Tests = [], Inconclusive = true, Evidence = clear.Evidence with { Executions = [structure] } }));
        Assert.Equal(AutomaticVerdict.NotComparable, ValidationService.Verdict(clear with { Inconclusive = true }));
    }

    [Fact]
    public async Task ReviewRejectsChangedBytesEvenWhenSizeAndDateAreRestored()
    {
        var root = EnvironmentPaths.NewDirectory(); var path = Path.Combine(root, "a.mp4"); File.WriteAllText(path, "abcdefghij");
        var store = new QueueStore(Path.Combine(root, "test.db")); store.Initialize(); store.Initialize();
        var job = store.CreateJob(root, new()); var info = new FileInfo(path);
        store.AddFile(job.Id, new(path, info.Length, info.LastWriteTimeUtc.Ticks)); var item = store.ClaimNext(job.Id)!;
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var result = Diagnosis(path, ""); result = result with { Evidence = result.Evidence! with { Sha256 = hash } };
        store.Save(item, result, QueueState.Completed);
        var review = Review() with { Sha256 = hash };
        var service = new ValidationService(store); await service.SaveReviewAsync(item.Id, review);
        Assert.Single(store.ReviewHistory(item.Id));
        var modified = File.GetLastWriteTimeUtc(path); File.WriteAllText(path, "0123456789"); File.SetLastWriteTimeUtc(path, modified);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveReviewAsync(item.Id, review));
        Assert.Single(store.ReviewHistory(item.Id));
        Assert.Throws<InvalidOperationException>(() => store.SaveReview(item.Id, review with { AnalysisId = "stale" }));
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
        store.SaveReview(item.Id, Review(HumanVerdict.Problem));
        store.Save(item, result with { Evidence = result.Evidence! with { AnalysisId = "analysis-2" } }, QueueState.Completed);
        store.Event(job.Id, "inventário de controle");
        var destination = Path.Combine(root, "report.json"); new ValidationService(store).ExportJson(job.Id, destination);
        File.WriteAllText(log, "modified after capture");
        var tamperedPath = Path.Combine(root, "tampered-log.json"); new ValidationService(store).ExportJson(job.Id, tamperedPath);
        using (var tampered = JsonDocument.Parse(File.ReadAllText(tamperedPath)))
            Assert.False(tampered.RootElement.GetProperty("files")[0].GetProperty("logs")[0].GetProperty("matches_capture").GetBoolean());
        File.Delete(log);
        using var report = JsonDocument.Parse(File.ReadAllText(destination)); var row = report.RootElement.GetProperty("files")[0];
        Assert.Equal("nexovideo.validation/1", report.RootElement.GetProperty("schema").GetString());
        Assert.Equal("original diagnostic", row.GetProperty("logs")[0].GetProperty("text").GetString());
        Assert.False(row.GetProperty("logs")[0].GetProperty("missing").GetBoolean());
        Assert.True(row.GetProperty("logs")[0].GetProperty("matches_capture").GetBoolean());
        Assert.Equal(2, row.GetProperty("analysis_history").GetArrayLength());
        Assert.Single(row.GetProperty("review_history").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("manual").ValueKind);
        Assert.Equal(0, report.RootElement.GetProperty("comparison").GetProperty("Overall").GetProperty("Comparable").GetInt32());
        var original = File.ReadAllBytes(destination);
        Assert.Throws<IOException>(() => new ValidationService(store).ExportJson(job.Id, destination));
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(root, "*.tmp")); Assert.Empty(Directory.GetFiles(root, "*.export-*"));
        new ValidationService(store).ExportJson(job.Id, Path.Combine(root, "missing-log.json"));
        using var missing = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "missing-log.json")));
        Assert.True(missing.RootElement.GetProperty("files")[0].GetProperty("logs")[0].GetProperty("missing").GetBoolean());
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
        Assert.Equal(AutomaticVerdict.Alert, ValidationService.Verdict(result));
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
        await new ValidationService(store).SaveReviewAsync(item.Id, Review() with
            { AnalysisId = result.Evidence.AnalysisId, Sha256 = result.Evidence.Sha256 });
        var metrics = new ValidationService(store).Summary(job.Id).Overall;
        Assert.Equal(1, metrics.ManuallyEvaluated); Assert.Equal(0, metrics.Comparable);
        Assert.Equal(AutomaticVerdict.NotComparable, ValidationService.Verdict(result));
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
