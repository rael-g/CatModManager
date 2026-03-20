using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CatModManager.Ui.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string body)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("BodyText")!.Text = body;
        this.FindControl<Button>("ConfirmBtn")!.Click += (_, _) => Close(true);
        this.FindControl<Button>("CancelBtn")!.Click  += (_, _) => Close(false);
    }
}
