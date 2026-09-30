using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace LaunchDeck.ProcessProxy
{
    internal enum ProxyState { Off, WaitingApproval, Ready, Failed }
    internal sealed class BrokerManifest
    {
        public string BundleId { get; set; }
        public System.Collections.Generic.Dictionary<string,string> Files { get; set; }
    }
    internal sealed class ProxyController : IDisposable
    {
        private readonly object gate=new object();
        private long generation;
        private int pendingCleanup;
        private bool failedCleanup;
        internal string LastCleanupRecord { get; private set; }
        internal int PendingCleanup { get { lock(gate) return pendingCleanup; } }
        internal void EnsureCleanupConfirmed()
        {
            lock(gate) if(pendingCleanup!=0 || failedCleanup)
                throw new InvalidOperationException("Previous owned proxy host cleanup is not confirmed. Enhanced sessions remain blocked. "+LastCleanupRecord);
        }
        private Process host;
        private NamedPipeServerStream pipe;
        private StreamWriter writer;
        private StreamReader reader;
        private CancellationTokenSource cancellation;
        public ProxyState State { get; private set; }
        public string TargetExecutable { get; private set; }
        internal static string InstallDirectory { get { return Path.Combine(ProxySecurity.ProtectedRoot,NativeArtifacts.BundleId); } }
        public void Prepare()
        {
            EnsureCleanupConfirmed();
            State=ProxyState.WaitingApproval;
            string directory=InstallDirectory;
            if(!Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\').Equals(directory,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Enhanced proxy is waiting for the reviewed protected installation. Start the installed Launch Deck; no driver was loaded.");
            ProxySecurity.VerifyProtectedDirectory(directory);
            string manifestPath=Path.Combine(directory,"bundle-manifest.json");
            ProxySecurity.VerifyProtectedFile(manifestPath);
            var manifest=new JavaScriptSerializer().Deserialize<BrokerManifest>(File.ReadAllText(manifestPath));
            if(manifest==null || manifest.BundleId!=NativeArtifacts.BundleId || manifest.Files==null) throw new InvalidOperationException("Approved bundle identity mismatch.");
            foreach(string name in new[]{"ChatGPTProxyLauncher.exe","ProcessProxyHost.exe","PackagedChatGPTLauncher.exe"})
            {
                string expected;
                if(!manifest.Files.TryGetValue(name,out expected)) throw new InvalidOperationException("Incomplete approved bundle.");
                string path=Path.Combine(directory,name); ProxySecurity.VerifyProtectedFile(path); ProxySecurity.VerifyHash(path,expected);
            }
            NativePins.Verify(Path.Combine(directory,"process-proxy-native"));
        }
        public static string ResolveExecutable()
        {
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
            var info=new ProcessStartInfo(powershell,"-NoProfile -NonInteractive -Command \"Get-AppxPackage | Where-Object { $_.Name -eq 'OpenAI.Codex' -or $_.Name -eq 'OpenAI.ChatGPT-Desktop' } | Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty InstallLocation | ConvertTo-Json -Compress\"")
            { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true };
            using(var process=Process.Start(info))
            {
                var read=process.StandardOutput.ReadToEndAsync();
                if(!process.WaitForExit(5000)) throw new InvalidOperationException("Package discovery did not complete.");
                if(process.ExitCode!=0) throw new InvalidOperationException("Package discovery failed.");
                string root=new JavaScriptSerializer().Deserialize<string>(read.Result.Trim());
                if(String.IsNullOrEmpty(root)) throw new InvalidOperationException("OpenAI package not found.");
                foreach(string name in new[]{"ChatGPT.exe","Codex.exe"})
                {
                    string path=Path.Combine(root,"app",name);
                    if(File.Exists(path)) return Path.GetFullPath(path);
                }
                throw new InvalidOperationException("Package executable not found.");
            }
        }
        public void Start(ProxyPlan plan)
        {
            plan.Validate(); Prepare(); ProxySecurity.VerifyRegisteredPackage(plan);
            string directory=InstallDirectory;
            var sid=WindowsIdentity.GetCurrent().User;
            string name=ProxyProtocol.NewPipeName();
            CancellationTokenSource pending;
            NamedPipeServerStream channel;
            CancellationToken token;
            long launchId;
            lock(gate)
            {
                EnsureCleanupConfirmed();
                if(pipe!=null) throw new InvalidOperationException("A proxy session is already active.");
                launchId=++generation;
                cancellation=pending=new CancellationTokenSource();
                token=pending.Token;
                pipe=channel=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,ProxySecurity.PipeAcl(sid));
                TargetExecutable=plan.ExecutablePath;
            }
            try
            {
                Task connect=Task.Factory.FromAsync(channel.BeginWaitForConnection,channel.EndWaitForConnection,null);
                var info=new ProcessStartInfo(Path.Combine(directory,"ProcessProxyHost.exe"),"--broker "+name+" "+Process.GetCurrentProcess().Id+" "+sid.Value)
                { UseShellExecute=true,Verb="runas",WorkingDirectory=directory,WindowStyle=ProcessWindowStyle.Normal };
                token.ThrowIfCancellationRequested();
                Process started=Process.Start(info);
                if(started==null) throw new InvalidOperationException("Windows did not return the owned broker process.");
                if(!PublishHost(launchId,pending,channel,started,token))
                {
                    ReapOwnedHost(started); // Dispose happened during UAC; this late child is still ours.
                    throw new OperationCanceledException("Proxy session was canceled during administrator approval.");
                }
                if(!connect.Wait(60000,token)) throw new TimeoutException("Administrator approval timed out.");
                uint peer;
                if(!ProxySecurity.GetNamedPipeClientProcessId(channel.SafePipeHandle,out peer) || peer!=(uint)started.Id) throw new InvalidOperationException("Unexpected proxy host connected.");
                var input=new StreamReader(channel,new UTF8Encoding(false,true),false,1024,true);
                var output=new StreamWriter(channel,new UTF8Encoding(false),1024,true) { AutoFlush=true };
                lock(gate)
                {
                    if(generation!=launchId || !Object.ReferenceEquals(pipe,channel) || token.IsCancellationRequested)
                        throw new OperationCanceledException();
                    reader=input; writer=output;
                }
                token.ThrowIfCancellationRequested();
                output.WriteLine(plan.Serialize());
                string reply=ReadReply(input,120000,token);
                if(reply=="CANCELLED") throw new OperationCanceledException("Enhanced proxy approval canceled.");
                if(reply!="APPROVED") throw new InvalidOperationException("Process proxy authorization failed.");
                token.ThrowIfCancellationRequested(); output.WriteLine("START");
                if(ReadReply(input,35000,token)!="READY") throw new InvalidOperationException("Process proxy did not become ready; ChatGPT was not launched.");
                token.ThrowIfCancellationRequested();
                lock(gate)
                {
                    if(generation!=launchId || token.IsCancellationRequested) throw new OperationCanceledException();
                    State=ProxyState.Ready;
                }
            }
            catch(Exception original)
            {
                var native=original as Win32Exception;
                bool cancelled=original is OperationCanceledException || (native!=null && native.NativeErrorCode==1223);
                Exception reported=cancelled && !(original is OperationCanceledException)
                    ?new OperationCanceledException("Administrator approval canceled.",original):original;
                Exception failure;
                try { failure=CollectCleanupFailure(reported,()=>DisposeSession(launchId)); }
                finally { UpdateEndedState(launchId,cancelled?ProxyState.Off:ProxyState.Failed); }
                if(Object.ReferenceEquals(failure,original)) throw;
                throw failure;
            }
        }
        internal static Exception CollectCleanupFailure(Exception primary,Action cleanup)
        {
            try { cleanup(); return primary; }
            catch(Exception secondary)
            {
                return new AggregateException("Proxy startup failed ("+ProxyDiagnostic.ErrorSummary(primary)+"); cleanup also failed ("+ProxyDiagnostic.ErrorSummary(secondary)+"). Both causes are retained.",primary,secondary);
            }
        }
        private static string ReadReply(TextReader input,int timeout,CancellationToken token)
        {
            Task<string> read=Task.Factory.StartNew(()=>ProxyProtocol.ReadLine(input));
            if(!read.Wait(timeout,token)) throw new TimeoutException("Proxy host response timed out.");
            return read.Result;
        }
        internal bool PublishHost(long launchId, CancellationTokenSource pending, NamedPipeServerStream channel, Process started, CancellationToken token)
        {
            lock(gate)
            {
                if(generation!=launchId || !Object.ReferenceEquals(cancellation,pending)
                    || !Object.ReferenceEquals(pipe,channel) || token.IsCancellationRequested) return false;
                host=started; return true;
            }
        }
        internal void ReapOwnedHost(Process owned,bool registered=false)
        {
            // Register before waiting so another Start cannot race the seven-second window.
            int pid=owned.Id;
            lock(gate) { if(!registered) pendingCleanup++; LastCleanupRecord="PID "+pid+": awaiting owned host exit"; }
            bool exited;
            try { exited=owned.WaitForExit(7000); }
            catch { lock(gate) failedCleanup=true; ObserveOwnedHost(owned,pid); throw; }
            if(!exited)
            {
                lock(gate) LastCleanupRecord="PID "+pid+": still running after 7000ms; ownership retained";
                ObserveOwnedHost(owned,pid);
                throw new InvalidOperationException("Owned proxy host cleanup was not confirmed. Its handle is retained and observed; new enhanced sessions are blocked. No process was forcibly terminated.");
            }
            int code=CompleteOwnedHost(owned,pid);
            if(code!=0) throw new InvalidOperationException("Owned proxy host exited unsuccessfully; enhanced sessions remain blocked. "+LastCleanupRecord);
        }
        private void ObserveOwnedHost(Process owned,int pid)
        {
            var observer=new Thread(()=> {
                try { owned.WaitForExit(); CompleteOwnedHost(owned,pid); }
                catch(Exception ex) { lock(gate) { failedCleanup=true; LastCleanupRecord="PID "+pid+": observation failed ("+ex.GetType().Name+"); ownership retained"; } }
            });
            observer.IsBackground=true; observer.Name="LaunchDeck owned host exit observer"; observer.Start();
        }
        private int CompleteOwnedHost(Process owned,int pid)
        {
            int code=owned.ExitCode; // Only reached after a confirmed exit.
            lock(gate)
            {
                pendingCleanup--; if(code!=0) failedCleanup=true;
                LastCleanupRecord="PID "+pid+": confirmed exit, code "+code;
                owned.Dispose();
            }
            return code;
        }
        private void UpdateEndedState(long launchId, ProxyState state)
        { lock(gate) if(generation==launchId+1 && pipe==null) State=state; }
        public void PollHealth()
        {
            Process owned; long launchId;
            lock(gate) { owned=host; launchId=generation; }
            if(State==ProxyState.Ready && (owned==null || owned.HasExited))
            {
                try { DisposeSession(launchId); } finally { UpdateEndedState(launchId,ProxyState.Failed); }
                throw new InvalidOperationException("Enhanced proxy host exited; enhanced routing is no longer confirmed. ChatGPT was not terminated.");
            }
        }
        public void Dispose()
        { long launchId; lock(gate) launchId=generation; DisposeSession(launchId); }
        private void DisposeSession(long launchId)
        {
            Process owned; NamedPipeServerStream channel; StreamWriter control; CancellationTokenSource pending; bool ready;
            lock(gate)
            {
                if(generation!=launchId) return; // A canceled old launch must not stop a newer session.
                generation++;
                owned=host; channel=pipe; control=writer; pending=cancellation; ready=State==ProxyState.Ready;
                if(owned!=null) pendingCleanup++; // Transfer ownership before releasing the session gate.
                host=null; pipe=null; writer=null; reader=null; cancellation=null; State=ProxyState.Off;
            }
            if(pending!=null) pending.Cancel();
            try { if(ready && control!=null) { control.WriteLine("STOP"); control.Flush(); } }
            catch(IOException) {} catch(ObjectDisposedException) {}
            finally { if(channel!=null) channel.Dispose(); if(pending!=null) pending.Dispose(); }
            if(owned==null) return;
            try { ReapOwnedHost(owned,true); }
            catch { UpdateEndedState(launchId,ProxyState.Failed); throw; }
        }
    }
}
