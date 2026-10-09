using System.Reflection;

namespace QuranReconciliation.Infrastructure;

internal static class BuildIdentity
{
    internal const string WorkstationName =
        "The Holy Quran TRP";

    internal const string ProjectName =
        "The Holy Translation Reconciliation Project";

    internal const string ProjectShortName =
        "THTRP";

    internal const string Version =
        "Build 1.9 R1";

    internal const string Label =
        "The Holy Quran TRP Build 1.9 R1";

    internal static string SourceRevision
    {
        get
        {
            string? informational =
                Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(informational))
            {
                return "unknown";
            }

            int plus = informational.LastIndexOf('+');

            return plus >= 0 &&
                plus < informational.Length - 1
                    ? informational[(plus + 1)..]
                    : informational;
        }
    }
}
