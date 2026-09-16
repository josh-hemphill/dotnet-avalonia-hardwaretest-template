using System.ComponentModel;
using HardwareTest.Shell;

namespace HardwareTest.ShellApps.Notes;

/// Sample engineer page shown when the Notes shell app is baked into the host.
public sealed class NotesViewModel : INotifyPropertyChanged
{
    public const string FileName = "notes.txt";

    private readonly string? _path;
    private string _body = string.Empty;

    public NotesViewModel()
        : this(host: null)
    {
    }

    public NotesViewModel(IShellHost? host)
    {
        if (host is null)
        {
            return;
        }

        _path = Path.Combine(host.GetAppDataDirectory(NotesApplication.PageId), FileName);
        if (File.Exists(_path))
        {
            _body = File.ReadAllText(_path);
        }
    }

    public string Title { get; } = "Station notes";

    public string Summary { get; } =
        "Sample shell application. Product images replace this pack with planning or analysis pages that share the operator shell.";

    public string Body
    {
        get => _body;
        set
        {
            if (_body == value)
            {
                return;
            }

            _body = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Body)));
            Persist();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Persist()
    {
        if (string.IsNullOrWhiteSpace(_path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, _body);
    }
}
