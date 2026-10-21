using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NexoVideo.App;

public partial class App : Application
{
    private Mutex? _instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\NexoVideo.Desktop", out var first);
        if (!first) { MessageBox.Show("O NexoVideo já está aberto."); Shutdown(); return; }
        string? Option(string option) { var index = Array.IndexOf(e.Args, option); return index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null; }
        var data = Path.GetFullPath(Option("--data") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoVideo"));
        try
        {
            var window = new MainWindow(data); MainWindow = window;
            await window.InitializeAsync();
            if (Option("--render-ui") is { } renderPath)
            {
                window.SelectPageForRender(int.TryParse(Option("--render-page"), out var page) ? page : 0);
                window.Width = int.TryParse(Option("--render-width"), out var renderWidth) ? Math.Max(window.MinWidth, renderWidth) : 1440;
                window.Height = int.TryParse(Option("--render-height"), out var renderHeight) ? Math.Max(window.MinHeight, renderHeight) : 1000;
                window.Show();
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var visual = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(renderPath))!);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(renderPath)) encoder.Save(output);
                VisualAudit.Write(window, visual, renderPath + ".json");
                Shutdown();
            }
            else window.Show();
        }
        catch (Exception ex)
        {
            if (Option("--render-ui") is { } render) File.WriteAllText(render + ".error", ex.ToString());
            else MessageBox.Show(ex.Message, "Não foi possível abrir o NexoVideo");
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
}
