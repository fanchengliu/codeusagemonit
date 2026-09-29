using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CodeUsageMonit {
    // Presets change information density; navigation and account actions retain geometry.
    public sealed partial class MonitorPanel {
        private Border compactRoot;
        private ScrollViewer compactScroll;
        private string activeSize = "full", compactPage = "summary", compactScrollKey = "";

        private System.Windows.Threading.DispatcherTimer copyNoticeTimer;
        private string copyNotice = "";
        private bool IsCompact { get { return activeSize != "full"; } }
        public static readonly string[] DisplaySizes = { "small", "medium", "large", "full" };
        private static string SizeName(string size) { return size == "small" ? "小" : size == "medium" ? "中" : size == "large" ? "大" : "完整"; }
        private void ApplyDisplaySize(string size) {
            activeSize = size;
            scaleRoot.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
            compactRoot.Visibility = IsCompact ? Visibility.Visible : Visibility.Collapsed;
            frame.SetDisplayMode(size); Render();
        }
        private void SetDisplaySize(string size) {
            if (!DisplaySizes.Contains(size)) return;
            config.DisplaySize = size;
            if (activeSize != size) ApplyDisplaySize(size);
            frame.Capture(); SaveConfig(); if (!window.IsVisible) Reveal();
        }
        private List<string> CompactProviders() { return new[] { "overview" }.Concat(EnabledIds()).ToList(); }
        private void CycleCompact(int step) {
            var ids = CompactProviders(); int index = ids.IndexOf(config.CompactProvider);
            SelectCompact(ids[((index < 0 ? 0 : index) + step + ids.Count) % ids.Count]);
        }
        private void SelectCompact(string id) {
            config.CompactProvider = id; compactPage = "summary"; compactScrollKey = ""; SaveConfig(); Render();
        }
        private void OpenCompactPage(string id, string page) {
            config.CompactProvider = id; compactPage = page; compactScrollKey = ""; SaveConfig(); Render();
        }
        private void CompactBack() {
            if (compactPage != "summary") OpenCompactPage(config.CompactProvider, "summary");
            else SelectCompact("overview");
        }
        private ContextMenu SizeMenu() {
            var menu = new ContextMenu();
            menu.Opened += delegate {
                menu.Items.Clear();
                foreach (string size in DisplaySizes) {
                    string captured = size;
                    var item = new MenuItem { Header = SizeName(size) + (size == "full" ? "面板" : "尺寸"), IsChecked = config.DisplaySize == size };
                    item.Click += delegate { SetDisplaySize(captured); }; menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
                AddMenuAction(menu, "返回概览", () => { if (IsCompact) SelectCompact("overview"); else SelectProvider("overview"); });
                AddMenuAction(menu, "刷新全部", () => { var ignored = Refresh(); });
                AddMenuAction(menu, "恢复当前布局默认尺寸", () => frame.ResetPosition());
                var top = new MenuItem { Header = "总在最前", IsChecked = config.AlwaysOnTop };
                top.Click += delegate { config.AlwaysOnTop = !config.AlwaysOnTop; SaveConfig(); UpdatePin(); }; menu.Items.Add(top);
                AddMenuAction(menu, "设置…", OpenSettings);
                AddMenuAction(menu, "收起到托盘", () => window.Hide());
            };
            menu.Items.Add(new MenuItem { Header = "显示尺寸" }); return menu;
        }
        private static void AddMenuAction(ContextMenu menu, string text, Action action) {
            var item = new MenuItem { Header = text }; item.Click += delegate { action(); }; menu.Items.Add(item);
        }
        private Button IconAction(string glyph, string name, Action action) {
            var b = new Button { Style = Styled("IconButton"), Content = glyph, Width = 26, Height = 28, FontSize = 12, ToolTip = name };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += delegate { action(); }; return b;
        }
        private void RenderCompact() {
            if (quitting || !IsCompact) return;
            List<string> ids = CompactProviders(); string id = config.CompactProvider;
            if (!ids.Contains(id)) { id = "overview"; config.CompactProvider = id; compactPage = "summary"; }
            if (id != "overview" && compactPage == "summary") {
                compactScroll = null;
                compactRoot.Child = activeSize == "small" ? SmallCard(states[id], ids) : activeSize == "medium" ? MediumCard(states[id], ids) : LargeCard(states[id], ids);
                return;
            }
            string scrollKey = id + ":" + compactPage + ":" + RangeKey(RangeChoice(id));
            double offset = scrollKey == compactScrollKey && compactScroll != null ? compactScroll.VerticalOffset : 0;
            compactScrollKey = scrollKey;
            var root = new Grid { Margin = new Thickness(10, 6, 10, 7) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var navigation = new StackPanel(); navigation.Children.Add(CompactToolbar(id));
            if (activeSize != "small") navigation.Children.Add(CoinStrip(ids, id, activeSize == "medium" ? 9 : 12, false));
            root.Children.Add(navigation);
            var content = new StackPanel { Margin = new Thickness(2, 8, 2, 5) };
            if (id == "overview") CompactOverview(content, ids.Where(x => x != "overview").ToList());
            else {
                ProviderState state = states[id];
                if (compactPage == "period") content.Children.Add(PeriodBlock(id, false));
                else if (compactPage == "connect") {
                    var heading = Label("连接 " + ProviderCatalog.Name(id), 15, Ink); heading.FontWeight = FontWeights.SemiBold; content.Children.Add(heading);
                    content.Children.Add(Hint(state.Status == "ready" ? "已连接。可以更新密钥或在原应用中切换账号。" : "完成登录后，只刷新这个平台即可。", 5));
                    content.Children.Add(ConnectPanel(state));
                } else CompactProviderContent(content, state, compactPage == "details");
            }
            compactScroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0) };
            Grid.SetRow(compactScroll, 1); root.Children.Add(compactScroll);
            var foot = new Grid { Margin = new Thickness(2, 4, 2, 0), Background = Brushes.Transparent };
            foot.Children.Add(activeSize == "small" && copyNotice.Length == 0 ? CoinStrip(ids, id, 13, true) : (UIElement)Label(copyNotice.Length > 0 ? copyNotice : demo ? "演示数据 · 拖边缘调整大小" : "右键切换布局 · 拖边缘调整大小", 9.5, copyNotice.Length > 0 ? AccentBrush : InkFaint));
            Grid.SetRow(foot, 2); root.Children.Add(foot);
            compactRoot.Child = root; compactScroll.ScrollToVerticalOffset(offset);
        }
        private FrameworkElement CompactToolbar(string id) {
            var head = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 1, 0, 3) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (id != "overview") head.Children.Add(IconAction("", compactPage == "summary" ? "返回概览" : "返回平台", CompactBack));
            var titleContent = new Grid(); titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); titleContent.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Icon(id, 15); icon.Margin = new Thickness(0, 0, 7, 0); titleContent.Children.Add(icon);
            var title = Label(ProviderCatalog.Name(id), 12, Ink); title.FontWeight = FontWeights.SemiBold; Grid.SetColumn(title, 1); titleContent.Children.Add(title);
            Grid.SetColumn(titleContent, 1); head.Children.Add(titleContent);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(id == "overview" ? RefreshAllButton(26) : ProviderRefreshButton(id, 26));
            actions.Children.Add(IconAction("", "设置", OpenSettings));
            Grid.SetColumn(actions, 2); head.Children.Add(actions);
            head.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) { try { window.DragMove(); } catch (InvalidOperationException) { } } };
            return head;
        }
        private void CompactOverview(StackPanel content, List<string> ids) {
            var usage = RangeUsage("overview");
            var head = Row(); AddRow(head, Label("本机用量", 10.5, InkDim), activeSize == "small" ? (UIElement)RangeIcon("overview") : RangeButton("overview")); content.Children.Add(head);
            content.Children.Add(BigValue(PeriodCost(usage), activeSize == "small" ? 22 : 29));
            content.Children.Add(Hint(PeriodTokens(usage) + " Token · " + (RangeChoice("overview").Preset == "custom" ? "所选时段" : RangeChoice("overview").Preset), 3));
            if (activeSize != "small") content.Children.Add(PeriodChart(usage, "overview", activeSize == "medium" ? 26 : 42));
            content.Children.Add(Separator());
            foreach (string id in ids) content.Children.Add(OverviewLine(id));
            int connected = ids.Count(id => states[id].Status == "ready");
            content.Children.Add(Hint(connected + " / " + ids.Count + " 已连接 · 点击平台查看详情", 7));
            content.Children.Add(PeriodNote("overview", usage));
        }
        private FrameworkElement OverviewLine(string id) {
            ProviderState state = states[id]; var card = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            card.ColumnDefinitions.Add(new ColumnDefinition()); card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(activeSize == "small" ? 44 : 54) });
            var inside = new Grid(); inside.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); inside.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Icon(id, 16); icon.Margin = new Thickness(0, 0, 9, 0); inside.Children.Add(icon);
            var lines = new StackPanel();
            var label = Row(); Quota quota = Headline(state);
            string value = quota != null ? quota.Remaining.ToString("0.#") + "%" : state.Balances.Count > 0 ? state.Balances[0].Currency + " " + state.Balances[0].Amount.ToString("N2") : StatusWord(state);
            AddRow(label, Label(ProviderCatalog.Name(id), 12, Ink), Label(value, 11, NeedsConnect(state) ? WarnBrush : InkDim)); lines.Children.Add(label);
            if (quota != null) { var bar = SegmentBar(quota.Remaining, ProviderCatalog.Color(id), 20, 5); bar.Margin = new Thickness(0, 6, 0, 1); lines.Children.Add(bar); }
            Grid.SetColumn(lines, 1); inside.Children.Add(lines);
            var open = new Button { Content = inside, Padding = new Thickness(10, 9, 10, 9), Background = CardFill, HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderBrush = CardLine, BorderThickness = new Thickness(1), ToolTip = "查看 " + ProviderCatalog.Name(id) + " · 额度与用量" };
            System.Windows.Automation.AutomationProperties.SetName(open, "查看 " + ProviderCatalog.Name(id));
            open.Click += delegate { SelectCompact(id); }; card.Children.Add(open);
            Button action = NeedsConnect(state) ? ConnectionButton(id, true) : ProviderRefreshButton(id, 26);
            action.Margin = new Thickness(4, 0, 0, 0); action.HorizontalAlignment = HorizontalAlignment.Center; action.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(action, 1); card.Children.Add(action);
            return card;
        }
        private void CompactProviderContent(StackPanel content, ProviderState state, bool details) {
            string id = state.Id;
            var identity = Label(state.Plan.Length > 0 ? state.Plan : StatusWord(state), 11, InkDim); content.Children.Add(identity);
            if (state.Stale || NeedsConnect(state)) content.Children.Add(Hint(state.Stale ? "上次成功读数 · 请检查连接状态" : state.Message, 6));
            var actions = new WrapPanel { Margin = new Thickness(0, 9, 0, 4) };
            if (!details) actions.Children.Add(ActionButton("用量详情", "查看 " + ProviderCatalog.Name(id) + " 用量详情", () => OpenCompactPage(id, "details"), false));
            if (!ProviderCatalog.LocalOnly(id)) actions.Children.Add(ConnectionButton(id, NeedsConnect(state)));
            actions.Children.Add(ActionButton("复制用量", "复制 " + ProviderCatalog.Name(id) + " 用量", () => CopyProviderSummary(id), false));
            content.Children.Add(actions);
            int max = details ? Int32.MaxValue : activeSize == "small" ? 1 : activeSize == "medium" ? 2 : 3;
            foreach (Quota quota in state.Quotas.Take(max)) content.Children.Add(InteractiveQuota(quota, state, !details));
            if (state.Quotas.Count > max) content.Children.Add(ActionButton("查看全部 " + state.Quotas.Count + " 项额度", "全部额度", () => OpenCompactPage(id, "details"), false));
            foreach (Balance balance in state.Balances) { content.Children.Add(BigValue(balance.Amount.ToString("N2", CultureInfo.InvariantCulture), 27)); content.Children.Add(Label(balance.Currency + " 可用余额", 11, InkDim)); }
            if (state.Quotas.Count == 0 && state.Balances.Count == 0) content.Children.Add(Hint(ProviderCatalog.LocalOnly(id) ? "本平台仅统计本机历史。" : state.Status == "loading" ? "正在读取…" : "暂未读取到账户额度。", 8));
            if (state.ResetCreditsAvailable.HasValue) content.Children.Add(Hint("限额重置额度 · " + state.ResetCreditsAvailable + " 次可用", 9));
            content.Children.Add(Separator());
            content.Children.Add(PeriodBlock(id, details));
            if (details) { string account = VisibleAccount(state); if (account.Length > 0) content.Children.Add(Hint(account, 12)); }
        }
        private UIElement InteractiveQuota(Quota q, ProviderState state, bool clickable) {
            var stack = new StackPanel { Margin = new Thickness(0, 9, 0, 4) };
            var title = new TextBlock { Foreground = Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            title.Inlines.Add(q.Label + " "); title.Inlines.Add(new System.Windows.Documents.Run(q.Remaining.ToString("0.#") + "%") { FontSize = activeSize == "small" ? 25 : 22, FontWeight = FontWeights.SemiBold }); title.Inlines.Add(" 剩余"); stack.Children.Add(title);
            var bar = SegmentBar(q.Remaining, ProviderCatalog.Color(state.Id), 24, 7); bar.Margin = new Thickness(0, 7, 0, 5); stack.Children.Add(bar);
            stack.Children.Add(Label(Countdown(q.ResetUtc), 10.5, InkDim));
            PaceInfo pace = UsageDetails.Pace(q, DateTime.UtcNow);
            if (pace != null) { var note = Hint(pace.Reserve >= 0 ? "余量 " + pace.Reserve.ToString("0") + "% · 按当前速度可持续到重置" : "超前消耗 " + (-pace.Reserve).ToString("0") + "%" + (pace.SecondsUntilEmpty.HasValue ? " · 约 " + Duration(pace.SecondsUntilEmpty.Value) + "后用尽" : ""), 4); note.Foreground = pace.Reserve >= 0 ? GoodBrush : WarnBrush; stack.Children.Add(note); }
            if (!clickable) return stack;
            var button = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(7, 1, 7, 4), Margin = new Thickness(-7, 0, -7, 0), ToolTip = "查看全部额度及每日用量" };
            System.Windows.Automation.AutomationProperties.SetName(button, "查看 " + q.Label + " 明细"); button.Click += delegate { OpenCompactPage(state.Id, "details"); }; return button;
        }
        private Button ActionButton(string text, string name, Action action, bool primary) {
            var button = new Button { Style = Styled(primary ? "PrimaryButton" : "SecondaryButton"), Content = text, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 6) };
            System.Windows.Automation.AutomationProperties.SetName(button, name); button.Click += delegate { action(); }; return button;
        }
        private static bool NeedsConnect(ProviderState s) { return s.Status == "setup" || s.Status == "expired" || s.Status == "error"; }
        private Button ConnectionButton(string id, bool primary) {
            return ActionButton(primary ? "连接" : "账号", "连接 " + ProviderCatalog.Name(id), () => OpenProviderConnection(id), primary);
        }
        private void OpenProviderConnection(string id) {
            if (IsCompact) OpenCompactPage(id, "connect");
            else { connectionDetails.Add(id); SelectProvider(id); }
        }
        private string VisibleAccount(ProviderState state) {
            string account = state.Account ?? ""; int at = account.IndexOf('@');
            return config.HideAccounts && at >= 0 ? account.Substring(0, Math.Min(2, at)) + "•••" + account.Substring(at) : account;
        }
        private string ProviderSummary(string id) {
            ProviderState state = states[id]; var text = new StringBuilder(ProviderCatalog.Name(id) + " · " + state.Plan + "\n");
            foreach (var q in state.Quotas) text.AppendLine(q.Label + "剩余 " + q.Remaining.ToString("0.#") + "% · " + Countdown(q.ResetUtc));
            foreach (var b in state.Balances) text.AppendLine(b.Currency + " " + b.Amount.ToString("N2", CultureInfo.InvariantCulture));
            if (state.Stale || state.Status != "ready") text.AppendLine(StatusWord(state) + (state.Stale ? "（上次读数）" : ""));
            var usage = RangeUsage(id);
            text.AppendLine(PeriodCaption(usage)); text.AppendLine(PeriodCost(usage) + " · " + PeriodTokens(usage) + " Token（API 等价估算，非订阅账单）"); return text.ToString();
        }
        private void CopyProviderSummary(string id) {
            try { Clipboard.SetText(ProviderSummary(id)); copyNotice = "已复制 " + ProviderCatalog.Name(id) + " 用量"; } catch { copyNotice = "剪贴板被占用，请重试"; }
            statusNote = copyNotice; Render();
            if (copyNoticeTimer != null) copyNoticeTimer.Stop();
            copyNoticeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            copyNoticeTimer.Tick += delegate { copyNoticeTimer.Stop(); if (statusNote == copyNotice) statusNote = ""; copyNotice = ""; Render(); }; copyNoticeTimer.Start();
        }
        private Button ProviderActionsButton(string id) {
            var button = IconAction("", ProviderCatalog.Name(id) + " 操作", () => { });
            button.Click += delegate {
                var menu = new ContextMenu();
                AddMenuAction(menu, "查看用量详情", () => { if (IsCompact) OpenCompactPage(id, "details"); else SelectProvider(id); });
                AddMenuAction(menu, "复制本平台用量", () => CopyProviderSummary(id));
                if (!ProviderCatalog.LocalOnly(id)) AddMenuAction(menu, "连接 / 更换账号", () => OpenProviderConnection(id));
                AddMenuAction(menu, "时间范围…", () => OpenRangePicker(id, button));
                AddMenuAction(menu, "返回概览", () => { if (IsCompact) SelectCompact("overview"); else SelectProvider("overview"); });
                AddMenuAction(menu, "设置…", OpenSettings);
                button.ContextMenu = menu; menu.PlacementTarget = button; menu.IsOpen = true;
            }; return button;
        }
        private static Quota Headline(ProviderState state) { return state.Quotas.OrderBy(q => q.Remaining).FirstOrDefault(); }
        private static TextBlock BigValue(string text, double size) { var t = Label(text, size, Ink); t.FontWeight = FontWeights.SemiBold; Tabular(t); t.Margin = new Thickness(0, 5, 0, 0); return t; }
        private static FrameworkElement Metric(string title, string value) { var s = new StackPanel { Margin = new Thickness(0, 0, 7, 8) }; s.Children.Add(Label(title, 10, InkFaint)); var t = Label(value, 14, Ink); t.FontWeight = FontWeights.SemiBold; Tabular(t); s.Children.Add(t); return s; }
    }
}
