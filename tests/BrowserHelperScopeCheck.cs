using System;
using LaunchDeck.ProcessProxy;
class BrowserHelperScopeCheck
{
    static int Main(string[] args)
    {
        if(args.Length!=2) throw new ArgumentException("Package and observed helper paths required.");
        var plan=new ProxyPlan {ExecutablePath=args[0], BrowserHelperPaths=BrowserHelperScope.Resolve(args[0]),
            ProxyHost="127.0.0.1",ProxyPort=7890,Process443Approved=true,DriverApproved=true};
        ProxySecurity.VerifyRegisteredPackage(plan);
        if(Array.Find(plan.BrowserHelperPaths,p=>p.Equals(args[1],StringComparison.OrdinalIgnoreCase))==null)
            throw new InvalidOperationException("Observed active helper is outside resolved scope.");
        Console.WriteLine("PASS registered package, copied helper path, reparse checks and vendor SHA256 match.");
        foreach(string path in plan.BrowserHelperPaths) Console.WriteLine(path);
        Console.WriteLine("Source SHA256: "+ProxySecurity.HashFile(BrowserHelperScope.Source(args[0])));
        return 0;
    }
}
