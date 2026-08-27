using CatModManager.Ui.Views;
using Xunit;

namespace CatModManager.Tests.ViewModels;

/// <summary>
/// The drag offsets are differences between two nearly equal coordinates, so tiny magnitudes are
/// routine rather than exotic — and the transform parser rejects the scientific notation that
/// default double formatting reaches for, which crashed the window mid-drag.
/// </summary>
public class DragTranslateFormattingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1e-14)]      // what a fast drag actually produced: formatted as "1E-14"
    [InlineData(-1e-14)]
    [InlineData(2.5e-7)]
    [InlineData(44)]
    [InlineData(-1234.5678)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnyOffsetProducesATransformTheParserAccepts(double offset)
    {
        Assert.NotNull(DragReorderAnimator.Translate(offset));
    }

    [Fact]
    public void ARealOffsetStillTranslatesByThatMuch()
    {
        // Guards against "fix the crash by always translating by zero", which would pass the
        // theory above while silently removing the animation.
        var transform = DragReorderAnimator.Translate(-40);
        Assert.Equal(-40, transform.Value.M32, precision: 3);
    }
}
