using System;
using System.Collections.Generic;
using System.IO;
using GGCalendar.Core;

namespace GGCalendar.App.Services;

/// <summary>
/// A small text log of the problems of the sync that the status bar does not show in full (for example "no network": the person sees a calm
/// "you are offline", the technical text stays here for whoever looks for the cause). One line per event in <c>sync.log</c> in the data
/// folder; when the file passes <see cref="MaxBytes"/> it is renamed <c>sync.old.log</c> (replacing the previous one). Writing never fails the sync.
/// </summary>
public static class SyncLog
{
    public const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => AppPaths.FileInDataDirectory("sync.log");

    /// <summary>The exception and the ones inside it as "Type: message", like "HttpRequestException: Host sconosciuto. > SocketException(HostNotFound): ...".</summary>
    public static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (var x = ex; x is not null; x = x.InnerException)
            parts.Add(x.GetType().Name + (x is System.Net.Sockets.SocketException so ? $"({so.SocketErrorCode})" : "") + ": " + x.Message);
        return string.Join(" > ", parts);
    }

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Move(path, Path.ChangeExtension(path, ".old.log"), overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line.ReplaceLineEndings(" ")}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log that cannot be written is not a reason to fail.
        }
    }
}
