using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CatModManager.Core.Services;
using CatModManager.Ui.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Avalonia;
using CatModManager.Ui;
using Avalonia.Headless.XUnit;
using CatModManager.Ui.Views;

namespace CatModManager.Tests;

public class ProfileManagementTests
{
    private static Task RefreshProfilesAsync(MainWindowViewModel vm)
        => vm.ProfileManager.RefreshListAsync();

    [AvaloniaFact]
    public async Task DeleteProfile_ShouldWork_And_CreateNewProfile()
    {
        var window = new MainWindow();
        var app = (App)Application.Current!;
        var vm = app.Services!.GetRequiredService<MainWindowViewModel>();
        var pathService = app.Services!.GetRequiredService<ICatPathService>();
        
        // Ensure profiles directory exists
        if (!Directory.Exists(pathService.ProfilesPath)) Directory.CreateDirectory(pathService.ProfilesPath);

        // Create a profile to delete
        string profileName = "ProfileToDelete";
        string profilePath = pathService.GetProfilePath(profileName);
        File.WriteAllText(profilePath, "Name = \"ProfileToDelete\"");

        // Refresh to ensure it's in AvailableProfiles
        await RefreshProfilesAsync(vm);
        
        // Setup VM state
        vm.ProfileManager.CurrentProfileName = profileName;
        
        Assert.Contains(profileName, vm.ProfileManager.AvailableProfiles);
        Assert.True(File.Exists(profilePath));

        // Delete the profile - This should NOT deadlock
        var deleteAction = vm.ProfileManager.DeleteProfileCommand.ExecuteAsync(null);
        
        // Wait for it with a timeout to detect deadlock
        var delayTask = Task.Delay(2000);
        var completedTask = await Task.WhenAny(deleteAction, delayTask);
        
        if (completedTask == delayTask)
        {
            throw new System.Exception("DeleteProfile deadlocked!");
        }

        // Assertions
        Assert.DoesNotContain(profileName, vm.ProfileManager.AvailableProfiles);
        Assert.False(File.Exists(profilePath));
        Assert.NotNull(vm.ProfileManager.CurrentProfileName);
        Assert.NotEqual(profileName, vm.ProfileManager.CurrentProfileName);
    }
}
