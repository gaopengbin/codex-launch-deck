using System;
using System.Reflection;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

internal static class LauncherWindowLifetimeTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static int Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(args[0]);
        var type = assembly.GetType("ChatGPTProxyLauncherLite.LauncherForm", true);
        var activationTimer=System.Diagnostics.Stopwatch.StartNew();
        var pendingActivation=(System.Threading.Tasks.Task)type.GetMethod("StartPackagedChatGPT",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"http://127.0.0.1:10808",null,null});
        if(activationTimer.ElapsedMilliseconds>=1500) throw new Exception("Activation blocked the calling UI thread.");
        if(!pendingActivation.Wait(5000)) throw new Exception("Parallel helper output draining did not finish.");
        Console.WriteLine("PASS activation returns without blocking and drains a full stderr pipe");
        var helper = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])), "PackagedChatGPTLauncher.exe"));
        var keys = (string[])helper.GetType("PackagedChatGPTLauncher", true).GetField("ProxyKeys", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach(string key in new[] { "WS_PROXY", "WSS_PROXY" })
            if(Array.IndexOf(keys,key)<0) throw new Exception("WebSocket key missing from environment backup/restore: " + key);
        Console.WriteLine("PASS WebSocket activation backup/restore key coverage");
        foreach (string scenario in new[] { "active-close", "explicit-exit", "shutdown", "inactive-close" })
        {
            var traceType=assembly.GetType("ChatGPTProxyLauncherLite.LauncherDiagnostics",true);
            var traceRoot=Path.Combine(Path.GetTempPath(),"launchdeck-ui-diagnostics-"+Guid.NewGuid().ToString("N"));
            var trace=(IDisposable)Activator.CreateInstance(traceType,Fields,null,new object[]{traceRoot},null);
            var form=(Form)Activator.CreateInstance(type,Fields,null,new object[]{trace},null);
            var controller = type.GetField("processProxy", Fields).GetValue(form);
            var state = controller.GetType().GetProperty("State");
            var tray = (NotifyIcon)type.GetField("sessionTray", Fields).GetValue(form);
            try
            {
                traceType.GetField("heartbeat",Fields).SetValue(trace,System.Diagnostics.Stopwatch.GetTimestamp()-10*System.Diagnostics.Stopwatch.Frequency);
                traceType.GetMethod("Stage",Fields).Invoke(trace,new object[]{"isolated-ui-wait"});
                traceType.GetMethod("Sample",Fields).Invoke(trace,null);
                var traceFiles=Directory.GetFiles(traceRoot,"launcher-*.log");
                var retained=traceFiles.Length==1?File.ReadAllText(traceFiles[0]):"";
                var lag=System.Text.RegularExpressions.Regex.Match(retained,@"stage=isolated-ui-wait uiLagSeconds=([0-9.]+)");
                if(!lag.Success || Double.Parse(lag.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)<10)
                    throw new Exception("Stalled UI diagnostic was not retained.");
                traceType.GetMethod("Heartbeat",Fields).Invoke(trace,null);
                if(scenario=="active-close" && args.Length>1) {
                    // Render this isolated form offscreen. Remove its startup handler
                    // so preview generation cannot probe ports or launch a client.
                    var eventKey=typeof(Form).GetField("EVENT_SHOWN",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                    var events=(System.ComponentModel.EventHandlerList)typeof(System.ComponentModel.Component).GetProperty("Events",Fields).GetValue(form,null);
                    events.RemoveHandler(eventKey,events[eventKey]);
                    form.StartPosition=FormStartPosition.Manual;
                    form.Location=new Point(-32000,-32000);
                    form.Show();
                    using(var bitmap=new Bitmap(form.Width,form.Height)) {
                        form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));
                        bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.Hide();
                }
                if (scenario != "inactive-close")
                    state.GetSetMethod(true).Invoke(controller, new[] { Enum.Parse(state.PropertyType, "Ready") });
                if (scenario == "explicit-exit") type.GetField("explicitExit", Fields).SetValue(form, true);
                // Suppress the notification; no client, native engine, or network is started.
                type.GetField("trayNoticeShown", Fields).SetValue(form, true);
                var closing = new FormClosingEventArgs(scenario == "shutdown" ? CloseReason.WindowsShutDown : CloseReason.UserClosing, false);
                typeof(Form).GetMethod("OnFormClosing", Fields).Invoke(form, new object[] { closing });
                bool retain = scenario == "active-close";
                if (closing.Cancel != retain || tray.Visible != retain || state.GetValue(controller, null).ToString() != (retain ? "Ready" : "Off"))
                    throw new Exception("Unexpected session lifetime: " + scenario);
                Console.WriteLine("PASS " + scenario);
            }
            finally
            {
                ((IDisposable)controller).Dispose();
                ((Timer)type.GetField("processProxyHealth", Fields).GetValue(form)).Dispose();
                tray.Dispose();
                form.Dispose();
                trace.Dispose();
            }
        }
        return 0;
    }
}
