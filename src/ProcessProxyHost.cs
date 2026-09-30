using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
namespace LaunchDeck.ProcessProxy
{
    internal static class ProxyHost
    {
        [STAThread]
        static int Main(string[] args)
        {
            ProxyDiagnostic diagnostic=new ProxyDiagnostic((Action<string>)null);
            try
            {
                int expectedPid;
                if(args.Length!=4 || args[0]!="--broker" || !ProxyProtocol.IsPipeName(args[1])
                    || !Int32.TryParse(args[2],out expectedPid) || expectedPid<=0) return 2;
                var sid=new SecurityIdentifier(args[3]);
                using(var identity=WindowsIdentity.GetCurrent())
                    if(identity.User!=sid || !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 2;
                diagnostic=ProxyDiagnostic.OpenProtected();
                string directory=Path.Combine(ProxySecurity.ProtectedRoot,NativeArtifacts.BundleId);
                string ownPath=Process.GetCurrentProcess().MainModule.FileName;
                string expectedPath=Path.Combine(directory,"ProcessProxyHost.exe");
                if(!Path.GetFullPath(ownPath).Equals(expectedPath,StringComparison.OrdinalIgnoreCase)) return 2;
                ProxySecurity.VerifyProtectedFile(ownPath);
                diagnostic.Record(DiagnosticStage.HostValidation,DiagnosticResult.Success);
                diagnostic.Record(DiagnosticStage.NativeVerification,DiagnosticResult.Enter);
                NativePins.Verify(Path.Combine(directory,"process-proxy-native"));
                diagnostic.Record(DiagnosticStage.Mutex,DiagnosticResult.Enter);
                bool created;
                using(var lease=new Mutex(true,@"Global\LaunchDeck.ProcessProxy.Engine",out created))
                {
                    if(!created) return 2; // Never share or stop another engine instance.
                    try
                    {
                        using(var pipe=new NamedPipeClientStream(".",args[1],PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Anonymous))
                        {
                            diagnostic.Record(DiagnosticStage.PipeConnect,DiagnosticResult.Enter);
                            pipe.Connect(15000);
                            diagnostic.Record(DiagnosticStage.PeerVerification,DiagnosticResult.Enter);
                            uint peer;
                            if(!ProxySecurity.GetNamedPipeServerProcessId(pipe.SafePipeHandle,out peer) || peer!=(uint)expectedPid) return 2;
                            string callerPath=Path.Combine(directory,"ChatGPTProxyLauncher.exe");
                            ProxySecurity.VerifyProtectedFile(callerPath);
                            ProxySecurity.VerifyPeer(peer,sid.Value,TrustedClient.Sha256);
                            using(var reader=new StreamReader(pipe,new UTF8Encoding(false,true),false,1024,true))
                            {
                                var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,true) { AutoFlush=true };
                                try
                                {
                                int code=BrokerSession.Run(reader,writer,ProxySecurity.VerifyRegisteredPackage,
                                    plan=>ProxyApproval.Confirm(plan,pipe),
                                    ()=>new NativeEngine(Path.Combine(directory,"process-proxy-native")),30000,diagnostic);
                                diagnostic.Record(DiagnosticStage.Exit,code==0?DiagnosticResult.Success:DiagnosticResult.Failed);
                                return code;
                                }
                                finally { ProxyProtocol.DisposePipeWriter(writer); }
                            }
                        }
                    }
                    finally { lease.ReleaseMutex(); }
                }
            }
            catch(Exception error) { diagnostic.Record(diagnostic.Stage,DiagnosticResult.Failed,error); return 1; }
            finally { diagnostic.Dispose(); }
        }
    }
}
