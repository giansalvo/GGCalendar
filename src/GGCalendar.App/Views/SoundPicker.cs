using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GGCalendar.App.Services;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>The file picker for the sound of a reminder: only .mp3 and .wav, and a file that is too large or does not open as audio is refused with a message (issue #32).</summary>
public static class SoundPicker
{
    /// <summary>The path of the file chosen, or null when the user gave up or the file cannot be used (then a message says why).</summary>
    public static async Task<string?> PickAsync(Window owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("common.choose_sound_title"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L.T("common.audio_files")) { Patterns = ["*.mp3", "*.wav"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return null;

        var key = SoundFileCheck.Check(path) switch
        {
            SoundFileProblem.None => null,
            SoundFileProblem.TooBig => "sound.too_big",
            _ => "sound.not_audio",
        };
        if (key is null) return path;

        await new WarningWindow(L.T("sound.problem_title"), L.T(key)).ShowDialog(owner);
        return null;
    }
}
