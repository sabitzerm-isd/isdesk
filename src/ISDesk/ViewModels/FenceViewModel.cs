using System.ComponentModel;
using System.Runtime.CompilerServices;
using ISDesk.Models;

namespace ISDesk.ViewModels;

public sealed class FenceViewModel : INotifyPropertyChanged
{
    private readonly FenceConfig _config;
    private readonly Action _persist;

    public FenceViewModel(FenceConfig config, Action? persist = null)
    {
        _config = config;
        _persist = persist ?? (static () => { });
    }

    public FenceConfig Config => _config;
    public Guid Id => _config.Id;

    public string Title
    {
        get => _config.Title;
        set { if (_config.Title != value) { _config.Title = value; OnChanged(); Persist(); } }
    }

    public double Opacity
    {
        get => _config.Opacity;
        set { if (Math.Abs(_config.Opacity - value) > double.Epsilon) { _config.Opacity = value; OnChanged(); Persist(); } }
    }

    public bool Blur
    {
        get => _config.Blur;
        set { if (_config.Blur != value) { _config.Blur = value; OnChanged(); Persist(); } }
    }

    public double X
    {
        get => _config.X;
        set { if (_config.X != value) { _config.X = value; OnChanged(); Persist(); } }
    }

    public double Y
    {
        get => _config.Y;
        set { if (_config.Y != value) { _config.Y = value; OnChanged(); Persist(); } }
    }

    public double Width
    {
        get => _config.Width;
        set { if (_config.Width != value) { _config.Width = value; OnChanged(); Persist(); } }
    }

    public double Height
    {
        get => _config.Height;
        set { if (_config.Height != value) { _config.Height = value; OnChanged(); Persist(); } }
    }

    private void Persist() => _persist();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
