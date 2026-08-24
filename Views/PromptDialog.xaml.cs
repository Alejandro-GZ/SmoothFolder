using System.Windows;
using SmoothFolder.Native;

namespace SmoothFolder.Views;

public partial class PromptDialog : Window
{
    private string? _result;

    private PromptDialog(string prompt, string current)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        ValueBox.Text = current;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };

        SourceInitialized += (_, _) => WindowEffects.ApplyPopupEffects(this, 22);
    }

    public static string? Show(string prompt, string current)
    {
        var dialog = new PromptDialog(prompt, current);
        dialog.ShowDialog();
        return dialog._result;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _result = ValueBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
