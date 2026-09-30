using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace LaunchDeck.ProcessProxy
{
    internal sealed class ApprovalLifetime
    {
        private readonly Func<bool> connected;
        private readonly Func<long> elapsed;
        private readonly long deadline;
        internal ApprovalLifetime(Func<bool> connected,Func<long> elapsed,long deadline) { this.connected=connected; this.elapsed=elapsed; this.deadline=deadline; }
        internal bool Active { get { return elapsed()<deadline && connected(); } }
    }
    internal static class ProxyApproval
    {
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool PeekNamedPipe(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,IntPtr buffer,uint size,out uint read,out uint available,out uint left);
        internal static bool Confirm(ProxyPlan plan,NamedPipeClientStream pipe)
        {
            var clock=Stopwatch.StartNew();
            var lifetime=new ApprovalLifetime(()=> { uint read,available,left; return PeekNamedPipe(pipe.SafePipeHandle,IntPtr.Zero,0,out read,out available,out left); },()=>clock.ElapsedMilliseconds,120000);
            using(var form=new Form { Text="Launch Deck driver and traffic approval",Width=620,Height=330,StartPosition=FormStartPosition.CenterScreen,MaximizeBox=false,MinimizeBox=false })
            using(var timer=new Timer { Interval=100 })
            {
                var description=new Label { Left=20,Top=20,Width=565,Height=210,Text="Enable enhanced proxy?\n\n"+plan.ExecutablePath+"\n\nRoute this executable's ALL remote TCP 443 through "+plan.ProxyHost+":"+plan.ProxyPort+". The driver captures TCP 443 and relay packets to identify flows; unmatched applications are direct. No UDP/DNS capture, TLS interception, system proxy or TUN changes.\n\nApproval cancels if Launch Deck disconnects or after two minutes." };
                var accept=new Button { Left=360,Top=240,Width=100,Text="Enable",DialogResult=DialogResult.OK };
                var cancel=new Button { Left=475,Top=240,Width=100,Text="Cancel",DialogResult=DialogResult.Cancel };
                form.Controls.Add(description);form.Controls.Add(accept);form.Controls.Add(cancel);form.AcceptButton=accept;form.CancelButton=cancel;
                timer.Tick+=delegate { if(!lifetime.Active) { form.DialogResult=DialogResult.Cancel;form.Close(); } };
                if(!lifetime.Active) return false;
                timer.Start();
                return form.ShowDialog()==DialogResult.OK && lifetime.Active;
            }
        }
    }
}
