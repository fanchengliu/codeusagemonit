using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CodeUsageMonit {
    // Connecting a provider from its own card (overview and detail page): API keys are
    // entered inline, GitHub uses the device login, CLI logins open a terminal, desktop
    // apps are launched, custom providers open their editor. Settings keep the same
    // options; both write the same DPAPI key files and config.
    public sealed partial class MonitorPanel {
        private static readonly string[] KeyProviders = { "deepseek", "kimi", "opencode", "zcode" };
        // Typed-but-unsaved keys and regions survive page rebuilds (refresh, clock tick).
        private readonly Dictionary<string, string> keyDrafts = new Dictionary<string, string>(), regionDrafts = new Dictionary<string, string>(), connectNotes = new Dictionary<string, string>();
        private sealed class CopilotFlow { public ProviderService.DeviceLogin Pending; public bool Cancelled; }
        private CopilotFlow copilotFlow;
        private bool renderDeferred;

        // While a key is being typed into a card, rebuilding the page would drop focus and
        // the half-typed key; Render() waits until the box loses focus.
        private bool EditingKey() {
            var focused = Keyboard.FocusedElement as DependencyObject;
            return focused is PasswordBox && (bodyScroll.IsAncestorOf(focused) || compactRoot.IsAncestorOf(focused));
        }
        private void KeyBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e) {
            // Focus moving to a button in the panel: its click renders; rebuilding now would
            // swallow that click.
            if (renderDeferred && !(e.NewFocus is ButtonBase)) app.Dispatcher.BeginInvoke(new Action(Render));
        }

        private UIElement ConnectPanel(ProviderState state) {
            string id = state.Id;
            var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (ProviderCatalog.Custom.ContainsKey(id)) CustomConnect(panel, id);
            else if (KeyProviders.Contains(id)) KeyConnect(panel, state);
            else if (id == "copilot") CopilotConnect(panel, state);
            else if (id == "codex" || id == "claude" || id == "grok") CliConnect(panel, id);
            else if (id == "cursor" || id == "antigravity") AppConnect(panel, id);
            string note;
            if (connectNotes.TryGetValue(id, out note) && note.Length > 0) { TextBlock line = Hint(note, 6); line.Foreground = InkDim; panel.Children.Add(line); }
            return panel;
        }
        private Button ConnectButton(string text, bool primary) {
            var button = new Button { Style = Styled(primary ? "PrimaryButton" : "SecondaryButton"), Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, MaxWidth = IsCompact && activeSize == "small" ? Math.Max(110, window.Width / config.UiScale - 50) : Double.PositiveInfinity, Margin = new Thickness(0, 0, 8, 8) };
            System.Windows.Automation.AutomationProperties.SetName(button, text); return button;
        }
        private Button CheckButton(string id) {
            var check = ConnectButton("我已登录，刷新", false);
            check.Click += delegate { connectNotes.Remove(id); var ignored = RefreshOne(id); };
            return check;
        }
        private static WrapPanel ButtonRow() { return new WrapPanel { Margin = new Thickness(0, 0, 0, 0) }; }

        // ── API key providers ─────────────────────────────────────────────
        private void KeyConnect(StackPanel panel, ProviderState state) {
            string id = state.Id;
            string env = demo ? "" : Store.KeyEnvironmentName(id, config);
            if (id == "kimi" || id == "zcode") {
                string current; if (!regionDrafts.TryGetValue(id, out current)) current = id == "kimi" ? config.KimiRegion : config.ZaiRegion;
                string[][] options = id == "kimi" ? new[] { new[] { "china", "国内" }, new[] { "international", "国际" } } : new[] { new[] { "china", "国内" }, new[] { "global", "国际" } };
                var row = Row(); row.Margin = new Thickness(0, 0, 0, 8);
                if (IsCompact && activeSize == "small") { panel.Children.Add(Hint("接口区域", 5)); row.Children.Add(Segmented(options, current, code => regionDrafts[id] = code)); }
                else AddRow(row, Label("接口区域", 11.5, InkDim), Segmented(options, current, code => regionDrafts[id] = code));
                panel.Children.Add(row);
            }
            var line = new Grid();
            if (IsCompact) { line.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); line.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            else { line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); }
            var box = new PasswordBox { IsEnabled = !demo, VerticalAlignment = VerticalAlignment.Center, ToolTip = state.Status == "expired" ? "粘贴新的 API Key" : "粘贴 API Key" };
            System.Windows.Automation.AutomationProperties.SetName(box, ProviderCatalog.Name(id) + " API Key");
            string draft; if (keyDrafts.TryGetValue(id, out draft)) box.Password = draft;
            box.PasswordChanged += delegate { keyDrafts[id] = box.Password; };
            box.LostKeyboardFocus += KeyBoxLostFocus;
            box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; SaveKeyFromCard(id, box.Password); } };
            line.Children.Add(box);
            var save = new Button { Style = Styled("PrimaryButton"), Content = "保存并连接", IsEnabled = !demo, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(save, ProviderCatalog.Name(id) + " 保存并连接");
            save.Click += delegate { SaveKeyFromCard(id, box.Password); };
            if (IsCompact) { Grid.SetRow(save, 1); save.Margin = new Thickness(0, 8, 0, 0); save.HorizontalAlignment = HorizontalAlignment.Left; }
            else Grid.SetColumn(save, 1);
            line.Children.Add(save);
            panel.Children.Add(line);
            panel.Children.Add(Hint(demo ? "演示模式不读取或保存密钥。" : env.Length > 0 ? "正在使用环境变量 " + env + "（优先于这里保存的密钥）。" : "以 Windows DPAPI 加密保存在本机 data 目录，只有当前 Windows 用户能解密。", 6));
        }
        private void SaveKeyFromCard(string id, string key) {
            if (demo) return;
            key = (key ?? "").Trim();
            string region; bool regionChanged = regionDrafts.TryGetValue(id, out region);
            bool haveKey = Store.KeyEnvironmentName(id, config).Length > 0 || Store.HasSavedKey(id);
            if (key.Length == 0 && !(regionChanged && haveKey)) { connectNotes[id] = "请先粘贴 API Key。"; Keyboard.ClearFocus(); Render(); return; }
            try {
                if (regionChanged) { if (id == "kimi") config.KimiRegion = region; else if (id == "zcode") config.ZaiRegion = region; Store.Write("settings.json", config); }
                if (key.Length > 0) Store.SetProviderKey(id, key);
            } catch { connectNotes[id] = "未能保存，请检查 data 目录是否可写。"; Keyboard.ClearFocus(); Render(); return; }
            keyDrafts.Remove(id); regionDrafts.Remove(id); connectNotes.Remove(id); connectionDetails.Remove(id);
            states[id] = new ProviderState { Id = id, Status = "loading", Message = "正在连接…" };
            Keyboard.ClearFocus(); // leave the key box so the page can re-render
            var ignored = RefreshOne(id);
        }

        // ── GitHub Copilot: OAuth device flow ─────────────────────────────
        private void CopilotConnect(StackPanel panel, ProviderState state) {
            CopilotFlow flow = copilotFlow;
            if (flow != null && flow.Pending != null) {
                panel.Children.Add(Hint("在浏览器里输入下面的代码并授权：", 0));
                var code = new TextBox { Text = flow.Pending.UserCode, IsReadOnly = true, FontSize = 22, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas, Segoe UI"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
                System.Windows.Automation.AutomationProperties.SetName(code, "GitHub 设备码");
                panel.Children.Add(code);
                var buttons = ButtonRow();
                var open = ConnectButton("复制代码并打开 GitHub", true);
                open.Click += delegate {
                    try { Clipboard.SetText(flow.Pending.UserCode); } catch { }
                    try { Process.Start(new ProcessStartInfo(flow.Pending.VerificationUri) { UseShellExecute = true }); } catch { }
                };
                var cancel = new Button { Style = Styled("LinkButton"), Content = "取消" };
                cancel.Click += delegate { flow.Cancelled = true; copilotFlow = null; connectNotes["copilot"] = "已取消登录。"; Render(); };
                buttons.Children.Add(open); buttons.Children.Add(cancel); panel.Children.Add(buttons);
                return;
            }
            var row = ButtonRow();
            var login = ConnectButton(state.Status == "expired" ? "重新使用 GitHub 登录" : "使用 GitHub 登录", true);
            login.IsEnabled = !demo && flow == null;
            System.Windows.Automation.AutomationProperties.SetName(login, "使用 GitHub 登录");
            login.Click += delegate { var ignored = StartCopilotFlow(); };
            row.Children.Add(login); panel.Children.Add(row);
            panel.Children.Add(Hint(demo ? "演示模式不登录。" : "OAuth 设备码登录，只申请 read:user 权限，用于读取 Copilot 额度。", 6));
        }
        private async Task StartCopilotFlow() {
            if (demo || copilotFlow != null) return;
            var flow = new CopilotFlow(); copilotFlow = flow;
            connectNotes["copilot"] = "正在向 GitHub 申请设备码…"; connectionDetails.Add("copilot"); Render();
            try {
                using (var service = new ProviderService(config)) {
                    flow.Pending = await service.StartCopilotLogin();
                    if (flow.Cancelled) return;
                    connectNotes["copilot"] = "等待你在 GitHub 上授权，完成后会自动连接。"; Render();
                    DateTime until = DateTime.UtcNow.AddSeconds(flow.Pending.ExpiresIn);
                    while (!flow.Cancelled && DateTime.UtcNow < until) {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, flow.Pending.Interval)));
                        if (flow.Cancelled) return;
                        string token = await service.PollCopilotLogin(flow.Pending);
                        if (flow.Cancelled || quitting) return;
                        if (token == null) continue;
                        Store.SetProviderKey("copilot", token);
                        copilotFlow = null; connectNotes.Remove("copilot"); connectionDetails.Remove("copilot");
                        states["copilot"] = new ProviderState { Id = "copilot", Status = "loading", Message = "正在连接…" };
                        var ignored = RefreshOne("copilot");
                        return;
                    }
                    if (!flow.Cancelled) connectNotes["copilot"] = "设备码已过期，请重新登录。";
                }
            } catch (Exception e) { if (!flow.Cancelled) connectNotes["copilot"] = e is ProviderException ? e.Message : "无法连接 GitHub，请检查网络或代理设置。"; }
            finally { if (copilotFlow == flow) { copilotFlow = null; Render(); } }
        }

        // ── CLI logins: Codex / Claude Code / Grok ────────────────────────
        private void CliConnect(StackPanel panel, string id) {
            string command = id == "codex" ? "codex" : id == "claude" ? "claude" : "grok";
            string product = id == "codex" ? "Codex CLI" : id == "claude" ? "Claude Code" : "Grok Build CLI";
            string path = FindOnPath(command);
            var row = ButtonRow();
            if (path.Length > 0) {
                var login = ConnectButton(id == "claude" ? "在终端打开 Claude Code" : "在终端登录", true);
                login.IsEnabled = !demo;
                System.Windows.Automation.AutomationProperties.SetName(login, ProviderCatalog.Name(id) + " 登录");
                login.Click += delegate { connectNotes[id] = LaunchCli(path, id == "claude" ? "" : "login") ? (id == "claude" ? "已打开终端。在 Claude Code 里输入 /login 完成登录，然后点“我已登录，刷新”。" : "已打开终端。按提示在浏览器完成登录，然后点“我已登录，刷新”。") : "无法打开终端。"; Render(); };
                row.Children.Add(login);
            }
            row.Children.Add(CheckButton(id));
            panel.Children.Add(row);
            string hint = demo ? "演示模式不启动登录。" : path.Length == 0 ? "未在 PATH 中找到 " + command + " 命令。请先安装 " + product + (id == "codex" ? "，或在 Codex 应用中登录" : "") + "，然后刷新。"
                : id == "codex" ? "将在新终端运行 codex login。" : id == "claude" ? "将在新终端运行 claude，然后输入 /login。" : "将在新终端运行 grok login。";
            panel.Children.Add(Hint(hint, 6));
        }
        // cmd /k keeps the window open so the login output stays readable. PATH is re-read
        // from the registry: a CLI installed after this app started is still found.
        private static bool LaunchCli(string path, string arguments) {
            try {
                var info = new ProcessStartInfo("cmd.exe", CliArguments(path, arguments, true)) {
                    UseShellExecute = false, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };
                info.EnvironmentVariables["PATH"] = SearchPath();
                Process.Start(info);
                return true;
            } catch { return false; }
        }
        // /d disables shell autorun; /s gives quoted paths the same rules for exe and cmd launchers.
        public static string CliArguments(string path, string arguments, bool keepOpen) {
            return "/d /s /" + (keepOpen ? "k" : "c") + " \"\"" + path + "\"" + (arguments.Length > 0 ? " " + arguments : "") + "\"";
        }
        private static string SearchPath() {
            var parts = new List<string>();
            foreach (EnvironmentVariableTarget target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }) {
                string value = null; try { value = Environment.GetEnvironmentVariable("PATH", target); } catch { }
                if (String.IsNullOrEmpty(value)) continue;
                foreach (string part in value.Split(';')) { string p = part.Trim(); if (p.Length > 0 && !parts.Contains(p, StringComparer.OrdinalIgnoreCase)) parts.Add(p); }
            }
            return String.Join(";", parts);
        }
        public static string FindOnPath(string command) {
            var folders = SearchPath().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(Environment.ExpandEnvironmentVariables).ToList();
            folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".local\bin"));
            foreach (string folder in folders) {
                foreach (string extension in new[] { ".exe", ".cmd", ".bat" }) {
                    try { string candidate = Path.Combine(folder.Trim('"'), command + extension); if (File.Exists(candidate)) return candidate; } catch { }
                }
            }
            return "";
        }

        // ── Desktop apps: Cursor / Antigravity ────────────────────────────
        private void AppConnect(StackPanel panel, string id) {
            string name = ProviderCatalog.Name(id);
            string path = id == "antigravity" ? LocalAntigravity.FindApplication() : FindCursor();
            var row = ButtonRow();
            if (path.Length > 0) {
                var open = ConnectButton("打开 " + name, true);
                open.IsEnabled = !demo;
                open.Click += delegate {
                    try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); connectNotes[id] = "已打开 " + name + "。登录后" + (id == "antigravity" ? "保持应用运行，" : "") + "点“我已登录，刷新”。"; }
                    catch { connectNotes[id] = "无法启动 " + name + "。"; }
                    Render();
                };
                row.Children.Add(open);
            }
            row.Children.Add(CheckButton(id));
            panel.Children.Add(row);
            panel.Children.Add(Hint(demo ? "演示模式不启动应用。" : path.Length == 0 ? "没有在默认位置找到 " + name + "，请手动打开并登录后刷新。" : "额度读取使用 " + name + " 自己的登录状态，本程序不保存它的密码。", 6));
        }
        private static string FindCursor() {
            // App Paths supports a custom install location without scanning the disk.
            foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine }) {
                using (var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\Cursor.exe")) {
                    string path = key == null ? "" : Convert.ToString(key.GetValue("")).Trim('"');
                    if (File.Exists(path)) return path;
                }
            }
            string launcher = FindOnPath("cursor");
            if (launcher.Length > 0) {
                string parent = Path.GetDirectoryName(launcher);
                foreach (string relative in new[] { "Cursor.exe", @"..\..\..\Cursor.exe" }) {
                    string path = Path.GetFullPath(Path.Combine(parent, relative));
                    if (File.Exists(path)) return path;
                }
            }
            string[] paths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\cursor\Cursor.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Cursor\Cursor.exe")
            };
            return paths.FirstOrDefault(File.Exists) ?? "";
        }

        // ── Custom providers ──────────────────────────────────────────────
        private void CustomConnect(StackPanel panel, string id) {
            var row = ButtonRow();
            CustomProvider definition;
            if (ProviderCatalog.Custom.TryGetValue(id, out definition) && definition.Auth != "none") KeyConnect(panel, states[id]);
            var edit = ConnectButton("编辑接口定义", false);
            edit.IsEnabled = !demo;
            edit.Click += delegate {
                CustomProvider provider; if (!ProviderCatalog.Custom.TryGetValue(id, out provider)) return;
                OpenSettings(); OpenCustomEditor(provider);
            };
            var retry = ConnectButton("重试", false);
            retry.Click += delegate { var ignored = RefreshOne(id); };
            row.Children.Add(edit); row.Children.Add(retry);
            panel.Children.Add(row);
        }
    }
}
