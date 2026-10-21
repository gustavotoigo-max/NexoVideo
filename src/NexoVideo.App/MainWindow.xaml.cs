using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using NexoVideo.Core;

namespace NexoVideo.App;

public partial class MainWindow : Window
{
    private readonly string _data;
    private readonly QueueStore _store;
    private readonly DispatcherTimer _timer;
    private Job? _job;
    private QueueCoordinator? _coordinator;
    private CancellationTokenSource? _cancellation;
    private Task? _activeTask;
    private int _page;
    private bool _busy, _refreshRequested, _refreshBusy, _closeReady;
    private long _detailRequest, _reviewRequest;
    private (long FileId, AnalysisEvidence Evidence)? _reviewTarget;
    private readonly Dictionary<string, CheckBox> _formats = new(StringComparer.OrdinalIgnoreCase);
    private string SettingsFile => Path.Combine(_data, "settings.json");
    public void SelectPageForRender(int page) => SelectPage(Math.Clamp(page, 0, 3));
    private void SelectPage(int page)
    {
        Pages.SelectedIndex = page;
        HeaderLocation.Text = new[] { "Análise de vídeos", "Recuperação", "Relatórios", "Configurações" }[page];
        var navigation = new[] { Nav0, Nav1, Nav2, Nav3 };
        for (var i = 0; i < navigation.Length; i++)
        {
            navigation[i].SetResourceReference(Control.ForegroundProperty, i == page ? "Nx.OnNavy" : "Nx.OnNavyMuted");
            navigation[i].Background = i == page ? (System.Windows.Media.Brush)FindResource("Nx.Navy3") : System.Windows.Media.Brushes.Transparent;
        }
    }

    public MainWindow(string data)
    {
        InitializeComponent(); _data = data; _store = new(Path.Combine(data, "nexovideo.db"));
        foreach (var extension in Formats.All)
        {
            var control = new CheckBox
            {
                Content = extension.TrimStart('.').ToUpperInvariant() + (Formats.Synthetic.Contains(extension) ? "" : "*"), IsChecked = Formats.Synthetic.Contains(extension),
                ToolTip = Formats.Synthetic.Contains(extension) ? "Identificação e decode executados em controle sintético; reparo não garantido." : "Variante não homologada; análise experimental."
            };
            _formats.Add(extension, control); FormatsPanel.Children.Add(control);
        }
        DataPathText.Text = _data;
        SelectPage(0);
        DetailBox.VerticalContentAlignment = VerticalAlignment.Top;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += async (_, _) => { if (_refreshRequested && !_refreshBusy) { _refreshRequested = false; await RefreshAsync(); } };
        _timer.Start(); Closing += Window_Closing;
    }

    public async Task InitializeAsync()
    {
        var loaded = await Task.Run(() =>
        {
            var sqlite = _store.Initialize();
            SavedSettings? saved = null; string? warning = null;
            try
            {
                if (File.Exists(SettingsFile))
                {
                    saved = JsonSerializer.Deserialize<SavedSettings>(File.ReadAllText(SettingsFile), QueueStore.Json);
                    if (saved?.Options is null || saved.Tools is null) throw new JsonException("Configuração incompleta.");
                    saved.Options.Validate();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or ArgumentException)
            { saved = null; warning = "Configurações anteriores inválidas; valores padrão carregados. " + ex.Message; }
            return (sqlite, saved, warning, job: _store.LatestJob());
        });
        if (loaded.saved is { } settings)
        {
            SourceBox.Text = settings.Source; OutputBox.Text = settings.Output;
            ProbeBox.Text = settings.Tools.Ffprobe; MpegBox.Text = settings.Tools.Ffmpeg; UntruncBox.Text = settings.Tools.Untrunc;
            SetOptions(settings.Options);
        }
        else
        {
            // Somente ambiente de desenvolvimento: reutiliza ferramentas já baixadas na pesquisa.
            for (DirectoryInfo? root = new(AppContext.BaseDirectory); root is not null; root = root.Parent)
            {
                var tools = Path.Combine(root.FullName, ".research", "tools");
                if (!Directory.Exists(Path.Combine(tools, "ffmpeg"))) continue;
                ProbeBox.Text = Directory.EnumerateFiles(Path.Combine(tools, "ffmpeg"), "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault() ?? "";
                MpegBox.Text = Path.Combine(Path.GetDirectoryName(ProbeBox.Text) ?? "", "ffmpeg.exe");
                UntruncBox.Text = Path.Combine(tools, "untrunc", "untrunc_x64", "untrunc.exe"); break;
            }
        }
        _job = loaded.job;
        if (_job is not null && string.IsNullOrWhiteSpace(SourceBox.Text)) SourceBox.Text = _job.Source;
        StageText.Text = _job is null ? "Pronto para iniciar" : "Sessão anterior disponível para consulta ou retomada";
        if (loaded.warning is not null) CurrentFile.Text = loaded.warning;
        SetBusy(false);
        await RefreshAsync();
    }

    private void SetOptions(AnalysisOptions options)
    {
        ModeBox.SelectedIndex = (int)options.Mode; SamplingBox.SelectedIndex = (int)options.Sampling;
        SampleCountBox.Text = options.Samples.ToString(); SecondsBox.Text = options.SampleSeconds.ToString(CultureInfo.InvariantCulture);
        MegabytesBox.Text = options.Megabytes.ToString(CultureInfo.InvariantCulture); TimeoutBox.Text = options.TimeoutSeconds.ToString();
        ThreadsBox.Text = options.DecoderThreads.ToString(); MemoryBox.Text = options.MemoryLimitMb.ToString();
        AudioCheck.IsChecked = options.Audio; VideoCheck.IsChecked = options.Video; RecursiveCheck.IsChecked = options.Recursive;
        foreach (var (extension, control) in _formats) control.IsChecked = options.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private AnalysisOptions Options()
    {
        double ReadDouble(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ? number : double.Parse(value, CultureInfo.InvariantCulture);
        var options = new AnalysisOptions
        {
            Mode = (AnalysisMode)ModeBox.SelectedIndex, Sampling = (SamplingMode)SamplingBox.SelectedIndex,
            Samples = int.Parse(SampleCountBox.Text), SampleSeconds = ReadDouble(SecondsBox.Text), Megabytes = ReadDouble(MegabytesBox.Text),
            TimeoutSeconds = int.Parse(TimeoutBox.Text), DecoderThreads = int.Parse(ThreadsBox.Text), MemoryLimitMb = int.Parse(MemoryBox.Text),
            Audio = AudioCheck.IsChecked == true, Video = VideoCheck.IsChecked == true, Recursive = RecursiveCheck.IsChecked == true,
            Extensions = _formats.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray(),
            ExcludedDirectory = string.IsNullOrWhiteSpace(OutputBox.Text) ? null : Path.GetFullPath(OutputBox.Text)
        };
        options.Validate(); return options;
    }
    private ToolPaths Tools() => new(ProbeBox.Text.Trim(), MpegBox.Text.Trim(), string.IsNullOrWhiteSpace(UntruncBox.Text) ? null : UntruncBox.Text.Trim());

    private async Task SaveSettingsAsync()
    {
        var saved = new SavedSettings(SourceBox.Text, OutputBox.Text, Options(), Tools());
        await Task.Run(() =>
        {
            Directory.CreateDirectory(_data);
            var temporary = SettingsFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(saved, QueueStore.Json));
            File.Move(temporary, SettingsFile, overwrite: true);
        });
    }

    private void SetBusy(bool busy)
    {
        _busy = busy; ScanInputs.IsEnabled = SettingsPanel.IsEnabled = ReviewPanel.IsEnabled = !busy;
        StartButton.IsEnabled = !busy; ResumeButton.IsEnabled = !busy && _job is not null;
        PauseButton.IsEnabled = CancelButton.IsEnabled = busy;
        RemuxButton.IsEnabled = UntruncButton.IsEnabled = SaveReviewButton.IsEnabled = !busy;
    }

    private async Task AnalyzeAsync(bool resume)
    {
        if (_busy) return;
        try
        {
            var tools = Tools(); tools.Validate(); var options = Options();
            var source = SourceBox.Text.Trim();
            if (!resume && !Directory.Exists(source)) throw new DirectoryNotFoundException("Escolha uma pasta de origem válida.");
            await SaveSettingsAsync();
            var analyzer = new MediaAnalyzer(tools, new WindowsProcessRunner(Path.Combine(_data, "logs")));
            _coordinator = new(_store, analyzer); _cancellation = new(); SetBusy(true); _page = 0;
            StageText.Text = resume ? "Revalidando a fila salva" : "Inventariando arquivos";
            CurrentFile.Text = "Aguarde a enumeração; ela pode ser cancelada.";
            var progress = new Progress<QueueProgress>(p =>
            {
                if (_job?.Id != p.JobId) _job = new(p.JobId, source, options);
                StageText.Text = p.Stage;
                CurrentFile.Text = p.Path.Length > 0 ? p.Path + (p.Fraction is { } fraction ? $" · {fraction * 100:0}% na etapa" : "") : "Resultados salvos no banco local.";
                UpdateCounts(p.Counts); _refreshRequested = true;
            });
            var operation = resume ? _coordinator.ResumeAsync(_job ?? throw new InvalidOperationException("Não há fila anterior."), progress, _cancellation.Token)
                : _coordinator.StartAsync(source, options, progress, _cancellation.Token);
            _activeTask = operation; _job = await operation;
            StageText.Text = _store.Counts(_job.Id).Pending > 0 ? "Fila pausada; use Retomar fila" : "Análise encerrada — consulte os diagnósticos";
        }
        catch (OperationCanceledException) { StageText.Text = "Fila cancelada; os itens podem ser retomados"; }
        catch (Exception ex) { StageText.Text = "Operação interrompida"; ShowError(ex); }
        finally
        {
            _activeTask = null; _cancellation?.Dispose(); _cancellation = null;
            _job = await Task.Run(_store.LatestJob); SetBusy(false); await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshBusy || _job is null) return;
        _refreshBusy = true;
        try
        {
            var id = _job.Id; var page = _page;
            var selected = (ResultsGrid.SelectedItem as FileRow)?.Id;
            var data = await Task.Run(() => (items: _store.Page(id, page), counts: _store.Counts(id)));
            var reviewSelected = (ReviewGrid.SelectedItem as FileRow)?.Id;
            ResultsGrid.ItemsSource = data.items.Select(item => new FileRow(item)).ToArray();
            if (selected is not null) ResultsGrid.SelectedItem = ResultsGrid.Items.Cast<FileRow>().FirstOrDefault(row => row.Id == selected);
            if (reviewSelected is not null) ReviewGrid.SelectedItem = ReviewGrid.Items.Cast<FileRow>().FirstOrDefault(row => row.Id == reviewSelected);
            PageLabel.Text = $"Página {page + 1} · até 100 arquivos"; UpdateCounts(data.counts);
            ResumeButton.IsEnabled = !_busy && (data.counts.Pending + data.counts.Cancelled + data.counts.Running > 0);
        }
        catch (Exception ex) { StageText.Text = "Falha ao consultar resultados: " + ex.Message; }
        finally { _refreshBusy = false; }
    }

    private void UpdateCounts(QueueCounts counts)
    {
        TotalCount.Text = counts.Total.ToString("N0"); CompletedCount.Text = counts.Completed.ToString("N0");
        FailedCount.Text = counts.Failed.ToString("N0"); PendingCount.Text = counts.Pending.ToString("N0");
        TotalProgress.Value = counts.Total == 0 ? 0 : 100.0 * (counts.Completed + counts.Failed + counts.Cancelled) / counts.Total;
        ReportSummary.Text = $"Sessão {_job?.Id}: {counts.Total} arquivos; {counts.Completed} análises concluídas, {counts.Failed} falhas de execução, {counts.Cancelled} cancelados e {counts.Pending} pendentes. Concluído é estado da fila, não garantia de integridade.";
    }

    private async void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var request = Interlocked.Increment(ref _detailRequest);
        if (ResultsGrid.SelectedItem is not FileRow row) return;
        SelectedPath.Text = row.Item.Path;
        try
        {
            var detail = await Task.Run(() => _store.Detail(row.Id));
            if (request != _detailRequest) return;
            DetailBox.Text = detail is null ? row.Item.Note.Length > 0 ? row.Item.Note : "Arquivo ainda não analisado." : JsonSerializer.Serialize(detail, QueueStore.Json);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Review_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var request = Interlocked.Increment(ref _reviewRequest);
        _reviewTarget = null;
        if (ReviewGrid.SelectedItem is not FileRow row) return;
        ReviewPath.Text = row.Item.Path;
        try
        {
            var data = await Task.Run(() => (result: _store.Detail(row.Id), reviews: _store.ReviewHistory(row.Id)));
            if (request != _reviewRequest) return;
            if (row.Item.State is QueueState.Pending or QueueState.Running || data.result?.Evidence is not { Sha256.Length: 64 } evidence)
            { ReviewStatus.Text = "Reanalise este arquivo para coletar a identidade e as evidências antes de avaliar."; return; }
            _reviewTarget = (row.Id, evidence);
            var review = data.reviews.LastOrDefault(r => r.AnalysisId == evidence.AnalysisId);
            HumanVerdictBox.SelectedIndex = (int)(review?.Verdict ?? HumanVerdict.NotEvaluated);
            SatisfactionBox.SelectedIndex = (int)(review?.Satisfaction ?? Satisfaction.NotEvaluated);
            WholeFileCheck.IsChecked = review?.WatchedWholeFile ?? false;
            CouldNotOpenCheck.IsChecked = review?.CouldNotOpen ?? false;
            ReviewerBox.Text = review?.Reviewer ?? ReviewerBox.Text; PlayerBox.Text = review?.Player ?? PlayerBox.Text;
            ReviewNotesBox.Text = review?.Notes ?? "";
            ReviewStatus.Text = review is null ? "Sem avaliação registrada para esta análise." : $"Avaliação salva em {review.ReviewedUtc.ToLocalTime():g}. Alterações ficam no histórico.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void SaveReview_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var target = _reviewTarget ?? throw new InvalidOperationException("Selecione um arquivo com evidência SHA-256.");
            var review = new ManualReview(target.Evidence.AnalysisId, target.Evidence.Sha256,
                (HumanVerdict)HumanVerdictBox.SelectedIndex, (Satisfaction)SatisfactionBox.SelectedIndex,
                WholeFileCheck.IsChecked == true, ReviewerBox.Text.Trim(), ReviewNotesBox.Text, DateTimeOffset.UtcNow, PlayerBox.Text.Trim(), CouldNotOpenCheck.IsChecked == true);
            _cancellation = new();
            SetBusy(true); PauseButton.IsEnabled = false;
            ReviewStatus.Text = "Conferindo os bytes antes de salvar a avaliação...";
            var operation = new ValidationService(_store).SaveReviewAsync(target.FileId, review, _cancellation.Token);
            _activeTask = operation; await operation;
            ReviewStatus.Text = "Avaliação salva; SHA-256 confirmado. Calcule ou exporte a comparação quando terminar a amostra.";
        }
        catch (OperationCanceledException) { ReviewStatus.Text = "Conferência cancelada; avaliação não salva."; }
        catch (Exception ex) { ReviewStatus.Text = "Avaliação não salva."; ShowError(ex); }
        finally { _activeTask = null; _cancellation?.Dispose(); _cancellation = null; SetBusy(false); }
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_busy || _job is null) throw new InvalidOperationException("Conclua ou pause uma sessão antes de comparar.");
            var summary = await Task.Run(() => new ValidationService(_store).Summary(_job.Id));
            var m = summary.Overall;
            static string Rate(double? value) => value is null ? "sem base" : value.Value.ToString("P1", CultureInfo.GetCultureInfo("pt-BR"));
            ComparisonText.Text = $"{m.Comparable}/{m.Total} pares comparáveis; {m.ManuallyEvaluated} avaliações. " +
                $"Acertos com problema: {m.TruePositive}; acertos sem problema: {m.TrueNegative}; falsos positivos: {m.FalsePositive}; falhas não detectadas: {m.FalseNegative}.\n" +
                $"Precisão dos alertas: {Rate(m.Precision)} · Sensibilidade: {Rate(m.Recall)} · Acurácia: {Rate(m.Accuracy)}. " +
                $"Automático inconclusivo: {m.AutomaticInconclusive}; revisão ausente/incompleta: {m.MissingOrIneligibleReview}. Grupos por modo/formato no JSON.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task RepairAsync(bool withReference)
    {
        if (_busy) return;
        try
        {
            if (_job is null || ResultsGrid.SelectedItem is not FileRow row) throw new InvalidOperationException("Selecione um arquivo nos resultados da análise.");
            if (row.Item.State != QueueState.Completed) throw new InvalidOperationException("O arquivo deve ser analisado antes de gerar candidato.");
            if (string.IsNullOrWhiteSpace(OutputBox.Text)) throw new ArgumentException("Escolha uma pasta de saída separada.");
            if (withReference && !File.Exists(ReferenceBox.Text)) throw new FileNotFoundException("Escolha um vídeo íntegro de referência.");
            var tools = Tools(); tools.Validate(); var options = Options();
            var source = _job.Source; var output = OutputBox.Text; var reference = withReference ? ReferenceBox.Text : null;
            var runner = new WindowsProcessRunner(Path.Combine(_data, "logs"));
            var analyzer = new MediaAnalyzer(tools, runner); var repair = new RepairService(tools, runner, analyzer);
            _coordinator = new(_store, analyzer); _cancellation = new(); SetBusy(true);
            RepairStatus.Text = "Gerando candidato e executando novo diagnóstico completo...";
            var operation = _coordinator.ExecuteAsync(() => repair.GenerateAsync(source, row.Item.Path, output, options, reference, _cancellation.Token).GetAwaiter().GetResult(), _cancellation.Token);
            _activeTask = operation; var result = await operation;
            RepairStatus.Text = result.Status + Environment.NewLine + result.Candidate + Environment.NewLine + result.Note;
            DetailBox.Text = JsonSerializer.Serialize(result.Diagnosis, QueueStore.Json);
            await Task.Run(() => _store.Event(row.Item.JobId, $"Candidato separado: {result.Candidate}. {result.Status}"));
        }
        catch (OperationCanceledException) { RepairStatus.Text = "Recuperação cancelada. Original preservado; área temporária mantida para inspeção."; }
        catch (Exception ex) { RepairStatus.Text = "Não foi possível gerar candidato."; ShowError(ex); }
        finally { _activeTask = null; _cancellation?.Dispose(); _cancellation = null; SetBusy(false); }
    }

    private void Navigate_Click(object sender, RoutedEventArgs e) => SelectPage(int.Parse((string)((Button)sender).Tag));
    private void Source_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Escolha a pasta dos vídeos" }; if (dialog.ShowDialog(this) == true) SourceBox.Text = dialog.FolderName; }
    private void Output_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Escolha a pasta dos candidatos" }; if (dialog.ShowDialog(this) == true) OutputBox.Text = dialog.FolderName; }
    private void Reference_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Title = "Vídeo íntegro de referência", Filter = "Vídeos|*.mp4;*.mov;*.m4v;*.3gp|Todos os arquivos|*.*" }; if (dialog.ShowDialog(this) == true) ReferenceBox.Text = dialog.FileName; }
    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Executáveis|*.exe", Title = "Escolha a ferramenta" };
        if (dialog.ShowDialog(this) != true) return;
        switch (((Button)sender).Tag) { case "probe": ProbeBox.Text = dialog.FileName; break; case "mpeg": MpegBox.Text = dialog.FileName; break; default: UntruncBox.Text = dialog.FileName; break; }
    }
    private void AllFormats_Click(object sender, RoutedEventArgs e) { var check = _formats.Values.Any(control => control.IsChecked != true); foreach (var control in _formats.Values) control.IsChecked = check; ((Button)sender).Content = check ? "Desmarcar todos" : "Marcar todos"; }
    private async void Start_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync(false);
    private async void Resume_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync(true);
    private void Pause_Click(object sender, RoutedEventArgs e) { _coordinator?.RequestPause(); StageText.Text = "Pausa solicitada; concluindo o arquivo atual"; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();
    private async void Previous_Click(object sender, RoutedEventArgs e) { if (_page > 0) _page--; await RefreshAsync(); }
    private async void Next_Click(object sender, RoutedEventArgs e) { if (_job is not null && (_page + 1) * 100 < (await Task.Run(() => _store.Counts(_job.Id))).Total) _page++; await RefreshAsync(); }
    private async void SaveSettings_Click(object sender, RoutedEventArgs e) { try { await SaveSettingsAsync(); MessageBox.Show(this, "Configurações salvas.", "NexoVideo"); } catch (Exception ex) { ShowError(ex); } }
    private async void Remux_Click(object sender, RoutedEventArgs e) => await RepairAsync(false);
    private async void Untrunc_Click(object sender, RoutedEventArgs e) => await RepairAsync(true);
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var exporting = false;
        try
        {
            if (_job is null) throw new InvalidOperationException("Não há sessão para exportar.");
            var dialog = new SaveFileDialog { Filter = "Relatório JSON|*.json", FileName = $"NexoVideo-sessao-{_job.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
            if (dialog.ShowDialog(this) != true) return;
            var jobId = _job.Id; SetBusy(true); exporting = true;
            PauseButton.IsEnabled = CancelButton.IsEnabled = false;
            var operation = Task.Run(() => new ValidationService(_store).ExportJson(jobId, dialog.FileName));
            _activeTask = operation; await operation;
            MessageBox.Show(this, "Relatório completo exportado, incluindo logs e avaliações.", "NexoVideo");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { if (exporting) { _activeTask = null; SetBusy(false); } }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeReady) return;
        if (_activeTask is { IsCompleted: false } active)
        {
            e.Cancel = true; _cancellation?.Cancel();
            try { await active; } catch (Exception) { /* O fluxo da operação apresenta/registre o erro. */ }
            _closeReady = true; _timer.Stop(); Close(); return;
        }
        _timer.Stop();
    }
    private void ShowError(Exception exception) => MessageBox.Show(this, exception.Message, "NexoVideo", MessageBoxButton.OK, MessageBoxImage.Information);

    private sealed record SavedSettings(string Source, string Output, AnalysisOptions Options, ToolPaths Tools);
    public sealed record FileRow(QueueItem Item)
    {
        public long Id => Item.Id;
        public string Name => Path.GetFileName(Item.Path);
        public string SizeLabel => Item.Size >= 1_000_000 ? $"{Item.Size / 1_000_000.0:0.0} MB" : $"{Item.Size / 1000.0:0.0} KB";
        public string Conclusion => string.IsNullOrEmpty(Item.Conclusion) ? Item.Note : Item.Conclusion;
        public string StateLabel => Item.State switch { QueueState.Pending => "Pendente", QueueState.Running => "Em análise", QueueState.Completed => "Concluído", QueueState.Failed => "Falhou", _ => "Cancelado" };
    }
}
