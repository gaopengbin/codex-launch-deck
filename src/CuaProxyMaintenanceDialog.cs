using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChatGPTProxyLauncherLite
{
    internal sealed class CuaProxyMaintenanceDialog : Form
    {
        private readonly CuaProxyForwardingMaintenance maintenance;
        private CuaProxyForwardingMaintenance.Inspection checkedConfig;
        private readonly bool english;
        private readonly TextBox report = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly Button check = new Button(), repair = new Button(), browserRead = new Button(), rollback = new Button();
        private string L(string zh, string en) { return english ? en : zh; }
        internal CuaProxyMaintenanceDialog(bool useEnglish) : this(useEnglish, CuaProxyForwardingMaintenance.CreateDefault()) { }
        internal CuaProxyMaintenanceDialog(bool useEnglish, CuaProxyForwardingMaintenance service)
        {
            english = useEnglish; maintenance = service;
            Text = L("浏览器检查 / 修复", "Browser check / repair");
            Font = new Font("Segoe UI", 10F); BackColor = Color.FromArgb(244, 246, 248);
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(800, 440); MinimumSize = new Size(800, 440);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            var note = new Label { Dock = DockStyle.Fill, Text = L("读取兼容修复让普通入口自动使用 DOM 读取和页面截图，避开无障碍接口超时；代理修复补齐转发。\n每项修改都有完整备份。修复后请用新聊天验证，旧工具进程不会被强制结束。应用更新后可重新检查。", "Read compatibility uses DOM and page screenshots instead of the stalled browser accessibility API. Forwarding repair adds proxy forwarding.\nEach change has a full backup. Validate in a fresh chat; existing tool processes are not terminated. Check again after app updates."), ForeColor = Color.FromArgb(71, 85, 105) };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
            check.Text = L("检查配置", "Check"); repair.Text = L("修复转发", "Repair forwarding"); rollback.Text = L("从备份回滚", "Rollback backup"); repair.Enabled = false;
            browserRead.Text = L("修复读取 / 截图", "Repair reads / screenshots"); browserRead.Enabled = false;
            foreach (Button button in new[] { check, repair, browserRead, rollback }) { button.Size = new Size(175, 32); button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(148, 163, 184); button.BackColor = Color.White; button.Cursor = Cursors.Hand; actions.Controls.Add(button); }
            layout.Controls.Add(note, 0, 0); layout.Controls.Add(report, 0, 1); layout.Controls.Add(actions, 0, 2); Controls.Add(layout);
            check.Click += async delegate { await CheckAsync(); };
            repair.Click += async delegate
            {
                var snapshot = checkedConfig; Busy(true);
                try { var result = await Task.Run(() => maintenance.Repair(snapshot)); checkedConfig = await Task.Run(() => maintenance.Check()); report.Text = checkedConfig.Summary + "\r\n\r\n" + (result.Changed ? L("修复完成。回滚记录：", "Repaired. Rollback record: ") + result.RecordPath : L("配置已正确，未写入。", "Already correct; no write.")); }
                catch (Exception ex) { Failure(ex); }
                finally { Busy(false); }
            };
            browserRead.Click += async delegate {
                var snapshot = checkedConfig; Busy(true);
                try {
                    var result = await Task.Run(() => maintenance.RepairBrowserRead(snapshot));
                    checkedConfig = await Task.Run(() => maintenance.Check());
                    report.Text = checkedConfig.Summary + "\r\n\r\n" + L("已设置读取兼容模式。请在新聊天中验证。回滚记录：", "Read compatibility configured. Verify in a fresh chat. Rollback record: ") + result.RecordPath;
                } catch (Exception ex) { Failure(ex); } finally { Busy(false); }
            };
            rollback.Click += async delegate
            {
                using (var picker = new OpenFileDialog { Title = L("选择 LaunchDeck 回滚记录", "Select LaunchDeck rollback record"), Filter = "LaunchDeck rollback (*.record.json)|*.record.json", InitialDirectory = maintenance.BackupRoot, CheckFileExists = true })
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    string recordPath = picker.FileName; Busy(true);
                    try { await Task.Run(() => maintenance.Rollback(recordPath)); checkedConfig = await Task.Run(() => maintenance.Check()); report.Text = L("已完整恢复原文件。\r\n\r\n", "Original file restored completely.\r\n\r\n") + checkedConfig.Summary; }
                    catch (Exception ex) { Failure(ex); }
                    finally { Busy(false); }
                }
            };
            Shown += async delegate { await CheckAsync(); };
        }
        private void Busy(bool busy) { check.Enabled = !busy; rollback.Enabled = !busy; repair.Enabled = !busy && checkedConfig != null && checkedConfig.Missing.Length > 0; browserRead.Enabled = !busy && checkedConfig != null && checkedConfig.BrowserReadMode == "1"; UseWaitCursor = busy; ControlBox = !busy; }
        private void Failure(Exception error) { checkedConfig = null; report.Text = L("操作未完成：", "Operation not completed: ") + error.Message + "\r\n\r\n" + L("没有重启应用。若配置正在同步，请稍后重新检查。备份目录：", "No app restart. If configuration is syncing, check again later. Backup directory: ") + maintenance.BackupRoot; }
        private async Task CheckAsync() { Busy(true); try { checkedConfig = await Task.Run(() => maintenance.Check()); report.Text = checkedConfig.Summary; } catch (Exception ex) { Failure(ex); } finally { Busy(false); } }
    }
}
