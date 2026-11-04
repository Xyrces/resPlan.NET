using System.Globalization;

namespace ResPlan.Library.Data.PickleAdapters;

/// <summary>Formats scalar IDs using NumPy's precision-specific shortest representation.</summary>
internal static class NumpyFloatFormatter
{
    internal static string Format(Half value)
        => Format((double)value, value.ToString("R", CultureInfo.InvariantCulture), 1e3);

    internal static string Format(float value)
        => Format(value, value.ToString("R", CultureInfo.InvariantCulture), 1e6);

    private static string Format(double value, string text, double maximumPositional)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        var sign = double.IsNegative(value) ? "-" : "";
        if (value == 0) return sign + "0.0";
        if (sign.Length != 0) text = text[1..];

        // Keep the shortest digits for the original precision; expanding the
        // scalar to double before formatting introduces digits NumPy omits.
        var exponentIndex = text.IndexOf('E');
        var exponent = exponentIndex < 0 ? 0 : int.Parse(text.AsSpan(exponentIndex + 1), CultureInfo.InvariantCulture);
        var significand = exponentIndex < 0 ? text : text[..exponentIndex];
        var point = significand.IndexOf('.');
        var digits = significand.Replace(".", "", StringComparison.Ordinal);
        var leadingZeros = digits.Length - digits.TrimStart('0').Length;
        var decimalPosition = (point < 0 ? significand.Length : point) + exponent - leadingZeros;
        digits = digits[leadingZeros..].TrimEnd('0');
        exponent = decimalPosition - 1;

        // NumPy chooses notation using the binary value, not the rounded
        // decimal exponent. For example float32(0.0001) is below 1e-4.
        var magnitude = Math.Abs(value);
        if (magnitude < 1e-4 || magnitude >= maximumPositional)
        {
            var mantissa = digits.Length == 1 ? digits : digits.Insert(1, ".");
            return sign + mantissa + "e" + (exponent < 0 ? "-" : "+")
                + Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
        }
        if (decimalPosition <= 0)
            return sign + "0." + new string('0', -decimalPosition) + digits;
        if (decimalPosition >= digits.Length)
            return sign + digits + new string('0', decimalPosition - digits.Length) + ".0";
        return sign + digits.Insert(decimalPosition, ".");
    }
}
