using Avalonia;
using Avalonia.Headless;
using CatModManager.Ui;

[assembly: AvaloniaTestApplication(typeof(CatModManager.Tests.TestAppBuilder))]

namespace CatModManager.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
