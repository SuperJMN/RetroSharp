namespace RetroSharp.Core;

public static class IntegerLiteral
{
    public static bool TryParse(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        var sign = 1;
        if (span[0] is '+' or '-')
        {
            sign = span[0] == '-' ? -1 : 1;
            span = span[1..];
            if (span.IsEmpty)
            {
                return false;
            }
        }

        var numberBase = 10;
        if (span.Length >= 2 && span[0] == '0' && (span[1] is 'x' or 'X'))
        {
            numberBase = 16;
            span = span[2..];
        }
        else if (span.Length >= 2 && span[0] == '0' && (span[1] is 'b' or 'B'))
        {
            numberBase = 2;
            span = span[2..];
        }

        span = StripSuffix(span);
        if (!IsValidDigits(span, numberBase))
        {
            return false;
        }

        try
        {
            var parsed = 0;
            foreach (var ch in span)
            {
                if (ch == '_')
                {
                    continue;
                }

                parsed = checked(parsed * numberBase + DigitValue(ch));
            }

            value = checked(parsed * sign);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static int Parse(string text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException($"Invalid integer literal '{text}'.");
        }

        return value;
    }

    private static ReadOnlySpan<char> StripSuffix(ReadOnlySpan<char> span)
    {
        foreach (var suffix in new[] { "u16", "i16", "u8", "i8" })
        {
            if (span.EndsWith(suffix.AsSpan(), StringComparison.Ordinal))
            {
                return span[..^suffix.Length];
            }
        }

        return span;
    }

    private static bool IsValidDigits(ReadOnlySpan<char> span, int numberBase)
    {
        if (span.IsEmpty || span[0] == '_' || span[^1] == '_')
        {
            return false;
        }

        var previousWasSeparator = false;
        foreach (var ch in span)
        {
            if (ch == '_')
            {
                if (previousWasSeparator)
                {
                    return false;
                }

                previousWasSeparator = true;
                continue;
            }

            if (DigitValue(ch) >= numberBase)
            {
                return false;
            }

            previousWasSeparator = false;
        }

        return true;
    }

    private static int DigitValue(char ch)
    {
        if (ch is >= '0' and <= '9')
        {
            return ch - '0';
        }

        if (ch is >= 'a' and <= 'f')
        {
            return ch - 'a' + 10;
        }

        if (ch is >= 'A' and <= 'F')
        {
            return ch - 'A' + 10;
        }

        return int.MaxValue;
    }
}
