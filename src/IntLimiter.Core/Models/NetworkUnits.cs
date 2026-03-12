namespace IntLimiter.Core.Models;

public enum NetworkUnit
{
    BytesPerSecond,
    KilobytesPerSecond,
    MegabytesPerSecond,
    KilobitsPerSecond,
    MegabitsPerSecond
}

public static class NetworkUnitConverter
{
    public static double ConvertFromBytesPerSecond(long bytesPerSecond, NetworkUnit targetUnit)
    {
        return targetUnit switch
        {
            NetworkUnit.BytesPerSecond => bytesPerSecond,
            NetworkUnit.KilobytesPerSecond => bytesPerSecond / 1024.0,
            NetworkUnit.MegabytesPerSecond => bytesPerSecond / (1024.0 * 1024.0),
            NetworkUnit.KilobitsPerSecond => (bytesPerSecond * 8) / 1000.0,
            NetworkUnit.MegabitsPerSecond => (bytesPerSecond * 8) / 1000000.0,
            _ => bytesPerSecond
        };
    }
}
