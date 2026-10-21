using System.Text.Json;
using NexoVideo.Core;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("NexoVideo dev — analisar --source PASTA --ffprobe EXE --ffmpeg EXE [--mode Quick|Sampled|Full] [--data PASTA] [--extensions .mp4,.mov] [--resume]");
    return 0;
}
string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
try
{
    var data = Path.GetFullPath(Option("--data") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoVideo"));
    var paths = new ToolPaths(Option("--ffprobe") ?? "", Option("--ffmpeg") ?? "", Option("--untrunc")); paths.Validate();
    var store = new QueueStore(Path.Combine(data, "nexovideo.db"));
    Console.WriteLine("SQLite " + store.Initialize());
    var analyzer = new MediaAnalyzer(paths, new WindowsProcessRunner(Path.Combine(data, "logs")));
    var coordinator = new QueueCoordinator(store, analyzer);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var progress = new Progress<QueueProgress>(p => Console.WriteLine($"{p.Stage}: {p.Counts.Completed + p.Counts.Failed}/{p.Counts.Total} {Path.GetFileName(p.Path)}"));
    Job job;
    if (args.Contains("--resume")) job = await coordinator.ResumeAsync(store.LatestJob() ?? throw new InvalidOperationException("Não há fila anterior."), progress, cancellation.Token);
    else job = await coordinator.StartAsync(Option("--source") ?? throw new ArgumentException("Informe --source."), new AnalysisOptions
    {
        Mode = Enum.Parse<AnalysisMode>(Option("--mode") ?? "Quick", true),
        Extensions = Option("--extensions")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? new AnalysisOptions().Extensions,
        TimeoutSeconds = int.Parse(Option("--timeout") ?? "300")
    }, progress, cancellation.Token);
    var report = Path.Combine(data, $"report-{job.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
    await Task.Run(() => coordinator.ExportJson(job.Id, report));
    Console.WriteLine(JsonSerializer.Serialize(new { counts = store.Counts(job.Id), report }));
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Fila cancelada; pode ser retomada."); return 2; }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
