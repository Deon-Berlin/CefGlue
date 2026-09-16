using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace CefGlue.Tests
{
    /// <summary>
    /// Skips a test on Windows, reported as skipped rather than failed. Stands in for two things that
    /// do not work here: <c>[Platform(Exclude = "Win")]</c>, whose constructor throws "Unknown
    /// framework version" under NUnit 3.12 on .NET 10 and so drops the whole fixture from discovery;
    /// and a runtime <c>Assert.Ignore</c>, which the assembly-wide <c>[Timeout]</c> reports as a failure.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ExcludeOnWindowsAttribute : NUnitAttribute, IApplyToTest
    {
        private readonly string _reason;

        public ExcludeOnWindowsAttribute(string reason) => _reason = reason;

        public void ApplyToTest(Test test)
        {
            if (test.RunState == RunState.NotRunnable || !OperatingSystem.IsWindows()) return;

            test.RunState = RunState.Ignored;
            test.Properties.Set(PropertyNames.SkipReason, _reason);
        }
    }
}
