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

    public event PropertyChangedEventHandler? PropertyChanged;
}
