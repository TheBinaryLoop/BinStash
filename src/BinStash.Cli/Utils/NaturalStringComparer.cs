using System.Text.RegularExpressions;

namespace BinStash.Cli.Utils;

public class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();
    
    private static readonly Regex SplitRegex = new(@"(\d+|\D+)", RegexOptions.Compiled);

    public int Compare(string? x, string? y)
    {
        if (x == y) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        var xParts = SplitRegex.Matches(x);
        var yParts = SplitRegex.Matches(y);

        var partCount = Math.Min(xParts.Count, yParts.Count);

        for (var i = 0; i < partCount; i++)
        {
            var xPart = xParts[i].Value;
            var yPart = yParts[i].Value;

            var xIsNumber = int.TryParse(xPart, out var xNum);
            var yIsNumber = int.TryParse(yPart, out var yNum);

            int result;
            if (xIsNumber && yIsNumber)
            {
                result = xNum.CompareTo(yNum);
            }
            else
            {
                result = string.Compare(xPart, yPart, StringComparison.OrdinalIgnoreCase);
            }

            if (result != 0)
                return result;
        }

        return xParts.Count.CompareTo(yParts.Count);
    }
}
