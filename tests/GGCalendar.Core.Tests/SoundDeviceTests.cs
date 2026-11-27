using System.Diagnostics;
using GGCalendar.App.Services;
using NAudio.Wave;

namespace GGCalendar.Core.Tests;

/// <summary>A test that needs a sound card: run only with GGCALENDAR_TEST_AUDIO=1, alone, because a failure can end the test process.</summary>
public sealed class SoundCardFactAttribute : FactAttribute
{
    public SoundCardFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GGCALENDAR_TEST_AUDIO") != "1") Skip = "Needs a sound card: set GGCALENDAR_TEST_AUDIO=1.";
    }
}

/// <summary>The real audio output (issue #105). Everything is played at volume zero.</summary>
public class SoundDeviceTests
{
    private static string Sound => Path.Combine(SoundService.MediaFolder, "Alarm01.wav");

    [SoundCardFact]
    [Trait("TestCase", "TM486")]
    public async Task TM486_TheRealOutputPlaysAFileToTheEndAndRepeatsALoop()
    {
        TimeSpan length;
        using (var probe = new AudioFileReader(Sound)) length = probe.TotalTime;

        // Once: the output reports the end, without errors, after about the length of the file.
        using (var reader = new AudioFileReader(Sound) { Volume = 0 })
        using (var output = SoundService.CreateDefaultOutput())
        {
            var ended = new TaskCompletionSource<Exception?>();
            output.PlaybackStopped += (_, e) => ended.TrySetResult(e.Exception);
            var watch = Stopwatch.StartNew();
            output.Init(reader);
            output.Play();
            Assert.Null(await ended.Task.WaitAsync(length + TimeSpan.FromSeconds(5)));
            Assert.InRange(watch.Elapsed, length - TimeSpan.FromSeconds(0.5), length + TimeSpan.FromSeconds(3));
        }

        // In a loop: still playing well after the end of the file, on the same output because there are no errors.
        var made = new List<IWavePlayer>();
        using var service = new SoundService(() =>
        {
            made.Add(SoundService.CreateDefaultOutput());
            return made[^1];
        });
        Assert.True(service.Play(Sound, 0, loop: true));
        await Task.Delay(length * 2 + TimeSpan.FromSeconds(1));
        Assert.Equal(PlaybackState.Playing, Assert.Single(made).PlaybackState);
    }

    [SoundCardFact]
    [Trait("TestCase", "TM487")]
    public void TM487_StoppingASoundJustStartedManyTimesDoesNotCrash()
    {
        // The crash of the issue: the sound stopped a few milliseconds after its start, with the processor busy and
        // the memory released a moment before taken again at once. A failure ends the process.
        var busy = true;
        var load = Enumerable.Range(0, Environment.ProcessorCount)
            .Select(_ => new Thread(() => { while (Volatile.Read(ref busy)) { } }) { IsBackground = true })
            .ToList();
        load.ForEach(t => t.Start());
        try
        {
            var random = new Random(1);
            using var service = new SoundService(SoundService.CreateDefaultOutput);
            for (var i = 0; i < 3000; i++)
            {
                Assert.True(service.Play(Sound, 0, loop: true), $"cycle {i}");
                var watch = Stopwatch.StartNew();
                var wait = random.Next(0, 5000);
                while (watch.Elapsed.TotalMicroseconds < wait) { }
                service.Stop();

                for (var k = 0; k < 300; k++) Array.Fill(new byte[random.Next(1000, 60000)], (byte)0xCD);
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            }
        }
        finally
        {
            Volatile.Write(ref busy, false);
        }
    }
}
