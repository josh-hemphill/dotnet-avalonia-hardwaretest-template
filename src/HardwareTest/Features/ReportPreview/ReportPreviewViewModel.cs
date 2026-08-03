using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using PDFtoImage;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using SkiaSharp;

namespace HardwareTest.Features.ReportPreview;

public partial class ReportPreviewViewModel : ReactiveObject
{
    private static readonly object PdfGate = new();
    private readonly IRunStore _runStore;
    private readonly IReportService _reportService;
    private readonly OperatorSession? _operatorSession;

    public ReportPreviewViewModel(
        IRunStore runStore,
        IReportService reportService,
        OperatorSession? operatorSession = null)
    {
        _runStore = runStore;
        _reportService = reportService;
        _operatorSession = operatorSession;
        Pages = [];
        Status = "Select a run PDF to preview.";

        LoadLatestCommand = ReactiveCommand.CreateFromTask(LoadLatestAsync);
        PrintCommand = ReactiveCommand.Create(Print);
        NavigateToResultsCommand = ReactiveCommand.Create(
            () => NavigateToResultsRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<Bitmap> Pages { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadLatestCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> PrintCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NavigateToResultsCommand { get; }

    /// Raised when the operator wants to open Results to pick a PDF.
    public event EventHandler? NavigateToResultsRequested;

    [Reactive] private string? _pdfPath;
    [Reactive] private string _status = string.Empty;
    [Reactive] private bool _isBusy;

    public bool ShowEmptyState => Pages.Count == 0 && !IsBusy;

    public async Task LoadFromPathAsync(string path)
    {
        _operatorSession?.TouchActivity();
        IsBusy = true;
        try
        {
            PdfPath = path;
            // Dispose any previously rendered bitmaps before clearing the collection to avoid leaks.
            foreach (var bitmap in Pages)
            {
                bitmap.Dispose();
            }

            Pages.Clear();
            this.RaisePropertyChanged(nameof(ShowEmptyState));
            if (!File.Exists(path))
            {
                Status = $"File not found: {path}";
                return;
            }

            try
            {
                var bitmaps = await Task.Run(() => RenderPages(path));
                foreach (var bitmap in bitmaps)
                {
                    Pages.Add(bitmap);
                }

                Status = $"Previewing {path} ({Pages.Count} page(s) shown).";
            }
            catch (Exception ex)
            {
                Status = $"Preview failed: {ex.Message}";
            }
            finally
            {
                this.RaisePropertyChanged(nameof(ShowEmptyState));
            }
        }
        finally
        {
            IsBusy = false;
            this.RaisePropertyChanged(nameof(ShowEmptyState));
        }
    }

    private async Task LoadLatestAsync()
    {
        var runs = await _runStore.ListAsync();
        var latest = runs.FirstOrDefault();
        if (latest is null)
        {
            Status = "No saved runs.";
            return;
        }

        var run = await _runStore.LoadAsync(latest.RunId);
        if (run is null)
        {
            Status = "Failed to load run.";
            return;
        }

        var path = run.ReportPdfPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            path = await _reportService.GeneratePdfAsync(run);
        }

        await LoadFromPathAsync(path);
    }

    private void Print()
    {
        if (string.IsNullOrWhiteSpace(PdfPath) || !File.Exists(PdfPath))
        {
            Status = "No PDF to print.";
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = PdfPath,
                    UseShellExecute = true,
                    Verb = "print",
                });
                Status = "Sent PDF to the system print handler.";
            }
            else
            {
                Process.Start("lp", $"\"{PdfPath}\"");
                Status = "Queued PDF with lp.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Print failed: {ex.Message}";
        }
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static List<Bitmap> RenderPages(string path)
    {
        lock (PdfGate)
        {
            using var input = File.OpenRead(path);
            var images = Conversion.ToImages(input).Take(10).ToList();
            var result = new List<Bitmap>(images.Count);
            foreach (var skBitmap in images)
            {
                result.Add(ToAvaloniaBitmap(skBitmap));
                skBitmap.Dispose();
            }

            return result;
        }
    }

    private static Bitmap ToAvaloniaBitmap(SKBitmap skBitmap)
    {
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = data.AsStream();
        return new Bitmap(stream);
    }
}
