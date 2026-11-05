using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace HardwareTest.Authoring;

public sealed partial class BenchExportWindow
{
    private CheckBox _includePackages = null!;
    private StackPanel _offlineOptions = null!;
    private ComboBox _platform = null!;
    private TextBox _packageFolder = null!;
    private Button _choosePackages = null!;
    private CheckBox _downloadPackages = null!;
    private TextBox _repository = null!;
    private StackPanel _repositoryOptions = null!;

    private void AddOfflineOptions(StackPanel body)
    {
        _includePackages = new CheckBox { Content = "Include packages for an offline bench", MinHeight = 40 };
        AutomationProperties.SetName(_includePackages, "Include packages for an offline bench");
        _offlineOptions = new StackPanel { Spacing = 8, IsVisible = false };
        _offlineOptions.Children.Add(new TextBlock
        {
            Text = "Collect required packages for the bench platform. Installation needs a matching operator release and creates a separate copy with fresh bench data.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        _offlineOptions.Children.Add(new TextBlock { Text = "Bench platform" });
        _platform = new ComboBox { ItemsSource = new[] { "Windows x64", "Linux x64", "Linux ARM64" }, MinHeight = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        _platform.SelectedIndex = OperatingSystem.IsWindows() ? 0 : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 2 : 1;
        AutomationProperties.SetName(_platform, "Offline bench platform");
        _platform.SelectionChanged += (_, _) => InvalidatePreparationFeedback();
        _offlineOptions.Children.Add(_platform);
        _offlineOptions.Children.Add(new TextBlock { Text = "Local package folder (optional)" });
        _packageFolder = new TextBox { MinHeight = 40, PlaceholderText = "Folder containing .TapPackage or .zip archives" };
        AutomationProperties.SetName(_packageFolder, "Offline package source folder");
        _packageFolder.TextChanged += (_, _) => InvalidatePreparationFeedback();
        _offlineOptions.Children.Add(_packageFolder);
        _choosePackages = new Button { Content = "Choose package folder…", MinHeight = 40 };
        AutomationProperties.SetName(_choosePackages, "Choose offline package source folder");
        _choosePackages.Click += async (_, _) =>
        {
            if (_busy || !Current()) return;
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Offline package archives", AllowMultiple = false });
                if (Current() && folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _packageFolder.Text = path;
            }
            catch (Exception error) { if (Current()) _status.Text = error.Message; }
            finally { if (Current()) _packageFolder.Focus(); }
        };
        _offlineOptions.Children.Add(_choosePackages);
        _downloadPackages = new CheckBox { Content = "Download missing packages for this bundle", IsChecked = _viewModel.DownloadRequiredPackages, MinHeight = 40 };
        AutomationProperties.SetName(_downloadPackages, "Download missing offline bundle packages");
        _offlineOptions.Children.Add(_downloadPackages);
        _repository = new TextBox { Text = _viewModel.PackageRepositoryUrl, MinHeight = 40 };
        AutomationProperties.SetName(_repository, "Offline bundle package repository URL");
        _repository.TextChanged += (_, _) => InvalidatePreparationFeedback();
        _repositoryOptions = new StackPanel { Spacing = 8, IsVisible = _downloadPackages.IsChecked == true };
        _repositoryOptions.Children.Add(new TextBlock { Text = "Package repository" });
        _repositoryOptions.Children.Add(_repository);
        _downloadPackages.IsCheckedChanged += (_, _) => { _repositoryOptions.IsVisible = _downloadPackages.IsChecked == true; InvalidatePreparationFeedback(); };
        _offlineOptions.Children.Add(_repositoryOptions);
        _offlineOptions.Children.Add(new TextBlock
        {
            Text = "If a package is unavailable, obtain the matching target archive from its publisher, place it in the local package folder and retry. Package inclusion stays enabled after a failure.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        _includePackages.IsCheckedChanged += (_, _) => UpdateOfflineMode();
        body.Children.Add(_includePackages);
        body.Children.Add(_offlineOptions);
    }

    private void UpdateOfflineMode()
    {
        var offline = _includePackages.IsChecked == true;
        _offlineOptions.IsVisible = offline;
        _prepare.Content = offline ? "Prepare offline bundle" : "Prepare / replace bench copy";
        AutomationProperties.SetName(_prepare, offline ? "Prepare offline bundle" : "Prepare bench copy");
        _receipt.IsVisible = false;
        InvalidatePreparationFeedback();
    }

    private OfflineBenchExportOptions CaptureOfflineOptions()
    {
        var target = _platform.SelectedIndex switch { 0 => ("Windows", "x64"), 2 => ("Linux", "arm64"), _ => ("Linux", "x64") };
        return new OfflineBenchExportOptions(target.Item1, target.Item2,
            _packageFolder.Text?.Trim(), string.IsNullOrWhiteSpace(_viewModel.OpenTapHomeOverride) ? null : _viewModel.OpenTapHomeOverride, _downloadPackages.IsChecked == true,
            _repository.Text?.Trim() ?? string.Empty);
    }

    private void SetPreparationBusy(bool busy)
    {
        foreach (var control in new Control[] { _prepare, _browse, _destination, _includePackages, _platform, _packageFolder, _choosePackages, _downloadPackages, _repository })
            control.IsEnabled = !busy;
        _cancel.IsVisible = busy;
        _cancel.IsEnabled = busy;
        _cancel.IsCancel = busy;
        _close.IsCancel = !busy;
    }

    private void ShowPreparationError(string message)
    {
        if (message.Length <= 260) { _status.Text = message; return; }
        _status.Text = message[..240] + "… Review the full preparation error below.";
        AutomationProperties.SetName(_receipt, "Bench file preparation error details");
        _receipt.Text = message;
        _receipt.IsVisible = true;
    }

    private void InvalidatePreparationFeedback()
    {
        if (_busy || _status is null || _receipt is null || string.IsNullOrEmpty(_status.Text)) return;
        _receipt.IsVisible = false;
        _status.Text = "Preparation options changed. Prepare again to create files for these choices.";
    }
}
