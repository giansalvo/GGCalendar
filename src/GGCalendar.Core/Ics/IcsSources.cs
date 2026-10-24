using System.Text;

namespace GGCalendar.Core.Ics;

/// <summary>How a .ics file is read from disk: at most <see cref="MaxBytes"/>, in whatever encoding it was written.</summary>
public static class IcsSources
{
    /// <summary>The longest file that is read (the same ceiling as a downloaded one).</summary>
    public const int MaxBytes = 10 * 1024 * 1024;

    /// <summary>The bytes of the stream, or null when there are more than <paramref name="max"/> (the rest is not read).</summary>
    /// <remarks>Skeleton: reads everything, with no limit yet (T86).</remarks>
    public static byte[]? ReadLimited(Stream input, int max = MaxBytes)
    {
        using var all = new MemoryStream();
        input.CopyTo(all);
        return all.ToArray();
    }

    /// <summary>UTF-8 (with or without BOM) and UTF-16; a file that is not valid UTF-8 is read as Windows-1252.</summary>
    /// <remarks>Skeleton: UTF-8 only, with the BOM taken off (T58).</remarks>
    public static string Decode(byte[] bytes) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes).TrimStart('﻿');
}
