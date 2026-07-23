using System.ComponentModel;
using System.Windows.Media;

namespace ISDesk.ViewModels;

public sealed class IconItemViewModel : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public IconItemViewModel(string path, string displayName, bool isFolder)
    {
        Path = path;
        DisplayName = displayName;
        IsFolder = isFolder;
    }

    public string Path { get; }
    public string DisplayName { get; }
    public bool IsFolder { get; }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    private bool _isHighlighted;
    private bool _isDimmed;

    /// Von der Live-Suche gesetzt: Treffer werden hervorgehoben, …
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted != value)
            {
                _isHighlighted = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHighlighted)));
            }
        }
    }

    /// … alle Nicht-Treffer abgedunkelt.
    public bool IsDimmed
    {
        get => _isDimmed;
        set
        {
            if (_isDimmed != value)
            {
                _isDimmed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDimmed)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
