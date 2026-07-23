using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ISDesk.Models;
using ISDesk.Services;

namespace ISDesk.ViewModels;

public sealed class TabViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly TabConfig _config;
    private readonly Action _persist;
    private readonly object _sync = new();
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;

    public TabViewModel(TabConfig config, Action persist)
    {
        _config = config;
        _persist = persist;
    }

    public TabConfig Config => _config;

    public string Title
    {
        get => _config.Title;
        set { if (_config.Title != value) { _config.Title = value; OnChanged(); _persist(); } }
    }

    public string FolderPath => _config.FolderPath;

    public int IconSize
    {
        get => _config.IconSize;
        set { if (_config.IconSize != value) { _config.IconSize = value; OnChanged(); _persist(); } }
    }

    public ObservableCollection<IconItemViewModel> Items { get; } = new();

    public void Reload()
    {
        Items.Clear();
        foreach (var path in FolderContents.ListVisibleEntries(_config.FolderPath))
        {
            var item = new IconItemViewModel(path, FolderContents.GetDisplayName(path), Directory.Exists(path));
            Items.Add(item);
            LoadIconAsync(item);
        }
    }

    private async void LoadIconAsync(IconItemViewModel item)
    {
        var icon = await ShellIconProvider.Instance.GetIconAsync(item.Path, _config.IconSize).ConfigureAwait(true);
        item.Icon = icon;
    }

    public void StartWatching()
    {
        try
        {
            if (!Directory.Exists(_config.FolderPath)) return;

            _debounce = new System.Timers.Timer(300) { AutoReset = false };
            _debounce.Elapsed += (_, _) => DispatchReload();

            _watcher = new FileSystemWatcher(_config.FolderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                             | NotifyFilters.Attributes | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
        }
        catch (Exception)
        {
            // Ordner nicht ueberwachbar → ohne Watcher weiter.
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        lock (_sync)
        {
            _debounce?.Stop();
            _debounce?.Start();
        }
    }

    private void DispatchReload()
    {
        var app = Application.Current;
        if (app != null)
            app.Dispatcher.Invoke(Reload);
        else
            Reload();
    }

    public void Dispose()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileSystemChanged;
            _watcher.Deleted -= OnFileSystemChanged;
            _watcher.Renamed -= OnFileSystemChanged;
            _watcher.Changed -= OnFileSystemChanged;
            _watcher.Dispose();
            _watcher = null;
        }
        _debounce?.Dispose();
        _debounce = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
