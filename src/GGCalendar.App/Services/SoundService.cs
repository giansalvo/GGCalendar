using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GGCalendar.App.Services;

/// <summary>Plays .wav/.mp3 files (Windows system sounds or user files) through NAudio.</summary>
public sealed class SoundService : IDisposable
{
    /// <summary>How many times a sound in a loop is started again on a new output after an error (the device was unplugged, for example).</summary>
    public const int ErrorRestarts = 3;

    private readonly Func<IWavePlayer>? _createOutput;
    private IWavePlayer? _output;
    private AudioFileReader? _reader;

    /// <param name="createOutput">The audio output of each playback; <see cref="NewOutput"/> when not given.</param>
    public SoundService(Func<IWavePlayer>? createOutput = null) => _createOutput = createOutput;

    /// <summary>
    /// The audio output used when a service is not given one; the tests replace it to leave the sound card alone.
    /// It must be one whose Stop waits for its playback thread: WaveOutEvent does not, and releasing it while it played
    /// left the audio driver with memory already freed, which crashed the process (issue #105).
    /// </summary>
    public static Func<IWavePlayer> NewOutput { get; set; } = CreateDefaultOutput;

    /// <summary>The default output device of Windows, shared with the other programs.</summary>
    public static IWavePlayer CreateDefaultOutput() => new WasapiOut(AudioClientShareMode.Shared, 200);

    public static string MediaFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <returns>True when the sound started; false when there is nothing to play or the file cannot be played (the caller may try another sound).</returns>
    public bool Play(string? path, int volumePercent = 80, bool loop = false)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            _reader = new AudioFileReader(path) { Volume = Math.Clamp(volumePercent, 0, 100) / 100f };
            Start(_reader, loop, ErrorRestarts);
            return true;
        }
        catch (Exception ex) when (CannotPlay(ex))
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
        output?.Dispose(); // first the output: when it returns nothing reads the file any more
        reader?.Dispose();
    }

    public void Dispose() => Stop();

    private static bool CannotPlay(Exception ex) =>
        ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException or FormatException or IOException or NAudio.MmException;

    private void Start(AudioFileReader reader, bool loop, int restarts)
    {
        var output = (_createOutput ?? NewOutput)();
        _output = output;
        // Raised on the thread that created the output (the UI one in the app).
        output.PlaybackStopped += (_, e) => OnPlaybackStopped(output, reader, loop, restarts, e.Exception);
        output.Init(reader);
        output.Play();
    }

    private void OnPlaybackStopped(IWavePlayer output, AudioFileReader reader, bool loop, int restarts, Exception? error)
    {
        if (!ReferenceEquals(_output, output)) return; // a playback already stopped or replaced
        try
        {
            if (loop && error is null)
            {
                reader.Position = 0;
                output.Play();
            }
            else if (loop && restarts > 0)
            {
                // The output stays tied to the device it started on: a new one takes the device Windows uses now.
                _output = null;
                output.Dispose();
                reader.Position = 0;
                Start(reader, loop, restarts - 1);
            }
            else Stop();
        }
        catch (Exception ex) when (CannotPlay(ex))
        {
            Stop();
        }
    }
}
