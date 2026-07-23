// UseWPF + UseWindowsForms importieren beide Namespaces implizit. Diese Aliase
// legen fuer mehrdeutige Typnamen die WPF-Variante fest (die App ist eine WPF-App;
// WinForms-Typen wie NotifyIcon/Screen werden voll qualifiziert verwendet).
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Point = System.Windows.Point;
global using DataObject = System.Windows.DataObject;
global using DataFormats = System.Windows.DataFormats;
global using IDataObject = System.Windows.IDataObject;
global using DragEventArgs = System.Windows.DragEventArgs;
global using DragDropEffects = System.Windows.DragDropEffects;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
