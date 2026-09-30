using System;
using System.IO;
using System.Threading.Tasks;
namespace LaunchDeck.ProcessProxy
{
    internal static class BrokerSession
    {
        internal static int Run(TextReader reader, TextWriter writer, Action<ProxyPlan> validate,
            Func<ProxyPlan,bool> approve, Func<IProxyEngine> factory, int startupTimeoutMs, ProxyDiagnostic diagnostic=null)
        {
            diagnostic=diagnostic??new ProxyDiagnostic((Action<string>)null);
            diagnostic.Record(DiagnosticStage.PlanValidation,DiagnosticResult.Enter);
            ProxySession session=null; bool startupPending=false;
            try
            {
                string first=ProxyProtocol.ReadLine(reader);
                if(first==null) return 0;
                var plan=ProxyPlan.Parse(first);
                validate(plan); // Production host always rechecks the registered protected package.
                diagnostic.Record(DiagnosticStage.Approval,DiagnosticResult.Enter);
                if(!approve(plan))
                {
                    diagnostic.Record(DiagnosticStage.Approval,DiagnosticResult.Cancelled);
                    // Approval ended before factory/start; a departed caller cannot receive status.
                    // This transport failure is not an engine/cleanup failure and owns no resources.
                    try { writer.WriteLine("CANCELLED"); writer.Flush(); } catch(IOException) {}
                    return 0;
                }
                writer.WriteLine("APPROVED"); writer.Flush();
                // A canceled/disconnected launcher never causes delayed activation after approval.
                string command=ProxyProtocol.ReadLine(reader);
                if(command==null) return 0;
                if(command!="START") throw new InvalidDataException("Expected START.");
                diagnostic.Record(DiagnosticStage.EngineConstruction,DiagnosticResult.Enter);
                session=new ProxySession(factory());
                diagnostic.Record(DiagnosticStage.EngineStart,DiagnosticResult.Enter);
                startupPending=true;
                Task start=Task.Factory.StartNew(()=>session.Start(plan));
                if(!start.Wait(startupTimeoutMs))
                {
                    diagnostic.Record(DiagnosticStage.EngineStart,DiagnosticResult.NotConfirmed,new TimeoutException());
                    writer.WriteLine("ERROR startup-timeout"); writer.Flush();
                    return 3; // Dedicated host exits; do not race Dispose against a live native Start.
                }
                startupPending=false;
                diagnostic.Record(DiagnosticStage.Ready,DiagnosticResult.Success);
                writer.WriteLine("READY"); writer.Flush();
                ProxyProtocol.RequireStop(ProxyProtocol.ReadLine(reader)); // STOP or EOF only.
                diagnostic.Record(DiagnosticStage.Stop,DiagnosticResult.Enter);
                session.Dispose();
                diagnostic.Record(DiagnosticStage.Cleanup,session.CleanupError==null?DiagnosticResult.Success:DiagnosticResult.NotConfirmed);
                if(session.CleanupError!=null) { writer.WriteLine("ERROR cleanup-not-confirmed"); writer.Flush(); return 2; }
                try { writer.WriteLine("STOPPED"); writer.Flush(); } catch(IOException) {} return 0;
            }
            catch(Exception error)
            {
                diagnostic.Record(diagnostic.Stage,DiagnosticResult.Failed,error);
                try { writer.WriteLine("ERROR session-failed"); writer.Flush(); } catch(IOException) {}
                return 1;
            }
            finally
            {
                if(startupPending) diagnostic.Record(DiagnosticStage.Cleanup,DiagnosticResult.NotConfirmed);
                if(session!=null && !startupPending) { session.Dispose(); diagnostic.Record(DiagnosticStage.Cleanup,session.CleanupError==null?DiagnosticResult.Success:DiagnosticResult.NotConfirmed); }
            }
        }
    }
}
