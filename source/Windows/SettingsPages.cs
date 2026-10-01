using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace CodeUsageMonit {
    // Settings › 数据 (other devices, SQL import / export, backups, WebDAV) and › 关于.
    public sealed partial class MonitorPanel {
        // What the 数据 page hands to Save; its buttons act at once.
        private sealed class DataFields {
            public TextBox Device, Url, User; public PasswordBox Password; public CheckBox Forget;
            public Func<int> BackupHours, BackupKeep, SyncMinutes; public Func<string> BackupFolder;
        }
        private static readonly int[] BackupChoices = { 0, 6, 24, 72, 168 }, SyncChoices = { 0, 15, 60, 360, 1440 };

        private DataFields BuildDataPage(StackPanel page) {
            var fields = new DataFields();
            DeviceIdentity self = demo ? new DeviceIdentity { Id = "demo", Name = Environment.MachineName } : Devices.Self();

            // Devices: this one (its name) and the ones imported here.
            page.Children.Add(SectionTitle(I18n.T("设备")));
            var deviceCard = new StackPanel();
            var nameRow = Row();
            fields.Device = new TextBox { Text = self.Name, Width = 170, MaxLength = 40, IsEnabled = !demo };
            System.Windows.Automation.AutomationProperties.SetName(fields.Device, I18n.T("本机名称"));
            AddRow(nameRow, FieldLabel(I18n.T("本机名称"), I18n.T("导出和同步时用它标识这台电脑")), fields.Device);
            deviceCard.Children.Add(nameRow);
            var deviceList = new StackPanel(); deviceCard.Children.Add(deviceList);
            Action renderDevices = null;
            renderDevices = () => {
                deviceList.Children.Clear(); deviceList.Children.Add(Separator());
                List<DeviceData> imported = demo ? new List<DeviceData>() : Devices.Imported();
                if (imported.Count == 0) { deviceList.Children.Add(Hint(I18n.T("还没有其他设备的用量。在另一台电脑上导出 SQL 后在下面导入，或两台都设置同一个 WebDAV 文件夹。"), 0)); return; }
                deviceList.Children.Add(Label(I18n.T("已导入的设备"), 11, InkDim));
                foreach (DeviceData d in imported) {
                    var row = Row(); row.Margin = new Thickness(0, 8, 0, 0);
                    var left = new StackPanel();
                    left.Children.Add(Label(d.Name, 12.5, Ink));
                    DateTime when; string at = LogIndex.Parse(d.Imported, out when) ? when.ToLocalTime().ToString("M/d HH:mm", CultureInfo.InvariantCulture) : "—";
                    var sub = Label(I18n.T("近一个月 {0} Token · {1} · {2} 更新", Compact(d.Tokens()), Usd(d.Cost()), at), 10.5, InkFaint); sub.Margin = new Thickness(0, 2, 0, 0); left.Children.Add(sub);
                    var remove = new Button { Style = Styled("LinkButton"), Content = I18n.T("移除"), Foreground = WarnBrush };
                    System.Windows.Automation.AutomationProperties.SetName(remove, I18n.T("移除") + " " + d.Name);
                    string id = d.Id; bool confirm = false;
                    remove.Click += delegate {
                        if (!confirm) { confirm = true; remove.Content = I18n.T("再次点击确认"); return; }
                        try { Devices.Remove(id); } catch { }
                        ReloadDevices(true); renderDevices();
                    };
                    AddRow(row, left, remove); deviceList.Children.Add(row);
                }
                deviceList.Children.Add(Hint(I18n.T("其他设备的用量计入概览、各平台和第三方页面。Cursor 的用量取自账户本身，不会重复计入。"), 10));
            };
            renderDevices();
            page.Children.Add(Card(deviceCard));

            // SQL import / export.
            page.Children.Add(SectionTitle(I18n.T("导入与导出")));
            var transfer = new StackPanel();
            transfer.Children.Add(Hint(I18n.T("把本机（和已导入设备）的每小时用量导出为 SQL 文件，在另一台电脑上导入即可合并查看；同一文件重复导入不会重复计算。文件只有 Token、请求数、费用和耗时，没有对话、密钥或账户，也可以直接导入 SQLite。"), 0));
            var transferButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var export = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("导出 SQL…"), IsEnabled = !demo, Margin = new Thickness(0, 0, 8, 0) };
            var import = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("导入 SQL…"), IsEnabled = !demo };
            transferButtons.Children.Add(export); transferButtons.Children.Add(import); transfer.Children.Add(transferButtons);
            var transferResult = Hint(demo ? I18n.T("演示模式不读写数据。") : "", 8); transfer.Children.Add(transferResult);
            export.Click += async delegate {
                string safe = new String(self.Name.Select(c => Char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
                var dialog = new Microsoft.Win32.SaveFileDialog { Title = I18n.T("导出用量"), Filter = I18n.T("SQL 文件") + "|*.sql", FileName = "codeusagemonit-" + safe + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".sql" };
                if (dialog.ShowDialog(settingsWindow) != true) return;
                export.IsEnabled = false; string path = dialog.FileName;
                try { await Task.Run(() => File.WriteAllText(path, Devices.ExportSql(DateTime.UtcNow, true), new UTF8Encoding(false))); Say(transferResult, true, I18n.T("已导出到 {0}", path)); }
                catch (Exception e) { Say(transferResult, false, I18n.T("导出失败：{0}", e.Message)); }
                finally { export.IsEnabled = true; }
            };
            import.Click += async delegate {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = I18n.T("导入用量"), Filter = I18n.T("SQL 文件") + "|*.sql|" + I18n.T("所有文件") + "|*.*" };
                if (dialog.ShowDialog(settingsWindow) != true) return;
                import.IsEnabled = false; string path = dialog.FileName;
                try {
                    if (new FileInfo(path).Length > 256L * 1024 * 1024) throw new InvalidDataException(I18n.T("文件超过 256 MB。"));
                    ImportResult result = await Task.Run(() => Devices.ImportSql(File.ReadAllText(path), DateTime.UtcNow));
                    Say(transferResult, result.Rows + result.Same + result.Duplicate + result.Own > 0, result.Summary());
                    if (result.Rows > 0) { ReloadDevices(false); renderDevices(); }
                } catch (Exception e) { Say(transferResult, false, I18n.T("导入失败：{0}", e.Message)); }
                finally { import.IsEnabled = true; }
            };
            page.Children.Add(Card(transfer));

            // Backups.
            page.Children.Add(SectionTitle(I18n.T("备份与恢复")));
            var backupCard = new StackPanel();
            int backupHours = config.BackupHours, backupKeep = Math.Max(1, Math.Min(50, config.BackupKeep)); string backupFolder = config.BackupFolder ?? "";
            Func<AppConfig> backupConfig = () => new AppConfig { BackupFolder = backupFolder, BackupKeep = backupKeep, BackupHours = backupHours };
            var autoRow = Row();
            AddRow(autoRow, FieldLabel(I18n.T("自动备份"), I18n.T("到时间后在后台备份一次")), Segmented(BackupChoices.Select(h => new[] { h.ToString(CultureInfo.InvariantCulture), h == 0 ? I18n.T("关闭") : h < 24 ? I18n.T("{0} 小时", h) : h == 24 ? I18n.T("每天") : h == 168 ? I18n.T("每周") : I18n.T("{0} 天", h / 24) }).ToArray(), Nearest(BackupChoices, backupHours).ToString(CultureInfo.InvariantCulture), code => backupHours = Int32.Parse(code, CultureInfo.InvariantCulture)));
            backupCard.Children.Add(autoRow); backupCard.Children.Add(Separator());
            var keepRow = Row(); var keepStepper = new StackPanel { Orientation = Orientation.Horizontal };
            var keepMinus = new Button { Style = Styled("IconButton"), Content = "", FontSize = 11, Width = 28, Height = 28, ToolTip = I18n.T("少保留一份") };
            var keepValue = Label(I18n.T("{0} 份", backupKeep), 12, Ink); keepValue.MinWidth = 48; keepValue.TextAlignment = TextAlignment.Center; Tabular(keepValue);
            var keepPlus = new Button { Style = Styled("IconButton"), Content = "", FontSize = 11, Width = 28, Height = 28, ToolTip = I18n.T("多保留一份") };
            keepMinus.Click += delegate { backupKeep = Math.Max(1, backupKeep - 1); keepValue.Text = I18n.T("{0} 份", backupKeep); };
            keepPlus.Click += delegate { backupKeep = Math.Min(50, backupKeep + 1); keepValue.Text = I18n.T("{0} 份", backupKeep); };
            keepStepper.Children.Add(keepMinus); keepStepper.Children.Add(keepValue); keepStepper.Children.Add(keepPlus);
            AddRow(keepRow, FieldLabel(I18n.T("保留份数"), I18n.T("只删除较早的自动备份，手动备份不会被删除")), keepStepper);
            backupCard.Children.Add(keepRow); backupCard.Children.Add(Separator());
            var whereRow = Row();
            var whereLabel = FieldLabel(I18n.T("备份位置"), Backups.Folder(backupConfig()));
            var whereButtons = new StackPanel { Orientation = Orientation.Horizontal };
            var change = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("更改…"), IsEnabled = !demo };
            var openBackups = new Button { Style = Styled("LinkButton"), Content = I18n.T("打开"), Margin = new Thickness(4, 0, 0, 0) };
            whereButtons.Children.Add(change); whereButtons.Children.Add(openBackups);
            AddRow(whereRow, whereLabel, whereButtons); backupCard.Children.Add(whereRow);
            var backupList = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            var backupResult = Hint(demo ? I18n.T("演示模式不读写数据。") : "", 8);
            Action renderBackups = null;
            Action<string> restore = path => {
                if (MessageBox.Show(settingsWindow, I18n.T("用这份备份覆盖当前的设置和用量数据吗？\n\n恢复前会先把当前状态另存一份备份；恢复后程序会重新启动。"), "codeusagemonit", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                try { Backups.Restore(backupConfig(), path, DateTime.UtcNow); RestartFresh(); }
                catch (Exception e) { Say(backupResult, false, I18n.T("恢复失败：{0}", e.Message)); }
            };
            renderBackups = () => {
                backupList.Children.Clear();
                ((TextBlock)((StackPanel)whereLabel).Children[1]).Text = Backups.Folder(backupConfig());
                List<BackupInfo> items;
                try { items = demo ? new List<BackupInfo>() : Backups.List(backupConfig()); } catch { items = new List<BackupInfo>(); }
                backupList.Children.Add(Separator());
                if (items.Count == 0) { backupList.Children.Add(Hint(I18n.T("还没有备份。"), 0)); return; }
                foreach (BackupInfo item in items.Take(5)) {
                    var row = Row(); row.Margin = new Thickness(0, 4, 0, 4);
                    var left = Label(item.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "  ·  " + (item.Auto ? I18n.T("自动") : I18n.T("手动")) + "  ·  " + FileSize(item.Size), 11.5, Ink); Tabular(left);
                    var restoreButton = new Button { Style = Styled("LinkButton"), Content = I18n.T("恢复"), IsEnabled = !demo };
                    string path = item.Path; restoreButton.Click += delegate { restore(path); };
                    AddRow(row, left, restoreButton); backupList.Children.Add(row);
                }
                if (items.Count > 5) backupList.Children.Add(Hint(I18n.T("共 {0} 份，较早的在备份文件夹里", items.Count), 2));
            };
            backupCard.Children.Add(backupList);
            var backupButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var backupNow = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("立即备份"), IsEnabled = !demo, Margin = new Thickness(0, 0, 8, 0) };
            var restoreFile = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("从文件恢复…"), IsEnabled = !demo };
            backupButtons.Children.Add(backupNow); backupButtons.Children.Add(restoreFile); backupCard.Children.Add(backupButtons);
            backupCard.Children.Add(backupResult);
            backupCard.Children.Add(Hint(I18n.T("备份包含设置、自定义平台、接口切换记录、用量索引和已导入的设备，不含 API 密钥（它们只能由本机当前的 Windows 用户解密，恢复时保持不变）。换电脑或多台电脑一起用，请用上面的导入导出或 WebDAV。"), 10));
            change.Click += delegate {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = I18n.T("选择备份文件夹"), SelectedPath = Backups.Folder(backupConfig()), ShowNewFolderButton = true }) {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    backupFolder = String.Equals(dialog.SelectedPath.TrimEnd('\\'), Path.Combine(Store.Data, "backups"), StringComparison.OrdinalIgnoreCase) ? "" : dialog.SelectedPath;
                    renderBackups(); Say(backupResult, true, I18n.T("保存设置后生效。"));
                }
            };
            openBackups.Click += delegate { string dir = Backups.Folder(backupConfig()); try { Directory.CreateDirectory(dir); Process.Start("explorer.exe", "\"" + dir + "\""); } catch { } };
            backupNow.Click += async delegate {
                backupNow.IsEnabled = false; AppConfig c = backupConfig();
                try { string path = await Task.Run(() => Backups.Create(c, false, DateTime.UtcNow)); Say(backupResult, true, I18n.T("已备份：{0}", Path.GetFileName(path))); renderBackups(); }
                catch (Exception e) { Say(backupResult, false, I18n.T("备份失败：{0}", e.Message)); }
                finally { backupNow.IsEnabled = true; }
            };
            restoreFile.Click += delegate {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = I18n.T("选择备份文件"), Filter = I18n.T("codeusagemonit 备份") + "|*.zip", InitialDirectory = Directory.Exists(Backups.Folder(backupConfig())) ? Backups.Folder(backupConfig()) : "" };
                if (dialog.ShowDialog(settingsWindow) == true) restore(dialog.FileName);
            };
            renderBackups();
            fields.BackupHours = () => backupHours; fields.BackupKeep = () => backupKeep; fields.BackupFolder = () => backupFolder;
            page.Children.Add(Card(backupCard));

            // WebDAV.
            page.Children.Add(SectionTitle(I18n.T("云同步（WebDAV）")));
            var syncCard = new StackPanel();
            syncCard.Children.Add(Hint(I18n.T("几台电脑填同一个 WebDAV 文件夹（坚果云、Nextcloud、群晖等）。每台只上传自己的用量、下载其他设备的用量，互不覆盖；上传的内容与 SQL 导出相同。"), 0));
            syncCard.Children.Add(FieldStack(I18n.T("文件夹地址"), fields.Url = new TextBox { Text = config.WebDavUrl, IsEnabled = !demo }, I18n.T("例如 https://dav.jianguoyun.com/dav/codeusagemonit/")));
            syncCard.Children.Add(FieldStack(I18n.T("用户名"), fields.User = new TextBox { Text = config.WebDavUser, IsEnabled = !demo }, null));
            bool savedPassword = !demo && Store.HasSavedKey(WebDavSync.KeyId);
            syncCard.Children.Add(FieldStack(I18n.T("密码"), fields.Password = new PasswordBox { IsEnabled = !demo }, savedPassword ? I18n.T("已保存（DPAPI 加密）。留空则保留。坚果云请用“应用密码”。") : I18n.T("坚果云请在“账户信息 › 安全选项”里生成应用密码。")));
            if (savedPassword) { fields.Forget = SwitchRow(I18n.T("移除已保存的密码"), null, false); fields.Forget.Margin = new Thickness(0, 8, 0, 0); syncCard.Children.Add(fields.Forget); }
            syncCard.Children.Add(Separator());
            int syncMinutes = Nearest(SyncChoices, config.SyncMinutes);
            var intervalRow = Row();
            AddRow(intervalRow, FieldLabel(I18n.T("自动同步"), null), Segmented(SyncChoices.Select(m => new[] { m.ToString(CultureInfo.InvariantCulture), m == 0 ? I18n.T("手动") : m < 60 ? I18n.T("{0} 分钟", m) : m < 1440 ? I18n.T("{0} 小时", m / 60) : I18n.T("每天") }).ToArray(), syncMinutes.ToString(CultureInfo.InvariantCulture), code => syncMinutes = Int32.Parse(code, CultureInfo.InvariantCulture)));
            syncCard.Children.Add(intervalRow);
            var syncButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var test = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("测试连接"), IsEnabled = !demo, Margin = new Thickness(0, 0, 8, 0) };
            var syncNow = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("立即同步"), IsEnabled = !demo };
            syncButtons.Children.Add(test); syncButtons.Children.Add(syncNow); syncCard.Children.Add(syncButtons);
            var syncResult = Hint("", 8); syncCard.Children.Add(syncResult);
            Action showState = () => {
                if (demo) { syncResult.Text = I18n.T("演示模式不读写数据。"); return; }
                SyncState state = WebDavSync.State(); DateTime last;
                if (!LogIndex.Parse(state.Last, out last)) { syncResult.Text = I18n.T("还没有同步过。"); syncResult.Foreground = InkFaint; return; }
                Say(syncResult, state.Ok, I18n.T("上次同步 {0} · {1}", last.ToLocalTime().ToString("M/d HH:mm", CultureInfo.InvariantCulture), state.Message));
            };
            Func<string> password = () => fields.Password.Password.Length > 0 ? fields.Password.Password : Store.SavedKey(WebDavSync.KeyId);
            test.Click += async delegate {
                test.IsEnabled = false; syncResult.Foreground = InkDim; syncResult.Text = I18n.T("正在连接…");
                string url = fields.Url.Text, user = fields.User.Text.Trim(), pass = password();
                try { Say(syncResult, true, await Task.Run(() => WebDavSync.Test(config, url, user, pass))); }
                catch (Exception e) { Say(syncResult, false, e is ProviderException ? e.Message : I18n.T("无法连接：{0}", e.Message)); }
                finally { test.IsEnabled = true; }
            };
            syncNow.Click += async delegate {
                syncNow.IsEnabled = false; syncResult.Foreground = InkDim; syncResult.Text = I18n.T("正在同步…");
                await SyncNow(fields.Url.Text, fields.User.Text.Trim(), password());
                showState(); renderDevices(); syncNow.IsEnabled = true;
            };
            showState();
            syncCard.Children.Add(Hint(I18n.T("密码以 Windows DPAPI 加密保存在本机；同步只上传用量数字，不上传密钥、对话或账户。"), 10));
            fields.SyncMinutes = () => syncMinutes;
            page.Children.Add(Card(syncCard));
            return fields;
        }
        private static int Nearest(int[] choices, int value) { return choices.OrderBy(c => Math.Abs(c - value)).First(); }
        private static string FileSize(long bytes) { return bytes >= 1 << 20 ? (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : Math.Max(1, bytes / 1024) + " KB"; }
        private static void Say(TextBlock text, bool ok, string message) { text.Foreground = ok ? GoodBrush : WarnBrush; text.Text = message; }
        private static FrameworkElement FieldStack(string title, Control input, string hint) {
            var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            stack.Children.Add(Label(title, 12, Ink));
            input.Margin = new Thickness(0, 6, 0, 0); System.Windows.Automation.AutomationProperties.SetName(input, title); stack.Children.Add(input);
            if (hint != null) stack.Children.Add(Hint(hint, 4));
            return stack;
        }
        // WebDAV sync with the given details (the 数据 page, before Save) or the saved ones.
        private async Task<bool> SyncNow(string url, string user, string password) {
            string message; bool ok; AppConfig c = config;
            try { message = await Task.Run(() => WebDavSync.Run(c, url, user, password, DateTime.UtcNow)); ok = true; }
            catch (Exception e) { message = e is ProviderException ? e.Message : I18n.T("同步失败：{0}", e.Message); ok = false; }
            WebDavSync.Record(ok, message, DateTime.UtcNow);
            if (ok) ReloadDevices(false);
            return ok;
        }

        // ── 关于 ──────────────────────────────────────────────────────────
        // Returns the automatic update check switch (saved with the other settings).
        private CheckBox BuildAboutPage(StackPanel page) {
            page.Children.Add(SectionTitle(I18n.T("关于")));
            var card = new StackPanel();
            var name = Label("codeusagemonit " + AppInfo.Version, 14, Ink); name.FontWeight = FontWeights.SemiBold; card.Children.Add(name);
            card.Children.Add(Hint(I18n.T("code用量监控 · 开源免费 · MIT 许可"), 3));
            card.Children.Add(Separator());
            var updateRow = Row();
            var updateLabel = FieldLabel(I18n.T("检查更新"), "");
            var updateText = (TextBlock)((StackPanel)updateLabel).Children[1]; updateText.TextWrapping = TextWrapping.Wrap; updateText.TextTrimming = TextTrimming.None;
            var updateButtons = new StackPanel { Orientation = Orientation.Horizontal };
            var download = new Button { Style = Styled("PrimaryButton"), Content = I18n.T("前往下载"), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
            var check = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("检查更新") };
            updateButtons.Children.Add(download); updateButtons.Children.Add(check);
            AddRow(updateRow, updateLabel, updateButtons); card.Children.Add(updateRow);
            Action showUpdate = () => {
                UpdateState u = updateState; DateTime at; string when = LogIndex.Parse(u.Checked, out at) ? at.ToLocalTime().ToString("M/d HH:mm", CultureInfo.InvariantCulture) : "";
                bool newer = Updates.Newer(u);
                download.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
                updateText.Foreground = newer ? GoodBrush : u.Error.Length > 0 ? WarnBrush : InkFaint;
                updateText.Text = I18n.T(newer ? I18n.T("发现新版本 v{0}（当前 v{1}）", u.Latest, AppInfo.Version)
                    : u.Error.Length > 0 ? u.Error
                    : u.Latest.Length > 0 ? I18n.T("已是最新版本 · {0} 检查", when)
                    : I18n.T("当前 v{0}", AppInfo.Version));
            };
            download.Click += delegate { string url = updateState.Url.Length > 0 ? updateState.Url : Updates.Releases; try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            check.Click += async delegate {
                check.IsEnabled = false; updateText.Foreground = InkDim; updateText.Text = I18n.T("正在检查…");
                updateState = await Updates.Check(config, DateTime.UtcNow);
                showUpdate(); UpdateStatus(); check.IsEnabled = true;
            };
            showUpdate();
            card.Children.Add(Separator());
            CheckBox auto = SwitchRow(I18n.T("每天自动检查更新"), I18n.T("只向 GitHub 查询最新版本号，不上传任何数据"), config.UpdateCheck);
            card.Children.Add(auto);
            page.Children.Add(Card(card));

            page.Children.Add(SectionTitle(I18n.T("链接")));
            var links = new StackPanel();
            foreach (var link in new[] {
                new { Title = I18n.T("使用说明"), Target = Path.Combine(Store.Root, "使用说明.md") },
                new { Title = I18n.T("项目主页（GitHub）"), Target = "https://github.com/fanchengliu/codeusagemonit" },
                new { Title = I18n.T("网站"), Target = "https://codeusagemonit.vercel.app/" },
                new { Title = I18n.T("许可证与第三方声明"), Target = Path.Combine(Store.Root, "THIRD-PARTY-NOTICES.md") } }) {
                string target = link.Target;
                var button = new Button { Style = Styled("LinkButton"), Content = link.Title, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 0, 0, 2) };
                button.Click += delegate { try { if (target.StartsWith("https://") || File.Exists(target)) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { } };
                links.Children.Add(button);
            }
            links.Children.Add(Hint(I18n.T("数据目录 {0}", Store.Data), 6));
            links.Children.Add(Hint(I18n.T("与 OpenAI、Anthropic、Cursor 等被监控的服务均无关联；产品名称和图标归各自所有者。"), 4));
            page.Children.Add(Card(links));
            return auto;
        }

        // The language changed on Save: the tray menu and every page are rebuilt in it.
        private void LanguageChanged() {
            try { Forms.ContextMenuStrip old = tray.ContextMenuStrip; tray.ContextMenuStrip = TrayMenu(); if (old != null) old.Dispose(); } catch { }
            UpdateTray(); Render();
        }
    }
}
