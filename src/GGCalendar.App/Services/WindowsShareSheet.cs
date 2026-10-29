using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace GGCalendar.App.Services;

/// <summary>
/// The share sheet of Windows ("Other apps…"): it lists the apps of the user that accept text and files, and gets the text of the event and its .ics file.
/// An app that is not packaged has to ask for the sheet through the window (<c>IDataTransferManagerInterop</c>).
/// </summary>
public static class WindowsShareSheet
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid DataTransferManagerId = new("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");

    private static DataTransferManager? _manager;
    private static IntPtr _managerWindow;
    private static string _title = "";
    private static string _text = "";
    private static string? _icsPath;

    /// <summary>Shows the sheet over the window <paramref name="window"/> (its handle); the file is written in a temporary folder first.</summary>
    public static async Task ShowAsync(IntPtr window, CalendarEvent ev, string text)
    {
        if (window == IntPtr.Zero) throw new InvalidOperationException("window");
        _title = ev.Title;
        _text = text;
        _icsPath = WriteIcs(ev);

        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        if (_manager is null || _managerWindow != window)
        {
            var id = DataTransferManagerId;
            var abi = interop.GetForWindow(window, ref id);
            _manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(abi);
            _manager.DataRequested += OnDataRequested;
            _managerWindow = window;
        }

        interop.ShowShareUIForWindow(window);
        await Task.CompletedTask;
    }

    private static void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var request = args.Request;
        request.Data.Properties.Title = string.IsNullOrWhiteSpace(_title) ? "GGCalendar" : _title;
        request.Data.SetText(_text);
        if (_icsPath is null) return;

        // The file is read when the app chosen asks for it: the deferral keeps the request open until it is ready.
        var deferral = request.GetDeferral();
        _ = Task.Run(async () =>
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(_icsPath);
                request.Data.SetStorageItems(new List<IStorageItem> { file });
            }
            catch (Exception)
            {
                // Without the file the text is still shared.
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    /// <summary>Writes the .ics file in a folder of its own in the temporary one, after removing what an earlier share left there.</summary>
    private static string WriteIcs(CalendarEvent ev)
    {
        var folder = Path.Combine(Path.GetTempPath(), "GGCalendar-share");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "*.ics"))
        {
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        var name = string.Concat((string.IsNullOrWhiteSpace(ev.Title) ? "evento" : ev.Title.Trim())
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(folder, name + ".ics");
        File.WriteAllText(path, IcsWriter.Write([ev], includeAttendees: false), new UTF8Encoding(false));
        return path;
    }
}
