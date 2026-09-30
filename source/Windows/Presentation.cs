using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CodeUsageMonit {
    public sealed partial class MonitorPanel {
        private readonly HashSet<string> connectionDetails = new HashSet<string>();
        // Keep in sync with the palette at the top of Panel.xaml.
        private static readonly Brush Ink = Brush("#F2F3F5"), InkDim = Brush("#A7ADB7"), InkFaint = Brush("#7D838D"),
            CardFill = Brush("#0AFFFFFF"), CardLine = Brush("#12FFFFFF"), Hairline = Brush("#14FFFFFF"),
            AccentBrush = Brush("#5CC8E0"), WarnBrush = Brush("#F2B36B"), GoodBrush = Brush("#7AD3A8");
        private static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        private void UpdateScaleLabel() {
            var text = window.FindName("ScaleText") as TextBlock;
            if (text != null) text.Text = (config.UiScale * 100).ToString("0") + "% · Ctrl+滚轮";
        }
        // Render() restores the previous offset; switching pages must land at the top, so
        // the reset is issued after it (scroll requests are applied in order on layout).
        private void SelectProvider(string id) { selected = id; Render(); bodyScroll.ScrollToVerticalOffset(0); }
        private IEnumerable<string> EnabledIds() { return ProviderCatalog.All.Where(config.Enabled.Contains); }
        private void UpdateStatus() {
            var ids = EnabledIds().ToList();
            int enabled = ids.Count, connected = ids.Count(id => states.ContainsKey(id) && states[id].Status == "ready");
            string text; Brush dot;
            if (demo) { text = "演示数据 · 不读取账户"; dot = AccentBrush; }
            else if (refreshing) { text = "正在刷新…"; dot = AccentBrush; }
            else if (statusNote.Length > 0) { text = statusNote; dot = WarnBrush; }
            else {
                text = connected + " / " + enabled + " 已连接 · 每 " + config.RefreshMinutes + " 分钟刷新" + (lastRefresh == DateTime.MinValue ? "" : " · " + lastRefresh.ToString("HH:mm") + " 更新");
                dot = connected == enabled ? GoodBrush : WarnBrush;
            }
            status.Text = text; statusDot.Fill = dot;
        }
        private void Render() {
            if (quitting) return;
            UpdateStatus();
            if (RangePickerOpen) return;
            if (EditingKey()) { renderDeferred = true; return; }
            renderDeferred = false;
            if (IsCompact) { RenderCompact(); return; }
            RenderTabs(); UpdateScaleLabel();
            double offset = bodyScroll.VerticalOffset;
            body.Children.Clear();
            if (selected == "overview") {
                heroCard = Card(HeroContent()); body.Children.Add(heroCard);
                foreach (string id in EnabledIds()) body.Children.Add(ProviderCard(states[id]));
                if (ThirdPartyVisible()) body.Children.Add(ThirdPartyCard());
            } else if (selected == ProviderCatalog.ThirdParty) RenderThirdParty();
            else RenderDetail(states[selected]);
            bodyScroll.ScrollToVerticalOffset(offset);
        }
        // The page appears once a third-party endpoint is in use or has recent usage.
        private bool ThirdPartyVisible() { return config.ShowThirdParty && thirdParty.Endpoints.Count > 0; }

        // ── Provider strip ────────────────────────────────────────────────
        private void RenderTabs() {
            tabs.Children.Clear();
            List<string> ids = new[] { "overview" }.Concat(EnabledIds()).ToList();
            if (ThirdPartyVisible()) ids.Add(ProviderCatalog.ThirdParty);
            if (!ids.Contains(selected)) selected = "overview";
            // More than eight tabs wrap into two rows instead of shrinking to unreadable labels.
            tabs.Rows = ids.Count > 8 ? 2 : 1;
            tabs.Columns = (int)Math.Ceiling(ids.Count / (double)tabs.Rows);
            foreach (string id in ids) {
                string captured = id; bool active = selected == id;
                var glyph = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
                FrameworkElement icon = Icon(id, 16); icon.Opacity = active ? 1 : .78; glyph.Children.Add(icon);
                string dot = TabDot(id);
                if (dot != null) glyph.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Brush(dot), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -5, 0) });
                var name = Label(ProviderCatalog.Name(id), 10.5, active ? Ink : InkDim);
                name.Margin = new Thickness(0, 5, 0, 0); name.HorizontalAlignment = HorizontalAlignment.Center;
                if (active) name.FontWeight = FontWeights.SemiBold;
                var content = new StackPanel(); content.Children.Add(glyph); content.Children.Add(name);
                var button = new Button { Content = content, Style = Styled("TabButton"), Background = Brush(active ? "#17FFFFFF" : "#00FFFFFF"), Margin = new Thickness(2, 0, 2, 0) };
                button.ToolTip = id == "overview" ? "全部平台" : id == ProviderCatalog.ThirdParty ? "经第三方接口的用量（本机日志）" : ProviderCatalog.Name(id) + " · " + StatusWord(states[id]);
                System.Windows.Automation.AutomationProperties.SetName(button, ProviderCatalog.Name(id));
                button.Click += delegate { SelectProvider(captured); };
                tabs.Children.Add(button);
            }
        }
        private string TabDot(string id) {
            if (!states.ContainsKey(id)) return null;
            string s = states[id].Status;
            return s == "error" || s == "expired" ? "#F2B36B" : s == "setup" ? "#7D838D" : null;
        }
        private static string StatusWord(ProviderState state) {
            switch (state.Status) { case "ready": return "已连接"; case "cached": return "显示上次读数"; case "loading": return "读取中"; case "setup": return "尚未连接"; default: return "需要处理"; }
        }

        // ── Overview ──────────────────────────────────────────────────────
        // "all" or one provider id: which platform the overview chart and totals show.
        private string heroFilter = "all";
        private Border heroCard;
        // Only the hero card is rebuilt when the chart filter changes, so the provider
        // cards (and their meters) below stay untouched.
        private UIElement HeroContent() {
            var stack = new StackPanel();
            List<DayUsage> all = history.Days.Where(d => config.Enabled.Contains(d.Agent)).ToList();
            List<string> agents = ProviderCatalog.Ids.Where(id => all.Any(d => d.Agent == id)).ToList();
            if (heroFilter != "all" && !agents.Contains(heroFilter)) heroFilter = "all";
            bool single = heroFilter != "all";
            string rangeId = single ? heroFilter : "overview"; PeriodUsage usage = RangeUsage(rangeId);
            List<DayUsage> days = usage.Days;
            var head = Row();
            var copy = new Button { Style = Styled("IconButton"), Content = "", Width = 26, Height = 26, FontSize = 12, ToolTip = "复制用量概览", Margin = new Thickness(0, -5, -7, -5) };
            copy.Click += delegate { CopySummary(copy); };
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; actions.Children.Add(RangeButton(rangeId)); actions.Children.Add(copy);
            AddRow(head, Label(single ? ProviderCatalog.Name(heroFilter) : "全部平台", 11, InkDim), actions); stack.Children.Add(head);
            var amount = Label(PeriodCost(usage), 26, Ink);
            amount.FontWeight = FontWeights.SemiBold; amount.Margin = new Thickness(0, 1, 0, 0); Tabular(amount); stack.Children.Add(amount);
            string line;
            if (all.Count == 0) line = scanning ? "正在读取本地历史…" : history.Error.Length > 0 ? history.Error : "暂无本地历史记录";
            else if (single) { string model = UsageDetails.MainModel(days); line = PeriodTokens(usage) + " Token" + (model.Length > 0 ? " · " + model : ""); }
            else line = PeriodTokens(usage) + " Token · " + agents.Count + " 个平台有记录";
            var sub = Label(line, 11, InkDim); sub.Margin = new Thickness(0, 2, 0, 0); stack.Children.Add(sub);
            stack.Children.Add(Hint(PeriodCaption(usage), 4));
            stack.Children.Add(PeriodChart(usage, rangeId, 42));
            if (agents.Count > 1) {
                // Filter chips double as the legend: click one to see only that platform.
                var chips = new WrapPanel { Margin = new Thickness(-3, 8, 0, -4) };
                foreach (string key in new[] { "all" }.Concat(agents)) {
                    string captured = key; bool active = heroFilter == key;
                    var content = new StackPanel { Orientation = Orientation.Horizontal };
                    if (key != "all") content.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = Brush(ProviderCatalog.Color(key)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                    var text = Label(key == "all" ? "全部" : ProviderCatalog.Name(key), 10.5, active ? Ink : InkDim); Tabular(text);
                    content.Children.Add(text);
                    var chip = new Button { Content = content, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 4), Background = Brush(active ? "#1CFFFFFF" : "#00FFFFFF"), BorderBrush = Brush(active ? "#30FFFFFF" : "#00FFFFFF"), BorderThickness = new Thickness(1), ToolTip = key == "all" ? "显示全部平台（按平台堆叠）" : "只看 " + ProviderCatalog.Name(key) };
                    System.Windows.Automation.AutomationProperties.SetName(chip, "图表：" + (key == "all" ? "全部" : ProviderCatalog.Name(key)));
                    chip.Click += delegate { heroFilter = captured; if (heroCard != null) heroCard.Child = HeroContent(); };
                    chips.Children.Add(chip);
                }
                stack.Children.Add(chips);
            }
            stack.Children.Add(PeriodNote(rangeId, usage));
            return stack;
        }
        private void CopySummary(Button source) {
            var report = new StringBuilder("codeusagemonit · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            foreach (string id in EnabledIds()) {
                ProviderState state = states[id];
                report.AppendLine(ProviderCatalog.Name(id) + " · " + (state.Status == "ready" ? String.Join(" / ", state.Quotas.Select(q => q.Label + "剩余 " + q.Remaining.ToString("0.#") + "%").Concat(state.Balances.Select(b => b.Currency + " " + b.Amount.ToString("N2", CultureInfo.InvariantCulture)))) : state.Message));
            }
            List<DayUsage> days = history.Days.Where(d => config.Enabled.Contains(d.Agent)).ToList();
            report.AppendLine("近 30 天 API 等价估算：" + Money(days) + " · " + Compact(days.Sum(d => d.Tokens)) + " Token（非订阅账单）");
            // The clipboard can be held by another process; that must not become an error dialog.
            try { Clipboard.SetText(report.ToString()); } catch { source.ToolTip = "剪贴板被占用，请重试"; return; }
            source.Content = ""; source.Foreground = GoodBrush;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            timer.Tick += delegate { timer.Stop(); source.Content = ""; source.ClearValue(Control.ForegroundProperty); };
            timer.Start();
        }
        private UIElement ProviderCard(ProviderState state) {
            var stack = new StackPanel();
            // The whole header row opens the provider page (hover highlight + chevron).
            var header = new Button { Content = Identity(state, false), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(-7, -5, 0, -5), ToolTip = "查看 " + ProviderCatalog.Name(state.Id) + " 详情" };
            System.Windows.Automation.AutomationProperties.SetName(header, ProviderCatalog.Name(state.Id) + " 详情");
            header.Click += delegate { SelectProvider(state.Id); };
            stack.Children.Add(WithRefresh(header, state.Id));
            AccountBody(stack, state, false);
            PeriodUsage usage = RangeUsage(state.Id);
            if (usage.HasData || config.UsageRanges.ContainsKey(state.Id)) {
                stack.Children.Add(new Border { Height = 1, Background = Hairline, Margin = new Thickness(0, 14, 0, 10) });
                var footer = Row();
                var summary = Label("所选时段 " + PeriodCost(usage), 11, InkDim); Tabular(summary);
                var tokens = Label(PeriodTokens(usage) + " Token", 11, InkFaint); Tabular(tokens);
                AddRow(footer, summary, tokens); stack.Children.Add(footer);
            }
            return Card(stack);
        }

        // ── Account section (shared by overview cards and the detail page) ──
        private UIElement Identity(ProviderState state, bool detail) {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            FrameworkElement icon = Icon(state.Id, detail ? 20 : 17); icon.Margin = new Thickness(0, 0, 10, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRowSpan(icon, 2); grid.Children.Add(icon);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var name = Label(ProviderCatalog.Name(state.Id), detail ? 16 : 14, Ink); name.FontWeight = FontWeights.SemiBold; title.Children.Add(name);
            if (!String.IsNullOrEmpty(state.Plan)) title.Children.Add(Pill(state.Plan));
            Grid.SetColumn(title, 1); grid.Children.Add(title);
            string account = state.Account ?? "";
            if (config.HideAccounts && account.Contains("@")) account = account.Substring(0, Math.Min(2, account.IndexOf('@'))) + "•••" + account.Substring(account.IndexOf('@'));
            var owner = Label(account, 11, InkDim); owner.MaxWidth = 170; owner.Margin = new Thickness(8, 0, 0, 0);
            if (account.Length > 0 && detail) owner.ToolTip = config.HideAccounts ? "可在设置中关闭邮箱遮挡" : account;
            Grid.SetColumn(owner, 2); grid.Children.Add(owner);
            if (!detail) {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var chevron = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 10, Foreground = InkFaint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                Grid.SetColumn(chevron, 3); Grid.SetRowSpan(chevron, 2); grid.Children.Add(chevron);
            }
            string freshness; Brush tone = InkFaint;
            if (!config.Enabled.Contains(state.Id)) freshness = "已关闭";
            else if (state.Status == "ready") freshness = UpdatedAgo(state.LastSuccess);
            else if (state.Stale) { freshness = "上次读数 · " + UpdatedAgo(state.LastSuccess); tone = state.Status == "cached" ? InkFaint : WarnBrush; }
            else if (state.Status == "loading") freshness = "正在读取…";
            else if (state.Status == "setup") freshness = "尚未连接";
            else { freshness = "需要处理"; tone = WarnBrush; }
            var fresh = Label(freshness, 10.5, tone); fresh.Margin = new Thickness(0, 2, 0, 0); fresh.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(fresh, 1); Grid.SetColumn(fresh, 1); Grid.SetColumnSpan(fresh, 2); grid.Children.Add(fresh);
            return grid;
        }
        private void AccountBody(StackPanel stack, ProviderState state, bool detail) {
            if (!config.Enabled.Contains(state.Id)) { stack.Children.Add(Notice("此平台已关闭，可在设置中启用。", false)); return; }
            if (ProviderCatalog.LocalOnly(state.Id)) { stack.Children.Add(Notice(ProviderCatalog.Help(state.Id), false)); return; }
            bool first = true;
            if (!String.IsNullOrWhiteSpace(state.GrokBotError)) stack.Children.Add(Notice(state.GrokBotError, false));
            int quotaLimit = detail ? 30 : state.Id == "cursor" ? 4 : 3;
            foreach (var quota in state.Quotas.Take(quotaLimit)) {
                FrameworkElement row = QuotaRow(quota, state.Id, state.Stale);
                if (first) { row.Margin = new Thickness(0, 14, 0, 0); first = false; }
                stack.Children.Add(row);
            }
            if (!detail && state.Quotas.Count > quotaLimit) {
                var more = new Button { Style = Styled("LinkButton"), Content = "查看其余 " + (state.Quotas.Count - quotaLimit) + " 项额度 ›", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 8, 0, 0) };
                more.Click += delegate { SelectProvider(state.Id); }; stack.Children.Add(more);
            }
            if (state.ProductUsage.Count > 0) stack.Children.Add(ProductUsageBlock(state));
            foreach (var balance in state.Balances) {
                var row = Row(); row.Margin = new Thickness(0, 14, 0, 0);
                var value = Label(balance.Currency + " " + balance.Amount.ToString("N2", CultureInfo.InvariantCulture), 20, Ink); value.FontWeight = FontWeights.SemiBold; Tabular(value);
                AddRow(row, Label("可用余额", 12.5, InkDim), value); stack.Children.Add(row);
            }
            if (state.Id == "codex" && (state.Status == "ready" || detail) && (state.ResetCreditsAvailable.HasValue || detail)) stack.Children.Add(ResetCredits(state));
            if (state.Status != "ready") {
                string message = state.Message;
                if (state.Id == "antigravity" && state.Status == "expired") message = "登录需要更新。请打开 Antigravity 并登录，保持应用运行后刷新。";
                if (state.Status != "loading" && state.Status != "cached") stack.Children.Add(Notice(message + (state.Stale ? " 当前显示的是上次成功读数。" : ""), state.Status == "error" || state.Status == "expired"));
                // Not connected / login expired: connect right here. Other errors (network,
                // proxy, rate limit) usually pass on a retry, so connecting is one click away.
                if (state.Status == "error") stack.Children.Add(ReconnectToggle(state.Id));
            }
            if (state.Status == "setup" || state.Status == "expired" || connectionDetails.Contains(state.Id)) {
                stack.Children.Add(ConnectPanel(state));
                if (state.Status == "ready") stack.Children.Add(ReconnectToggle(state.Id));
            }
        }
        private UIElement ReconnectToggle(string id) {
            bool open = connectionDetails.Contains(id);
            var toggle = new Button { Style = Styled("SecondaryButton"), Content = open ? "收起连接选项" : "重新连接", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            toggle.Click += delegate { if (!connectionDetails.Add(id)) connectionDetails.Remove(id); Render(); };
            return toggle;
        }
        // The card / detail header with the provider's own refresh button at the right.
        private UIElement WithRefresh(UIElement header, string id) {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(header);
            Button refresh = ProviderRefreshButton(id, 28); refresh.Margin = new Thickness(4, -4, -8, -4); refresh.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(refresh, 1); grid.Children.Add(refresh);
            var range = RangeIcon(id); range.Margin = new Thickness(5, -4, 0, -4); Grid.SetColumn(range, 2); grid.Children.Add(range);
            var more = ProviderActionsButton(id); more.Margin = new Thickness(0, -4, -8, -4); Grid.SetColumn(more, 3); grid.Children.Add(more);
            return grid;
        }
        // Per-provider refresh: spins while that provider (or a full refresh) is in flight.
        private Button ProviderRefreshButton(string id, double size) {
            string name = ProviderCatalog.Name(id);
            bool busy = refreshing || refreshingIds.Contains(id);
            Button button = SpinButton(size, busy, busy ? "正在刷新 " + name + "…" : "只刷新 " + name + (ProviderCatalog.LocalOnly(id) ? " 本机历史" : " 额度"));
            System.Windows.Automation.AutomationProperties.SetName(button, "刷新 " + name);
            button.Click += delegate(object sender, RoutedEventArgs e) { e.Handled = true; if (!refreshing && !refreshingIds.Contains(id)) { var ignored = RefreshOne(id); } };
            return button;
        }
        private Button RefreshAllButton(double size) {
            Button button = SpinButton(size, refreshing, refreshing ? "正在刷新…" : "刷新全部平台（F5）");
            System.Windows.Automation.AutomationProperties.SetName(button, "刷新全部");
            button.Click += delegate(object sender, RoutedEventArgs e) { e.Handled = true; var ignored = Refresh(); };
            return button;
        }
        // Pages are rebuilt while a refresh runs; the start angle comes from the clock so a
        // rebuilt button keeps spinning in phase instead of jumping back to 0°.
        private Button SpinButton(double size, bool busy, string tip) {
            var glyph = new TextBlock { Text = "", FontFamily = IconFont, FontSize = Math.Round(size * .44), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(.5, .5) };
            var rotate = new RotateTransform(); glyph.RenderTransform = rotate;
            if (busy) {
                double phase = DateTime.Now.TimeOfDay.TotalMilliseconds % 900 / 900 * 360;
                rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(phase, phase + 360, TimeSpan.FromSeconds(.9)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            var button = new Button { Style = Styled("IconButton"), Content = glyph, Width = size, Height = size, ToolTip = tip, IsEnabled = !busy };
            if (busy) button.Foreground = AccentBrush;
            return button;
        }
        // The segmented meter is the user's own design: 24 cells, 2 px gaps, remaining
        // fraction filled in the provider colour, warm colour below 10%. Keep it as is.
        private FrameworkElement QuotaRow(Quota quota, string id, bool stale) {
            var stack = new StackPanel { Margin = new Thickness(0, 16, 0, 0), Opacity = stale ? .62 : 1 };
            var row = Row();
            var heading = new TextBlock { Foreground = Ink, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            heading.Inlines.Add(quota.Label + " ");
            heading.Inlines.Add(new Run(quota.Remaining.ToString("0.#") + "%") { FontSize = 17, FontWeight = FontWeights.SemiBold });
            heading.Inlines.Add(" 剩余");
            heading.ToolTip = quota.Label + " " + quota.Remaining.ToString("0.#") + "% 剩余（已用 " + quota.Used.ToString("0.#") + "%）";
            var reset = Label(Countdown(quota.ResetUtc), 10.5, InkDim); reset.Margin = new Thickness(8, 0, 0, 0);
            reset.ToolTip = "重置时间：" + LocalTime(quota.ResetUtc);
            AddRow(row, heading, reset); stack.Children.Add(row);
            var segments = SegmentBar(quota.Remaining, ProviderCatalog.Color(id), 24, 8); segments.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(segments);
            PaceInfo pace = UsageDetails.Pace(quota, DateTime.UtcNow);
            if (pace != null) {
                bool even = Math.Abs(pace.Reserve) < 1;
                string reserve = even ? "进度均衡" : pace.Reserve >= 0 ? "余量 " + Math.Abs(pace.Reserve).ToString("0") + "%" : "超前消耗 " + Math.Abs(pace.Reserve).ToString("0") + "%";
                string duration = pace.Lasts ? "按当前速度可持续到重置" : pace.SecondsUntilEmpty.HasValue ? "预计 " + Duration(pace.SecondsUntilEmpty.Value) + "后用尽" : "使用节奏估算";
                string buffer = pace.Reserve > 15 && pace.SpeedMultiplier >= 1.5 ? " · ≥1.5 倍余量" : "";
                var line = new TextBlock { FontSize = 10.5, Foreground = InkDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) };
                line.Inlines.Add(new Run(reserve) { Foreground = even ? InkDim : pace.Reserve >= 0 ? GoodBrush : WarnBrush });
                line.Inlines.Add(" · " + duration + buffer);
                line.ToolTip = "按本周期平均使用速度线性估算，不是官方承诺。";
                stack.Children.Add(line);
            }
            return stack;
        }
        // The segmented meter itself: equal cells with 2 px gaps, the remaining fraction in
        // the provider colour (warm below 10%). Full rows use 24 cells; compact sizes use fewer.
        // The fill is laid out with star columns in the first layout pass (no SizeChanged
        // callback), so rebuilding a page never shows empty meters for a frame.
        private static UniformGrid SegmentBar(double remaining, string color, int count, double height) {
            var segments = new UniformGrid { Columns = count, Height = height };
            Brush empty = Brush("#30FFFFFF"), fill = Brush(remaining < 10 ? "#E6A083" : color);
            for (int i = 0; i < count; i++) {
                double fraction = Math.Max(0, Math.Min(1, remaining / 100 * count - i));
                var cell = new Grid { Background = fraction >= 1 ? fill : empty, Margin = new Thickness(0, 0, i == count - 1 ? 0 : 2, 0) };
                if (fraction > 0 && fraction < 1) {
                    cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
                    cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
                    cell.Children.Add(new Border { Background = fill });
                }
                segments.Children.Add(cell);
            }
            return segments;
        }
        private UIElement ResetCredits(ProviderState state) {
            var block = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var row = Row();
            var count = Label(state.ResetCreditsAvailable.HasValue ? state.ResetCreditsAvailable + " 次可用" : "暂未返回", 12.5, Ink); count.FontWeight = FontWeights.SemiBold;
            AddRow(row, Label("限额重置额度", 12.5, Ink), count); block.Children.Add(row);
            string detail = state.ResetCreditExpiries.Count > 0 ? "到期 " + String.Join(" · ", state.ResetCreditExpiries.Take(3).Select(ExpiryCountdown)) : !state.ResetCreditsAvailable.HasValue && state.ResetCreditsError.Length > 0 ? state.ResetCreditsError : "";
            if (detail.Length > 0) {
                var hint = Label(detail, 10.5, InkFaint); hint.Margin = new Thickness(0, 3, 0, 0);
                hint.ToolTip = state.ResetCreditExpiries.Count == 0 ? "只读显示，不会自动使用重置额度" : "有效期：\n" + String.Join("\n", state.ResetCreditExpiries.Select(LocalTime)) + "\n只读显示，不会自动使用。";
                block.Children.Add(hint);
            }
            return block;
        }
        // ── Detail page ───────────────────────────────────────────────────
        private void RenderDetail(ProviderState state) {
            var top = new StackPanel();
            top.Children.Add(WithRefresh(Identity(state, true), state.Id));
            AccountBody(top, state, true);
            body.Children.Add(Card(top));
            if (state.Id == "cursor") body.Children.Add(GrokBotActivityCard());
            body.Children.Add(Card(PeriodBlock(state.Id, false)));
            List<DayUsage> days = history.Days.Where(d => d.Agent == state.Id).ToList();
            if (days.Count > 0) body.Children.Add(Card(UsageBlock(state, days)));
            else if (!ProviderCatalog.Custom.ContainsKey(state.Id)) {
                var empty = new StackPanel();
                var title = Label("本机用量", 13, Ink); title.FontWeight = FontWeights.SemiBold; empty.Children.Add(title);
                empty.Children.Add(Notice(scanning ? "正在扫描本地历史…" : "本机暂未发现该平台的 Token 历史。账户额度来自服务商接口，本机用量来自本地会话日志，两者相互独立。", false));
                body.Children.Add(Card(empty));
            }
        }
        private UIElement ProductUsageBlock(ProviderState state) {
            var breakdown = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Opacity = state.Stale ? .62 : 1 };
            breakdown.Children.Add(Label("本期消耗构成 · 共用当前账期额度", 10.5, InkFaint));
            foreach (var product in state.ProductUsage) {
                var row = Row(); row.Margin = new Thickness(0, 5, 0, 0);
                var amount = Label("已消耗总额度 " + product.UsedPercent.ToString("0.#") + "%", 11, InkDim);
                Tabular(amount); AddRow(row, Label(product.DisplayName, 11, InkDim), amount);
                row.ToolTip = "该消耗已计入当前账期的已用额度，各产品共享上方显示的剩余额度。";
                breakdown.Children.Add(row);
            }
            return breakdown;
        }
        private UIElement GrokBotActivityCard() {
            var stack = new StackPanel(); var heading = Label("Grok Bot · 本机活动", 13, Ink); heading.FontWeight = FontWeights.SemiBold; stack.Children.Add(heading);
            if (grokBotActivity.Updated.Length == 0) { stack.Children.Add(Notice(grokBotActivity.Warning.Length > 0 ? grokBotActivity.Warning : "正在读取本机缓存…", false)); return Card(stack); }
            BotActivityDay today = grokBotActivity.Days.FirstOrDefault(d => d.Day == HistoryService.DayKey(DateTime.Today));
            foreach (var item in new[] {
                new[] { "今日", grokBotActivity.TodaySessions + " 个会话 · " + (today == null ? 0 : today.UserMessages + today.AssistantMessages) + " 条消息" },
                new[] { "近 30 天缓存", grokBotActivity.Sessions + " 个会话 · " + grokBotActivity.Days.Sum(d => d.UserMessages + d.AssistantMessages) + " 条消息" },
                new[] { "Token / 费用", "本机记录未提供" }
            }) { var row = Row(); row.Margin = new Thickness(0, 10, 0, 0); AddRow(row, Label(item[0], 11, InkDim), Label(item[1], 11, Ink)); stack.Children.Add(row); }
            stack.Children.Add(Hint("统计 Grok Bot 当前登录账号的本机缓存，仅含已确认消息；缓存可能截断，不代表完整云端历史。不能由消息数或额度百分比反推 Token / 费用。", 10));
            if (grokBotActivity.Warning.Length > 0) stack.Children.Add(Notice(grokBotActivity.Warning, false));
            return Card(stack);
        }
        private WindowUsage CurrentWindow(string id, List<DayUsage> days, Quota quota, int previous) {
            // The subscription window counts only Codex's official provider, not relays.
            return UsageDetails.Window(days, quota, DateTime.UtcNow, previous, id == "codex" ? codexLogs : null, TimeZoneInfo.Local, id == "codex" ? (Func<string, bool>)CodexLogs.IsOfficial : null);
        }
        private UIElement UsageBlock(ProviderState state, List<DayUsage> days) {
            var stack = new StackPanel();
            var head = Row();
            var title = Label("本机用量", 13, Ink); title.FontWeight = FontWeights.SemiBold;
            var badge = Label("API 等价 · 官方价目 ⓘ", 10.5, InkFaint); badge.ToolTip = PricingHint();
            AddRow(head, title, badge); stack.Children.Add(head);

            string today = Today();
            List<DayUsage> todayRows = days.Where(d => d.Day == today).ToList();
            Quota quota = UsageDetails.MainWindow(state);
            WindowUsage current = quota == null ? null : CurrentWindow(state.Id, days, quota, 0);

            // Period × metric table: columns are periods, rows are $ and tokens.
            var table = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            for (int i = 0; i < 3; i++) table.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string windowHint = WindowHint(current);
            Cell(table, 0, 1, Label("今日", 10.5, InkFaint), null);
            Cell(table, 0, 2, Label(quota == null ? "当前窗口" : WindowName(quota), 10.5, InkFaint), windowHint);
            Cell(table, 0, 3, Label("近 30 天", 10.5, InkFaint), null);
            Cell(table, 1, 0, Label("费用", 11, InkDim), null);
            Cell(table, 1, 1, Figure(TodayMoney(days), 15), "本机今天已记录的 API 等价费用");
            Cell(table, 1, 2, Figure(WindowMoney(current), 15), windowHint);
            Cell(table, 1, 3, Figure(Money(days), 15), "离线价格表下的本地 API 等价估算，不是订阅账单");
            Cell(table, 2, 0, Label("Token", 11, InkDim), null);
            Cell(table, 2, 1, Figure(todayRows.Count == 0 ? "0" : Compact(todayRows.Sum(d => d.Tokens)), 12), "新增输入 + 输出 + 缓存命中 + 缓存写入");
            Cell(table, 2, 2, Figure(WindowTokens(current), 12), windowHint);
            Cell(table, 2, 3, Figure(Compact(days.Sum(d => d.Tokens)), 12), "输入、输出及缓存 Token 的已记录小计");
            stack.Children.Add(table);

            DayUsage basis = todayRows.FirstOrDefault() ?? days.OrderByDescending(d => d.Day, StringComparer.Ordinal).First();
            UIElement composition = Composition(basis, basis.Day == today ? "今日 Token 构成" : ShortDate(basis.Day) + " Token 构成");
            if (composition != null) stack.Children.Add(composition);
            double relayed = thirdParty.AppMonth(state.Id);
            if (ThirdPartyVisible() && relayed > 0) {
                var link = new Button { Style = Styled("LinkButton"), Content = "其中经第三方接口：近 30 天 " + Compact(relayed) + " Token ›", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 8, 0, 0), ToolTip = "上面的今日 / 30 天包含所有本机用量；额度窗口只计官方订阅。" };
                link.Click += delegate { SelectProvider(ProviderCatalog.ThirdParty); };
                stack.Children.Add(link);
            }

            stack.Children.Add(Chart(days, state.Id));

            if (quota != null) {
                stack.Children.Add(new Border { Height = 1, Background = Hairline, Margin = new Thickness(0, 14, 0, 12) });
                var caption = Row();
                AddRow(caption, Label("额度窗口用量", 11, InkDim), Label(current != null && current.Exact ? "按小时精确 Token" : "只计完整自然日 · 下界", 10.5, InkFaint));
                caption.ToolTip = windowHint; stack.Children.Add(caption);
                for (int i = 0; i < 3; i++) {
                    WindowUsage usage = i == 0 ? current : CurrentWindow(state.Id, days, quota, i);
                    if (usage == null || (i > 0 && usage.Tokens <= 0)) continue;
                    var row = Row(); row.Margin = new Thickness(0, 7, 0, 0); row.ToolTip = WindowHint(usage);
                    string range = usage.StartUtc.ToLocalTime().ToString("M/d HH:mm") + " – " + usage.EndUtc.ToLocalTime().ToString("M/d HH:mm");
                    var left = Label((i == 0 ? "当前 · " : "") + range, 11, i == 0 ? Ink : InkDim);
                    string value = usage.Tokens <= 0 && usage.Days.Count == 0 ? "暂无记录" : (usage.HasCost ? WindowMoney(usage) + " · " : "") + WindowTokens(usage);
                    var right = Label(value, 11, i == 0 ? Ink : InkDim); Tabular(right);
                    AddRow(row, left, right); stack.Children.Add(row);
                }
            }
            string mainModel = UsageDetails.MainModel(days);
            var foot = Label((mainModel.Length > 0 ? "最常用模型 " + mainModel + " · " : "") + "数据来自本机日志，不含其他设备", 10.5, InkFaint);
            foot.Margin = new Thickness(0, 14, 0, 0); foot.ToolTip = "按近 30 天记录的 Token 数排序；只统计这台电脑上的会话日志。";
            stack.Children.Add(foot);
            return stack;
        }
        private static UIElement Composition(DayUsage day, string caption) {
            double input = day.InputTokens, output = day.OutputTokens, read = day.CachedTokens, write = day.CacheCreationTokens;
            if (input + output + read + write <= 0) return null;
            var stack = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            double prompt = input + read + write;
            var head = Row();
            var rate = Label(prompt > 0 ? "缓存命中率 " + (read / prompt * 100).ToString("0.0") + "%" : "", 10.5, InkDim);
            rate.ToolTip = "缓存命中 ÷（新增输入 + 缓存命中 + 缓存写入）。与 CC Switch 的口径一致。";
            AddRow(head, Label(caption, 10.5, InkFaint), rate); stack.Children.Add(head);
            var grid = new UniformGrid { Columns = write > 0 ? 4 : 3, Margin = new Thickness(0, 6, 0, 0) };
            grid.Children.Add(MiniStat("新增输入", input, "未命中缓存、按全价计费的输入"));
            grid.Children.Add(MiniStat("输出", output, "含推理 Token"));
            grid.Children.Add(MiniStat("缓存命中", read, "命中提示缓存的输入，按缓存价计费"));
            if (write > 0) grid.Children.Add(MiniStat("缓存写入", write, "写入提示缓存的输入（Claude）"));
            stack.Children.Add(grid);
            return stack;
        }
        private static UIElement MiniStat(string label, double value, string tip) {
            var box = new StackPanel { ToolTip = tip, Margin = new Thickness(0, 0, 6, 0) };
            box.Children.Add(Label(label, 10, InkFaint));
            var number = Label(Compact(value), 12.5, Ink); number.Margin = new Thickness(0, 2, 0, 0); Tabular(number); box.Children.Add(number);
            return box;
        }
        private UIElement Chart(List<DayUsage> days, string provider) {
            var wrapper = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var series = Enumerable.Range(0, 30).Select(i => DateTime.Today.AddDays(i - 29)).Select((date, i) => {
                string key = HistoryService.DayKey(date);
                return new { Date = date, Index = i, Cost = days.Where(x => x.Day == key && x.CostKnown).Sum(x => x.Cost), Tokens = days.Where(x => x.Day == key).Sum(x => x.Tokens) };
            }).ToList();
            double max = Math.Max(.01, series.Max(d => d.Cost));
            var head = Row();
            AddRow(head, Label("每日费用 · 近 30 天", 10.5, InkFaint), Label("峰值 " + Usd(max, max < 10), 10.5, InkFaint));
            wrapper.Children.Add(head);
            var columns = new UniformGrid { Columns = 30, Height = 52, Margin = new Thickness(0, 6, 0, 0) };
            string color = ProviderCatalog.Color(provider);
            foreach (var item in series) {
                var column = new Grid { Margin = new Thickness(1, 0, 1, 0), Background = Brushes.Transparent, ToolTip = item.Date.ToString("M月d日 ddd", CultureInfo.GetCultureInfo("zh-CN")) + "\n" + Usd(item.Cost) + " · " + Compact(item.Tokens) + " Token" };
                column.Children.Add(new Border { Height = item.Cost > 0 ? Math.Max(2, 52 * item.Cost / max) : 1, Background = Brush(item.Cost > 0 ? color : "#FFFFFF"), Opacity = item.Cost <= 0 ? .12 : item.Index == 29 ? 1 : .55, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1.5, 1.5, 0, 0) });
                columns.Children.Add(column);
            }
            wrapper.Children.Add(columns);
            wrapper.Children.Add(Axis());
            return wrapper;
        }
        private UIElement StackedChart(List<DayUsage> days, int dayCount = 30) {
            var wrapper = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            List<string> order = ProviderCatalog.Ids.Where(id => days.Any(d => d.Agent == id)).ToList();
            var series = Enumerable.Range(0, dayCount).Select(i => DateTime.Today.AddDays(i + 1 - dayCount)).Select(date => {
                string key = HistoryService.DayKey(date);
                return new { Date = date, Parts = order.Select(id => new { Id = id, Cost = days.Where(x => x.Day == key && x.Agent == id && x.CostKnown).Sum(x => x.Cost) }).ToList() };
            }).ToList();
            double max = Math.Max(.01, series.Max(d => d.Parts.Sum(p => p.Cost)));
            var columns = new UniformGrid { Columns = dayCount, Height = 36 };
            foreach (var item in series) {
                double total = item.Parts.Sum(p => p.Cost);
                var tip = item.Date.ToString("M月d日 ddd", CultureInfo.GetCultureInfo("zh-CN")) + " · " + Usd(total) + String.Concat(item.Parts.Where(p => p.Cost > 0).Select(p => "\n" + ProviderCatalog.Name(p.Id) + "  " + Usd(p.Cost)));
                var column = new Grid { Margin = new Thickness(1, 0, 1, 0), Background = Brushes.Transparent, ToolTip = tip };
                var stackBar = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                if (total <= 0) stackBar.Children.Add(new Border { Height = 1, Background = Brush("#1FFFFFFF") });
                else foreach (var part in Enumerable.Reverse(item.Parts)) if (part.Cost > 0) stackBar.Children.Add(new Border { Height = Math.Max(1.5, 36 * part.Cost / max), Background = Brush(ProviderCatalog.Color(part.Id)), Opacity = item.Date == DateTime.Today ? 1 : .7 });
                column.Children.Add(stackBar); columns.Children.Add(column);
            }
            wrapper.Children.Add(columns);
            wrapper.Children.Add(Axis(dayCount));
            return wrapper;
        }
        private static UIElement Axis(int dayCount = 30) {
            var axis = Row(); axis.Margin = new Thickness(0, 4, 0, 0);
            AddRow(axis, Label(DateTime.Today.AddDays(1 - dayCount).ToString("M/d"), 9.5, InkFaint), Label("今天", 9.5, InkFaint));
            return axis;
        }

        // ── Third-party endpoints ─────────────────────────────────────────
        // Usage only: relays set their own weekly/monthly limits and do not publish
        // them, so there is deliberately no quota meter here.
        private UIElement ThirdPartyCard() {
            var stack = new StackPanel();
            var header = new Button { Content = ThirdPartyHeader(false), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(-7, -5, -7, -5), ToolTip = "查看第三方接口用量" };
            System.Windows.Automation.AutomationProperties.SetName(header, "第三方 API 详情");
            header.Click += delegate { SelectProvider(ProviderCatalog.ThirdParty); };
            stack.Children.Add(header);
            foreach (EndpointUsage endpoint in thirdParty.Endpoints.Take(4)) {
                var row = Row(); row.Margin = new Thickness(0, 12, 0, 0);
                var left = new StackPanel();
                var title = new TextBlock { FontSize = 12.5, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis };
                title.Inlines.Add(endpoint.Title);
                if (endpoint.Current) title.Inlines.Add(new Run("  使用中") { FontSize = 10.5, Foreground = AccentBrush });
                left.Children.Add(title);
                var sub = Label(AppName(endpoint.App) + (endpoint.Name.Length > 0 && endpoint.Host.Length > 0 ? " · " + endpoint.Host : ""), 10.5, InkFaint); sub.Margin = new Thickness(0, 2, 0, 0); left.Children.Add(sub);
                var right = Label(endpoint.Month > 0 ? "今日 " + Compact(endpoint.Today) + " · 30 天 " + Compact(endpoint.Month) : "暂无用量", 11, InkDim); Tabular(right);
                AddRow(row, left, right); stack.Children.Add(row);
            }
            if (thirdParty.Endpoints.Count > 4) { var more = Label("另有 " + (thirdParty.Endpoints.Count - 4) + " 个接口", 10.5, InkFaint); more.Margin = new Thickness(0, 10, 0, 0); stack.Children.Add(more); }
            stack.Children.Add(new Border { Height = 1, Background = Hairline, Margin = new Thickness(0, 14, 0, 10) });
            stack.Children.Add(Label("单位 Token · 限额由各服务商决定，本机无法得知", 10.5, InkFaint));
            return Card(stack);
        }
        private UIElement ThirdPartyHeader(bool detail) {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement icon = Icon(ProviderCatalog.ThirdParty, detail ? 20 : 17); icon.Margin = new Thickness(0, 0, 10, 0); grid.Children.Add(icon);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var name = Label("第三方 API", detail ? 16 : 14, Ink); name.FontWeight = FontWeights.SemiBold; title.Children.Add(name);
            title.Children.Add(Pill("本机日志"));
            Grid.SetColumn(title, 1); grid.Children.Add(title);
            if (!detail) {
                var chevron = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 10, Foreground = InkFaint, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(chevron, 2); grid.Children.Add(chevron);
            }
            return grid;
        }
        private void RenderThirdParty() {
            var intro = new StackPanel();
            intro.Children.Add(ThirdPartyHeader(true));
            intro.Children.Add(new TextBlock { Text = "统计本机 Claude Code / Codex 经第三方接口（中转站）发出的请求用量。各服务商的周、月限额由其自行设定且不公开，所以这里只显示用了多少，不显示剩余额度。", FontSize = 11, Foreground = InkDim, TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 10, 0, 4) });
            foreach (var current in new[] { new { App = "Claude Code", Mark = thirdParty.ClaudeNow }, new { App = "Codex", Mark = thirdParty.CodexNow } }) {
                var row = Row(); row.Margin = new Thickness(0, 8, 0, 0);
                var value = Label(CurrentLabel(current.Mark), 11.5, current.Mark != null && !current.Mark.Official ? Ink : InkDim);
                value.MaxWidth = 250; value.ToolTip = current.Mark != null && current.Mark.Key.Length > 0 ? "密钥指纹 " + current.Mark.Key + "（只保存指纹，不保存密钥）" : null;
                AddRow(row, Label("当前 " + current.App, 11.5, InkDim), value); intro.Children.Add(row);
            }
            if (thirdParty.ClaudeUnattributed > 0) {
                DateTime since; string when = LogIndex.Parse(thirdParty.ClaudeTrackedSince, out since) ? since.ToLocalTime().ToString("M月d日 HH:mm") : "开始记录";
                intro.Children.Add(Notice("Claude Code 的日志不记录接口地址，本软件从 " + when + " 起记录切换；在此之前带官方 request-id 的 " + Compact(thirdParty.ClaudeUnattributed) + " Token 无法判断是官方还是透传型中转，未计入下面的任何接口。", false));
            }
            body.Children.Add(Card(intro));
            foreach (EndpointUsage endpoint in thirdParty.Endpoints) body.Children.Add(Card(EndpointBlock(endpoint)));
        }
        private UIElement EndpointBlock(EndpointUsage endpoint) {
            var stack = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement icon = Icon(endpoint.App, 16); icon.Margin = new Thickness(0, 0, 9, 0); head.Children.Add(icon);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var name = Label(endpoint.Title, 14, Ink); name.FontWeight = FontWeights.SemiBold; name.MaxWidth = 210; title.Children.Add(name);
            if (endpoint.Current) { var pill = Pill("使用中"); ((TextBlock)pill.Child).Foreground = AccentBrush; pill.Background = Brush("#1A5CC8E0"); title.Children.Add(pill); }
            Grid.SetColumn(title, 1); head.Children.Add(title);
            var last = Label(endpoint.LastUsed.Length == 0 ? "近 30 天未使用" : "最后使用 " + Ago(endpoint.LastUsed), 10.5, InkFaint);
            Grid.SetColumn(last, 2); head.Children.Add(last);
            stack.Children.Add(head);
            string origin = endpoint.Host.Length > 0 ? " · " + endpoint.Host : endpoint.Key == ClaudeLogs.NoRequestId ? " · 记录缺少官方 request-id，无法确定地址" : " · 本软件开始记录之前的第三方会话";
            string where = AppName(endpoint.App) + origin + (endpoint.Key.Length > 0 && endpoint.Host.Length > 0 ? " · 密钥 …" + endpoint.Key : "");
            var sub = Label(where, 10.5, InkFaint); sub.Margin = new Thickness(25, 3, 0, 0); stack.Children.Add(sub);
            if (endpoint.Key == ClaudeLogs.NoRequestId) sub.ToolTip = "Anthropic 官方接口的每次响应都带 request-id，Claude Code 会把它写进日志。这些记录没有 request-id，通常是经中转站转发（中转站往往不透传这个响应头），但无法确定是哪一家。";

            var table = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            for (int i = 0; i < 3; i++) { table.ColumnDefinitions.Add(new ColumnDefinition()); table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            string costTip = "按官方 API 单价估算的参考值（ccusage 价目）。中转站按自己的倍率或套餐扣费，实际花费以服务商后台为准。";
            Cell(table, 0, 1, Label("今日", 10.5, InkFaint), null); Cell(table, 0, 2, Label("近 7 天", 10.5, InkFaint), null); Cell(table, 0, 3, Label("近 30 天", 10.5, InkFaint), null);
            Cell(table, 1, 0, Label("Token", 11, InkDim), null);
            Cell(table, 1, 1, Figure(Compact(endpoint.Today), 15), null); Cell(table, 1, 2, Figure(Compact(endpoint.Week), 15), null); Cell(table, 1, 3, Figure(Compact(endpoint.Month), 15), "近 30 天 " + endpoint.Requests.ToString("N0") + " 次请求");
            Cell(table, 2, 0, Label("官方价参考", 11, InkDim), costTip);
            string approx = endpoint.CostPartial ? "≥" : "≈";
            Cell(table, 2, 1, Figure(endpoint.Today > 0 ? approx + Usd(endpoint.CostToday) : "—", 12), costTip); Cell(table, 2, 2, Figure(endpoint.Week > 0 ? approx + Usd(endpoint.CostWeek) : "—", 12), costTip); Cell(table, 2, 3, Figure(endpoint.Month > 0 ? approx + Usd(endpoint.CostMonth) : "—", 12), costTip);
            stack.Children.Add(table);
            if (endpoint.Month > 0) stack.Children.Add(TokenChart(endpoint.Daily, ProviderCatalog.Color(endpoint.App)));
            var foot = Label((endpoint.MainModel.Length > 0 ? "主要模型 " + endpoint.MainModel + " · " : "") + "近 30 天 " + endpoint.Requests.ToString("N0") + " 次请求 · 限额未知", 10.5, InkFaint);
            foot.Margin = new Thickness(0, 12, 0, 0); stack.Children.Add(foot);
            return stack;
        }
        private static UIElement TokenChart(double[] daily, string color) {
            var wrapper = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            double max = Math.Max(1, daily.Max());
            var head = Row(); AddRow(head, Label("每日 Token · 近 30 天", 10.5, InkFaint), Label("峰值 " + Compact(max), 10.5, InkFaint)); wrapper.Children.Add(head);
            var columns = new UniformGrid { Columns = 30, Height = 40, Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < 30; i++) {
                DateTime date = DateTime.Today.AddDays(i - 29);
                var column = new Grid { Margin = new Thickness(1, 0, 1, 0), Background = Brushes.Transparent, ToolTip = date.ToString("M月d日 ddd", CultureInfo.GetCultureInfo("zh-CN")) + "\n" + Compact(daily[i]) + " Token" };
                column.Children.Add(new Border { Height = daily[i] > 0 ? Math.Max(2, 40 * daily[i] / max) : 1, Background = Brush(daily[i] > 0 ? color : "#FFFFFF"), Opacity = daily[i] <= 0 ? .12 : i == 29 ? 1 : .55, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1.5, 1.5, 0, 0) });
                columns.Children.Add(column);
            }
            wrapper.Children.Add(columns); wrapper.Children.Add(Axis());
            return wrapper;
        }
        private static string AppName(string app) { return app == Endpoints.ClaudeApp ? "Claude Code" : app == Endpoints.CodexApp ? "Codex" : app; }
        private static string CurrentLabel(EndpointMark mark) {
            if (mark == null) return "尚未检测";
            if (mark.Official) return "官方";
            return mark.Name.Length > 0 ? mark.Name + " · " + mark.Host : mark.Host;
        }
        private static string Ago(string iso) {
            DateTimeOffset date; if (!DateTimeOffset.TryParse(iso, out date)) return "时间未知";
            double minutes = (DateTimeOffset.UtcNow - date).TotalMinutes;
            return minutes < 60 ? "1 小时内" : minutes < 24 * 60 ? (int)(minutes / 60) + " 小时前" : date.LocalDateTime.ToString("M月d日");
        }

        // ── Building blocks ───────────────────────────────────────────────
        private Style Styled(string key) { return (Style)window.Resources[key]; }
        private static Border Card(UIElement child) { return new Border { Background = CardFill, BorderBrush = CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(0, 0, 0, 10), Child = child }; }
        private static Border Pill(string text) {
            return new Border { Background = Brush("#14FFFFFF"), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Label(text, 10.5, InkDim) };
        }
        private static UIElement Notice(string text, bool warning) {
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text = warning ? "" : "", FontFamily = IconFont, FontSize = 12, Foreground = warning ? WarnBrush : InkDim, Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top });
            var body = new TextBlock { Text = text, FontSize = 11, Foreground = warning ? Brush("#F5D6AE") : InkDim, TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
            Grid.SetColumn(body, 1); grid.Children.Add(body);
            return new Border { Background = Brush(warning ? "#14F2B36B" : "#0AFFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 12, 0, 0), Child = grid };
        }
        private static TextBlock Figure(string text, double size) { var t = Label(text, size, Ink); t.FontWeight = size >= 14 ? FontWeights.SemiBold : FontWeights.Normal; Tabular(t); return t; }
        private static void Cell(Grid grid, int row, int column, FrameworkElement element, string tip) {
            Grid.SetRow(element, row); Grid.SetColumn(element, column);
            element.Margin = new Thickness(0, row == 0 ? 0 : row == 1 ? 4 : 2, 6, 0); element.HorizontalAlignment = HorizontalAlignment.Left;
            if (tip != null) element.ToolTip = tip;
            grid.Children.Add(element);
        }
        private static void Tabular(TextBlock text) { Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular); }
        private static Brush Brush(string color) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color); brush.Freeze(); return brush; }
        private static TextBlock Label(string text, double size, Brush color) { return new TextBlock { Text = text, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; }
        private static Grid Row() { var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); return row; }
        private static void AddRow(Grid row, UIElement left, UIElement right) { row.Children.Add(left); Grid.SetColumn(right, 1); if (right is FrameworkElement) ((FrameworkElement)right).VerticalAlignment = VerticalAlignment.Center; row.Children.Add(right); }

        // ── Formatting ────────────────────────────────────────────────────
        private static string Today() { return HistoryService.DayKey(DateTime.Today); }
        private static string ShortDate(string day) { DateTime date; return DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date.ToString("M月d日") : day; }
        private static string Usd(double value, bool cents) { return "$" + value.ToString(cents ? "N2" : value < 10 ? "0.##" : "N0", CultureInfo.InvariantCulture); }
        private static string Usd(double value) { return Usd(value, true); }
        private static string Money(IEnumerable<DayUsage> days) { var rows = days.Where(d => d.CostKnown).ToList(); return rows.Count == 0 ? "—" : Usd(rows.Sum(d => d.Cost)); }
        // A scanned history without a row for today means nothing was recorded today.
        private string TodayMoney(IEnumerable<DayUsage> days) { string today = Today(); var rows = days.Where(d => d.Day == today).ToList(); return rows.Count == 0 ? (history.Updated.Length > 0 ? "$0.00" : "—") : Money(rows); }
        private static string WindowName(Quota quota) { return quota.Label.Contains("每周") ? "本周额度" : quota.Label.Contains("5 小时") ? "5 小时窗口" : "当前周期"; }
        private static string WindowMoney(WindowUsage usage) {
            if (usage == null || !usage.HasCost) return "—";
            return (usage.Exact ? "≈" : "≥") + Usd(usage.Cost);
        }
        private static string WindowTokens(WindowUsage usage) {
            if (usage == null) return "—";
            if (usage.Exact) return Compact(usage.Tokens);
            return usage.Days.Count == 0 ? "—" : "≥" + Compact(usage.Tokens);
        }
        private static string WindowHint(WindowUsage usage) {
            if (usage == null) return "服务商尚未返回可计算的额度窗口起止时间。";
            string range = usage.StartUtc.ToLocalTime().ToString("M月d日 HH:mm") + " – " + usage.EndUtc.ToLocalTime().ToString("M月d日 HH:mm");
            if (usage.Exact) return "窗口 " + range + "（由重置时间和周期长度反推）。\nToken 按会话日志逐小时精确累计，只计官方订阅、不含第三方接口；费用按各自然日的 API 等价单价折算，因此标 ≈。";
            return "窗口 " + range + "（由重置时间和周期长度反推）。\n本地历史只有按日汇总，跨越窗口起点的那一天不计入，因此是保守下界（≥）。";
        }
        private static string PricingHint() {
            return "费用 = 本机会话日志里的 Token × 模型官方 API 单价（ccusage 20.0.26 内置价目，离线）。\n" +
                "已计入缓存折扣与长上下文分档，例如 GPT-6 Astra 单次请求输入超过 272K 时，整次请求按输入/缓存 ×2、输出 ×1.5 计价。CC Switch 目前按统一单价计算、且未收录部分新模型，所以它显示的金额更低。\n" +
                "订阅套餐（如 Pro）实际按月费计费，这里只是 API 等价参考。";
        }
        private static string Compact(double n) {
            var c = CultureInfo.InvariantCulture;
            return n >= 1000000000 ? (n / 1000000000).ToString("0.##", c) + "B" : n >= 1000000 ? (n / 1000000).ToString("0.##", c) + "M" : n >= 1000 ? (n / 1000).ToString("0.#", c) + "K" : n.ToString("N0", c);
        }
        private static string LocalTime(string iso) { DateTimeOffset date; return DateTimeOffset.TryParse(iso, out date) ? date.LocalDateTime.ToString("MM-dd HH:mm") : "时间未知"; }
        private static string Countdown(string iso) { DateTimeOffset date; if (!DateTimeOffset.TryParse(iso, out date)) return "重置时间未知"; var span = date - DateTimeOffset.UtcNow; if (span.TotalSeconds <= 0) return "等待新窗口"; return (span.TotalDays >= 1 ? ((int)span.TotalDays) + "天 " + span.Hours + "小时" : span.TotalHours >= 1 ? ((int)span.TotalHours) + "小时 " + span.Minutes + "分" : Math.Max(1, (int)span.TotalMinutes) + "分钟") + "后重置"; }
        private static string UpdatedAgo(string iso) { DateTimeOffset date; if (!DateTimeOffset.TryParse(iso, out date)) return "更新时间未知"; double minutes = (DateTimeOffset.UtcNow - date).TotalMinutes; return minutes < 1 ? "刚刚更新" : minutes < 60 ? (int)minutes + " 分钟前更新" : "更新于 " + LocalTime(iso); }
        private static string Duration(double seconds) { if (seconds <= 0) return "现在"; var t = TimeSpan.FromSeconds(seconds); return t.TotalDays >= 1 ? (int)t.TotalDays + "天 " + t.Hours + "小时" : t.TotalHours >= 1 ? (int)t.TotalHours + "小时 " + t.Minutes + "分" : Math.Max(1, (int)t.TotalMinutes) + "分钟"; }
        private static string ExpiryCountdown(string iso) { DateTimeOffset date; return DateTimeOffset.TryParse(iso, out date) ? Duration(Math.Max(0, (date - DateTimeOffset.UtcNow).TotalSeconds)) : "到期时间未知"; }
    }
}
