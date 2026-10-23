using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ChatGPTProxyLauncherLite
{
    // Numeric state only: no credentials, request content or proxy destinations.
    internal sealed class LauncherDiagnostics : IDisposable
    {
        private readonly object gate=new object();
        private readonly Timer timer;
        private readonly string path;
        private long heartbeat=Stopwatch.GetTimestamp();
        private string stage="idle";
        private int disposed, ticks;
        internal LauncherDiagnostics(string root=null)
        {
            try {
                root=root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LaunchDeck","Diagnostics");
                Directory.CreateDirectory(root);
                // Keep existing evidence. Refuse additional files after eight sessions.
                if(Directory.GetFiles(root,"launcher-*.log").Length<8)
                    using(var process=Process.GetCurrentProcess())
                        path=Path.Combine(root,"launcher-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+process.Id+".log");
                Sample();
            } catch { }
            timer=new Timer(_=> {if(Interlocked.Increment(ref ticks)%12==0 || LagSeconds()>=5) Sample();},null,5000,5000);
        }
        internal void Heartbeat() { Interlocked.Exchange(ref heartbeat,Stopwatch.GetTimestamp()); }
        internal void Stage(string value) { Interlocked.Exchange(ref stage,value); }
        private double LagSeconds() {return (Stopwatch.GetTimestamp()-Interlocked.Read(ref heartbeat))/(double)Stopwatch.Frequency;}
        private void Sample()
        {
            if(path==null || disposed!=0 || !Monitor.TryEnter(gate)) return;
            try {
                if(File.Exists(path) && new FileInfo(path).Length>=1048576) return;
                using(var process=Process.GetCurrentProcess())
                    File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" stage="+stage+" uiLagSeconds="+LagSeconds().ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+" workingMB="+(process.WorkingSet64/1048576)+" privateMB="+(process.PrivateMemorySize64/1048576)+" handles="+process.HandleCount+Environment.NewLine,new UTF8Encoding(false));
            } catch { } finally {Monitor.Exit(gate);}
        }
        public void Dispose() {Interlocked.Exchange(ref disposed,1);timer.Dispose();}
    }
}
