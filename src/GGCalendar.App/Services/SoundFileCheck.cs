using System;
using System.IO;
using GGCalendar.Core.Reminders;
using NAudio.Wave;

namespace GGCalendar.App.Services;

/// <summary>Why a file cannot be the sound of a reminder (issue #32).</summary>
public enum SoundFileProblem { None, Missing, TooBig, NotAudio }

/// <summary>Checks a sound chosen by the user: it must be there, be no larger than 10 MB, be a .mp3 or .wav file and open as audio that lasts.</summary>
public static class SoundFileCheck
{
    public static SoundFileProblem Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return SoundFileProblem.Missing;
        if (Array.IndexOf(ReminderSounds.Extensions, Path.GetExtension(path).ToLowerInvariant()) < 0) return SoundFileProblem.NotAudio;
        try
        {
            if (new FileInfo(path).Length > ReminderSounds.MaxFileBytes) return SoundFileProblem.TooBig;
            using var reader = new AudioFileReader(path);
            return reader.TotalTime > TimeSpan.Zero ? SoundFileProblem.None : SoundFileProblem.NotAudio;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or FormatException or IOException
                                       or UnauthorizedAccessException or NAudio.MmException or ArgumentException)
        {
            return SoundFileProblem.NotAudio;
        }
    }
}
