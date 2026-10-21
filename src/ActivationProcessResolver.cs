using System;
using System.Collections.Generic;
internal sealed class ActivationCandidate
{
    internal int Id; internal string Executable; internal DateTime StartedUtc; internal bool HasWindow;
}
internal static class ActivationProcessResolver
{
    internal static int Wait(string expected,DateTime started,Func<string,IEnumerable<ActivationCandidate>> read,Action pause,Func<DateTime> now,TimeSpan timeout)
    {
        DateTime deadline=now()+timeout;
        do {
            foreach(var candidate in read(expected))
                if(candidate.Id>0 && candidate.HasWindow && candidate.StartedUtc>=started.AddSeconds(-1)
                    && String.Equals(candidate.Executable,expected,StringComparison.OrdinalIgnoreCase)) return candidate.Id;
            if(now()>=deadline) break;
            pause();
        } while(true);
        throw new TimeoutException("The package was activated, but no verified new client window appeared within ten seconds. The approved proxy session may remain active for an already running client.");
    }
}
