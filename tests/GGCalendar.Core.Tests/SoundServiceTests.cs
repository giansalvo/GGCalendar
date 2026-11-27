using GGCalendar.App.Services;
using GGCalendar.Core.Tests.Support;

namespace GGCalendar.Core.Tests;

/// <summary>How the sound service starts and releases the audio output (issue #105): with a fake output, no sound card.</summary>
public sealed class SoundServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "ggcal-suono-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
    private readonly List<FakeOutput> _outputs = [];
    private readonly SoundService _service;

    public SoundServiceTests()
    {
        WriteWav(_file);
        _service = new SoundService(NewFake);
    }

    public void Dispose()
    {
        _service.Dispose();
        File.Delete(_file);
    }

    private FakeOutput NewFake()
    {
        var output = new FakeOutput();
        _outputs.Add(output);
        return output;
    }

    /// <summary>A tenth of a second of silence, 8 kHz mono.</summary>
    private static void WriteWav(string path)
    {
        using var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(8000, 16, 1));
        writer.Write(new byte[1600], 0, 1600);
    }

    [Fact]
    [Trait("TestCase", "T478")]
    public void T478_PlayStartsOneOutputAndStopReleasesItBeforeTheFile()
    {
        Assert.True(_service.Play(_file));
        var output = Assert.Single(_outputs);
        Assert.Equal(["init", "play"], output.Calls);
        var readable = false;
        output.Disposing = () => readable = output.Source!.Read(new byte[32], 0, 32) > 0; // the file is still open

        _service.Stop();
        _service.Stop();

        Assert.True(readable);
        Assert.Equal(["init", "play", "dispose"], output.Calls);
        File.Delete(_file); // and now it is let go of
    }

    [Fact]
    [Trait("TestCase", "T479")]
    public void T479_ANewSoundReleasesThePreviousOne()
    {
        _service.Play(_file, loop: true);
        _service.Play(_file);

        Assert.Equal(2, _outputs.Count);
        Assert.True(_outputs[0].Disposed);
        Assert.False(_outputs[1].Disposed);
    }

    [Fact]
    [Trait("TestCase", "T480")]
    public void T480_AFileThatCannotBePlayedUsesNoOutput()
    {
        var damaged = _file + ".damaged.wav";
        File.WriteAllText(damaged, "non è un suono");
        try
        {
            Assert.False(_service.Play(null));
            Assert.False(_service.Play(_file + ".missing"));
            Assert.False(_service.Play(damaged));
            Assert.Empty(_outputs);
        }
        finally
        {
            File.Delete(damaged);
        }
    }

    [Fact]
    [Trait("TestCase", "T481")]
    public void T481_AnOutputThatDoesNotOpenIsReleased()
    {
        var broken = new FakeOutput { InitError = new System.Runtime.InteropServices.COMException("no device") };
        using var service = new SoundService(() => broken);

        Assert.False(service.Play(_file));
        Assert.True(broken.Disposed);

        // No device at all: making the output fails.
        using var without = new SoundService(() => throw new System.Runtime.InteropServices.COMException("no device"));
        Assert.False(without.Play(_file));
        File.Delete(_file);
    }

    [Fact]
    [Trait("TestCase", "T482")]
    public void T482_ASoundInALoopStartsAgainFromTheBeginning()
    {
        _service.Play(_file, loop: true);
        var output = Assert.Single(_outputs);
        var source = (NAudio.Wave.WaveStream)output.Source!;
        source.Position = source.Length;

        output.End();

        Assert.Single(_outputs);
        Assert.Equal(["init", "play", "play"], output.Calls);
        Assert.Equal(0, source.Position);
    }

    [Fact]
    [Trait("TestCase", "T483")]
    public void T483_ASoundPlayedOnceIsReleasedAtItsEnd()
    {
        _service.Play(_file);

        _outputs[0].End();

        Assert.True(Assert.Single(_outputs).Disposed);
        File.Delete(_file);
    }

    [Fact]
    [Trait("TestCase", "T484")]
    public void T484_AfterAnErrorALoopMovesToANewOutputAFewTimesOnly()
    {
        _service.Play(_file, loop: true);

        for (var i = 0; i < SoundService.ErrorRestarts; i++)
        {
            _outputs[^1].End(new InvalidOperationException("device removed"));
            Assert.Equal(i + 2, _outputs.Count);
            Assert.True(_outputs[^2].Disposed);
            Assert.Equal(["init", "play"], _outputs[^1].Calls);
        }

        _outputs[^1].End(new InvalidOperationException("device removed"));

        Assert.Equal(SoundService.ErrorRestarts + 1, _outputs.Count);
        Assert.All(_outputs, o => Assert.True(o.Disposed));
    }

    [Fact]
    [Trait("TestCase", "T484")]
    public void T484_AnErrorOfASoundPlayedOnceOnlyReleasesIt()
    {
        _service.Play(_file);

        _outputs[0].End(new InvalidOperationException("device removed"));

        Assert.True(Assert.Single(_outputs).Disposed);
    }

    [Fact]
    [Trait("TestCase", "T485")]
    public void T485_TheEndOfAReplacedSoundChangesNothing()
    {
        _service.Play(_file, loop: true);
        _service.Play(_file, loop: true);

        _outputs[0].End();
        _outputs[0].End(new InvalidOperationException("late"));

        Assert.Equal(2, _outputs.Count);
        Assert.Equal(["init", "play", "dispose"], _outputs[0].Calls);
        Assert.Equal(["init", "play"], _outputs[1].Calls);
    }
}
