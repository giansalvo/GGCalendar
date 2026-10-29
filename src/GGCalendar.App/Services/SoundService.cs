using System;
using System.IO;
using NAudio.Wave;

namespace GGCalendar.App.Services;

/// <summary>Plays .wav/.mp3 files (Windows system sounds or user files) through NAudio.</summary>
public sealed class SoundService : IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    public static string MediaFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <returns>True when the sound started; false when there is nothing to play or the file cannot be played (the caller may try another sound).</returns>
    public bool Play(string? path, int volumePercent = 80, bool loop = false)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            var reader = new AudioFileReader(path) { Volume = Math.Clamp(volumePercent, 0, 100) / 100f };
            var output = new WaveOutEvent();
            _reader = reader;
            _output = output;
            output.PlaybackStopped += (_, _) =>
            {
                if (loop && ReferenceEquals(_output, output))
                {
                    reader.Position = 0;
                    output.Play();
                }
                else if (ReferenceEquals(_output, output)) Stop();
            };
            output.Init(reader);
            output.Play();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or FormatException or IOException or NAudio.MmException)
        {
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        var output = _output;
        var reader = _reader;
        _output = null;
        _reader = null;
        output?.Dispose();
        reader?.Dispose();
    }

    public void Dispose() => Stop();
}
