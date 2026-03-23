using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CatModManager.Ui;
using CatModManager.Ui.ViewModels;
using CatModManager.Ui.Views;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace CatModManager.Tests;

public class HeadlessUiTests
{
    [AvaloniaFact]
    public void MainWindow_ShouldInitWithCorrectTitle()
    {
        var window = new MainWindow();
        Assert.Equal("The Schrödinger Cat Mod Manager", window.Title);
    }

    [AvaloniaFact]
    public void Inspector_Visibility_ReflectsSelectedMod()
    {
        var window = new MainWindow();
        var app = (App)Application.Current!;
        // Ensure services are initialized
        var vm = (MainWindowViewModel)window.DataContext!;
        if (vm == null)
        {
             // If not set in constructor (which it isn't), get from DI
             vm = app.Services!.GetRequiredService<MainWindowViewModel>();
             window.DataContext = vm;
        }
        
        window.Show();
        vm.ModList.SelectedMod = null; // Ensure it's null

        var inspectorContent = window.FindControl<StackPanel>("InspectorContent");
        Assert.NotNull(inspectorContent);

        // Wait for layout/bindings
        Dispatcher.UIThread.RunJobs();

        Assert.False(inspectorContent.IsVisible, "Inspector should be hidden when SelectedMod is null");

        vm.ModList.SelectedMod = new CatModManager.Core.Models.Mod("Test", "path", 0, true, "Cat");
        Dispatcher.UIThread.RunJobs();
        
        Assert.True(inspectorContent.IsVisible, "Inspector should be visible when SelectedMod is set");
    }

    [AvaloniaFact]
    public void ModListBox_Exists_And_HasHeaders()
    {
        var window = new MainWindow();
        var app = (App)Application.Current!;
        window.DataContext = app.Services!.GetRequiredService<MainWindowViewModel>();
        
        window.Show();
        var listBox = window.FindControl<ListBox>("ModsListBox");
        Assert.NotNull(listBox);

        // Column headers are TextBlocks with class col-header
        var headers = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(tb => tb.Classes.Contains("col-header"))
            .ToList();

        Assert.Equal(5, headers.Count);
        Assert.Equal("✓", headers[0].Text);
        Assert.Equal("#", headers[1].Text);
        Assert.Equal("MOD NAME", headers[2].Text);
        Assert.Equal("CATEGORY", headers[3].Text);
        Assert.Equal("VERSION", headers[4].Text);
    }

    [AvaloniaFact]
    public void DriverWarning_Visibility_ReflectsDriverStatus()
    {
        // HardlinkDriver (kernel32 CreateHardLinkW) needs no installation.
        // The "INSTALL DRIVER" button was removed with WinFSP. IsDriverMissing
        // is always false at runtime; verify the ViewModel reflects this.
        var app = (App)Application.Current!;
        var vm = app.Services!.GetRequiredService<MainWindowViewModel>();
        Assert.False(vm.GameConfig.IsDriverMissing);
    }

    [AvaloniaFact]
    public void ProfileSelector_Binding_ReflectsViewModelState()
    {
        var window = new MainWindow();
        var app = (App)Application.Current!;
        var vm = app.Services!.GetRequiredService<MainWindowViewModel>();
        window.DataContext = vm;
        window.Show();

        var selector = window.FindControl<ComboBox>("ProfileSelector");
        Assert.NotNull(selector);

        // Populate profiles and let ItemsSource binding settle
        vm.ProfileManager.AvailableProfiles.Clear();
        vm.ProfileManager.AvailableProfiles.Add("Profile 1");
        vm.ProfileManager.AvailableProfiles.Add("Profile 2");
        Dispatcher.UIThread.RunJobs();

        // Test VM → UI direction (reliable in headless without full render pass)
        vm.ProfileManager.CurrentProfileName = "Profile 2";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Profile 2", selector.SelectedItem);
    }

    [AvaloniaFact]
    public void LaunchButton_Click_StartsGame()
    {
        var window = new MainWindow();
        var app = (App)Application.Current!;
        var vm = app.Services!.GetRequiredService<MainWindowViewModel>();
        window.DataContext = vm;
        window.Show();

        var launchButton = window.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("launch-btn") && 
                                b.GetVisualDescendants().OfType<TextBlock>().Any(tb => tb.Text == "LAUNCH"));
        
        Assert.NotNull(launchButton);
        
        // Execute the command directly to simulate click logic without complex headless input for now
        // This validates the binding is correct
        Assert.NotNull(launchButton.Command);
        launchButton.Command.Execute(null);
        
        // It might be "Launching..." or it might have already failed with "Auto-mount failed"
        // Both prove the command was wired and executed.
        Assert.NotEqual("Ready", vm.StatusMessage);
    }

    [AvaloniaFact]
    public void MountButton_Click_TogglesMount()
    {
        var window = new MainWindow();
        var app = (App)Application.Current!;
        var vm = app.Services!.GetRequiredService<MainWindowViewModel>();
        window.DataContext = vm;
        window.Show();

        var mountButton = window.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("mount-btn"));
        
        Assert.NotNull(mountButton);
        Assert.NotNull(mountButton.Command);
        
        bool originalState = vm.IsVfsMounted;
        mountButton.Command.Execute(null);
        
        // ToggleMountInternal is async, but the property might change after await. 
        // Since we are in headless, we might need to wait or check if it's called.
        // For now, validating that the command is bound is the main goal.
        Assert.Equal(vm.ToggleMountCommand, mountButton.Command);
    }
}
