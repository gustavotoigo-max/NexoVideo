using System.Net;
using NexoVideo.Core;
using Xunit;

namespace NexoVideo.Tests;

public sealed class ToolArchiveFactAttribute : FactAttribute
{
    public ToolArchiveFactAttribute()
    { if (!File.Exists(DistributionTests.Archive)) Skip = "Pacote oficial de FFmpeg da pesquisa não disponível."; }
}

public sealed class DistributionTests
{
    public static string Archive => Path.Combine(EnvironmentPaths.Root, ".research", "tools", "ffmpeg-mirror.zip");

    [ToolArchiveFact]
    public async Task OriginalArchiveImportsOfflineAndProvidesRunnableToolsAndLicense()
    {
        var data = EnvironmentPaths.NewDirectory(); using var client = new HttpClient(new RefuseNetwork());
        var tools = await new ToolInstaller(client).InstallAsync(data, Archive, null, default);
        Assert.Equal(tools, ToolInstaller.Find(data, Path.Combine(data, "unused")));
        Assert.True(File.Exists(Path.Combine(data, "tools", "ffmpeg-9.0.2-essentials_build", "LICENSE")));
        var result = await new WindowsProcessRunner(Path.Combine(data, "logs")).RunAsync(tools.Ffprobe, ["-version"], 15, 512, default, captureOutput: true);
        Assert.Equal(0, result.ExitCode); Assert.Contains("ffprobe version 9.0.2", result.Output);
        Assert.Empty(Directory.GetDirectories(Path.Combine(data, "tools"), ".download-*"));
    }

    [Fact]
    public async Task RejectsWrongSizeAndWrongHashAndCleansStaging()
    {
        var root = EnvironmentPaths.NewDirectory(); var archive = Path.Combine(root, "wrong.zip");
        File.WriteAllText(archive, "invalid archive"); using var client = new HttpClient(new RefuseNetwork());
        var installer = new ToolInstaller(client); var data = Path.Combine(root, "data");
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(data, archive, null, default));
        using (var file = File.Open(archive, FileMode.Open, FileAccess.Write)) file.SetLength(ToolInstaller.ArchiveBytes);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(data, archive, null, default));
        Assert.Contains("SHA-256", error.Message);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(data, "tools")));
    }

    [Fact]
    public async Task CancellationAndExistingInstallationDoNotActivatePartialOrOverwriteFiles()
    {
        var root = EnvironmentPaths.NewDirectory(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var client = new HttpClient(new RefuseNetwork()); var installer = new ToolInstaller(client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(root, null, null, cancellation.Token));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "tools")));
        var folder = Path.Combine(root, "tools", "ffmpeg-9.0.2-essentials_build"); Directory.CreateDirectory(folder);
        var sentinel = Path.Combine(folder, "keep.txt"); File.WriteAllText(sentinel, "preserve");
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(root, null, null, default));
        Assert.Equal("preserve", File.ReadAllText(sentinel));
    }

    private sealed class RefuseNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
