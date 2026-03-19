using Avalonia.Controls;
using CatModManager.Ui.ViewModels;

namespace CatModManager.Ui.Views;

public partial class PluginBrowserWindow : Window
{
    public PluginBrowserWindow(PluginBrowserViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Initialize();
    }
}
