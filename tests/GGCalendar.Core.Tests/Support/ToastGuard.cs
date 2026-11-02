using System.Runtime.CompilerServices;
using GGCalendar.App.Views;

namespace GGCalendar.Core.Tests.Support;

/// <summary>The tests click the buttons of a toast the moment it appears: the guard time that ignores early clicks is off, except in the test that checks it.</summary>
internal static class ToastGuard
{
    [ModuleInitializer]
    internal static void Off() => ReminderNotificationWindow.GuardTime = TimeSpan.Zero;
}
