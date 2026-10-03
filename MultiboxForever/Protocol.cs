namespace MultiboxForever;

/// <summary>
/// Tiny LAN protocol: one ASCII key token per line, terminated by '\n'.
/// Supported tokens: digits 0-9, '-' (minus), '+' (plus/equals key).
/// Examples: "7\n", "-\n", "+\n".
/// </summary>
internal static class Protocol
{
    public const int DefaultPort = 47211;

    // US keyboard main row (to the right of 0): -_ and =/+
    public const uint VK_OEM_MINUS = 0xBD;
    public const uint VK_OEM_PLUS = 0xBB;
    // Numpad fallbacks
    public const uint VK_SUBTRACT = 0x6D;
    public const uint VK_ADD = 0x6B;

    /// <summary>Human-readable list of chords for UI and docs.</summary>
    public const string SupportedKeysDescription =
        "0–9, Minus (-), and Plus (=/+ key on a US keyboard)";

    public static bool IsSupportedKey(char key) =>
        key is >= '0' and <= '9' or '-' or '+';

    public static bool TryParseKey(string? line, out char key)
    {
        key = '\0';
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        line = line.Trim();
        if (line.Length != 1 || !IsSupportedKey(line[0]))
        {
            return false;
        }

        key = line[0];
        return true;
    }

    public static string FormatKey(char key)
    {
        if (!IsSupportedKey(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), "Supported keys are 0-9, '-', and '+'.");
        }

        return key + "\n";
    }

    public static string DescribeKey(char key) => key switch
    {
        >= '0' and <= '9' => key.ToString(),
        '-' => "Minus (-)",
        '+' => "Plus (=/+)",
        _ => key.ToString()
    };

    /// <summary>
    /// Maps a virtual-key code to a protocol token.
    /// US layout: VK_OEM_MINUS is the -_ key; VK_OEM_PLUS is the =/+ key.
    /// Numpad minus/plus are accepted and mapped to the same tokens.
    /// </summary>
    public static bool TryMapVirtualKeyToKey(uint vkCode, out char key)
    {
        if (vkCode is >= 0x30 and <= 0x39)
        {
            key = (char)('0' + (vkCode - 0x30));
            return true;
        }

        if (vkCode is >= 0x60 and <= 0x69)
        {
            key = (char)('0' + (vkCode - 0x60));
            return true;
        }

        if (vkCode is VK_OEM_MINUS or VK_SUBTRACT)
        {
            key = '-';
            return true;
        }

        if (vkCode is VK_OEM_PLUS or VK_ADD)
        {
            key = '+';
            return true;
        }

        key = '\0';
        return false;
    }

    /// <summary>
    /// Virtual key to inject for a protocol token (bare key, no modifiers).
    /// '+' injects VK_OEM_PLUS (US =/+ key), not Shift+Equals.
    /// </summary>
    public static ushort ToVirtualKey(char key) => key switch
    {
        >= '0' and <= '9' => (ushort)key,
        '-' => (ushort)VK_OEM_MINUS,
        '+' => (ushort)VK_OEM_PLUS,
        _ => throw new ArgumentOutOfRangeException(nameof(key), "Supported keys are 0-9, '-', and '+'.")
    };
}
