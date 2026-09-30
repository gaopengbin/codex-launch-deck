using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using LaunchDeck.ProcessProxy;
class Tests
{
    class Engine:IProxyEngine
    {
        public List<string> Calls=new List<string>(); public bool FailStart,FailStop,FailRule,HangStart;
        public uint AddHttpProxy(string host,ushort port) { Calls.Add("proxy:"+host+":"+port); return 1; }
        public uint AddDirectLoopbackRule() { Calls.Add("loopback"); return 2; }
        public uint AddProcess443Rule(string path,uint id) { Calls.Add("target:"+path); return FailRule?0u:3u; }
        public void DeleteRule(uint id) { Calls.Add("delete"+id); }
        public void DeleteProxy(uint id) { Calls.Add("deleteproxy"); }
        public bool Start() { Calls.Add("start"); if(HangStart) new ManualResetEvent(false).WaitOne(); return !FailStart; }
        public bool Stop() { Calls.Add("stop"); return !FailStop; }
        public void Dispose() { Calls.Add("dispose"); }
    }
    class BrokenStatusWriter:StringWriter
    {
        public bool FailFlush;
        public override void WriteLine(string value) { if(!FailFlush) throw new IOException("peer closed");base.WriteLine(value); }
        public override void Flush() { if(FailFlush) throw new IOException("peer closed"); }
    }
    static int count;
    static void Check(bool value,string name) { if(!value) throw new Exception("FAIL: "+name); count++; }
    static void Reject(Action action,string name) { bool failed=false; try { action(); } catch { failed=true; } Check(failed,name); }
    static ProxyPlan Plan() { return new ProxyPlan { ExecutablePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"WindowsApps\OpenAI.Codex_26.928.2636.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"), ProxyHost="127.0.0.1",ProxyPort=10808,Process443Approved=true,DriverApproved=true }; }
    static int Session(string commands,Engine engine,bool approved,out string transcript)
    {
        var output=new StringWriter();
        int result=BrokerSession.Run(new StringReader(commands),output,p=>p.Validate(),p=>approved,()=>engine,1000);
        transcript=output.ToString(); return result;
    }
    static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--timeout-worker") return BrokerSession.Run(new StringReader(Plan().Serialize()+"\nSTART\n"),Console.Out,p=>p.Validate(),p=>true,()=>new Engine{HangStart=true},100);
        if(args.Length>0 && args[0]=="--controlled-exit-worker") { Console.WriteLine("WAITING"); Console.Out.Flush(); return Console.ReadLine()=="EXIT"?0:8; }
        if(args.Length>0 && args[0]=="--delayed-exit-worker") { Thread.Sleep(100); return 0; }
        if(args.Length>0 && args[0]=="--crash-worker") { Console.WriteLine("READY"); Console.Out.Flush(); return 9; }
        if(args.Length==2 && (args[0]=="--pipe-worker" || args[0]=="--cancel-pipe-worker"))
        {
            var pipeEngine=new Engine();
            using(var pipe=new NamedPipeClientStream(".",args[1],PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Anonymous))
            {
                pipe.Connect(3000); uint parent;
                if(!ProxySecurity.GetNamedPipeServerProcessId(pipe.SafePipeHandle,out parent)||parent==0) return 8;
                using(var input=new StreamReader(pipe,new UTF8Encoding(false),false,1024,true))
                {
                    var pipeOutput=new StreamWriter(pipe,new UTF8Encoding(false),1024,true) { AutoFlush=true };
                    try
                    {
                    int code=BrokerSession.Run(input,pipeOutput,p=>p.Validate(),p=> { if(args[0]=="--cancel-pipe-worker") { Console.Error.WriteLine("APPROVAL_WAIT");ProxyProtocol.ReadLine(input);return false; } return true; },()=>pipeEngine,1000);
                    Console.Error.WriteLine("STOP="+pipeEngine.Calls.Contains("stop")+" ENGINE_CALLS="+pipeEngine.Calls.Count); return code;
                    }
                    finally { ProxyProtocol.DisposePipeWriter(pipeOutput); }
                }
            }
        }
        var controller=new ProxyController(); Check(controller.State==ProxyState.Off,"initial off"); Reject(()=>controller.Prepare(),"uninstalled bundle rejected before UAC"); Check(controller.State==ProxyState.WaitingApproval,"waiting approval state");
        var plan=Plan();Check(ProxyPlan.Parse(plan.Serialize()).ExecutablePath==plan.ExecutablePath,"serialization");
        foreach(string remote in new[]{"203.0.113.1","2001:db8::1"}) Check(plan.Matches(plan.ExecutablePath,IPAddress.Parse(remote),443,true),"TCP443 remote "+remote);
        foreach(string local in new[]{"127.0.0.1","127.2.3.4","::1","::ffff:127.0.0.1","0.0.0.0","::"}) Check(!plan.Matches(plan.ExecutablePath,IPAddress.Parse(local),443,true),"loopback/unspecified exclusion "+local);
        Check(!plan.Matches(plan.ExecutablePath,IPAddress.Parse("203.0.113.1"),80,true),"other port"); Check(!plan.Matches(plan.ExecutablePath,IPAddress.Parse("203.0.113.1"),443,false),"UDP excluded"); Check(!plan.Matches(@"C:\other\ChatGPT.exe",IPAddress.Parse("203.0.113.1"),443,true),"basename insufficient"); Check(!plan.Matches(plan.ExecutablePath.Replace("26.928.2636.0","26.928.2637.0"),IPAddress.Parse("203.0.113.1"),443,true),"package version exact");
        plan.Process443Approved=false;Reject(()=>plan.Validate(),"scope approval required");plan=Plan();plan.DriverApproved=false;Reject(()=>plan.Validate(),"driver approval required");plan=Plan();plan.ProxyHost="203.0.113.1";Reject(()=>plan.Validate(),"nonlocal upstream rejected");plan=Plan();plan.ProxyPort=0;Reject(()=>plan.Validate(),"invalid port");plan=Plan();plan.ExecutablePath=plan.ExecutablePath.Replace("ChatGPT","*");Reject(()=>plan.Validate(),"wildcard denied");plan=Plan();plan.ExecutablePath=@"C:\fake\WindowsApps\OpenAI.Codex_fake\app\ChatGPT.exe";Reject(()=>plan.Validate(),"fake WindowsApps substring denied");
        Reject(()=>NativePins.Verify(@"C:\nonexistent"),"unprotected native directory denied");Reject(()=>ProxySecurity.VerifyHash(Process.GetCurrentProcess().MainModule.FileName,new string('0',64)),"wrong hash denied");
        var engine=new Engine();var session=new ProxySession(engine);session.Start(Plan());Check(session.Ready,"ready after start");session.Dispose();int original=engine.Calls.Count;session.Dispose();Check(original==engine.Calls.Count,"dispose idempotent");Check(engine.Calls.IndexOf("delete3")<engine.Calls.IndexOf("delete2")&&engine.Calls.IndexOf("delete2")<engine.Calls.IndexOf("deleteproxy"),"reverse rollback");Check(engine.Calls[0]=="proxy:127.0.0.1:10808"&&engine.Calls[2]=="target:"+Plan().ExecutablePath,"same upstream and exact target passed");
        engine=new Engine{FailStart=true};session=new ProxySession(engine);Reject(()=>session.Start(Plan()),"partial start rejects ready");Check(engine.Calls.Contains("stop")&&engine.Calls.Contains("delete3"),"partial start rollback");engine=new Engine{FailRule=true};session=new ProxySession(engine);Reject(()=>session.Start(Plan()),"rule fail");Check(!engine.Calls.Contains("start")&&engine.Calls.Contains("delete2"),"rule fail cleanup before start");engine=new Engine{FailStop=true};session=new ProxySession(engine);session.Start(Plan());session.Dispose();Check(session.CleanupError!=null&&!engine.Calls.Contains("delete3")&&!engine.Calls.Contains("deleteproxy"),"stop uncertain retains rules");
        string output;engine=new Engine();Check(Session(Plan().Serialize()+"\nSTART\nSTOP\n",engine,true,out output)==0&&output.Contains("READY")&&output.Contains("STOPPED"),"broker successful protocol");Check(engine.Calls.Contains("stop"),"broker STOP cleanup");engine=new Engine();Check(Session(Plan().Serialize()+"\n",engine,false,out output)==0&&output.Contains("CANCELLED")&&engine.Calls.Count==0,"user cancellation loads nothing");engine=new Engine();Check(Session(Plan().Serialize()+"\n",engine,true,out output)==0&&!engine.Calls.Contains("start"),"disconnect after approval prevents start");engine=new Engine();Check(Session(Plan().Serialize()+"\nSTART\n",engine,true,out output)==0&&engine.Calls.Contains("stop"),"EOF after ready rollback");engine=new Engine();Check(Session(Plan().Serialize()+"\nRUN cmd.exe\n",engine,true,out output)==1&&engine.Calls.Count==0,"arbitrary command denied before engine creation");engine=new Engine();Check(Session(Plan().Serialize()+"\nSTART\nEVIL\n",engine,true,out output)==1&&engine.Calls.Contains("stop"),"unknown command triggers rollback");engine=new Engine{FailStop=true};Check(Session(Plan().Serialize()+"\nSTART\nSTOP\n",engine,true,out output)==2&&output.Contains("cleanup-not-confirmed"),"stop failure explicit");
        Reject(()=>ProxyProtocol.ReadLine(new StringReader(new string('a',4097)+"\n")),"bounded frame");Reject(()=>ProxyProtocol.ReadLine(new StringReader("START")),"truncated frame");Reject(()=>ProxyProtocol.ReadLine(new StringReader("a\0b\n")),"NUL frame");Check(ProxyProtocol.IsPipeName(ProxyProtocol.NewPipeName()),"pipe nonce accepted");Check(!ProxyProtocol.IsPipeName("LaunchDeck.ProcessProxy.foo")&&!ProxyProtocol.IsPipeName("\\\\remote\\pipe\\foo"),"arbitrary pipe path denied");
        var acl=new DirectorySecurity();var admin=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);var users=new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null);acl.SetOwner(admin);acl.AddAccessRule(new FileSystemAccessRule(admin,FileSystemRights.FullControl,AccessControlType.Allow));acl.AddAccessRule(new FileSystemAccessRule(users,FileSystemRights.ReadAndExecute,AccessControlType.Allow));Check(!ProxySecurity.HasUnprivilegedWrite(acl),"read-only users ACL accepted");acl.AddAccessRule(new FileSystemAccessRule(users,FileSystemRights.WriteData,AccessControlType.Allow));Check(ProxySecurity.HasUnprivilegedWrite(acl),"user writable ACL denied");
        using(var child=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--timeout-worker") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true })) { Check(child.WaitForExit(3000)&&child.ExitCode==3&&child.StandardOutput.ReadToEnd().Contains("startup-timeout"),"hung fake engine worker exits within deadline"); }
        string name=ProxyProtocol.NewPipeName();using(var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,ProxySecurity.PipeAcl(WindowsIdentity.GetCurrent().User)))
        using(var child=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--pipe-worker "+name) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true }))
        {
            var connect=System.Threading.Tasks.Task.Factory.FromAsync(pipe.BeginWaitForConnection,pipe.EndWaitForConnection,null);Check(connect.Wait(3000),"real local pipe connects");uint peer;Check(ProxySecurity.GetNamedPipeClientProcessId(pipe.SafePipeHandle,out peer)&&peer==(uint)child.Id,"pipe client PID verified");
            var input=new StreamReader(pipe,new UTF8Encoding(false),false,1024,true);var control=new StreamWriter(pipe,new UTF8Encoding(false),1024,true) { AutoFlush=true };control.WriteLine(Plan().Serialize());Check(ProxyProtocol.ReadLine(input)=="APPROVED","pipe approval");control.WriteLine("START");Check(ProxyProtocol.ReadLine(input)=="READY","pipe ready");pipe.Dispose();bool exited=child.WaitForExit(3000); string diagnostic=exited?child.StandardError.ReadToEnd():"child still running"; int childCode=exited?child.ExitCode:-1; Check(exited&&childCode==0&&diagnostic.Contains("STOP=True"),"launcher pipe loss rolls back real child fake engine; exited="+exited+", code="+childCode+", stderr="+diagnostic);
        }
        using(var child=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--crash-worker") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true }))
        {
            child.WaitForExit(3000);var crashed=new ProxyController();typeof(ProxyController).GetField("host",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(crashed,child);typeof(ProxyController).GetProperty("State").GetSetMethod(true).Invoke(crashed,new object[]{ProxyState.Ready});Reject(()=>crashed.PollHealth(),"owned worker crash reported");Check(crashed.State==ProxyState.Failed,"crash leaves failed state");Reject(()=>crashed.EnsureCleanupConfirmed(),"nonzero owned exit keeps future sessions blocked");Check(crashed.LastCleanupRecord.Contains("code 9"),"nonzero exit evidence retained");
        }
        bool connected=true; long elapsed=0;
        var approval=new ApprovalLifetime(()=>connected,()=>elapsed,120000);
        Check(approval.Active,"approval active while connected");connected=false;Check(!approval.Active,"disconnected approval cancels");connected=true;elapsed=119999;Check(approval.Active,"approval before deadline");elapsed=120000;Check(!approval.Active,"approval deadline cancels");
        var race=new ProxyController(); var oldCancellation=new CancellationTokenSource(); var oldToken=oldCancellation.Token;
        var oldPipe=new NamedPipeServerStream(ProxyProtocol.NewPipeName(),PipeDirection.InOut,1);
        var fields=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        typeof(ProxyController).GetField("generation",fields).SetValue(race,41L);
        typeof(ProxyController).GetField("cancellation",fields).SetValue(race,oldCancellation);
        typeof(ProxyController).GetField("pipe",fields).SetValue(race,oldPipe);
        var lateChild=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--delayed-exit-worker") { UseShellExecute=false,CreateNoWindow=true });
        race.Dispose();
        Check(!race.PublishHost(41L,oldCancellation,oldPipe,lateChild,oldToken),"Dispose before host publication rejects late child");
        race.ReapOwnedHost(lateChild);Check(true,"unpublished owned child reaped without termination");
        using(var newCancellation=new CancellationTokenSource())
        using(var newPipe=new NamedPipeServerStream(ProxyProtocol.NewPipeName(),PipeDirection.InOut,1))
        {
            typeof(ProxyController).GetField("generation",fields).SetValue(race,43L);
            typeof(ProxyController).GetField("cancellation",fields).SetValue(race,newCancellation);
            typeof(ProxyController).GetField("pipe",fields).SetValue(race,newPipe);
            typeof(ProxyController).GetMethod("DisposeSession",fields).Invoke(race,new object[]{41L});
            Check(!newCancellation.IsCancellationRequested && !newPipe.SafePipeHandle.IsClosed,"old cancellation cannot dispose newer session");
            Check(Object.ReferenceEquals(typeof(ProxyController).GetField("pipe",fields).GetValue(race),newPipe),"newer session ownership preserved");
        }
        var held=new ProxyController();
        var heldChild=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--controlled-exit-worker") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true });
        Check(heldChild.StandardOutput.ReadLine()=="WAITING","controlled child waits for explicit release");
        // Match the BOM-free wire encoding used by the production control pipe; Console defaults vary on CI.
        var release=new StreamWriter(heldChild.StandardInput.BaseStream,new UTF8Encoding(false)) { AutoFlush=true };
        var watch=Stopwatch.StartNew();
        Reject(()=>held.ReapOwnedHost(heldChild),"seven-second live host wait reports unconfirmed cleanup");
        Check(watch.ElapsedMilliseconds>=7000 && !heldChild.HasExited,"real >7-second branch retains a live process handle");
        Check(held.PendingCleanup==1 && held.LastCleanupRecord.Contains("ownership retained"),"pending owned host is tracked and recorded");
        Reject(()=>held.EnsureCleanupConfirmed(),"pending exit blocks next enhanced session before installation or elevation");
        release.WriteLine("EXIT");release.Flush();
        Check(SpinWait.SpinUntil(()=>held.PendingCleanup==0,3000),"observer confirms released child exit");
        Check(held.LastCleanupRecord.Contains("confirmed exit, code 0"),"successful eventual exit recorded: "+held.LastCleanupRecord);
        held.EnsureCleanupConfirmed(); Check(true,"confirmed clean exit permits next session");
        var primary=new TimeoutException("token=DO-NOT-LOG private user payload");
        var secondary=new IOException("secret cleanup path");
        Check(Object.ReferenceEquals(ProxyController.CollectCleanupFailure(primary,()=>{}),primary),"successful cleanup preserves original exception object");
        var both=(AggregateException)ProxyController.CollectCleanupFailure(primary,()=>{throw secondary;});
        Check(both.InnerExceptions.Count==2 && Object.ReferenceEquals(both.InnerExceptions[0],primary) && Object.ReferenceEquals(both.InnerExceptions[1],secondary),"double failure preserves both original causes in order");
        Check(!both.Message.Contains("DO-NOT-LOG") && !both.Message.Contains("secret cleanup path"),"double-failure summary redacts raw messages");
        var rows=new System.Collections.Generic.List<string>();var diag=new ProxyDiagnostic(rows.Add);
        diag.Record(DiagnosticStage.PeerVerification,DiagnosticResult.Failed,primary);
        Check(rows[0].Contains("stage=PeerVerification") && rows[0].Contains("TimeoutException") && rows[0].Contains("hresult=0x"),"diagnostic contains actionable stage type and code");
        Check(!rows[0].Contains("DO-NOT-LOG") && !rows[0].Contains("private user"),"diagnostic omits arbitrary exception payload");
        diag.Record(DiagnosticStage.PipeConnect,DiagnosticResult.Failed,new System.ComponentModel.Win32Exception(5,"credential=hidden"));
        Check(rows[1].Contains("win32=5") && !rows[1].Contains("hidden"),"native error code retained without message");
        var brokenLog=new ProxyDiagnostic(line=>{throw new IOException("log unavailable");});
        engine=new Engine();var diagnosticOutput=new StringWriter();
        Check(BrokerSession.Run(new StringReader(Plan().Serialize()+"\nSTART\nSTOP\n"),diagnosticOutput,p=>p.Validate(),p=>true,()=>engine,1000,brokenLog)==0,"logging failure does not change successful session result");
        Check(brokenLog.WriteFailed && engine.Calls.Contains("stop") && engine.Calls.Contains("deleteproxy"),"logging failure still performs engine stop and rollback");
        engine=new Engine{FailStart=true};
        Check(BrokerSession.Run(new StringReader(Plan().Serialize()+"\nSTART\n"),new StringWriter(),p=>p.Validate(),p=>true,()=>engine,1000,brokenLog)==1 && engine.Calls.Contains("stop"),"logging failure also preserves failed-start cleanup");
        engine=new Engine(); rows.Clear();
        Check(BrokerSession.Run(new StringReader(Plan().Serialize()+"\nSTART\nSTOP\n"),new StringWriter(),p=>p.Validate(),p=>true,()=>engine,1000,diag)==0 && rows.Exists(line=>line.Contains("stage=Cleanup result=Success")),"resource cleanup result is diagnosed");
        var noPath=ProxyDiagnostic.OpenProtected(); // Unelevated test must not create files in protected installation.
        Check(noPath.WriteFailed,"protected log unavailable is nonfatal to unelevated tests");noPath.Dispose();
        Check(!ProxySecurity.HasUnprivilegedWrite(ProxyDiagnostic.LogSecurity()),"new protected diagnostic ACL denies unprivileged write and has privileged owner");
        rows.Clear();var factoryError=new InvalidOperationException("cookie=NEVER-RECORD");
        Check(BrokerSession.Run(new StringReader(Plan().Serialize()+"\nSTART\n"),new StringWriter(),p=>p.Validate(),p=>true,()=>{throw factoryError;},1000,diag)==1,"engine constructor failure is reported without driver start");
        Check(rows.Exists(line=>line.Contains("stage=EngineConstruction result=Failed") && line.Contains("InvalidOperationException")) && !rows.Exists(line=>line.Contains("stage=EngineStart")),"constructor failure stage precedes engine start");
        Check(!rows.Exists(line=>line.Contains("NEVER-RECORD")),"constructor failure payload is absent from every diagnostic line");
        engine=new Engine{FailStop=true};rows.Clear();
        Check(BrokerSession.Run(new StringReader(Plan().Serialize()+"\nSTART\nSTOP\n"),new StringWriter(),p=>p.Validate(),p=>true,()=>engine,1000,diag)==2 && rows.Exists(line=>line.Contains("stage=Cleanup result=NotConfirmed")),"unconfirmed stop retains resource uncertainty in diagnosis");
        engine=new Engine{FailStop=true};
        Check(BrokerSession.Run(new StringReader(Plan().Serialize()+"\nSTART\nSTOP\n"),new StringWriter(),p=>p.Validate(),p=>true,()=>engine,1000,brokenLog)==2,"logging failure cannot turn failed cleanup into success");
        diag.Record(DiagnosticStage.HostValidation,DiagnosticResult.Failed,new AggregateException());Check(true,"empty aggregate diagnostic cannot disrupt failure handling");
        brokenLog.Record(DiagnosticStage.Cleanup,DiagnosticResult.Failed,new AggregateException());Check(brokenLog.WriteFailed,"summary and sink failures are both contained");
        int factories=0;rows.Clear();
        foreach(bool flushFailure in new[]{false,true})
            Check(BrokerSession.Run(new StringReader(Plan().Serialize()+"\n"),new BrokenStatusWriter{FailFlush=flushFailure},p=>p.Validate(),p=>false,()=>{factories++;return new Engine();},1000,diag)==0,"pre-engine cancellation tolerates departed-peer "+(flushFailure?"flush":"write")+" failure");
        Check(factories==0 && rows.Exists(line=>line.Contains("stage=Approval result=Cancelled")) && !rows.Exists(line=>line.Contains("result=Failed")),"departed approval cancellation creates no engine and remains cancelled");
        Check(BrokerSession.Run(new StringReader(Plan().Serialize()+"\nSTART\n"),new BrokenStatusWriter(),p=>p.Validate(),p=>true,()=>new Engine(),1000)==1,"approved handshake errors are still failures");
        string cancelledName=ProxyProtocol.NewPipeName();
        using(var cancelledPipe=new NamedPipeServerStream(cancelledName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,ProxySecurity.PipeAcl(WindowsIdentity.GetCurrent().User)))
        using(var cancelledChild=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--cancel-pipe-worker "+cancelledName){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}))
        {
            var connection=System.Threading.Tasks.Task.Factory.FromAsync(cancelledPipe.BeginWaitForConnection,cancelledPipe.EndWaitForConnection,null);Check(connection.Wait(3000),"real cancellation pipe connects");
            var control=new StreamWriter(cancelledPipe,new UTF8Encoding(false),1024,true){AutoFlush=true};control.WriteLine(Plan().Serialize());
            var waiting=cancelledChild.StandardError.ReadLineAsync();Check(waiting.Wait(3000) && waiting.Result=="APPROVAL_WAIT","real child reaches approval before caller disconnect");
            cancelledPipe.Dispose();bool ended=cancelledChild.WaitForExit(3000);string result=ended?cancelledChild.StandardError.ReadToEnd():"pending";
            Check(ended && cancelledChild.ExitCode==0 && result.Contains("ENGINE_CALLS=0"),"actual approval pipe loss exits cleanly without constructing an engine");
        }
        Console.WriteLine("PASS "+count+" assertions; simulated engines and owned test processes only, no native loading, elevation or traffic forwarding.");return 0;
    }
}
