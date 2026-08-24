using Screendrop.Core;
using Xunit;

namespace Screendrop.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads()
    {
        Assert.Equal("Screendrop.Core", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
