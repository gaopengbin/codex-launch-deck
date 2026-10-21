using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
namespace LaunchDeck.ProcessProxy
{
    internal enum DiagnosticStage { HostValidation, NativeVerification, Mutex, PipeConnect, PeerVerification, PlanValidation, Approval, EngineConstruction, EngineStart, Ready, Stop, Cleanup, Exit }
    internal enum DiagnosticResult { Enter, Success, Failed, Cancelled, NotConfirmed }
    internal sealed class ProxyDiagnostic : IDisposable
    {
        internal const int MaxBytes=1048576, MaxMetadataEvents=2048, QueueCapacity=256, MaxSessionFiles=8;
        private readonly Action<string> sink;
        private readonly IDisposable resource;
        private readonly object gate=new object(), pumpGate=new object();
        private readonly BlockingCollection<string> queue=new BlockingCollection<string>(QueueCapacity);
        private Thread pump;
        private int accepted, dropped, bytes;
        private volatile bool closing, disposed;
        internal DiagnosticStage Stage { get; private set; }
        internal bool WriteFailed { get; private set; }
        internal int BytesWritten { get { lock(gate) return bytes; } }
        internal int DroppedEvents { get { return dropped; } }
        internal ProxyDiagnostic(Action<string> sink) { this.sink=sink; }
        private ProxyDiagnostic(StreamWriter writer) { sink=writer.WriteLine; resource=writer; }
        internal static string ErrorSummary(Exception error)
        {
            if(error is AggregateException) { var causes=((AggregateException)error).Flatten().InnerExceptions; if(causes.Count>0) error=causes[0]; }
            var native=error as Win32Exception;
            string reason=error is UnauthorizedAccessException?"access-denied":error is TimeoutException?"timeout":error is OperationCanceledException?"cancelled":error is InvalidDataException?"invalid-protocol":error is IOException?"io-failure":error is InvalidOperationException?"validation-or-state-failure":"unexpected-failure";
            return error.GetType().Name+" hresult=0x"+error.HResult.ToString("X8")+(native==null?"":" win32="+native.NativeErrorCode)+" reason="+reason;
        }
        private void WriteBounded(string line,bool metadata=false)
        {
            lock(gate) {
                if(disposed || sink==null) return;
                int length=Encoding.UTF8.GetByteCount(line)+2;
                if(bytes+length>(metadata?MaxBytes-16384:MaxBytes)) { Interlocked.Increment(ref dropped); return; }
                try { sink(line); bytes+=length; } catch { WriteFailed=true; }
            }
        }
        internal void Record(DiagnosticStage stage,DiagnosticResult result,Exception error=null)
        {
            Stage=stage;
            try { WriteBounded(DateTime.UtcNow.ToString("o")+" stage="+stage+" result="+result+(error==null?"":" "+ErrorSummary(error))); } catch { WriteFailed=true; }
        }
        internal void RecordPlan(ProxyPlan plan)
        {
            try { plan.Validate();
                string package=Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(plan.ExecutablePath)));
                if(!Regex.IsMatch(package,@"^OpenAI\.(Codex|ChatGPT-Desktop)_[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+_(x64|x86|arm64)__[a-z0-9]+$",RegexOptions.IgnoreCase)) {WriteFailed=true;return;}
                WriteBounded(DateTime.UtcNow.ToString("o")+" plan="+new JavaScriptSerializer().Serialize(new { targetExe=plan.ExecutablePath,upstreamHost=plan.ProxyHost,upstreamPort=plan.ProxyPort,scope=ProxyPlan.Scope,browserHelperCount=(plan.BrowserHelperPaths??new string[0]).Length,coreHelperCount=(plan.CoreHelperPaths??new string[0]).Length })); } catch { WriteFailed=true; }
        }
        internal void RecordRules(uint proxy,uint direct,uint target)
        { try {WriteBounded(DateTime.UtcNow.ToString("o")+" rules proxyId="+proxy+" loopbackRule="+direct+" targetRule="+target);}catch{WriteFailed=true;} }
        internal static string FormatMetadata(ulong flow,uint pid,uint family,ushort port,uint rule,uint proxy,uint stage,int result,uint error,uint status)
        {
            if(flow==0 || pid==0 || (family!=4 && family!=6) || port==0 || rule==0 || stage<1 || stage>5 || result<0 || result>2 || (status!=0 && (status<100 || status>999))) return null;
            string[] stages={"","decision","relay_accept","upstream_connect","http_connect","relay_close"};
            return DateTime.UtcNow.ToString("o")+" flow="+flow+" pid="+pid+" af="+family+" srcPort="+port+" targetRule="+rule+" proxy="+proxy+" stage="+stages[stage]+" result="+result+" error="+error+" httpStatus="+status;
        }
        internal void QueueMetadata(ulong flow,uint pid,uint family,ushort port,uint rule,uint proxy,uint stage,int result,uint error,uint status)
        {
            // Numeric-only ABI. Nonblocking queue; callback never writes disk or throws into C.
            try {
                if(closing || sink==null) return;
                string line=FormatMetadata(flow,pid,family,port,rule,proxy,stage,result,error,status);
                if(line==null) { Interlocked.Increment(ref dropped); return; }
                int old;
                do { old=accepted; if(old>=MaxMetadataEvents){Interlocked.Increment(ref dropped);return;} } while(Interlocked.CompareExchange(ref accepted,old+1,old)!=old);
                if(!queue.TryAdd(line)){Interlocked.Increment(ref dropped);return;}
                lock(pumpGate) if(pump==null) { pump=new Thread(Pump){IsBackground=true,Name="LaunchDeck metadata writer"};pump.Start(); }
            } catch { WriteFailed=true; }
        }
        private void Pump()
        {
            try { foreach(string line in queue.GetConsumingEnumerable()) WriteBounded(line,true); } catch { WriteFailed=true; }
        }
        internal static FileSecurity LogSecurity()
        {
            var security=new FileSecurity(); security.SetAccessRuleProtection(true,false);
            var admins=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null); security.SetOwner(admins);
            foreach(var sid in new[]{admins,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)}) security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,AccessControlType.Allow)); return security;
        }
        internal static ProxyDiagnostic OpenProtected()
        {
            try {
                string directory=Path.Combine(ProxySecurity.ProtectedRoot,NativeArtifacts.BundleId); ProxySecurity.VerifyProtectedDirectory(directory);
                // No deletion of retained/active logs. Refuse logging after 8 bounded session files.
                if(Directory.GetFiles(directory,"host-diagnostic-*.log").Length>=MaxSessionFiles) throw new IOException("diagnostic-capacity");
                FileStream file=null;
                for(int slot=0;slot<MaxSessionFiles;slot++) {
                    string path=Path.Combine(directory,"host-diagnostic-session-"+slot.ToString("00")+".log");
                    try {file=new FileStream(path,FileMode.CreateNew,FileSystemRights.Write,FileShare.Read,4096,FileOptions.None,LogSecurity());break;}
                    catch(IOException) {if(!File.Exists(path))throw;} // Atomic fixed slots bound concurrent creations.
                }
                if(file==null)throw new IOException("diagnostic-capacity");
                try { return new ProxyDiagnostic(new StreamWriter(file,new UTF8Encoding(false)){AutoFlush=true}); } catch { file.Dispose();throw; }
            } catch { return new ProxyDiagnostic((Action<string>)null){WriteFailed=true}; }
        }
        public void Dispose()
        {
            if(closing) return;closing=true;
            try { queue.CompleteAdding();Thread writer;lock(pumpGate) writer=pump;
                if(writer!=null && !writer.Join(1000)) { WriteFailed=true;return; } // Never stall routing for a slow disk.
                WriteBounded(DateTime.UtcNow.ToString("o")+" diagnostic_end dropped="+dropped+" accepted="+accepted);
                lock(gate) {disposed=true;if(resource!=null)resource.Dispose();}
            } catch { WriteFailed=true; }
        }
    }
}
