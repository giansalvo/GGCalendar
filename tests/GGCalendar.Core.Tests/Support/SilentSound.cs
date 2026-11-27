using System.Runtime.CompilerServices;
using GGCalendar.App.Services;
using NAudio.Wave;

namespace GGCalendar.Core.Tests.Support;

/// <summary>An audio output that plays nothing and records what it is asked: the tests do not use the sound card (issue #105).</summary>
public sealed class FakeOutput : IWavePlayer
{
    public List<string> Calls { get; } = [];
    public IWaveProvider? Source { get; private set; }
    public Exception? InitError { get; init; }

    /// <summary>Called inside Dispose, to see the state of the service while the output is released.</summary>
    public Action? Disposing { get; set; }

    public bool Disposed => Calls.Contains("dispose");
    public PlaybackState PlaybackState { get; private set; }
    public float Volume { get; set; } = 1f;
    public WaveFormat OutputWaveFormat => Source!.WaveFormat;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public void Init(IWaveProvider waveProvider)
    {
        Calls.Add("init");
        if (InitError is not null) throw InitError;
        Source = waveProvider;
    }

    public void Play()
    {
        Calls.Add("play");
        PlaybackState = PlaybackState.Playing;
    }

    public void Pause()
    {
        Calls.Add("pause");
        PlaybackState = PlaybackState.Paused;
    }

    public void Stop()
    {
        Calls.Add("stop");
        PlaybackState = PlaybackState.Stopped;
    }

    public void Dispose()
    {
        Disposing?.Invoke();
        Calls.Add("dispose");
        PlaybackState = PlaybackState.Stopped;
    }

    /// <summary>The playback ends on its own: at the end of the file, or because of an error.</summary>
    public void End(Exception? error = null)
    {
        PlaybackState = PlaybackState.Stopped;
        PlaybackStopped?.Invoke(this, new StoppedEventArgs(error));
    }
}

internal static class SilentSound
{
    [ModuleInitializer]
    internal static void Install() => SoundService.NewOutput = () => new FakeOutput();
}
