using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NexoVideo.Core;

public sealed class QueueStore
{
    public string DatabasePath { get; }
    private readonly string _connectionString;
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public QueueStore(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false, DefaultTimeout = 10 }.ToString();
    }

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL;";
        command.ExecuteNonQuery();
        return connection;
    }

    public string Initialize()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        var version = (string)command.ExecuteScalar()!;
        if (Version.Parse(version) < new Version(3, 51, 3))
            throw new NotSupportedException($"SQLite {version}: é necessário 3.51.3 ou superior para habilitar WAL com a correção verificada.");
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS jobs(
                id INTEGER PRIMARY KEY, source TEXT NOT NULL, options TEXT NOT NULL, created TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS files(
                id INTEGER PRIMARY KEY, job_id INTEGER NOT NULL REFERENCES jobs(id), path TEXT NOT NULL COLLATE NOCASE,
                size INTEGER NOT NULL, modified INTEGER NOT NULL, state TEXT NOT NULL,
                conclusion TEXT NOT NULL DEFAULT '', note TEXT NOT NULL DEFAULT '', result TEXT,
                UNIQUE(job_id,path));
            CREATE INDEX IF NOT EXISTS ix_files_queue ON files(job_id,state,path);
            CREATE TABLE IF NOT EXISTS analysis_history(
                id INTEGER PRIMARY KEY, file_id INTEGER NOT NULL REFERENCES files(id), result TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_history_file ON analysis_history(file_id,id);
            CREATE TABLE IF NOT EXISTS reviews(
                id INTEGER PRIMARY KEY, file_id INTEGER NOT NULL REFERENCES files(id), review TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_reviews_file ON reviews(file_id,id);
            CREATE TABLE IF NOT EXISTS events(
                id INTEGER PRIMARY KEY, job_id INTEGER NOT NULL REFERENCES jobs(id), message TEXT NOT NULL, created TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        return version;
    }

    public QueueStore Snapshot(string destination)
    {
        using var source = Connect();
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        target.Open(); source.BackupDatabase(target);
        return new QueueStore(destination);
    }

    // Chamado somente depois de adquirir a exclusão mútua entre processos.
    public void RecoverInterrupted()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE files SET state='Pending',note='Execução anterior interrompida; aguarda retomada.' WHERE state='Running';";
        command.ExecuteNonQuery();
    }

    public Job CreateJob(string source, AnalysisOptions options)
    {
        options.Validate();
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO jobs(source,options,created) VALUES($source,$options,$created); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$source", Path.GetFullPath(source));
        command.Parameters.AddWithValue("$options", JsonSerializer.Serialize(options, Json));
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        return new((long)command.ExecuteScalar()!, Path.GetFullPath(source), options);
    }

    public Job? LatestJob()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,source,options FROM jobs ORDER BY id DESC LIMIT 1;";
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetInt64(0), reader.GetString(1), JsonSerializer.Deserialize<AnalysisOptions>(reader.GetString(2), Json)!) : null;
    }

    public Job GetJob(long id)
    {
        using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,source,options FROM jobs WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id); using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetInt64(0), reader.GetString(1), JsonSerializer.Deserialize<AnalysisOptions>(reader.GetString(2), Json)!)
            : throw new ArgumentException("Sessão não encontrada.");
    }

    public IReadOnlyList<ManualReview> ReviewHistory(long fileId)
    {
        using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT review FROM reviews WHERE file_id=$id ORDER BY id;";
        command.Parameters.AddWithValue("$id", fileId); using var reader = command.ExecuteReader();
        var reviews = new List<ManualReview>();
        while (reader.Read()) reviews.Add(JsonSerializer.Deserialize<ManualReview>(reader.GetString(0), Json)!);
        return reviews;
    }

    public void SaveReview(long fileId, ManualReview review)
    {
        if (!Enum.IsDefined(review.Verdict) || !Enum.IsDefined(review.Satisfaction) || review.Notes.Length > 8000
            || review.Player.Length > 200 || string.IsNullOrWhiteSpace(review.Reviewer) || review.Reviewer.Length > 200)
            throw new ArgumentException("Informe avaliador (até 200 caracteres), avaliação válida e notas de até 8.000 caracteres.");
        if (review.CouldNotOpen && (review.WatchedWholeFile || review.Verdict != HumanVerdict.Problem))
            throw new ArgumentException("Impossibilidade de abrir exige Problema observado e não pode ser marcada junto com reprodução inteira.");
        using var connection = Connect(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT result FROM files WHERE id=$id AND state IN ('Completed','Failed','Cancelled');";
        command.Parameters.AddWithValue("$id", fileId);
        var evidence = command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<AnalysisResult>(json, Json)?.Evidence : null;
        if (evidence is null || evidence.AnalysisId != review.AnalysisId || evidence.Sha256 != review.Sha256 || review.Sha256.Length != 64)
            throw new InvalidOperationException("A avaliação não corresponde à análise atual. Reanalise/atualize a seleção.");
        command.CommandText = "INSERT INTO reviews(file_id,review) VALUES($id,$review);";
        command.Parameters.AddWithValue("$review", JsonSerializer.Serialize(review, Json)); command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void WriteEvents(System.Text.Json.Utf8JsonWriter writer, long jobId)
    {
        using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,message,created FROM events WHERE job_id=$job ORDER BY id;";
        command.Parameters.AddWithValue("$job", jobId); using var reader = command.ExecuteReader();
        writer.WriteStartArray();
        while (reader.Read())
        {
            writer.WriteStartObject(); writer.WriteNumber("id", reader.GetInt64(0));
            writer.WriteString("message", reader.GetString(1)); writer.WriteString("created_utc", reader.GetString(2)); writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    public void AddFile(long jobId, InventoryFile file)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO files(job_id,path,size,modified,state) VALUES($job,$path,$size,$modified,'Pending');";
        command.Parameters.AddWithValue("$job", jobId); command.Parameters.AddWithValue("$path", file.Path);
        command.Parameters.AddWithValue("$size", file.Size); command.Parameters.AddWithValue("$modified", file.ModifiedTicks);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<QueueItem> Page(long jobId, int page, int pageSize = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        if (pageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(pageSize));
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,job_id,path,size,modified,state,conclusion,note FROM files WHERE job_id=$job ORDER BY path LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$job", jobId); command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (long)page * pageSize);
        using var reader = command.ExecuteReader();
        var result = new List<QueueItem>(pageSize);
        while (reader.Read()) result.Add(ReadItem(reader));
        return result;
    }

    private static QueueItem ReadItem(SqliteDataReader reader) => new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
        reader.GetInt64(3), reader.GetInt64(4), Enum.Parse<QueueState>(reader.GetString(5)), reader.GetString(6), reader.GetString(7));

    public QueueItem? ClaimNext(long jobId)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            UPDATE files SET state='Running',conclusion='',note='' WHERE id=(
                SELECT id FROM files WHERE job_id=$job AND state='Pending'
                AND NOT EXISTS(SELECT 1 FROM files WHERE state='Running') ORDER BY path LIMIT 1)
            RETURNING id,job_id,path,size,modified,state,conclusion,note;
            """;
        command.Parameters.AddWithValue("$job", jobId);
        QueueItem? item;
        using (var reader = command.ExecuteReader()) item = reader.Read() ? ReadItem(reader) : null;
        transaction.Commit();
        return item;
    }

    public void Save(QueueItem item, AnalysisResult? result, QueueState state, string note = "")
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE files SET state=$state,conclusion=$conclusion,note=$note,result=$result WHERE id=$id;";
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$conclusion", result?.Conclusion ?? "");
        command.Parameters.AddWithValue("$note", result?.Note ?? note);
        command.Parameters.AddWithValue("$result", result is null ? DBNull.Value : JsonSerializer.Serialize(result, Json));
        command.Parameters.AddWithValue("$id", item.Id); command.ExecuteNonQuery();
        if (result is not null)
        {
            command.CommandText = "INSERT INTO analysis_history(file_id,result) VALUES($id,$result);";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyDictionary<string, string> WriteAnalysisHistory(Utf8JsonWriter writer, long fileId, JsonSerializerOptions options)
    {
        using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT result FROM analysis_history WHERE file_id=$id ORDER BY id;";
        command.Parameters.AddWithValue("$id", fileId); using var reader = command.ExecuteReader();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        writer.WriteStartArray();
        while (reader.Read())
        {
            var result = JsonSerializer.Deserialize<AnalysisResult>(reader.GetString(0), Json)!;
            JsonSerializer.Serialize(writer, result, options);
            foreach (var path in (result.Evidence?.Executions.Select(e => e.LogPath) ?? []).Concat(result.Tests.Select(t => t.LogPath)).Append(result.ProbeLog))
                if (!string.IsNullOrEmpty(path)) paths.TryAdd(path, "");
            foreach (var run in result.Evidence?.Executions ?? []) paths[run.LogPath] = run.LogSha256;
        }
        writer.WriteEndArray();
        return paths;
    }

    public AnalysisResult? Detail(long id)
    {
        using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT result FROM files WHERE id=$id;"; command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<AnalysisResult>(json, Json) : null;
    }

    public QueueCounts Counts(long jobId)
    {
        using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT state,COUNT(*) FROM files WHERE job_id=$job GROUP BY state;";
        command.Parameters.AddWithValue("$job", jobId);
        using var reader = command.ExecuteReader(); var counts = new Dictionary<string, long>();
        while (reader.Read()) counts[reader.GetString(0)] = reader.GetInt64(1);
        return new(counts.Values.Sum(), counts.GetValueOrDefault("Pending"), counts.GetValueOrDefault("Running"),
            counts.GetValueOrDefault("Completed"), counts.GetValueOrDefault("Failed"), counts.GetValueOrDefault("Cancelled"));
    }

    public void PrepareResume(long jobId)
    {
        // Valida metadados de cada registro, sem carregar todo o inventário na memória.
        for (var page = 0; ; page++)
        {
            var items = Page(jobId, page);
            if (items.Count == 0) break;
            foreach (var item in items)
            {
                var info = new FileInfo(item.Path);
                if (!info.Exists) { Save(item, null, QueueState.Failed, "Arquivo não está mais disponível."); continue; }
                if (item.State is QueueState.Cancelled or QueueState.Running || info.Length != item.Size || info.LastWriteTimeUtc.Ticks != item.ModifiedTicks)
                {
                    using var connection = Connect(); using var command = connection.CreateCommand();
                    command.CommandText = "UPDATE files SET state='Pending',size=$size,modified=$modified,result=NULL,conclusion='',note='Revalidar arquivo.' WHERE id=$id;";
                    command.Parameters.AddWithValue("$size", info.Length); command.Parameters.AddWithValue("$modified", info.LastWriteTimeUtc.Ticks);
                    command.Parameters.AddWithValue("$id", item.Id); command.ExecuteNonQuery();
                }
            }
        }
    }

    public void Event(long jobId, string message)
    {
        using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO events(job_id,message,created) VALUES($job,$message,$created);";
        command.Parameters.AddWithValue("$job", jobId); command.Parameters.AddWithValue("$message", message[..Math.Min(message.Length, 4000)]);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O")); command.ExecuteNonQuery();
    }
}
