using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexoVideo.Core;
using Xunit;

namespace NexoVideo.Tests;

public sealed class ReportsScreenTests
{
    [Fact]
    public async Task ReportsShowsBatchExportWithoutPerVideoReviewOrComparisonControls()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            // Não executar o startup do produto nem acessar os dados reais do usuário.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var theme in new[] { "Brand", "Dark", "Controls", "Icons" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/NexoVideo;component/Themes/{theme}.xaml", UriKind.Relative) });
            app.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    var root = EnvironmentPaths.NewDirectory(); var store = new QueueStore(Path.Combine(root, "nexovideo.db")); store.Initialize();
                    var job = store.CreateJob(root, new());
                    for (var i = 0; i < 205; i++) store.AddFile(job.Id, new(Path.Combine(root, $"{i:000}.mp4"), 10, 1));
                    var window = new NexoVideo.App.MainWindow(root); await window.InitializeAsync(); window.SelectPageForRender(2);
                    var content = (FrameworkElement)window.Content;
                    content.Measure(new Size(1440, 1000)); content.Arrange(new Rect(0, 0, 1440, 1000)); content.UpdateLayout();
                    foreach (var name in new[] { "ReviewGrid", "SaveReviewButton", "ReviewerBox", "PlayerBox", "HumanVerdictBox", "SatisfactionBox", "WholeFileCheck", "AdvanceReviewCheck", "ComparisonText" })
                        Assert.Null(window.FindName(name));
                    var pages = (TabControl)window.FindName("Pages"); var report = (DependencyObject)((TabItem)pages.Items[2]).Content;
                    var buttons = Descendants(report).OfType<Button>().ToArray();
                    Assert.Equal("Exportar relatório completo JSON", Assert.Single(buttons).Content);
                    Assert.Empty(Descendants(report).OfType<DataGrid>());
                    Assert.Contains("205", ((TextBlock)window.FindName("ReportSummary")).Text);
                    var bitmap = new RenderTargetBitmap(1440, 1000, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var output = File.Create(Path.Combine(EnvironmentPaths.Root, ".research", "reports-without-manual-ui.png"))) encoder.Save(output);
                    window.Close(); completed.SetResult();
                }
                catch (Exception ex) { completed.SetException(ex); }
                finally { app.Shutdown(); }
            }));
            app.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
