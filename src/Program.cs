using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChatGPTProxyLauncherLite
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            PortableRuntime.ReadArguments(args);
            Application.Run(new LauncherForm());
        }
    }

    internal sealed class LauncherForm : Form
    {
        private readonly LaunchDeck.ProcessProxy.ProxyController processProxy = new LaunchDeck.ProcessProxy.ProxyController();
        private CuaProxyForwardingWatch browserForwardingWatch;
        private string lastBrowserForwardingError;
        private readonly System.Windows.Forms.Timer processProxyHealth = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly TextBox hostBox = new TextBox { Text = "127.0.0.1" };
        private readonly NumericUpDown portBox = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 10808 };
        private readonly Button launchButton = new Button { Height = 44 };
        private readonly Button browserProxyButton = new Button { Height = 32 };
        private readonly Button languageButton = new Button { Height = 28, Width = 48 };
        private readonly Label headerDescription = new Label();
        private readonly Label hostLabel = new Label();
        private readonly Label portLabel = new Label();
        private readonly Label footnote = new Label();
        private readonly Label statusLabel = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label statusMark = new Label { AutoSize = false, Text = "●", TextAlign = ContentAlignment.MiddleCenter };
        private readonly Panel statusPanel = new Panel();
        private bool english;
        private readonly NotifyIcon sessionTray = new NotifyIcon();
        private bool explicitExit;
        private bool trayNoticeShown;


        public LauncherForm()
        {
            Text = "ChatGPT Proxy Launcher";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            sessionTray.Icon = Icon;
            sessionTray.Text = "LaunchDeck";
            sessionTray.DoubleClick += delegate { RestoreFromTray(); };
            var trayMenu = new ContextMenuStrip();
            var openTrayItem = trayMenu.Items.Add("打开启动器 / Open launcher");
            openTrayItem.Click += delegate { RestoreFromTray(); };
            var exitTrayItem = trayMenu.Items.Add("退出并停止增强代理 / Exit and stop enhanced proxy");
            exitTrayItem.Click += delegate { explicitExit = true; Close(); };
            sessionTray.ContextMenuStrip = trayMenu;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 458);
            MinimumSize = new Size(496, 497);
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(244, 246, 248);
            ForeColor = Color.FromArgb(23, 23, 23);

            var header = new Panel { Dock = DockStyle.Top, Height = 118, BackColor = Color.FromArgb(23, 23, 23) };
            var badge = new Label
            {
                Text = "LOCAL PROXY",
                Font = new Font("Segoe UI Semibold", 8F),
                ForeColor = Color.FromArgb(212, 175, 55),
                Location = new Point(28, 19),
                AutoSize = true
            };
            languageButton.Location = new Point(402, 16);
            languageButton.FlatStyle = FlatStyle.Flat;
            languageButton.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            languageButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            languageButton.BackColor = Color.FromArgb(23, 23, 23);
            languageButton.ForeColor = Color.FromArgb(212, 175, 55);
            languageButton.Cursor = Cursors.Hand;
            languageButton.TabStop = true;
            languageButton.Click += delegate { english = !english; ApplyLanguage(); };
            var title = new Label
            {
                Text = "ChatGPT Proxy Launcher",
                Font = new Font("Segoe UI Semibold", 20F),
                ForeColor = Color.White,
                Location = new Point(26, 38),
                AutoSize = true
            };
            headerDescription.ForeColor = Color.FromArgb(190, 190, 190);
            headerDescription.Location = new Point(29, 82);
            headerDescription.Size = new Size(423, 22);
            header.Controls.Add(badge);
            header.Controls.Add(languageButton);
            header.Controls.Add(title);
            header.Controls.Add(headerDescription);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 22) };
            var fields = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, Location = new Point(28, 24), Size = new Size(424, 62) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            hostLabel.AutoSize = true;
            hostLabel.ForeColor = Color.FromArgb(64, 64, 64);
            portLabel.AutoSize = true;
            portLabel.ForeColor = Color.FromArgb(64, 64, 64);
            fields.Controls.Add(hostLabel, 0, 0);
            fields.Controls.Add(portLabel, 1, 0);
            hostBox.Dock = DockStyle.Fill;
            hostBox.Margin = new Padding(0, 6, 10, 0);
            portBox.Dock = DockStyle.Fill;
            portBox.Margin = new Padding(0, 6, 0, 0);
            fields.Controls.Add(hostBox, 0, 1);
            fields.Controls.Add(portBox, 1, 1);

            launchButton.Location = new Point(28, 104);
            launchButton.Size = new Size(424, 44);
            launchButton.BackColor = Color.FromArgb(23, 23, 23);
            launchButton.ForeColor = Color.White;
            launchButton.FlatStyle = FlatStyle.Flat;
            launchButton.FlatAppearance.BorderSize = 0;
            launchButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            launchButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 0, 0);
            launchButton.Cursor = Cursors.Hand;
            launchButton.Click += LaunchButtonClick;

            statusPanel.Location = new Point(28, 204);
            statusPanel.Size = new Size(424, 48);
            statusPanel.BackColor = Color.White;
            statusPanel.BorderStyle = BorderStyle.FixedSingle;
            statusMark.Location = new Point(8, 7);
            statusMark.Size = new Size(24, 26);
            statusMark.ForeColor = Color.FromArgb(100, 116, 139);
            statusLabel.Location = new Point(35, 4);
            statusLabel.Size = new Size(378, 38);
            statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
            statusPanel.Controls.Add(statusMark);
            statusPanel.Controls.Add(statusLabel);

            footnote.ForeColor = Color.FromArgb(100, 116, 139);
            footnote.Font = new Font("Segoe UI", 9F);
            footnote.Location = new Point(29, 264);
            footnote.Size = new Size(423, 48);
            body.Controls.Add(fields);
            body.Controls.Add(launchButton);
            body.Controls.Add(statusPanel);
            body.Controls.Add(footnote);
            browserProxyButton.Location = new Point(28, 160);
            browserProxyButton.Size = new Size(424, 32);
            browserProxyButton.FlatStyle = FlatStyle.Flat;
            browserProxyButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            browserProxyButton.BackColor = Color.White;
            browserProxyButton.Cursor = Cursors.Hand;
            browserProxyButton.Click += delegate {
                try { using (var dialog = new CuaProxyMaintenanceDialog(english)) dialog.ShowDialog(this); }
                catch (Exception ex) { SetStatus(ex.Message, false); }
            };
            body.Controls.Add(browserProxyButton);
            processProxyHealth.Tick += delegate {
                try { processProxy.PollHealth(); }
                catch (Exception ex) { SetStatus(ex.Message, false); return; }
                string error=browserForwardingWatch==null?null:browserForwardingWatch.LastError;
                if(error!=lastBrowserForwardingError) {
                    if(error!=null) SetStatus(L("浏览器代理配置检查失败：", "Browser proxy configuration check failed: ")+error,false);
                    else if(lastBrowserForwardingError!=null) SetStatus(L("浏览器代理配置已恢复；已运行的工具可能需要重新连接", "Browser proxy configuration restored; existing tools may need reconnecting"),null);
                    lastBrowserForwardingError=error;
                }
            };
            processProxyHealth.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                // Closing the window must not silently cut an active client's network.
                // Windows shutdown and the explicit tray exit still perform cleanup.
                if(e.CloseReason == CloseReason.UserClosing && !explicitExit &&
                    processProxy.State == LaunchDeck.ProcessProxy.ProxyState.Ready) {
                    e.Cancel = true;
                    sessionTray.Visible = true;
                    Hide();
                    if(!trayNoticeShown) {
                        trayNoticeShown = true;
                        sessionTray.ShowBalloonTip(4000, "LaunchDeck",
                            L("增强代理继续运行。双击托盘图标打开；右键可退出并停止代理。",
                              "Enhanced proxy remains active. Double-click to open; right-click to exit and stop it."), ToolTipIcon.Info);
                    }
                    return;
                }
                processProxyHealth.Stop();
                if(browserForwardingWatch!=null) browserForwardingWatch.Dispose();
                try { processProxy.Dispose(); } catch (Exception ex) { MessageBox.Show(ex.Message); }
            };
            FormClosed += delegate { sessionTray.Dispose(); trayMenu.Dispose(); };
            Controls.Add(body);
            Controls.Add(header);
            AcceptButton = launchButton;
            ApplyLanguage();
            Shown += async delegate {
                var resume = PortableRuntime.Resume;
                if (resume != null) {
                    hostBox.Text = resume.Host; portBox.Value = resume.Port;
                    english = resume.English; ApplyLanguage();
                    LaunchButtonClick(this, EventArgs.Empty);
                } else {
                    // Reuse a listening local proxy instead of resetting this machine
                    // to a port where no proxy is running.
                    foreach (int candidate in new[] { 7890, 10808, 1080 })
                        if (await Task.Run(() => CanConnect("127.0.0.1", candidate))) { portBox.Value = candidate; break; }
                }
            };
        }

        private string L(string chinese, string englishText)
        {
            return english ? englishText : chinese;
        }

        private void ApplyLanguage()
        {
            languageButton.Text = english ? "中文" : "EN";
            languageButton.AccessibleName = english ? "切换到中文" : "Switch to English";
            headerDescription.Text = L(
                "无需开启全局代理，只代理本次启动的 ChatGPT / Codex",
                "Proxy ChatGPT / Codex without enabling a global proxy");
            hostLabel.Text = L("代理主机", "Proxy host");
            portLabel.Text = L("HTTP / Mixed 端口", "HTTP / Mixed port");
            launchButton.Text = L("启动 ChatGPT / Codex", "Launch ChatGPT / Codex");
            footnote.Text = L(
                "默认启用增强代理。关闭窗口将收至托盘继续运行；右键托盘图标可退出并停止代理。",
                "Enhanced proxy is always enabled. Closing keeps it in the tray; right-click the tray icon to exit and stop it.");
            browserProxyButton.Text = L("检查 / 修复浏览器", "Check / repair browser");
            browserProxyButton.AccessibleName = browserProxyButton.Text;
            SetStatus(L("准备就绪 · 增强代理默认启用", "Ready · Enhanced proxy enabled"), null);
        }

        private async void LaunchButtonClick(object sender, EventArgs e)
        {
            launchButton.Enabled = false;
            browserProxyButton.Enabled = false;
            hostBox.Enabled = false; portBox.Enabled = false;
            UseWaitCursor = true;
            try
            {
                string host = hostBox.Text.Trim();
                int port = (int)portBox.Value;
                if (host.Length == 0) throw new Exception(L("请输入代理主机。", "Enter a proxy host."));
                SetStatus(L("正在检查代理…", "Checking proxy…"), null);

                bool reachable = await Task.Run(() => CanConnect(host, port));
                if (!reachable) throw new Exception(String.Format(L("无法连接代理 {0}:{1}。", "Cannot reach proxy {0}:{1}."), host, port));
                if (IsDesktopAppRunning()) throw new Exception(L("ChatGPT 已在运行，请完全退出后再试。", "ChatGPT is already running. Fully quit it and try again."));

                string proxyUrl = String.Format("http://{0}:{1}", host, port);
                {
                    SetStatus(L("正在准备增强代理组件…", "Preparing enhanced proxy components…"), null);
                    var resumeSettings = new PortableResume {
                        Host=host, Port=port, English=english
                    };
                    bool handedOff = await Task.Run(() => PortableRuntime.Handoff(resumeSettings));
                    if (handedOff) { Close(); return; }
                    // Verify the protected, pinned bundle before requesting administrator approval.
                    SetStatus(L("增强代理：等待引擎与驱动授权", "Enhanced proxy: waiting for engine and driver approval"), null);
                    processProxy.Prepare();

                    await Task.Run(() => {
                        string executable=LaunchDeck.ProcessProxy.ProxyController.ResolveExecutable();
                        processProxy.Start(new LaunchDeck.ProcessProxy.ProxyPlan {
                            ExecutablePath=executable,
                            BrowserHelperPaths=LaunchDeck.ProcessProxy.BrowserHelperScope.Resolve(executable),
                            CoreHelperPaths=LaunchDeck.ProcessProxy.CoreHelperScope.Resolve(executable),
                            ProxyHost=host, ProxyPort=port, Process443Approved=true, DriverApproved=true
                        });
                    });
                    SetStatus(L("增强代理已就绪", "Enhanced proxy ready"), true);
                }
                if(browserForwardingWatch==null) browserForwardingWatch=CuaProxyForwardingWatch.CreateDefault();
                {
                    SetStatus(L("正在查找 ChatGPT…", "Finding ChatGPT…"), null);
                    StartPackagedChatGPT(proxyUrl, null, processProxy.State == LaunchDeck.ProcessProxy.ProxyState.Ready ? processProxy.TargetExecutable : null);
                    SetStatus(L("已通过 " + proxyUrl + " 启动 ChatGPT", "ChatGPT started through " + proxyUrl), true);
                }
            }
            catch (Exception ex) {
                // Keep an approved session for its live client, including when
                // Launch is pressed again while that client is already running.
                bool keepProxy=false;
                try { keepProxy=processProxy.State==LaunchDeck.ProcessProxy.ProxyState.Ready && IsDesktopAppRunning(); } catch { }
                if(keepProxy) SetStatus(L("客户端已启动，增强代理保持运行；后续步骤失败：", "Client started; enhanced proxy remains active. A later step failed: ")+ex.Message,false);
                else {
                    string cleanup="";
                    try { processProxy.Dispose(); } catch(Exception stopError) { cleanup=" "+stopError.Message; }
                    SetStatus(ex.Message+cleanup,ex is OperationCanceledException?(bool?)null:false);
                }
            }
            finally { UseWaitCursor = false; browserProxyButton.Enabled = true; launchButton.Enabled = true; hostBox.Enabled = true; portBox.Enabled = true; }
        }

        private void SetStatus(string text, bool? success)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = success == true ? Color.FromArgb(21, 128, 61) : success == false ? Color.FromArgb(185, 28, 28) : Color.FromArgb(71, 85, 105);
            statusMark.ForeColor = statusLabel.ForeColor;
            statusPanel.BackColor = success == true ? Color.FromArgb(240, 253, 244) : success == false ? Color.FromArgb(254, 242, 242) : Color.White;
        }

        private static bool CanConnect(string host, int port)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    var result = client.BeginConnect(host, port, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(1800)) return false;
                    client.EndConnect(result);
                    return client.Connected;
                }
                catch { return false; }
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            sessionTray.Visible = false;
        }

        private static bool IsDesktopAppRunning()
        {
            return Process.GetProcessesByName("ChatGPT").Any(p => IsAppExecutable(p, "ChatGPT.exe")) ||
                   Process.GetProcessesByName("Codex").Any(p => IsAppExecutable(p, "Codex.exe"));
        }

        private static bool IsAppExecutable(Process process, string fileName)
        {
            try { return process.MainModule.FileName.EndsWith("\\app\\" + fileName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
            finally { process.Dispose(); }
        }

        private static void StartPackagedChatGPT(string proxyUrl, int? cdpPort, string expectedExecutable)
        {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PackagedChatGPTLauncher.exe");
            if (!File.Exists(helper))
                throw new FileNotFoundException("PackagedChatGPTLauncher.exe is missing.", helper);
            string arguments = QuoteArgument(proxyUrl) + (expectedExecutable != null ? " " + (cdpPort ?? 0) + " " + QuoteArgument(expectedExecutable) : (cdpPort.HasValue ? " " + cdpPort.Value : ""));
            var info = new ProcessStartInfo(helper, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd().Trim();
                string error = process.StandardError.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode != 0 || output.Length == 0)
                    throw new InvalidOperationException(error.Length == 0 ? "ChatGPT package activation failed." : error);
            }
        }

    }
}
