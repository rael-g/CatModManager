using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace CatModManager.Ui.Views;

/// <summary>
/// Asks for one line of text. Written for renaming a profile, once the sidebar text box that used
/// to do it was taken out — creating, renaming and deleting live in the menus now, and a menu entry
/// has nowhere to type.
/// </summary>
public partial class TextInputDialog : Window
{
    private string? _result;

    public TextInputDialog() : this("", "") { }

    public TextInputDialog(string header, string initialValue)
    {
        InitializeComponent();

        this.FindControl<TextBlock>("HeaderText")!.Text = header;

        var box = this.FindControl<TextBox>("ValueBox")!;
        box.Text = initialValue;
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); };

        this.FindControl<Button>("OkBtn")!.Click     += (_, _) => Accept();
        this.FindControl<Button>("CancelBtn")!.Click += (_, _) => Close();

        Opened += (_, _) => { box.SelectAll(); box.Focus(); };
    }

    /// <summary>The text the user accepted, or null if they backed out or left it blank.</summary>
    public static async Task<string?> ShowAsync(Window owner, string header, string initialValue)
    {
        var dialog = new TextInputDialog(header, initialValue);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    private void Accept()
    {
        var typed = this.FindControl<TextBox>("ValueBox")!.Text;
        _result = string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();
        Close();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
