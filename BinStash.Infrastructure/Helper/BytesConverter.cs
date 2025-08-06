namespace BinStash.Infrastructure.Helper;

public static class BytesConverter
{
    public static double ConvertBytesToKB(long bytes)
    {
        return bytes / 1024f;
    }

    public static double ConvertBytesToMB(long bytes)
    {
        return ConvertBytesToKB(bytes) / 1024f;
    }

    public static double ConvertBytesToGB(long bytes)
    {
        return ConvertBytesToMB(bytes) / 1024f;
    }

    public static double ConvertBytesToTB(long bytes)
    {
        return ConvertBytesToGB(bytes) / 1024f;
    }

    public static double ConvertBytesToPB(long bytes)
    {
        return ConvertBytesToTB(bytes) / 1024f;
    }

    public static string BytesToHuman(long size)
    {
        var kb = 1 * 1024L;
        var mb = kb * 1024;
        var gb = mb * 1024;
        var tb = gb * 1024;
        var pb = tb * 1024;
        var eb = pb * 1024;

        if (size == 0) return "0 Mb";
        if (size < kb) return FloatForm(size) + " byte";
        if (size < mb) return FloatForm((double)size / kb) + " Kb";
        if (size < gb) return FloatForm((double)size / mb) + " Mb";
        if (size < tb) return FloatForm((double)size / gb) + " Gb";
        if (size < pb) return FloatForm((double)size / tb) + " Tb";
        if (size < eb) return FloatForm((double)size / pb) + " Pb";
        return FloatForm((double)size / eb) + " Eb";
    }

    public static string FloatForm(double d)
    {
        return Math.Round(d, 2).ToString("##.##");
    }
}