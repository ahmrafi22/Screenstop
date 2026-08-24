using Screenstop.Core;
using Xunit;

namespace Screenstop.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads()
    {
        Assert.Equal("Screenstop.Core", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
