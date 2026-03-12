using IntLimiter.Core.Models;
using Xunit;

namespace IntLimiter.Core.Tests;

public class UnitConversionTests
{
    [Fact]
    public void ConvertFromBytesPerSecond_ToMegabytesPerSecond_ReturnsCorrectValue()
    {
        long bytes = 1048576; // 1 MB
        var result = NetworkUnitConverter.ConvertFromBytesPerSecond(bytes, NetworkUnit.MegabytesPerSecond);
        Assert.Equal(1.0, result);
    }
    
    [Fact]
    public void ConvertFromBytesPerSecond_ToKilobitsPerSecond_ReturnsCorrectValue()
    {
        long bytes = 125000; // 1000 kbit/s
        var result = NetworkUnitConverter.ConvertFromBytesPerSecond(bytes, NetworkUnit.KilobitsPerSecond);
        Assert.Equal(1000.0, result);
    }
}
