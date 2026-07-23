using System.Windows;
using ISDesk.Interop;

namespace ISDesk.Views;

public partial class InputDialog : Window
{
    public InputDialog(string prompt, string initialText = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        Input.Text = initialText;

        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    public string Value { get; private set; } = "";

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Dunkler, fast deckender Acrylic-Hintergrund.
        WindowBackdrop.Apply(this, 0.95, true);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var text = Input.Text.Trim();
        if (text.Length == 0) return; // leere Eingabe nicht bestaetigen
        Value = text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// Zeigt den Dialog zentriert ueber dem uebergebenen Fenster (ohne Owner-Beziehung,
    /// da Bereiche bottom-most sind). Gibt den eingegebenen Text zurueck oder null.
    public static string? Ask(string prompt, string initialText, Window? centerOn)
    {
        var dialog = new InputDialog(prompt, initialText);

        if (centerOn != null)
        {
            dialog.Loaded += (_, _) =>
            {
                dialog.Left = centerOn.Left + (centerOn.ActualWidth - dialog.ActualWidth) / 2;
                dialog.Top = centerOn.Top + (centerOn.ActualHeight - dialog.ActualHeight) / 2;
            };
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true ? dialog.Value : null;
    }
}
