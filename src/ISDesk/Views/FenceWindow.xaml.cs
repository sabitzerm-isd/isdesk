using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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
