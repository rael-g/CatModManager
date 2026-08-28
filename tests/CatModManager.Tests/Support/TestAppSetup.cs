using Avalonia;
using Avalonia.Headless;
using CatModManager.Ui;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(CatModManager.Tests.Support.TestAppBuilder))]

// Avalonia's headless session is one dispatcher shared by the whole assembly, and seven test
// classes now use [AvaloniaFact]. xUnit runs separate collections in parallel by default, so two of
// them could enter Dispatcher.PushFrame at once — which fails with
// "PlatformNotSupportedException: Operation is not supported on this platform", on whichever
// AvaloniaFact happened to be running. That produced an intermittent failure that moved between
// tests and never reproduced in isolation, so it read as a flaky test rather than as the harness.
//
// The alternative — putting every AvaloniaFact class in one named collection — leaves the trap
// armed for the next class that forgets the attribute. The suite runs in about three seconds, so
// serialising it costs nothing worth measuring.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CatModManager.Tests.Support;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
