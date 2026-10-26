using System;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Services;

/// <summary>Why a calendar could not be created or deleted on Google. The name is the key of the message (<c>gcal.problem.&lt;name&gt;</c>).</summary>
public enum GoogleCalendarProblem
{
    EmptyName, NoAccount, NoNetwork, RateLimited, NotAllowed, NotOwner, Primary, AuthExpired, Server, Other,
}

/// <summary>A creation or a deletion of a calendar that Google (or the rules of the app) did not allow; nothing was changed here.</summary>
public sealed class GoogleCalendarException(GoogleCalendarProblem problem, Exception? inner = null) : Exception(problem.ToString(), inner)
{
    public GoogleCalendarProblem Problem { get; } = problem;

    /// <summary>The reason as a message in the language shown (never the text of Google's answer: it may hold names).</summary>
    public Msg Reason => Msg.Of("gcal.problem." + Problem);
}
