using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace ChatGPTProxyLauncherLite
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
        }
    }

    internal sealed class LauncherForm : Form
    {
        private readonly TextBox hostBox = new TextBox { Text = "127.0.0.1" };
        private readonly NumericUpDown portBox = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 10808 };
        private readonly CheckBox themeCheckBox = new CheckBox { AutoSize = true };
        private readonly ComboBox themeComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button refreshThemesButton = new Button { Height = 30 };
        private readonly LinkLabel storeLink = new LinkLabel { AutoSize = true };
        private readonly Button launchButton = new Button { Height = 44 };
        private readonly Button restoreThemeButton = new Button { Height = 32 };
        private readonly Button languageButton = new Button { Height = 28, Width = 48 };
        private readonly Label headerDescription = new Label();
        private readonly Label hostLabel = new Label();
        private readonly Label portLabel = new Label();
        private readonly Label footnote = new Label();
        private readonly Label statusLabel = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label statusMark = new Label { AutoSize = false, Text = "●", TextAlign = ContentAlignment.MiddleCenter };
        private readonly Panel statusPanel = new Panel();
        private Process themeWatcher;
        private string themeWatcherError = "";
        private bool loadingThemes;
        private bool english;

        private const int CodeDrobePort = 9335;
        private const string ThemeFileName = "miku-future-beats-1.2.2.codedrobe-theme";

        public LauncherForm()
        {
            Text = "ChatGPT Proxy Launcher";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 500);
            MinimumSize = new Size(496, 539);
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

            themeCheckBox.Location = new Point(28, 102);
            themeCheckBox.Checked = FindThemePackage() != null;
            themeCheckBox.CheckedChanged += delegate { themeComboBox.Enabled = themeCheckBox.Checked; };

            themeComboBox.Location = new Point(28, 130);
            themeComboBox.Size = new Size(302, 30);
            themeComboBox.DisplayMember = "DisplayName";
            themeComboBox.Items.Add(CreateBundledTheme());
            themeComboBox.SelectedIndex = 0;
            themeComboBox.Enabled = themeCheckBox.Checked;

            refreshThemesButton.Location = new Point(340, 129);
            refreshThemesButton.Size = new Size(112, 30);
            refreshThemesButton.FlatStyle = FlatStyle.Flat;
            refreshThemesButton.FlatAppearance.BorderColor = Color.FromArgb(148, 163, 184);
            refreshThemesButton.BackColor = Color.White;
            refreshThemesButton.Cursor = Cursors.Hand;
            refreshThemesButton.Click += async delegate { await RefreshThemesAsync(true); };

            storeLink.Location = new Point(28, 166);
            storeLink.LinkColor = Color.FromArgb(14, 116, 144);
            storeLink.ActiveLinkColor = Color.FromArgb(8, 145, 178);
            storeLink.LinkClicked += delegate
            {
                Process.Start(new ProcessStartInfo("https://codedrobe.app/themes") { UseShellExecute = true });
            };

            launchButton.Location = new Point(28, 194);
            launchButton.Size = new Size(424, 44);
            launchButton.BackColor = Color.FromArgb(23, 23, 23);
            launchButton.ForeColor = Color.White;
            launchButton.FlatStyle = FlatStyle.Flat;
            launchButton.FlatAppearance.BorderSize = 0;
            launchButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            launchButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 0, 0);
            launchButton.Cursor = Cursors.Hand;
            launchButton.Click += LaunchButtonClick;

            restoreThemeButton.Location = new Point(28, 250);
            restoreThemeButton.Size = new Size(424, 32);
            restoreThemeButton.FlatStyle = FlatStyle.Flat;
            restoreThemeButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            restoreThemeButton.BackColor = Color.White;
            restoreThemeButton.Cursor = Cursors.Hand;
            restoreThemeButton.Click += RestoreThemeButtonClick;

            statusPanel.Location = new Point(28, 294);
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
            footnote.Location = new Point(29, 354);
            footnote.Size = new Size(423, 48);
            body.Controls.Add(fields);
            body.Controls.Add(themeCheckBox);
            body.Controls.Add(themeComboBox);
            body.Controls.Add(refreshThemesButton);
            body.Controls.Add(storeLink);
            body.Controls.Add(launchButton);
            body.Controls.Add(restoreThemeButton);
            body.Controls.Add(statusPanel);
            body.Controls.Add(footnote);
            Controls.Add(body);
            Controls.Add(header);
            AcceptButton = launchButton;
            ApplyLanguage();
            Shown += async delegate { await RefreshThemesAsync(false); };
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
            launchButton.Text = L("通过代理启动 ChatGPT", "Launch ChatGPT through proxy");
            themeCheckBox.Text = L("启用主题（CodeDrobe）", "Enable theme (CodeDrobe)");
            refreshThemesButton.Text = L("刷新商店", "Refresh store");
            storeLink.Text = L("浏览 CodeDrobe 主题商店", "Browse the CodeDrobe theme store");
            restoreThemeButton.Text = L("恢复 Codex 原生外观", "Restore native Codex appearance");
            footnote.Text = L(
                "线上主题下载后会缓存到本机；代理仅应用于本次启动，不修改 Windows 系统代理。主题模式需要 Node.js / npx。",
                "Store themes are cached locally. The proxy applies only to this launch and does not change the Windows system proxy. Theme mode requires Node.js / npx.");
            RefreshThemeDisplayNames();
            SetStatus(L("准备就绪", "Ready"), null);
        }

        private async void LaunchButtonClick(object sender, EventArgs e)
        {
            launchButton.Enabled = false;
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
                if (themeCheckBox.Checked)
                {
                    ThemeItem selectedTheme = themeComboBox.SelectedItem as ThemeItem ?? CreateBundledTheme();
                    string themePath = await ResolveThemePackageAsync(selectedTheme, proxyUrl);
                    if (themePath == null) throw new Exception(L("未找到 CodeDrobe 主题包。", "The CodeDrobe theme package was not found."));

                    SetStatus(L("正在通过 CodeDrobe 启动并应用主题…", "Starting Codex and applying the theme through CodeDrobe…"), null);
                    string applyArguments =
                        "apply --app codex --port " + CodeDrobePort +
                        " --theme " + QuoteArgument(themePath);
                    CodeDrobeResult apply = await RunCodeDrobeAsync(applyArguments, proxyUrl);
                    if (apply.ExitCode != 0) throw new Exception(GetCodeDrobeError(apply));

                    themeWatcher = StartCodeDrobe(
                        applyArguments + " --no-launch --watch",
                        proxyUrl,
                        true);

                    SetStatus(String.Format(
                        L("已通过代理启动 Codex，并应用主题：{0}", "Codex started through the proxy with theme: {0}"),
                        selectedTheme.DisplayName), true);
                }
                else
                {
                    SetStatus(L("正在查找 ChatGPT…", "Finding ChatGPT…"), null);
                    string executable = await Task.Run(() => FindDesktopExecutable());
                    if (executable == null) throw new Exception(L("未找到 ChatGPT/Codex Windows 桌面应用。", "ChatGPT/Codex Desktop was not found."));

                    var info = new ProcessStartInfo(executable) { UseShellExecute = false };
                    ApplyProxyEnvironment(info, proxyUrl);
                    Process.Start(info);
                    SetStatus(L("已通过 " + proxyUrl + " 启动 ChatGPT", "ChatGPT started through " + proxyUrl), true);
                }
            }
            catch (Exception ex) { SetStatus(ex.Message, false); }
            finally { UseWaitCursor = false; launchButton.Enabled = true; }
        }

        private async void RestoreThemeButtonClick(object sender, EventArgs e)
        {
            restoreThemeButton.Enabled = false;
            UseWaitCursor = true;
            try
            {
                if (IsDesktopAppRunning())
                {
                    throw new Exception(L(
                        "请先完全退出 Codex，再恢复原生外观。",
                        "Fully quit Codex before restoring its native appearance."));
                }

                SetStatus(L("正在恢复 Codex 原生外观…", "Restoring the native Codex appearance…"), null);
                CodeDrobeResult result = await RunCodeDrobeAsync(
                    "restore --app codex --port " + CodeDrobePort,
                    null);
                if (result.ExitCode != 0) throw new Exception(GetCodeDrobeError(result));
                SetStatus(L("已恢复 Codex 原生外观", "Native Codex appearance restored"), true);
            }
            catch (Exception ex) { SetStatus(ex.Message, false); }
            finally { UseWaitCursor = false; restoreThemeButton.Enabled = true; }
        }

        private void SetStatus(string text, bool? success)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = success == true ? Color.FromArgb(21, 128, 61) : success == false ? Color.FromArgb(185, 28, 28) : Color.FromArgb(71, 85, 105);
            statusMark.ForeColor = statusLabel.ForeColor;
            statusPanel.BackColor = success == true ? Color.FromArgb(240, 253, 244) : success == false ? Color.FromArgb(254, 242, 242) : Color.White;
        }

        private async Task RefreshThemesAsync(bool userInitiated)
        {
            if (loadingThemes) return;
            loadingThemes = true;
            refreshThemesButton.Enabled = false;
            if (userInitiated) SetStatus(L("正在加载主题商店…", "Loading the theme store…"), null);

            try
            {
                string proxyUrl = String.IsNullOrWhiteSpace(hostBox.Text)
                    ? null
                    : String.Format("http://{0}:{1}", hostBox.Text.Trim(), (int)portBox.Value);
                CodeDrobeResult result = await RunCodeDrobeAsync(
                    "theme search --app codex --limit 100 --json",
                    proxyUrl);
                if (result.ExitCode != 0) throw new Exception(GetCodeDrobeError(result));

                List<ThemeItem> storeThemes = ParseStoreThemes(result.StandardOutput);
                string selectedSlug = (themeComboBox.SelectedItem as ThemeItem ?? CreateBundledTheme()).Slug;
                ThemeItem bundled = CreateBundledTheme();

                themeComboBox.BeginUpdate();
                themeComboBox.Items.Clear();
                themeComboBox.Items.Add(bundled);
                foreach (ThemeItem item in storeThemes.Where(item => item.Slug != bundled.Slug))
                    themeComboBox.Items.Add(item);
                RefreshThemeDisplayNames();

                ThemeItem selected = themeComboBox.Items.Cast<ThemeItem>().FirstOrDefault(item => item.Slug == selectedSlug);
                themeComboBox.SelectedItem = selected ?? bundled;
                themeComboBox.EndUpdate();

                SetStatus(String.Format(
                    L("主题商店已加载：{0} 个线上主题", "Theme store loaded: {0} online themes"),
                    storeThemes.Count), true);
            }
            catch (Exception ex)
            {
                if (userInitiated)
                    SetStatus(ex.Message, false);
                else
                    SetStatus(L("主题商店暂时不可用，仍可使用内置初音主题", "The theme store is unavailable; the bundled Miku theme is still available"), null);
            }
            finally
            {
                refreshThemesButton.Enabled = true;
                loadingThemes = false;
            }
        }

        private async Task<string> ResolveThemePackageAsync(ThemeItem theme, string proxyUrl)
        {
            if (theme.Bundled) return FindThemePackage();

            string cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChatGPTProxyLauncher",
                "themes");
            Directory.CreateDirectory(cacheDirectory);
            string fileName = SafeFilePart(theme.Slug) + "-" + SafeFilePart(theme.Version) + ".codedrobe-theme";
            string themePath = Path.Combine(cacheDirectory, fileName);
            if (File.Exists(themePath)) return themePath;

            SetStatus(String.Format(
                L("正在下载主题：{0}", "Downloading theme: {0}"),
                theme.DisplayName), null);
            CodeDrobeResult result = await RunCodeDrobeAsync(
                "theme download " + QuoteArgument(theme.Slug) +
                " --output " + QuoteArgument(themePath) +
                " --json",
                proxyUrl);
            if (result.ExitCode != 0)
            {
                if (File.Exists(themePath)) File.Delete(themePath);
                throw new Exception(GetCodeDrobeError(result));
            }
            if (!File.Exists(themePath)) throw new Exception(L("主题下载完成，但未找到主题文件。", "The theme download completed, but its package was not found."));
            return themePath;
        }

        private void RefreshThemeDisplayNames()
        {
            foreach (ThemeItem item in themeComboBox.Items)
                item.DisplayName = item.GetDisplayName(english);
            themeComboBox.DisplayMember = "";
            themeComboBox.DisplayMember = "DisplayName";
            themeComboBox.Refresh();
        }

        private static List<ThemeItem> ParseStoreThemes(string json)
        {
            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            object themesValue;
            object[] rows;
            if (root == null || !root.TryGetValue("themes", out themesValue) || (rows = themesValue as object[]) == null)
                throw new Exception("CodeDrobe returned an invalid theme catalog.");

            var themes = new List<ThemeItem>();
            foreach (object rowValue in rows)
            {
                var row = rowValue as Dictionary<string, object>;
                if (row == null) continue;
                var names = GetDictionary(row, "name");
                string slug = GetString(row, "slug");
                string version = GetString(row, "version");
                if (String.IsNullOrWhiteSpace(slug) || String.IsNullOrWhiteSpace(version)) continue;

                var item = new ThemeItem
                {
                    Slug = slug,
                    Version = version,
                    NameZh = GetString(names, "zh"),
                    NameEn = GetString(names, "en"),
                    Author = GetString(row, "author")
                };
                item.DisplayName = item.GetDisplayName(false);
                themes.Add(item);
            }
            return themes;
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value)
                ? value as Dictionary<string, object>
                : null;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : "";
        }

        private static string SafeFilePart(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        }

        private static ThemeItem CreateBundledTheme()
        {
            var item = new ThemeItem
            {
                Slug = "miku-future-beats",
                Version = "1.2.2",
                NameZh = "初音未来 · Future Beats（内置）",
                NameEn = "Hatsune Miku · Future Beats (bundled)",
                Author = "local",
                Bundled = true
            };
            item.DisplayName = item.NameZh;
            return item;
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

        private Process StartCodeDrobe(string arguments, string proxyUrl, bool captureErrors)
        {
            ProcessStartInfo info = CreateCodeDrobeStartInfo(arguments);
            if (proxyUrl != null) ApplyProxyEnvironment(info, proxyUrl);
            info.RedirectStandardOutput = captureErrors;
            info.RedirectStandardError = captureErrors;

            Process process = new Process { StartInfo = info, EnableRaisingEvents = true };
            if (captureErrors)
            {
                themeWatcherError = "";
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!String.IsNullOrWhiteSpace(e.Data)) themeWatcherError = e.Data;
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!String.IsNullOrWhiteSpace(e.Data)) themeWatcherError = e.Data;
                };
            }

            if (!process.Start()) throw new Exception(L("无法启动 CodeDrobe。", "Could not start CodeDrobe."));
            if (captureErrors)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            return process;
        }

        private async Task<CodeDrobeResult> RunCodeDrobeAsync(string arguments, string proxyUrl)
        {
            ProcessStartInfo info = CreateCodeDrobeStartInfo(arguments);
            if (proxyUrl != null) ApplyProxyEnvironment(info, proxyUrl);
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            using (Process process = Process.Start(info))
            {
                if (process == null) throw new Exception(L("无法启动 CodeDrobe。", "Could not start CodeDrobe."));
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                await Task.Run(() => process.WaitForExit());
                return new CodeDrobeResult(process.ExitCode, await standardOutput, await standardError);
            }
        }

        private static ProcessStartInfo CreateCodeDrobeStartInfo(string arguments)
        {
            string runner = FindOnPath("codedrobe.cmd");
            string runnerArguments = arguments;
            if (runner == null)
            {
                runner = FindOnPath("npx.cmd");
                if (runner == null)
                {
                    throw new Exception("CodeDrobe requires Node.js / npx. Install Node.js or install @codedrobe/core globally.");
                }
                runnerArguments = "--yes @codedrobe/core@latest " + arguments;
            }

            string commandLine = "/d /s /c \"\"" + runner + "\" " + runnerArguments + "\"";
            return new ProcessStartInfo("cmd.exe", commandLine)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        private static void ApplyProxyEnvironment(ProcessStartInfo info, string proxyUrl)
        {
            foreach (string key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy" })
                info.EnvironmentVariables[key] = proxyUrl;
            info.EnvironmentVariables["NO_PROXY"] = "localhost,127.0.0.1,::1";
            info.EnvironmentVariables["no_proxy"] = "localhost,127.0.0.1,::1";
        }

        private static string FindOnPath(string fileName)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (String.IsNullOrWhiteSpace(directory)) continue;
                try
                {
                    string candidate = Path.Combine(directory.Trim(), fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        private static string FindThemePackage()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string parentDirectory = Directory.GetParent(baseDirectory.TrimEnd(Path.DirectorySeparatorChar)) == null
                ? null
                : Directory.GetParent(baseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "themes", ThemeFileName),
                Path.Combine(baseDirectory, ThemeFileName),
                parentDirectory == null ? "" : Path.Combine(parentDirectory, "themes", ThemeFileName)
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private string GetCodeDrobeError(CodeDrobeResult result)
        {
            string message = String.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            string[] lines = (message ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string codedrobeMessage = lines.LastOrDefault(line => line.TrimStart().StartsWith("[codedrobe]", StringComparison.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(codedrobeMessage)) return codedrobeMessage.Trim();
            return lines.Length == 0
                ? L("CodeDrobe 操作失败。", "CodeDrobe operation failed.")
                : lines[lines.Length - 1];
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

        private static string FindDesktopExecutable()
        {
            const string command = "$p=@(Get-AppxPackage -Name 'OpenAI.Codex';Get-AppxPackage -Name 'OpenAI.ChatGPT-Desktop')|Sort-Object Version -Descending|Select-Object -First 1;if($p){$p.InstallLocation}";
            var info = new ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -NonInteractive -Command \"" + command + "\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                string location = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode != 0 || location.Length == 0) return null;
                foreach (string name in new[] { "ChatGPT.exe", "Codex.exe" })
                {
                    string path = Path.Combine(location, "app", name);
                    if (File.Exists(path)) return path;
                }
            }
            return null;
        }

        private sealed class CodeDrobeResult
        {
            public readonly int ExitCode;
            public readonly string StandardOutput;
            public readonly string StandardError;

            public CodeDrobeResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }
        }

        private sealed class ThemeItem
        {
            public string Slug { get; set; }
            public string Version { get; set; }
            public string NameZh { get; set; }
            public string NameEn { get; set; }
            public string Author { get; set; }
            public string DisplayName { get; set; }
            public bool Bundled { get; set; }

            public string GetDisplayName(bool english)
            {
                string name = english ? NameEn : NameZh;
                if (String.IsNullOrWhiteSpace(name)) name = english ? NameZh : NameEn;
                if (String.IsNullOrWhiteSpace(name)) name = Slug;
                return Bundled || String.IsNullOrWhiteSpace(Author)
                    ? name
                    : name + "  ·  " + Author;
            }
        }
    }
}
