using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("GGCalendar.Core.Tests.Support.RunLogFramework", "GGCalendar.Core.Tests")]

namespace GGCalendar.Core.Tests.Support;

/// <summary>
/// The test framework of the project: xUnit as usual, plus a log written while the tests run. Set GGCALENDAR_TEST_LOG to the path of
/// the file (or to "1" for a file in the temp folder) and each test adds a line as soon as it ends:
/// date | time | version of the app | id of the test case | PASS, FAIL or SKIP | seconds | name of the test function | what failed (if it did).
/// </summary>
public sealed class RunLogFramework(IMessageSink diagnosticMessageSink) : XunitTestFramework(diagnosticMessageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
        : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
        protected override async void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            var sink = RunLog.Wrap(executionMessageSink);
            using var runner = new XunitTestAssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink, sink, executionOptions);
            await runner.RunAsync();
        }
    }
}

internal static class RunLog
{
    private static readonly object Gate = new();

    private static string? Path
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("GGCALENDAR_TEST_LOG");
            if (string.IsNullOrEmpty(value)) return null;
            return value == "1" ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"GGCalendar_test_{DateTime.Now:yyyyMMdd_HHmmss}.log") : value;
        }
    }

    public static IMessageSink Wrap(IMessageSink inner) => Path is { } path ? new Sink(inner, path) : inner;

    private static string OneLine(string? text) => (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Version => GGCalendar.App.AppInfo.Version;

    private sealed class Sink(IMessageSink inner, string path) : LongLivedMarshalByRefObject, IMessageSink
    {
        private int _pass, _fail, _skip;

        private void Write(string line)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }

        private void Result(ITest test, string result, decimal seconds, string detail)
        {
            var number = test.TestCase.Traits.TryGetValue("TestCase", out var codes) && codes.Count > 0 ? string.Join("+", codes) : "-";
            Write($"{DateTime.Now:yyyy-MM-dd} | {DateTime.Now:HH:mm:ss} | {Version} | {number} | {result} | {seconds:0.0} s | {test.DisplayName} | {OneLine(detail)}");
        }

        public bool OnMessage(IMessageSinkMessage message)
        {
            switch (message)
            {
                case ITestAssemblyStarting:
                    Write($"=== GGCalendar {Version}: tests started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    break;
                case ITestPassed passed:
                    _pass++;
                    Result(passed.Test, "PASS", passed.ExecutionTime, "");
                    break;
                case ITestFailed failed:
                    _fail++;
                    Result(failed.Test, "FAIL", failed.ExecutionTime, ExceptionUtility.CombineMessages(failed));
                    break;
                case ITestSkipped skipped:
                    _skip++;
                    Result(skipped.Test, "SKIP", 0, skipped.Reason);
                    break;
                case ITestAssemblyFinished finished:
                    Write($"=== end {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {_pass} PASS, {_fail} FAIL, {_skip} SKIP, {finished.ExecutionTime:0.0} s ===");
                    break;
            }

            return inner.OnMessage(message);
        }
    }
}
