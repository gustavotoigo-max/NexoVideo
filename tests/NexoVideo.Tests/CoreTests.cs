using System.Diagnostics;
using System.Security.Cryptography;
using NexoVideo.Core;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace NexoVideo.Tests;

internal static class EnvironmentPaths
{
    public static string Root
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "NexoVideo.sln"))) return directory.FullName;
            throw new DirectoryNotFoundException("Não foi possível localizar a solução.");
        }
    }
    public static string NewDirectory()
    { var path = Path.Combine(Root, ".research", "tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    public static string? Probe => Directory.Exists(Path.Combine(Root, ".research", "tools", "ffmpeg"))
        ? Directory.EnumerateFiles(Path.Combine(Root, ".research", "tools", "ffmpeg"), "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
    public static string Fixture(string name) => Path.Combine(Root, ".research", "run-01", name);
}

public sealed class MediaFactAttribute : FactAttribute
{
    public MediaFactAttribute()
    { if (EnvironmentPaths.Probe is null || !File.Exists(EnvironmentPaths.Fixture("av.mp4"))) Skip = "Execute os experimentos sintéticos conforme docs/pesquisa/EXPERIMENTOS.md."; }
}

public sealed class CoreTests
{
    [Fact]
    public void SamplingIncludesStartEndAndRequestedCount()
    {
        var options = new AnalysisOptions { Samples = 5, SampleSeconds = 3 };
        var positions = MediaAnalyzer.SamplePositions(103, 1000, options);
        Assert.Equal(new double[] { 0, 25, 50, 75, 100 }, positions);
        Assert.Single(MediaAnalyzer.SamplePositions(2, 1000, options));
    }

    [Fact]
    public void ApproximateByteSamplingHasBoundedWindows()
    {
        var positions = MediaAnalyzer.SamplePositions(100_000, long.MaxValue,
            new AnalysisOptions { Sampling = SamplingMode.ApproximateMegabytes, Megabytes = 20 });
        Assert.Equal(200, positions.Count); Assert.Equal(0, positions[0]); Assert.Equal(99_997, positions[^1]);
    }

    [Fact]
    public void InventoryExcludesSidecarsOutputAndOtherExtensions()
    {
        var root = EnvironmentPaths.NewDirectory();
        Directory.CreateDirectory(Path.Combine(root, "nested")); Directory.CreateDirectory(Path.Combine(root, "out"));
        foreach (var name in new[] { "a.mp4", "b.txt", "._a.mp4", "nested/b.MOV", "out/ignore.mp4" }) File.WriteAllText(Path.Combine(root, name), "fixture");
        var files = Inventory.Enumerate(root, new AnalysisOptions { Extensions = [".mp4", ".mov"], ExcludedDirectory = Path.Combine(root, "out") }, null, default).ToArray();
        Assert.Equal(2, files.Length); Assert.Contains(files, file => file.Path.EndsWith("b.MOV"));
        Assert.False(Inventory.IsInside(root + "-other", root));
    }

    [Fact]
    public void StoreRecoversInterruptedAndReusesUnchangedCompletedItems()
    {
        var root = EnvironmentPaths.NewDirectory(); var path = Path.Combine(root, "video.mp4"); File.WriteAllText(path, "synthetic");
        var store = new QueueStore(Path.Combine(root, "local.db"));
        Assert.True(Version.Parse(store.Initialize()) >= new Version(3, 51, 3));
        var job = store.CreateJob(root, new()); var info = new FileInfo(path);
        store.AddFile(job.Id, new(path, info.Length, info.LastWriteTimeUtc.Ticks));
        Assert.NotNull(store.ClaimNext(job.Id));
        var reopened = new QueueStore(store.DatabasePath); reopened.Initialize(); reopened.RecoverInterrupted();
        Assert.Equal(1, reopened.Counts(job.Id).Pending);
        var item = reopened.ClaimNext(job.Id)!;
        reopened.Save(item, new(path, AnalysisMode.Quick, "Estrutura identificada", "mp4", 8, [], [], "", "", "test", false), QueueState.Completed);
        reopened.PrepareResume(job.Id); Assert.Null(reopened.ClaimNext(job.Id));
        File.AppendAllText(path, "changed"); reopened.PrepareResume(job.Id);
        Assert.NotNull(reopened.ClaimNext(job.Id));
    }

    [Fact]
    public void StorePaginatesWithoutLoadingResultsAndClaimsOnlyOnce()
    {
        var root = EnvironmentPaths.NewDirectory(); var store = new QueueStore(Path.Combine(root, "local.db")); store.Initialize();
        var job = store.CreateJob(root, new());
        for (var i = 0; i < 205; i++) store.AddFile(job.Id, new(Path.Combine(root, $"{i:000}.mp4"), 10, 1));
        Assert.Equal(100, store.Page(job.Id, 0).Count); Assert.Equal(5, store.Page(job.Id, 2).Count);
        var first = store.ClaimNext(job.Id)!;
        Assert.Null(store.ClaimNext(job.Id)); Assert.Equal(1, store.Counts(job.Id).Running);
        store.Save(first, null, QueueState.Completed);
        Assert.NotEqual(first.Id, store.ClaimNext(job.Id)!.Id);
    }

    [Fact]
    public async Task WindowsSupervisorKillsProcessTreeOnTimeout()
    {
        var root = EnvironmentPaths.NewDirectory(); var marker = Path.Combine(root, "child.pid");
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var script = Path.Combine(root, "spawn.ps1");
        File.WriteAllText(script, $"$child = Start-Process -FilePath '{shell}' -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30' -PassThru -WindowStyle Hidden\n$child.Id | Set-Content -LiteralPath '{marker}'\nStart-Sleep -Seconds 30\n");
        var runner = new WindowsProcessRunner(Path.Combine(root, "logs"));
        var result = await runner.RunAsync(shell, ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script], 3, 512, default);
        Assert.True(result.TimedOut); Assert.True(File.Exists(marker));
        var id = int.Parse(File.ReadAllText(marker));
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(id));
    }

    [Fact]
    public async Task WindowsSupervisorCancellationIsReportedAndReleasesResources()
    {
        var root = EnvironmentPaths.NewDirectory();
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        using var cancellation = new CancellationTokenSource(500);
        var runner = new WindowsProcessRunner(Path.Combine(root, "logs"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(shell,
            ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"], 30, 512, cancellation.Token));
        var result = await runner.RunAsync(shell, ["-NoProfile", "-Command", "Write-Output 'ready'"], 10, 512, default, captureOutput: true);
        Assert.Equal(0, result.ExitCode); Assert.Contains("ready", result.Output);
    }

    [MediaFact]
    public async Task RealToolsDetectMissingMoovAndLegitimateAbsenceOfAudio()
    {
        var analyzer = Analyzer(EnvironmentPaths.NewDirectory());
        var noAudio = await analyzer.AnalyzeAsync(EnvironmentPaths.Fixture("video_only.mp4"), new(), default);
        Assert.Single(noAudio.Streams); Assert.Contains("legítima", noAudio.Note);
        var broken = await analyzer.AnalyzeAsync(EnvironmentPaths.Fixture("missing_moov.mp4"), new(), default);
        Assert.True(broken.Inconclusive); Assert.Contains("Falha de estrutura", broken.Conclusion);
    }

    [MediaFact]
    public async Task RealSampledDecodeStoresActualOriginalTimestampsIncludingFlvStart()
    {
        var analyzer = Analyzer(EnvironmentPaths.NewDirectory());
        var result = await analyzer.AnalyzeAsync(EnvironmentPaths.Fixture("av.flv"),
            new() { Mode = AnalysisMode.Sampled, Samples = 3, SampleSeconds = 1 }, default);
        Assert.Equal(6, result.Tests.Count); Assert.All(result.Tests, test => Assert.True(test.Frames > 0));
        Assert.Contains(result.Tests, test => test.RequestedStart > 1 && test.ActualStart > 1);
        Assert.All(result.Tests.Where(test => test.RequestedStart >= 7), test => Assert.True(test.ActualEnd >= 7.75));
    }

    [MediaFact]
    public async Task FullDecodeDoesNotTreatZeroExitAsCleanWhenPayloadIsDamaged()
    {
        var analyzer = Analyzer(EnvironmentPaths.NewDirectory()); var path = EnvironmentPaths.Fixture("payload_damaged.mp4");
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var result = await analyzer.AnalyzeAsync(path, new() { Mode = AnalysisMode.Full }, default);
        Assert.Contains(result.Tests, test => test.Warnings > 0);
        Assert.Contains("avisos/erros", result.Conclusion);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [MediaFact]
    public async Task QueuePausePersistsBeforeNextAndResumeSkipsCompleted()
    {
        var root = EnvironmentPaths.NewDirectory(); var input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
        File.Copy(EnvironmentPaths.Fixture("av.mp4"), Path.Combine(input, "a.mp4"));
        File.Copy(EnvironmentPaths.Fixture("av.mp4"), Path.Combine(input, "b.mp4"));
        var store = new QueueStore(Path.Combine(root, "state.db")); store.Initialize(); var coordinator = new QueueCoordinator(store, Analyzer(root));
        var progress = new DirectProgress(p => { if (p.Stage == "Em análise") coordinator.RequestPause(); });
        var job = await coordinator.StartAsync(input, new(), progress, default);
        Assert.Equal(1, store.Counts(job.Id).Completed); Assert.Equal(1, store.Counts(job.Id).Pending);
        var firstResult = store.Detail(store.Page(job.Id, 0)[0].Id);
        await coordinator.ResumeAsync(job, null, default);
        Assert.Equal(2, store.Counts(job.Id).Completed);
        Assert.Equal(firstResult?.ProbeLog, store.Detail(store.Page(job.Id, 0)[0].Id)?.ProbeLog);
    }

    [MediaFact]
    public async Task RemuxCreatesSeparateCandidateAndPreservesInputHierarchy()
    {
        var root = EnvironmentPaths.NewDirectory(); var source = Path.Combine(root, "input", "camera"); Directory.CreateDirectory(source);
        var original = Path.Combine(source, "av.mp4"); File.Copy(EnvironmentPaths.Fixture("av.mp4"), original);
        var before = SHA256.HashData(File.ReadAllBytes(original));
        var tools = Paths(); var runner = new WindowsProcessRunner(Path.Combine(root, "logs"));
        var repair = new RepairService(tools, runner, new(tools, runner));
        var result = await repair.GenerateAsync(Path.Combine(root, "input"), original, Path.Combine(root, "output"), new(), null, default);
        Assert.True(File.Exists(result.Candidate)); Assert.Equal("camera", Path.GetFileName(Path.GetDirectoryName(result.Candidate)));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(original))); Assert.Contains("experimental", result.Status);
    }

    private static ToolPaths Paths() => new(EnvironmentPaths.Probe!, Path.Combine(Path.GetDirectoryName(EnvironmentPaths.Probe)!, "ffmpeg.exe"));

    [Fact]
    public async Task TwoCoordinatorsCannotRunAtTheSameTimeAndGateIsReleased()
    {
        var root = EnvironmentPaths.NewDirectory(); var store = new QueueStore(Path.Combine(root, "state.db")); store.Initialize();
        var dummy = new MediaAnalyzer(new("unused", "unused"), new(root));
        var first = new QueueCoordinator(store, dummy); var second = new QueueCoordinator(store, dummy);
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var operation = first.ExecuteAsync(() => { started.Set(); release.Wait(TimeSpan.FromSeconds(10)); return 1; }, default);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => second.ExecuteAsync(() => 2, default));
        }
        finally { release.Set(); await operation; }
        Assert.Equal(3, await second.ExecuteAsync(() => 3, default));
    }

    [Fact]
    public async Task SupervisorDrainsLargeOutputWithBoundedCapture()
    {
        var root = EnvironmentPaths.NewDirectory();
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var result = await new WindowsProcessRunner(Path.Combine(root, "logs")).RunAsync(shell,
            ["-NoProfile", "-Command", "[Console]::Out.Write(('a' * 3000000))"], 10, 512, default, captureOutput: true);
        Assert.Equal(0, result.ExitCode); Assert.True(result.OutputTruncated); Assert.Equal(2 * 1024 * 1024, result.Output.Length);
    }

    [MediaFact]
    public async Task UntruncWorksOnlyOnCopiesAndKeepsExperimentalClassification()
    {
        var root = EnvironmentPaths.NewDirectory(); var input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
        var source = Path.Combine(input, "broken.mp4"); File.Copy(EnvironmentPaths.Fixture("missing_moov.mp4"), source);
        var original = SHA256.HashData(File.ReadAllBytes(source));
        var paths = Paths() with { Untrunc = Path.Combine(EnvironmentPaths.Root, ".research", "tools", "untrunc", "untrunc_x64", "untrunc.exe") };
        Assert.True(File.Exists(paths.Untrunc));
        var runner = new WindowsProcessRunner(Path.Combine(root, "logs"));
        var result = await new RepairService(paths, runner, new(paths, runner)).GenerateAsync(input, source,
            Path.Combine(root, "output"), new(), EnvironmentPaths.Fixture("reference.mp4"), default);
        Assert.Equal(2, result.Diagnosis.Streams.Count); Assert.Contains("experimental", result.Status);
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(source))); Assert.True(File.Exists(result.Candidate + ".nexovideo.json"));
    }

    private static MediaAnalyzer Analyzer(string root) => new(Paths(), new(Path.Combine(root, "logs")));
    private sealed class DirectProgress(Action<QueueProgress> report) : IProgress<QueueProgress> { public void Report(QueueProgress value) => report(value); }
}
