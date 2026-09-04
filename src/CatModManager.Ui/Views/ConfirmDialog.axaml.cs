using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CatModManager.Ui.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() => InitializeComponent();

    /// <summary>
    /// Whether the optional checkbox was ticked. Meaningless unless the dialog was built with an
    /// <paramref name="optionLabel"/>. Read after awaiting the dialog — it is a second answer
    /// alongside the yes/no, which beats a second dialog for a confirmation that has a variant.
    /// </summary>
    public bool IsOptionChecked { get; private set; }

    public ConfirmDialog(string title, string body, string? optionLabel = null)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("BodyText")!.Text = body;

        var option = this.FindControl<CheckBox>("OptionCheck")!;
        if (optionLabel != null)
        {
            option.Content   = optionLabel;
            option.IsVisible = true;
        }

        this.FindControl<Button>("ConfirmBtn")!.Click += (_, _) =>
        {
            IsOptionChecked = option.IsChecked == true;
            Close(true);
        };
        this.FindControl<Button>("CancelBtn")!.Click  += (_, _) => Close(false);
    }
}
