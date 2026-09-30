using System;
using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
namespace LaunchDeck.ProcessProxy
{
    internal enum DiagnosticStage { HostValidation, NativeVerification, Mutex, PipeConnect, PeerVerification, PlanValidation, Approval, EngineConstruction, EngineStart, Ready, Stop, Cleanup, Exit }
    internal enum DiagnosticResult { Enter, Success, Failed, Cancelled, NotConfirmed }
    internal sealed class ProxyDiagnostic : IDisposable
    {
        private readonly Action<string> sink;
        private readonly IDisposable resource;
        private readonly object gate=new object();
        internal DiagnosticStage Stage { get; private set; }
        internal bool WriteFailed { get; private set; }
        internal ProxyDiagnostic(Action<string> sink) { this.sink=sink; }
        private ProxyDiagnostic(StreamWriter writer) { sink=writer.WriteLine; resource=writer; }
        internal static string ErrorSummary(Exception error)
        {
            if(error is AggregateException) { var causes=((AggregateException)error).Flatten().InnerExceptions; if(causes.Count>0) error=causes[0]; }
            var native=error as Win32Exception;
            string reason=error is UnauthorizedAccessException?"access-denied":error is TimeoutException?"timeout":error is OperationCanceledException?"cancelled":error is InvalidDataException?"invalid-protocol":error is IOException?"io-failure":error is InvalidOperationException?"validation-or-state-failure":"unexpected-failure";
            return error.GetType().Name+" hresult=0x"+error.HResult.ToString("X8")+(native==null?"":" win32="+native.NativeErrorCode)+" reason="+reason;
        }
        internal void Record(DiagnosticStage stage,DiagnosticResult result,Exception error=null)
        {
            lock(gate)
            {
                Stage=stage;
                if(sink==null) return;
                // No exception.Message, stack, SID, pipe name, command line, path or payload.
                try { string line=DateTime.UtcNow.ToString("o")+" stage="+stage+" result="+result+(error==null?"":" "+ErrorSummary(error)); sink(line); } catch { WriteFailed=true; } // Diagnostic failures never change control/cleanup.
            }
        }
        internal static FileSecurity LogSecurity()
        {
            var security=new FileSecurity(); security.SetAccessRuleProtection(true,false);
            var admins=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);
            security.SetOwner(admins);
            foreach(var sid in new[]{admins,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})
                security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,AccessControlType.Allow));
            return security;
        }
        internal static ProxyDiagnostic OpenProtected()
        {
            try
            {
                // Fixed protected version directory only; no caller-controlled target or existing-file overwrite.
                string directory=Path.Combine(ProxySecurity.ProtectedRoot,NativeArtifacts.BundleId);
                ProxySecurity.VerifyProtectedDirectory(directory);
                var security=LogSecurity();
                string path=Path.Combine(directory,"host-diagnostic-"+Guid.NewGuid().ToString("N")+".log");
                var file=new FileStream(path,FileMode.CreateNew,FileSystemRights.Write,FileShare.Read,4096,FileOptions.None,security);
                try { return new ProxyDiagnostic(new StreamWriter(file,new UTF8Encoding(false)) { AutoFlush=true }); }
                catch { file.Dispose(); throw; }
            }
            catch { return new ProxyDiagnostic((Action<string>)null) { WriteFailed=true }; }
        }
        public void Dispose() { try { if(resource!=null) resource.Dispose(); } catch { WriteFailed=true; } }
    }
}
