using System.IO.Compression;
using System.Security.Cryptography;

namespace NexoVideo.Core;

/// <summary>Obtém o pacote original do fornecedor, com identidade fixa e instalação atômica por usuário.</summary>
public sealed class ToolInstaller(HttpClient client)
{
    public const string Version = "9.0.2";
    public const string DownloadUrl = "https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip";
    public const string ArchiveSha256 = "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba";
    public const long ArchiveBytes = 114768076;
    private const string PackageFolder = "ffmpeg-9.0.2-essentials_build";

    public static ToolPaths? Find(string dataDirectory, string appDirectory)
    {
        foreach (var bin in new[] { Path.Combine(dataDirectory, "tools", PackageFolder, "bin"), Path.Combine(appDirectory, "tools", "ffmpeg", "bin") })
            if (File.Exists(Path.Combine(bin, "ffprobe.exe")) && File.Exists(Path.Combine(bin, "ffmpeg.exe")))
                return new(Path.Combine(bin, "ffprobe.exe"), Path.Combine(bin, "ffmpeg.exe"));
        return null;
    }

    public async Task<ToolPaths> InstallAsync(string dataDirectory, string? localArchive,
        IProgress<string>? progress, CancellationToken cancellation)
    {
        var root = Path.GetFullPath(Path.Combine(dataDirectory, "tools")); Directory.CreateDirectory(root);
        var staging = Path.Combine(root, ".download-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, PackageFolder);
        // Todos os alvos de movimentação/limpeza precisam permanecer dentro desta pasta de ferramentas.
        void Check(string path)
        {
            if (!Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Destino fora da pasta de ferramentas.");
        }
        Check(staging); Check(destination);
        if (Directory.Exists(destination))
            throw new IOException("O pacote desta versão já existe. Configure os executáveis ou remova manualmente apenas essa pasta para reinstalar: " + destination);
        Directory.CreateDirectory(staging);
        try
        {
            var archive = Path.Combine(staging, "package.zip");
            await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                async Task Copy(Stream input)
                {
                    var buffer = new byte[128 * 1024]; long received = 0; var last = -1;
                    int count;
                    while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
                    {
                        received += count;
                        if (received > ArchiveBytes) throw new InvalidDataException("Pacote maior que o tamanho esperado.");
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
                        var percent = (int)(100 * received / ArchiveBytes);
                        if (percent != last) { last = percent; progress?.Report($"Obtendo FFmpeg {Version}: {percent}%"); }
                    }
                    if (received != ArchiveBytes) throw new InvalidDataException("Download incompleto ou pacote diferente do esperado.");
                }
                if (localArchive is not null)
                {
                    await using var input = new FileStream(localArchive, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
                    await Copy(input);
                }
                else
                {
                    using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
                    response.EnsureSuccessStatusCode();
                    await using var input = await response.Content.ReadAsStreamAsync(cancellation); await Copy(input);
                }
            }
            progress?.Report("Verificando SHA-256 do pacote original...");
            await using (var input = File.OpenRead(archive))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation));
                if (!hash.Equals(ArchiveSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 divergente. Pacote recusado.");
            }
            cancellation.ThrowIfCancellationRequested(); progress?.Report("Extraindo ferramentas e licenças do fornecedor...");
            var extracted = Path.Combine(staging, "extracted");
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, extracted), cancellation);
            cancellation.ThrowIfCancellationRequested();
            var source = Path.Combine(extracted, PackageFolder); Check(source);
            var tools = new ToolPaths(Path.Combine(source, "bin", "ffprobe.exe"), Path.Combine(source, "bin", "ffmpeg.exe")); tools.Validate();
            if (!File.Exists(Path.Combine(source, "LICENSE"))) throw new InvalidDataException("Pacote sem licença do fornecedor.");
            Check(source); Check(destination); Directory.Move(source, destination);
            return new(Path.Combine(destination, "bin", "ffprobe.exe"), Path.Combine(destination, "bin", "ffmpeg.exe"));
        }
        finally { Check(staging); if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }
}
