using System.Text;

namespace GGCalendar.Core.Ics;

/// <summary>How a .ics file is read from disk: at most <see cref="MaxBytes"/>, in whatever encoding it was written.</summary>
public static class IcsSources
{
    /// <summary>The longest file that is read (the same ceiling as a downloaded one).</summary>
    public const int MaxBytes = 10 * 1024 * 1024;

    /// <summary>The bytes of the stream, or null when there are more than <paramref name="max"/> (the rest is not read).</summary>
    public static byte[]? ReadLimited(Stream input, int max = MaxBytes)
    {
        using var all = new MemoryStream();
        var buffer = new byte[16 * 1024];
        long total = 0;
        int n;
        while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += n;
            if (total > max) return null;
            all.Write(buffer, 0, n);
        }

        return all.ToArray();
    }

    /// <summary>
    /// The text of a file: UTF-8 (with or without BOM) and UTF-16 (BOM, or the zeros of ASCII text); a file that is not valid
    /// UTF-8 is read as Windows-1252. Folded lines are joined on the bytes, before decoding: a producer may fold in the middle of a
    /// multi-byte character.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 4 && bytes[0] != 0 && bytes[1] == 0 && bytes[2] != 0 && bytes[3] == 0) return Encoding.Unicode.GetString(bytes);
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] != 0 && bytes[2] == 0 && bytes[3] != 0) return Encoding.BigEndianUnicode.GetString(bytes);

        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        var joined = Unfold(bytes, start);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(joined);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(joined);
        }
    }

    /// <summary>Removes every line break followed by a space or a tab, together with that one space or tab.</summary>
    private static byte[] Unfold(byte[] bytes, int start)
    {
        var result = new byte[bytes.Length - start];
        var length = 0;
        for (var i = start; i < bytes.Length; i++)
        {
            if (bytes[i] is 0x0D or 0x0A)
            {
                var next = i + 1;
                if (bytes[i] == 0x0D && next < bytes.Length && bytes[next] == 0x0A) next++;
                if (next < bytes.Length && bytes[next] is 0x20 or 0x09)
                {
                    i = next; // the line break and the space/tab that follows it are dropped
                    continue;
                }
            }

            result[length++] = bytes[i];
        }

        return result.AsSpan(0, length).ToArray();
    }
}
