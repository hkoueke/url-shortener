namespace UrlShortener.Api.Core;

/// <summary>Converts numeric identifiers to and from Base62 strings.</summary>
public static class Base62Converter
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>Encodes a positive numeric value to Base62.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>A Base62 encoded string.</returns>
    public static string Encode(long value)
    {
        if (value == 0) return "0";
        Span<char> buffer = stackalloc char[11];
        var i = buffer.Length;
        while (value > 0)
        {
            buffer[--i] = Alphabet[(int)(value % 62)];
            value /= 62;
        }
        return new string(buffer[i..]);
    }

    /// <summary>Decodes a Base62 string to its numeric value.</summary>
    /// <param name="input">The Base62 string.</param>
    /// <returns>The decoded numeric value.</returns>
    public static long Decode(string input) => input.Aggregate(0L, (acc, c) => acc * 62 + Alphabet.IndexOf(c));
}
