using System;
using System.Collections.Generic;
internal static class BrowserReadLaunchPolicy
{
    internal const string EnvironmentKey = "BROWSER_USE_DISABLE_API_MEMBERS";
    // Preserve existing API restrictions; add only the stalled browser AX API.
    internal static string Merge(string userValue, string inheritedValue)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string input in new[] { userValue, inheritedValue, "Tab.ax" })
            foreach (string part in (input ?? "").Split(',')) {
                string value = part.Trim();
                if (value.Length > 0 && seen.Add(value)) values.Add(value);
            }
        return String.Join(",", values);
    }
}
