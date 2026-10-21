using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexoVideo.Core;
using Xunit;

namespace NexoVideo.Tests;

public sealed class PlayerReviewTests
{
    [Fact]
    public async Task OpenSaveAndAdvanceKeepEachReviewBoundToItsFileAcrossPages()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            // Aplicação WPF isolada: não executar OnStartup do produto nem abrir o banco real do usuário.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var theme in new[] { "Brand", "Dark", "Controls", "Icons" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/NexoVideo;component/Themes/{theme}.xaml", UriKind.Relative) });
            app.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Scenario(); completed.SetResult(); }
                catch (Exception ex) { completed.SetException(ex); }
                finally { app.Shutdown(); }
            }));
            app.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }

    private static async Task Scenario()
    {
        var root = EnvironmentPaths.NewDirectory(); var store = new QueueStore(Path.Combine(root, "nexovideo.db")); store.Initialize();
        var job = store.CreateJob(root, new() { Mode = AnalysisMode.Full });
        for (var i = 1; i <= 101; i++)
        {
            var path = Path.Combine(root, $"{i:000} vídeo & espaços.mp4"); File.WriteAllText(path, "review fixture " + i);
            var file = new FileInfo(path); store.AddFile(job.Id, new(path, file.Length, file.LastWriteTimeUtc.Ticks));
        }
        QueueItem? claimed;
        while ((claimed = store.ClaimNext(job.Id)) is not null)
        {
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(claimed.Path)));
            var evidence = new AnalysisEvidence(Guid.NewGuid().ToString("N"), sha, claimed.Size, claimed.ModifiedTicks,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "test", "Windows", "", job.Options, "Completed", []);
            store.Save(claimed, new(claimed.Path, AnalysisMode.Full, "test", "mp4", 1, [], [], "", "", "test", false, evidence), QueueState.Completed);
        }
        var opened = new List<string>(); var window = new NexoVideo.App.MainWindow(root, opened.Add);
        await window.InitializeAsync(); window.SelectPageForRender(2);
        T Find<T>(string name) => (T)window.FindName(name);
        var grid = Find<DataGrid>("ReviewGrid"); var button = Find<Button>("SaveReviewButton");
        void Layout()
        {
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1440, 1400)); content.Arrange(new Rect(0, 0, 1440, 1400)); content.UpdateLayout();
        }
        void DoubleClick(DataGrid target, object source) => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Control.MouseDoubleClickEvent, Source = source });
        async Task Select(int index)
        {
            grid.SelectedIndex = index; grid.ScrollIntoView(grid.SelectedItem); Layout();
            await Wait(() => !Find<TextBlock>("ReviewStatus").Text.StartsWith("Carregando"));
        }
        void Fill()
        {
            Find<ComboBox>("HumanVerdictBox").SelectedIndex = (int)HumanVerdict.Problem;
            Find<ComboBox>("SatisfactionBox").SelectedIndex = (int)Satisfaction.Partial;
            Find<CheckBox>("WholeFileCheck").IsChecked = true;
            Find<TextBox>("ReviewerBox").Text = "AUTOMATED UI TEST";
            Find<TextBox>("PlayerBox").Text = "Injected launcher; no real playback";
            Find<TextBox>("ReviewNotesBox").Text = "Synthetic review at 00:01";
        }
        await Select(0);
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap(1440, 1400, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(EnvironmentPaths.Root, ".research", "player-review-ui.png"))) encoder.Save(output);
        var first = (NexoVideo.App.MainWindow.FileRow)grid.SelectedItem;
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        DoubleClick(grid, row);
        await Wait(() => opened.Count == 1);
        Assert.Equal(first.Item.Path, opened[0]); Assert.Empty(store.ReviewHistory(first.Id));
        DoubleClick(grid, grid); // Cabeçalho/espaço sem linha não abre a seleção anterior.
        await Task.Delay(50); Assert.Single(opened);
        Fill(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(() => button.IsEnabled && grid.SelectedIndex == 1 && store.ReviewHistory(first.Id).Count == 1);
        var saved = Assert.Single(store.ReviewHistory(first.Id));
        Assert.Equal(store.Detail(first.Id)!.Evidence!.Sha256, saved.Sha256);
        Assert.Equal(HumanVerdict.Problem, saved.Verdict); Assert.True(saved.WatchedWholeFile);
        Assert.Equal(0, Find<ComboBox>("HumanVerdictBox").SelectedIndex);
        Assert.False(Find<CheckBox>("WholeFileCheck").IsChecked); Assert.Empty(Find<TextBox>("ReviewNotesBox").Text);
        Assert.Single(opened); // Selecionar próximo não abre outro player nem presume reprodução.

        await Select(99); var lastOnPage = (NexoVideo.App.MainWindow.FileRow)grid.SelectedItem;
        Fill(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(() => button.IsEnabled && grid.Items.Count == 1 && grid.SelectedIndex == 0);
        Assert.Single(store.ReviewHistory(lastOnPage.Id));
        var last = (NexoVideo.App.MainWindow.FileRow)grid.SelectedItem; Assert.StartsWith("101", last.Name);
        Assert.Empty(store.ReviewHistory(last.Id)); Assert.Empty(Find<TextBox>("ReviewNotesBox").Text);
        window.SelectPageForRender(0); Layout();
        var results = Find<DataGrid>("ResultsGrid");
        var analysisRow = Assert.IsType<DataGridRow>(results.ItemContainerGenerator.ContainerFromIndex(0));
        DoubleClick(results, analysisRow);
        await Wait(() => opened.Count == 2);
        Assert.Equal(last.Item.Path, opened[1]);
        Assert.Equal(2, Find<TabControl>("Pages").SelectedIndex);
        Find<CheckBox>("AdvanceReviewCheck").IsChecked = false;
        Fill(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(() => button.IsEnabled && store.ReviewHistory(last.Id).Count == 1);
        Assert.Equal(last.Id, ((NexoVideo.App.MainWindow.FileRow)grid.SelectedItem).Id);
        Assert.Equal(2, opened.Count);
        window.Close();
    }

    private static async Task Wait(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        { if (DateTime.UtcNow > until) throw new TimeoutException("A interface não concluiu a operação esperada."); await Task.Delay(20); }
    }
}
