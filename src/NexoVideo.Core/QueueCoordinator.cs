using System.Text;
using System.Text.Json;

namespace NexoVideo.Core;

public sealed class QueueCoordinator(QueueStore store, MediaAnalyzer analyzer)
{
    private int _active, _pause;
    public void RequestPause() => Interlocked.Exchange(ref _pause, 1);

    public Task<Job> StartAsync(string source, AnalysisOptions options, IProgress<QueueProgress>? progress, CancellationToken cancellation)
        => ExecuteAsync(() =>
        {
            options.Validate();
            var job = store.CreateJob(source, options);
            foreach (var file in Inventory.Enumerate(source, options, message => store.Event(job.Id, message), cancellation))
                store.AddFile(job.Id, file);
            store.Event(job.Id, "Inventário concluído. Exclusões: auxiliares, ocultos, sistema e links.");
            Run(job, progress, cancellation);
            return job;
        }, cancellation);

    public Task<Job> ResumeAsync(Job job, IProgress<QueueProgress>? progress, CancellationToken cancellation)
        => ExecuteAsync(() =>
        {
            store.PrepareResume(job.Id);
            Run(job, progress, cancellation);
            return job;
        }, cancellation);

    public Task<T> ExecuteAsync<T>(Func<T> work, CancellationToken cancellation)
    {
        if (Interlocked.Exchange(ref _active, 1) != 0) throw new InvalidOperationException("Já existe uma operação ativa.");
        return Task.Run(() =>
        {
            // Mutex pertence a este thread até o fim. Operações async internas são aguardadas aqui.
            using var gate = new Mutex(false, @"Local\NexoVideo.Analysis.SingleWorker");
            var acquired = false;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                try { acquired = gate.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new InvalidOperationException("Outra instância do NexoVideo está analisando/reparando um arquivo.");
                Interlocked.Exchange(ref _pause, 0);
                store.RecoverInterrupted();
                return work();
            }
            finally { if (acquired) gate.ReleaseMutex(); Interlocked.Exchange(ref _active, 0); }
        }, CancellationToken.None);
    }

    private void Run(Job job, IProgress<QueueProgress>? progress, CancellationToken cancellation)
    {
        while (Volatile.Read(ref _pause) == 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var item = store.ClaimNext(job.Id);
            if (item is null) break;
            progress?.Report(new(job.Id, item.Path, "Em análise", store.Counts(job.Id)));
            try
            {
                var result = analyzer.AnalyzeAsync(item.Path, job.Options, cancellation, (stage, fraction) =>
                    progress?.Report(new(job.Id, item.Path, stage, store.Counts(job.Id), fraction))).GetAwaiter().GetResult();
                var after = new FileInfo(item.Path);
                if (!after.Exists || after.Length != item.Size || after.LastWriteTimeUtc.Ticks != item.ModifiedTicks)
                    result = result with { Conclusion = "Arquivo alterado durante a análise", Inconclusive = true,
                        Note = result.Note + " O tamanho/data mudou; repetir sobre uma cópia estável.",
                        Evidence = result.Evidence is null ? null : result.Evidence with { Termination = "SourceChanged" } };
                // Falha de banco NÃO é falha de mídia: precisa interromper a fila.
                store.Save(item, result, QueueState.Completed);
            }
            catch (AnalysisCancelledException ex)
            {
                store.Save(item, ex.Result, QueueState.Cancelled);
                throw;
            }
            catch (AnalysisFailedException ex)
            {
                store.Save(item, ex.Result, QueueState.Failed);
            }
            catch (OperationCanceledException)
            {
                store.Save(item, null, QueueState.Cancelled, "Cancelado pelo usuário; recursos do subprocesso liberados.");
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException)
            {
                store.Save(item, null, QueueState.Failed, ex.Message);
            }
            progress?.Report(new(job.Id, item.Path, "Resultado persistido; recursos liberados", store.Counts(job.Id), 1));
        }
        progress?.Report(new(job.Id, "", Volatile.Read(ref _pause) != 0 ? "Fila pausada entre arquivos" : "Fila concluída", store.Counts(job.Id)));
    }

    public void ExportJson(long jobId, string destination)
    {
        new ValidationService(store).ExportJson(jobId, destination);
    }
}
