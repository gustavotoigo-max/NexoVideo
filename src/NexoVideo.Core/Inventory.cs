namespace NexoVideo.Core;

public static class Inventory
{
    public static bool IsInside(string child, string parent)
    {
        var normalized = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(child);
        return candidate.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar), normalized.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<InventoryFile> Enumerate(string source, AnalysisOptions options, Action<string>? warning,
        CancellationToken cancellation)
    {
        source = Path.GetFullPath(source);
        var root = new DirectoryInfo(source);
        if (!root.Exists) throw new DirectoryNotFoundException(source);
        if (root.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("A pasta de origem não pode ser junction ou link simbólico nesta versão.");
        if (options.ExcludedDirectory is { Length: > 0 } output && IsInside(source, output))
            throw new ArgumentException("A origem não pode estar dentro da pasta excluída de saída.");
        var extensions = new HashSet<string>(options.Extensions, StringComparer.OrdinalIgnoreCase);
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = false, IgnoreInaccessible = false, ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
        };
        foreach (var entry in Walk(source, 0)) yield return entry;

        IEnumerable<InventoryFile> Walk(string directory, int depth)
        {
            cancellation.ThrowIfCancellationRequested();
            if (options.ExcludedDirectory is { Length: > 0 } excluded && IsInside(directory, excluded)) yield break;
            if (depth > 256) { warning?.Invoke($"Profundidade de pastas excedida: {directory}"); yield break; }
            // Enumeração em streaming: a ordenação global será feita pelo índice do SQLite.
            using var entries = Directory.EnumerateFileSystemEntries(directory, "*", enumeration).GetEnumerator();
            while (true)
            {
                string path;
                try { if (!entries.MoveNext()) break; path = entries.Current; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                { warning?.Invoke($"Não foi possível listar {directory}: {ex.Message}"); break; }
                cancellation.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                { warning?.Invoke($"Não foi possível inspecionar {path}: {ex.Message}"); continue; }
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (options.Recursive) foreach (var child in Walk(path, depth + 1)) yield return child;
                    continue;
                }
                if (Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal) || !extensions.Contains(Path.GetExtension(path))) continue;
                InventoryFile? entry = null;
                try { var info = new FileInfo(path); entry = new(path, info.Length, info.LastWriteTimeUtc.Ticks); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                { warning?.Invoke($"Arquivo inacessível: {path}: {ex.Message}"); }
                if (entry is not null) yield return entry;
            }
        }
    }
}
