using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ISDesk.Interop;
using ISDesk.ViewModels;

namespace ISDesk.Views;

public partial class FenceWindow : Window
{
    private readonly FenceViewModel _vm;

    public FenceWindow(FenceViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = vm.X;
        Top = vm.Y;
        Width = vm.Width;
        Height = vm.Height;

        _vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += (_, _) => _vm.ActivateAllTabs();
        Closed += (_, _) => _vm.DisposeTabs();
    }

    public FenceViewModel ViewModel => _vm;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        BottomMostBehavior.Attach(this);
        WindowBackdrop.Apply(this, _vm.Opacity, _vm.Blur);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FenceViewModel.Opacity) or nameof(FenceViewModel.Blur))
            WindowBackdrop.Apply(this, _vm.Opacity, _vm.Blur);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { /* DragMove kann in Randfaellen werfen */ }
        }
    }

    private void IconList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (FindItem(e.OriginalSource as DependencyObject) is { } item)
            Launch(item.Path);
    }

    private static IconItemViewModel? FindItem(DependencyObject? source)
    {
        while (source != null && source is not ListBoxItem)
            source = VisualTreeHelper.GetParent(source);
        return (source as ListBoxItem)?.DataContext as IconItemViewModel;
    }

    private static void Launch(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Start fehlgeschlagen: {path} — {ex.Message}");
        }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        _vm.X = Left;
        _vm.Y = Top;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _vm.Width = ActualWidth;
        _vm.Height = ActualHeight;
    }
}
