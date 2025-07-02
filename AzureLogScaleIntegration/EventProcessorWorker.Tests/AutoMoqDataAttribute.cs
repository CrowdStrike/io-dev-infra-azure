using AutoFixture;
using AutoFixture.AutoMoq;
using AutoFixture.Xunit2;

namespace EventProcessorWorker.Test;

public class AutoMoqDataAttribute : AutoDataAttribute
{
    public AutoMoqDataAttribute() : base(() =>
        new Fixture().Customize(new AutoMoqCustomization()))
    {
    }
}