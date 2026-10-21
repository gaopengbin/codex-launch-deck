using System;
using ChatGPTProxyLauncherLite;
internal static class BrowserReadRepairCli
{
    static int Main(string[] args)
    {
        try {
            var maintenance=CuaProxyForwardingMaintenance.CreateDefault();
            var inspection=maintenance.Check();
            Console.WriteLine(inspection.Summary);
            if(args.Length==1 && args[0]=="repair") {
                var result=maintenance.RepairBrowserRead(inspection);
                Console.WriteLine("Changed="+result.Changed+" Backup="+result.RecordPath);
                Console.WriteLine(maintenance.Check().Summary);
            } else if(args.Length==1 && args[0]=="proxy") {
                var result=maintenance.Repair(inspection);
                Console.WriteLine("Changed="+result.Changed+" Backup="+result.RecordPath);
            } else if(args.Length!=0) return 2;
            return 0;
        } catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
