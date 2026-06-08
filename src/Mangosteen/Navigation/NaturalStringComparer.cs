using System.Collections;

namespace Mangosteen.Navigation;

public sealed class NaturalStringComparer : IComparer<string>, IComparer
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(object? x, object? y)
    {
        return Compare(x?.ToString(), y?.ToString());
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var ix = 0;
        var iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            var cx = x[ix];
            var cy = y[iy];

            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                var numberCompare = CompareNumberRuns(x, ref ix, y, ref iy);
                if (numberCompare != 0) return numberCompare;
                continue;
            }

            var charCompare = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
            if (charCompare != 0) return charCompare;

            ix++;
            iy++;
        }

        return x.Length.CompareTo(y.Length);
    }

    private static int CompareNumberRuns(string x, ref int ix, string y, ref int iy)
    {
        var startX = ix;
        var startY = iy;

        while (ix < x.Length && char.IsDigit(x[ix])) ix++;
        while (iy < y.Length && char.IsDigit(y[iy])) iy++;

        var trimmedX = TrimLeadingZeros(x, startX, ix);
        var trimmedY = TrimLeadingZeros(y, startY, iy);
        var lengthCompare = (ix - trimmedX).CompareTo(iy - trimmedY);
        if (lengthCompare != 0) return lengthCompare;

        for (var i = 0; i < ix - trimmedX; i++)
        {
            var digitCompare = x[trimmedX + i].CompareTo(y[trimmedY + i]);
            if (digitCompare != 0) return digitCompare;
        }

        return (ix - startX).CompareTo(iy - startY);
    }

    private static int TrimLeadingZeros(string value, int start, int end)
    {
        while (start < end - 1 && value[start] == '0')
        {
            start++;
        }

        return start;
    }
}
