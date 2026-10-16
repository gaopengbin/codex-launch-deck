using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LaunchDeck.ProcessProxy;
class DiagnosticTests
{
    static int checks;
    static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL "+label);checks++;}
    static int Main()
    {
        var rows=new List<string>();var d=new ProxyDiagnostic(rows.Add);
        var plan=new ProxyPlan {ExecutablePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"WindowsApps\OpenAI.Codex_26.930.7945.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"),ProxyHost="127.0.0.1",ProxyPort=7890,Process443Approved=true,DriverApproved=true};
        d.RecordPlan(plan);d.RecordRules(1,2,3);
        d.Record(DiagnosticStage.EngineStart,DiagnosticResult.Failed,new IOException("Authorization: Bearer SENTINEL-SECRET https://host/path?token=private chat=SENTINEL-CHAT"));
        d.QueueMetadata(1,95112,6,50000,3,1,1,0,0,0);
        d.QueueMetadata(1,95112,6,50000,3,1,2,0,0,0);
        d.QueueMetadata(1,95112,6,50000,3,1,3,1,10061,0);
        d.QueueMetadata(1,95112,6,50000,3,1,4,1,0,403);
        d.Dispose();string output=String.Join("\n",rows);
        Check(output.Contains("7945")&&output.Contains("7890")&&output.Contains("targetRule=3"),"verified package/upstream/rule association");
        Check(output.Contains("httpStatus=403")&&output.Contains("error=10061"),"CONNECT status and Winsock codes retained");
        Check(!output.Contains("SENTINEL")&&!output.Contains("Authorization")&&!output.Contains("token=")&&!output.Contains("https://")&&!output.Contains("chat="),"raw exception credentials, URL and chat excluded");
        Check(rows.Count(x=>x.Contains(" flow=1 "))==4,"one immutable flow links numeric stages");
        Check(ProxyDiagnostic.FormatMetadata(0,1,4,1,1,1,1,0,0,0)==null,"zero flow rejected");
        Check(ProxyDiagnostic.FormatMetadata(1,1,7,1,1,1,1,0,0,0)==null,"unknown address family rejected");
        Check(ProxyDiagnostic.FormatMetadata(1,1,4,1,1,1,99,0,0,0)==null,"unknown stage rejected");
        Check(ProxyDiagnostic.FormatMetadata(1,1,4,1,1,1,4,1,0,999999)==null,"untrusted status clamped by schema");
        rows=new List<string>();var unsafePlan=new ProxyDiagnostic(rows.Add);plan.ExecutablePath=plan.ExecutablePath.Replace("26.930.7945.0","SENTINEL-SECRET");unsafePlan.RecordPlan(plan);unsafePlan.Dispose();
        Check(!String.Join("\n",rows).Contains("SENTINEL"),"unverified package text is never logged");
        int bytes=0;var cap=new ProxyDiagnostic(line=>Interlocked.Add(ref bytes,Encoding.UTF8.GetByteCount(line)+2));
        for(int i=0;i<50000;i++)cap.Record(DiagnosticStage.Ready,DiagnosticResult.Success);cap.Dispose();
        Check(bytes<=ProxyDiagnostic.MaxBytes&&cap.BytesWritten<=ProxyDiagnostic.MaxBytes,"file budget includes phase lines and UTF8/newlines");
        int events=0;var eventCap=new ProxyDiagnostic(line=>{if(line.Contains(" flow="))Interlocked.Increment(ref events);});
        for(int i=0;i<10000;i++)eventCap.QueueMetadata((ulong)i+1,1,4,50000,3,1,1,0,0,0);eventCap.Dispose();
        Check(events<=ProxyDiagnostic.MaxMetadataEvents&&eventCap.DroppedEvents>0,"per-session event capacity is enforced");
        var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);
        var slow=new ProxyDiagnostic(line=>{entered.Set();release.WaitOne();});slow.QueueMetadata(1,1,4,50000,3,1,1,0,0,0);Check(entered.WaitOne(1000),"slow writer reached isolated sink");
        var watch=Stopwatch.StartNew();for(int i=0;i<600;i++)slow.QueueMetadata((ulong)i+2,1,4,50000,3,1,1,0,0,0);watch.Stop();
        Check(watch.ElapsedMilliseconds<1000&&slow.DroppedEvents>0,"packet callback remains nonblocking under slow disk/backpressure");release.Set();slow.Dispose();
        var broken=new ProxyDiagnostic(line=>{throw new IOException("Bearer SENTINEL-SECRET");});broken.QueueMetadata(1,1,4,50000,3,1,3,1,10061,0);broken.Dispose();Check(broken.WriteFailed,"sink failure is contained");
        Console.WriteLine("PASS "+checks+" diagnostic assertions; no native DLL/driver, network or protected writes.");return 0;
    }
}
