using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CatModManager.Ui.ViewModels;
using CatModManager.Ui.Views;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using CatModManager.Ui;
using Avalonia;

namespace CatModManager.Tests;

public class ProfileSelectorTests
{
    [AvaloniaFact]
    public void UI_ShouldHaveProfileSelector_InMainLayout()
    {
        var window = new MainWindow();
        var app = (App)Application.Current!;
        var vm = app.Services!.GetRequiredService<MainWindowViewModel>();
        window.DataContext = vm;
        window.Show();

        // Check for ComboBox named ProfileSelector
        var selector = window.FindControl<ComboBox>("ProfileSelector");
        Assert.NotNull(selector);
        
        // Check bindings
        Assert.NotNull(selector.ItemsSource);
        // ItemsSource should be bound to AvailableProfiles
    }

    [AvaloniaFact]
    public async Task ChangingProfile_InUI_ShouldUpdateViewModel()
    {
        var window = new MainWindow();
        var app = (App)Application.Current!;
        var vm = app.Services!.GetRequiredService<MainWindowViewModel>();
        var pathService = app.Services!.GetRequiredService<CatModManager.Core.Services.ICatPathService>();
        window.DataContext = vm;
        window.Show();

        var selector = window.FindControl<ComboBox>("ProfileSelector");
        Assert.NotNull(selector);

        // Setup mock profiles on disk
        var profilesDir = pathService.ProfilesPath;
        if (!System.IO.Directory.Exists(profilesDir)) System.IO.Directory.CreateDirectory(profilesDir);
        
        System.IO.File.WriteAllText(System.IO.Path.Combine(profilesDir, "Modded.toml"), "Name = \"Modded\"");

        // Refresh available profiles
        vm.AvailableProfiles.Clear();
        vm.AvailableProfiles.Add("Modded");

        // Simulate UI selection
        selector.SelectedIndex = 0; // "Modded"
        
        // Wait for async LoadProfile
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Modded", vm.CurrentProfileName);
    }
}
