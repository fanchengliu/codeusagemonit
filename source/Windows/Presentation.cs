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
                if (Updates.Newer(updateState)) text += " · " + I18n.T("有新版本 v{0}", updateState.Latest);
            }
            status.Text = text; statusDot.Fill = dot;
        }
        private void Render() {
            if (quitting) return;
            UpdateStatus();
            if (IsCompact) { RenderCompact(); return; }
            if (EditingKey()) { renderDeferred = true; return; }
            renderDeferred = false;
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
            List<string> enabled = EnabledIds().ToList();
            UsageRange range = RangeFor("overview");
            RangeData all = RangeUsage(enabled, range, true, null);
            List<string> agents = all.Parts.Where(p => p.Value.Tokens() > 0 || p.Value.D > 0).Select(p => p.Key).OrderBy(id => Array.IndexOf(ProviderCatalog.Ids, id)).ToList();
            if (heroFilter != "all" && !agents.Contains(heroFilter)) heroFilter = "all";
            bool single = heroFilter != "all";
            RangeData data = single ? RangeUsage(new List<string> { heroFilter }, range, false, heroFilter) : all;
            Action rerender = () => { if (heroCard != null) heroCard.Child = HeroContent(); };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Label((single ? ProviderCatalog.Name(heroFilter) : "全部平台") + " · API 等价费用", 11, InkDim));
            var picker = RangeButton("overview", rerender, false); picker.Margin = new Thickness(8, -3, 4, -3); Grid.SetColumn(picker, 1); head.Children.Add(picker);
            var copy = new Button { Style = Styled("IconButton"), Content = "\uE8C8", Width = 26, Height = 26, FontSize = 12, ToolTip = "复制用量概览", Margin = new Thickness(0, -5, -7, -5) };
            copy.Click += delegate { CopySummary(copy); };
            Grid.SetColumn(copy, 2); head.Children.Add(copy); stack.Children.Add(head);
            var figures = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            figures.ColumnDefinitions.Add(new ColumnDefinition()); figures.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var amount = Label(data.Total.D > 0 || data.Total.Tokens() > data.Total.U ? Usd(data.Total.D) : "—", 26, Ink);
            amount.FontWeight = FontWeights.SemiBold; Tabular(amount); figures.Children.Add(amount);
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) };
            var tokenLine = Label(Compact(data.Total.Tokens()) + " Token", 12, Ink); tokenLine.HorizontalAlignment = HorizontalAlignment.Right; Tabular(tokenLine); side.Children.Add(tokenLine);
            if (data.Total.R > 0) { var req = Label(data.Total.R.ToString("N0", CultureInfo.InvariantCulture) + " 次请求", 10.5, InkFaint); req.HorizontalAlignment = HorizontalAlignment.Right; side.Children.Add(req); }
            Grid.SetColumn(side, 1); figures.Children.Add(side); stack.Children.Add(figures);
            string line;
            if (history.Days.Count == 0 && !data.FromLogs) line = scanning ? "正在读取本地记录…" : history.Error.Length > 0 ? history.Error : "暂无本地记录";
            else if (single) { var top = data.Parts.FirstOrDefault(); line = "今日 " + TodayMoney(history.Days.Where(d => d.Agent == heroFilter)) + (top.Key != null ? " · 主要模型 " + top.Key : ""); }
            else line = "今日 " + TodayMoney(history.Days.Where(d => enabled.Contains(d.Agent))) + " · " + agents.Count + " 个平台有记录" + (data.Total.U > 0 ? " · 部分模型无价目" : "");
            var sub = Label(line, 11, InkDim); sub.Margin = new Thickness(0, 2, 0, 0); stack.Children.Add(sub);
            if (data.Bars.Count > 0) stack.Children.Add(UsageChart("hero|" + heroFilter, data.Hourly ? "每小时" : "每日", data.Bars, 46, true, false, rerender));
            if (agents.Count > 1) {
                // Filter chips double as the legend: click one to see only that platform.
                var chips = new WrapPanel { Margin = new Thickness(-3, 10, 0, -4) };
                foreach (string key in new[] { "all" }.Concat(agents)) {
                    string captured = key; bool active = heroFilter == key;
                    var content = new StackPanel { Orientation = Orientation.Horizontal };
                    if (key != "all") content.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = Brush(ProviderCatalog.Color(key)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                    Bucket part = key == "all" ? null : all.Parts.First(p => p.Key == key).Value;
                    var text = Label(key == "all" ? "全部" : ProviderCatalog.Name(key) + " " + Usd(part.D), 10.5, active ? Ink : InkDim); Tabular(text);
                    content.Children.Add(text);
                    var chip = new Button { Content = content, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 4), Background = Brush(active ? "#1CFFFFFF" : "#00FFFFFF"), BorderBrush = Brush(active ? "#30FFFFFF" : "#00FFFFFF"), BorderThickness = new Thickness(1), ToolTip = key == "all" ? "显示全部平台（按平台堆叠）" : "只看 " + ProviderCatalog.Name(key) + "（按模型堆叠）" };
                    System.Windows.Automation.AutomationProperties.SetName(chip, "图表：" + (key == "all" ? "全部" : ProviderCatalog.Name(key)));
                    chip.Click += delegate { heroFilter = captured; rerender(); };
                    chips.Children.Add(chip);
                }
                stack.Children.Add(chips);
            }
            var note = Label("本机会话日志（Cursor 为账户后台）× 官方 API 价目估算，不是订阅账单 ⓘ", 10.5, InkFaint);
            note.Margin = new Thickness(0, 10, 0, 0); note.ToolTip = PricingHint(); stack.Children.Add(note);
            return stack;
        }
        private void CopySummary(Button source) {
            // The clipboard can be held by another process; that must not become an error dialog.
            try { Clipboard.SetText(SummaryText()); } catch { source.ToolTip = "剪贴板被占用，请重试"; return; }
            source.Content = ""; source.Foreground = GoodBrush;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            timer.Tick += delegate { timer.Stop(); source.Content = ""; source.ClearValue(Control.ForegroundProperty); };
            timer.Start();
        }
        private string SummaryText() {
            var report = new StringBuilder("codeusagemonit · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            foreach (string id in EnabledIds()) {
                ProviderState state = states[id];
                report.AppendLine(ProviderCatalog.Name(id) + " · " + (state.Status == "ready" ? String.Join(" / ", state.Quotas.Select(q => q.Label + "剩余 " + q.Remaining.ToString("0.#") + "%").Concat(state.Balances.Select(b => b.Currency + " " + b.Amount.ToString("N2", CultureInfo.InvariantCulture)))) : state.Message));
            }
            List<DayUsage> days = history.Days.Where(d => config.Enabled.Contains(d.Agent)).ToList();
            report.AppendLine("近 30 天 API 等价估算：" + Money(days) + " · " + Compact(days.Sum(d => d.Tokens)) + " Token（非订阅账单）");
            return report.ToString();
        }
        private UIElement ProviderCard(ProviderState state) {
            var stack = new StackPanel();
            // The whole header opens the provider page; its refresh button sits inside, on
            // the name's line, with the account right-aligned beneath it.
            var header = new Button { Content = Identity(state, false), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(7, 5, 5, 5), Margin = new Thickness(-7, -5, -5, -5), ToolTip = "查看 " + ProviderCatalog.Name(state.Id) + " 详情" };
            System.Windows.Automation.AutomationProperties.SetName(header, ProviderCatalog.Name(state.Id) + " 详情");
            header.Click += delegate { SelectProvider(state.Id); };
            stack.Children.Add(header);
            AccountBody(stack, state, false);
            List<DayUsage> days = history.Days.Where(d => d.Agent == state.Id).ToList();
            if (days.Count > 0) {
                stack.Children.Add(new Border { Height = 1, Background = Hairline, Margin = new Thickness(0, 14, 0, 10) });
                var footer = Row();
                var summary = Label("今日 " + TodayMoney(days) + " · 30 天 " + Money(days), 11, InkDim); Tabular(summary);
                double? speed = RecentSpeed(state.Id, days);
                var tokens = Label(Compact(days.Sum(d => d.Tokens)) + " Token" + (speed.HasValue ? " · " + OutputTiming.Text(speed) : ""), 11, InkFaint); Tabular(tokens);
                if (speed.HasValue) tokens.ToolTip = ProviderCatalog.Name(state.Id) + " 近 7 天输出速度 " + OutputTiming.Text(speed) + "\n" + OutputTiming.Definition;
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
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(detail ? 26 : 24) }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            FrameworkElement icon = Icon(state.Id, detail ? 22 : 18); icon.Margin = new Thickness(0, 0, 10, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRowSpan(icon, 2); grid.Children.Add(icon);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var name = Label(ProviderCatalog.Name(state.Id), detail ? 16 : 14, Ink); name.FontWeight = FontWeights.SemiBold; title.Children.Add(name);
            if (!String.IsNullOrEmpty(state.Plan)) title.Children.Add(Pill(state.Plan));
            Grid.SetColumn(title, 1); grid.Children.Add(title);
            if (!ProviderCatalog.LocalOnly(state.Id)) {
                Button refresh = ProviderRefreshButton(state.Id, 26); refresh.Margin = new Thickness(8, -1, -2, -1); refresh.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(refresh, 2); grid.Children.Add(refresh);
            }
            string freshness; Brush tone = InkFaint;
            if (!config.Enabled.Contains(state.Id)) freshness = "已关闭";
            else if (state.Status == "ready") freshness = UpdatedAgo(state.LastSuccess);
            else if (state.Stale) { freshness = "上次读数 · " + UpdatedAgo(state.LastSuccess); tone = state.Status == "cached" ? InkFaint : WarnBrush; }
            else if (state.Status == "loading") freshness = "正在读取…";
            else if (state.Status == "setup") freshness = "尚未连接";
            else { freshness = "需要处理"; tone = WarnBrush; }
            var fresh = Label(freshness, 10.5, tone); fresh.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(fresh, 1); Grid.SetColumn(fresh, 1); grid.Children.Add(fresh);
            string account = state.Account ?? "";
            if (config.HideAccounts && account.Contains("@")) account = account.Substring(0, Math.Min(2, account.IndexOf('@'))) + "•••" + account.Substring(account.IndexOf('@'));
            if (account.Length > 0) {
                var owner = Label(account, 10.5, InkFaint); owner.MaxWidth = 190; owner.Margin = new Thickness(10, 0, 0, 0); owner.HorizontalAlignment = HorizontalAlignment.Right;
                owner.ToolTip = detail ? (config.HideAccounts ? "可在设置中关闭邮箱遮挡" : account) : null;
                Grid.SetRow(owner, 1); Grid.SetColumn(owner, 1); Grid.SetColumnSpan(owner, 2); grid.Children.Add(owner);
            }
            return grid;
        }
        private void AccountBody(StackPanel stack, ProviderState state, bool detail) {
            if (!config.Enabled.Contains(state.Id)) { stack.Children.Add(Notice("此平台已关闭，可在设置中启用。", false)); return; }
            if (ProviderCatalog.LocalOnly(state.Id)) { stack.Children.Add(Notice(ProviderCatalog.Help(state.Id), false)); return; }
            bool first = true;
            // Cursor lists Grok Bot under the three plan meters, including on the overview card.
            int preview = detail ? 30 : state.Id == "cursor" ? state.Quotas.Count : 3;
            foreach (var quota in state.Quotas.Take(preview)) {
                FrameworkElement row = QuotaRow(quota, state.Id, state.Stale);
                if (first) { row.Margin = new Thickness(row.Margin.Left, 9, row.Margin.Right, 0); first = false; }
                stack.Children.Add(row);
            }
            if (!detail && state.Quotas.Count > preview) {
                var more = new Button { Style = Styled("LinkButton"), Content = "查看其余 " + (state.Quotas.Count - preview) + " 项额度 ›", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 8, 0, 0) };
                more.Click += delegate { SelectProvider(state.Id); }; stack.Children.Add(more);
            }
            if (state.ProductUsage != null && state.ProductUsage.Count > 0) stack.Children.Add(ProductUsageBlock(state));
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
                if (state.Status == "setup" || state.Status == "expired" || (state.Status == "error" && connectionDetails.Contains(state.Id))) stack.Children.Add(ConnectPanel(state));
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
        private UIElement ReconnectToggle(string id) {
            bool open = connectionDetails.Contains(id);
            var toggle = new Button { Style = Styled("LinkButton"), Content = open ? "收起连接选项" : "重新连接…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 6, 0, 0) };
            toggle.Click += delegate { if (!connectionDetails.Add(id)) connectionDetails.Remove(id); Render(); };
            return toggle;
        }
        // Per-provider refresh: spins while that provider (or a full refresh) is in flight.
        private Button ProviderRefreshButton(string id, double size) {
            string name = ProviderCatalog.Name(id);
            bool busy = refreshing || refreshingIds.Contains(id);
            Button button = SpinButton(size, busy, busy ? "正在刷新 " + name + "…" : "只刷新 " + name);
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
            var button = new Button { Style = Styled("IconButton"), Content = glyph, Width = size, Height = size, ToolTip = tip };
            if (busy) button.Foreground = AccentBrush;
            return button;
        }
        // The segmented meter is the user's own design: 24 cells, 2 px gaps, remaining
        // fraction filled in the provider colour, warm colour below 10%. Keep it as is.
        // Click a window to expand its exact reset time, usage and window length.
        private FrameworkElement QuotaRow(Quota quota, string id, bool stale) {
            string key = id + "|" + quota.Label; bool open = expandedQuotas.Contains(key);
            var stack = new StackPanel { Opacity = stale ? .62 : 1 };
            var row = Row();
            var heading = new TextBlock { Foreground = Ink, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            heading.Inlines.Add(quota.Label + " ");
            heading.Inlines.Add(new Run(quota.Remaining.ToString("0.#") + "%") { FontSize = 17, FontWeight = FontWeights.SemiBold });
            heading.Inlines.Add(" 剩余");
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            var reset = Label(Countdown(quota.ResetUtc), 10.5, InkDim); reset.Margin = new Thickness(8, 0, 0, 0); right.Children.Add(reset);
            right.Children.Add(new TextBlock { Text = open ? "\uE70E" : "\uE70D", FontFamily = IconFont, FontSize = 8, Foreground = InkFaint, Margin = new Thickness(7, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            AddRow(row, heading, right); stack.Children.Add(row);
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
                stack.Children.Add(line);
            }
            if (open) {
                var detail = new Border { Background = Brush("#0AFFFFFF"), CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 8, 0, 0) };
                var lines = new StackPanel();
                Action<string, string> add = (label, value) => { var r = Row(); r.Margin = new Thickness(0, 2, 0, 1); var v = Label(value, 10.5, Ink); Tabular(v); AddRow(r, Label(label, 10.5, InkFaint), v); lines.Children.Add(r); };
                add("已用 / 剩余", quota.Used.ToString("0.#") + "% / " + quota.Remaining.ToString("0.#") + "%");
                if (quota.WindowSeconds > 0) add("窗口长度", quota.WindowSeconds % 86400 == 0 ? (quota.WindowSeconds / 86400) + " 天" : (quota.WindowSeconds / 3600.0).ToString("0.#") + " 小时");
                DateTimeOffset resetAt; if (DateTimeOffset.TryParse(quota.ResetUtc, out resetAt)) add("重置时间", resetAt.LocalDateTime.ToString("M月d日 ddd HH:mm", Zh));
                if (pace != null) add("线性节奏下应已用", Math.Max(0, Math.Min(100, quota.Used - pace.Reserve)).ToString("0") + "%");
                lines.Children.Add(new TextBlock { Text = "节奏按本周期平均速度线性估算，不是官方承诺。", FontSize = 10, Foreground = InkFaint, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
                detail.Child = lines; stack.Children.Add(detail);
            }
            var button = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 5, 6, 6), Margin = new Thickness(-6, 11, -6, 0), ToolTip = open ? "点击收起" : "点击查看重置时间与用量明细" };
            System.Windows.Automation.AutomationProperties.SetName(button, ProviderCatalog.Name(id) + " " + quota.Label + " 额度");
            button.Click += delegate { if (!expandedQuotas.Add(key)) expandedQuotas.Remove(key); Render(); };
            return button;
        }
        // The segmented meter itself: equal cells with 2 px gaps, the remaining fraction in
        // the provider colour (warm below 10%). Panel rows use 24 cells; widgets use fewer.
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
            top.Children.Add(Identity(state, true));
            AccountBody(top, state, true);
            body.Children.Add(Card(top));
            List<DayUsage> days = history.Days.Where(d => d.Agent == state.Id).ToList();
            if (days.Count > 0 || usageIndexes.ContainsKey(state.Id)) body.Children.Add(Card(UsageBlock(state, days)));
            else if (!ProviderCatalog.Custom.ContainsKey(state.Id)) {
                var empty = new StackPanel();
                var title = Label("本机用量", 13, Ink); title.FontWeight = FontWeights.SemiBold; empty.Children.Add(title);
                empty.Children.Add(Notice(scanning ? "正在扫描本地历史…" : "本机暂未发现该平台的 Token 历史。账户额度来自服务商接口，本机用量来自本地会话日志，两者相互独立。", false));
                body.Children.Add(Card(empty));
            }
        }
        private WindowUsage CurrentWindow(string id, List<DayUsage> days, Quota quota, int previous) {
            // A subscription window counts only the official endpoint: Codex's own provider,
            // Claude responses that carry Anthropic's request id; relays are left out.
            LogIndex logs; usageIndexes.TryGetValue(id, out logs);
            Func<string, bool> official = id == "codex" ? (Func<string, bool>)CodexLogs.IsOfficial : id == "claude" ? (Func<string, bool>)(p => p.Length == 0) : null;
            return UsageDetails.Window(days, quota, DateTime.UtcNow, previous, logs, TimeZoneInfo.Local, official);
        }
        // Local usage of one provider over a chosen period (button: 当天 / 1d / 7d / 14d / 30d
        // or a custom start and end): totals, token composition, an interactive chart and
        // the models used; then the subscription windows.
        private UIElement UsageBlock(ProviderState state, List<DayUsage> days) {
            var stack = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Label(ProviderCatalog.UsageTitle(state.Id), 13, Ink); title.FontWeight = FontWeights.SemiBold; head.Children.Add(title);
            bool account = ProviderCatalog.AccountUsage(state.Id);
            var picker = RangeButton(state.Id, Render, false); picker.Margin = new Thickness(8, -3, 8, -3); Grid.SetColumn(picker, 1); head.Children.Add(picker);
            var badge = Label("API 等价 ⓘ", 10.5, InkFaint); badge.ToolTip = PricingHint(); Grid.SetColumn(badge, 2); head.Children.Add(badge);
            stack.Children.Add(head);
            RangeData data = RangeUsage(new List<string> { state.Id }, RangeFor(state.Id), false, state.Id);
            Bucket total = data.Total;
            bool timed = OutputTiming.Supported(state.Id);
            var figures = new UniformGrid { Columns = timed ? 4 : 3, Margin = new Thickness(0, 12, 0, 0) };
            figures.Children.Add(BigFigure("费用", total.D > 0 || total.Tokens() > total.U ? Usd(total.D) : "—", total.U > 0 ? "部分模型没有价目，未计入费用" : account ? "Cursor 后台给出的每次调用 API 等价价格，不是订阅账单" : "按各模型官方 API 单价估算，不是订阅账单"));
            figures.Children.Add(BigFigure("Token", Compact(total.Tokens()), "新增输入 + 输出 + 缓存命中 + 缓存写入"));
            figures.Children.Add(BigFigure("请求", total.R > 0 ? total.R.ToString("N0", CultureInfo.InvariantCulture) : "—", data.FromLogs ? (account ? "Cursor 后台记录的模型调用次数（含按次计入套餐的请求）" : "本机日志里的模型请求次数") : "这一时间段只有按日汇总，没有请求次数"));
            if (timed) figures.Children.Add(BigFigure("输出速度", OutputTiming.Text(total.Speed()), ProviderCatalog.Name(state.Id) + "：" + (total.Speed().HasValue ? "这一时间段 " + Math.Round(total.TN).ToString("N0", CultureInfo.InvariantCulture) + " 次请求计时，" + Compact(total.TO) + " 输出 Token ÷ " + Duration(total.TS) + "\n" : "这一时间段没有可计时的请求\n") + OutputTiming.Definition));
            stack.Children.Add(figures);
            UIElement composition = Composition(total.I, total.O, total.C, total.W, "Token 构成");
            if (composition != null) stack.Children.Add(composition);
            double relayed = thirdParty.AppMonth(state.Id);
            if (ThirdPartyVisible() && relayed > 0) {
                var link = new Button { Style = Styled("LinkButton"), Content = "其中经第三方接口：近 30 天 " + Compact(relayed) + " Token ›", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 8, 0, 0), ToolTip = "上面的用量包含所有本机用量；额度窗口只计官方订阅。" };
                link.Click += delegate { SelectProvider(ProviderCatalog.ThirdParty); };
                stack.Children.Add(link);
            }
            if (data.Bars.Count > 0) stack.Children.Add(UsageChart("usage|" + state.Id, (data.Hourly ? "每小时" : "每日") + " · " + RangeFor(state.Id).Label(), data.Bars, 58, true, false, Render, true, timed ? ProviderCatalog.Color(state.Id) : null));
            List<KeyValuePair<string, Bucket>> models = data.Parts.Where(p => p.Value.Tokens() > 0 || p.Value.D > 0).ToList();
            if (models.Count > 0) {
                var list = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
                var caption = Row(); AddRow(caption, Label("模型", 10.5, InkFaint), Label("费用 · Token" + (timed ? " · 速度" : ""), 10.5, InkFaint)); list.Children.Add(caption);
                double sum = Math.Max(1e-9, models.Sum(p => p.Value.D) > 0 ? models.Sum(p => p.Value.D) : models.Sum(p => p.Value.Tokens()));
                bool byCost = models.Sum(p => p.Value.D) > 0; List<string> order = models.Select(p => p.Key).ToList();
                foreach (var pair in models.Take(6)) {
                    var line = new Grid { Margin = new Thickness(0, 6, 0, 0), ToolTip = pair.Key + "\n" + Usd(pair.Value.D) + " · " + Compact(pair.Value.Tokens()) + " Token" + (pair.Value.Speed().HasValue ? "\n输出速度 " + OutputTiming.Text(pair.Value.Speed()) + "（" + Math.Round(pair.Value.TN).ToString("N0", CultureInfo.InvariantCulture) + " 次请求计时）" : "") + (pair.Value.U > 0 ? "\n没有这个模型的价目" : "") };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    line.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = Brush(ModelColor(state.Id, pair.Key, order)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                    var name = Label(pair.Key, 11, Ink); Grid.SetColumn(name, 1); line.Children.Add(name);
                    double share = (byCost ? pair.Value.D : pair.Value.Tokens()) / sum;
                    var track = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0), Background = Brush("#14FFFFFF") };
                    track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, share), GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, 1 - share), GridUnitType.Star) });
                    track.Children.Add(new Border { Background = Brush(ModelColor(state.Id, pair.Key, order)), CornerRadius = new CornerRadius(2) });
                    Grid.SetColumn(track, 2); line.Children.Add(track);
                    var value = Label((pair.Value.U > 0 && pair.Value.D <= 0 ? "—" : Usd(pair.Value.D)) + " · " + Compact(pair.Value.Tokens()) + (timed ? " · " + OutputTiming.Text(pair.Value.Speed()) : ""), 10.5, InkDim); Tabular(value); Grid.SetColumn(value, 3); line.Children.Add(value);
                    list.Children.Add(line);
                }
                if (models.Count > 6) { var more = Label("另有 " + (models.Count - 6) + " 个模型", 10.5, InkFaint); more.Margin = new Thickness(15, 6, 0, 0); list.Children.Add(more); }
                stack.Children.Add(list);
            }
            Quota quota = UsageDetails.MainWindow(state);
            WindowUsage current = quota == null ? null : CurrentWindow(state.Id, days, quota, 0);
            if (quota != null) {
                string windowHint = WindowHint(current);
                stack.Children.Add(new Border { Height = 1, Background = Hairline, Margin = new Thickness(0, 14, 0, 12) });
                var caption = Row();
                AddRow(caption, Label("额度窗口用量 · " + WindowName(quota), 11, InkDim), Label(current != null && current.Exact ? "按小时精确" : "只计完整自然日 · 下界", 10.5, InkFaint));
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
            var foot = Label((account ? "数据来自 Cursor 账户后台，含该账户所有设备 · 没有请求耗时，无法算输出速度" : ProviderCatalog.UsageSource(state.Id) != null ? I18n.T("数据来自本机 {0} 日志，不含其他设备", ProviderCatalog.UsageSource(state.Id)) : "数据来自本机日志，不含其他设备") + (data.FromLogs ? " · 按小时统计" : ""), 10.5, InkFaint);
            foot.TextWrapping = TextWrapping.Wrap;
            foot.Margin = new Thickness(0, 14, 0, 0); stack.Children.Add(foot);
            return stack;
        }
        // One agent's output speed over the last 7 days (hourly index, else the daily history).
        private double? RecentSpeed(string id, List<DayUsage> days) {
            if (!OutputTiming.Supported(id)) return null;
            LogIndex index;
            if (usageIndexes.TryGetValue(id, out index) && index != null) return index.Usage(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, null).Speed();
            string from = HistoryService.DayKey(DateTime.Today.AddDays(-6));
            List<DayUsage> recent = days.Where(d => String.CompareOrdinal(d.Day, from) >= 0).ToList();
            return OutputTiming.Speed(recent.Sum(d => d.TimedOutput), recent.Sum(d => d.TimedSeconds));
        }
        private static FrameworkElement BigFigure(string title, string value, string tip) {
            var box = new StackPanel { ToolTip = tip };
            box.Children.Add(Label(title, 10.5, InkFaint));
            var number = Label(value, 17, Ink); number.FontWeight = FontWeights.SemiBold; number.Margin = new Thickness(0, 2, 0, 0); Tabular(number); box.Children.Add(number);
            return box;
        }
        private static UIElement Composition(DayUsage day, string caption) { return Composition(day.InputTokens, day.OutputTokens, day.CachedTokens, day.CacheCreationTokens, caption); }
        private static UIElement Composition(double input, double output, double read, double write, string caption) {
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
        private static string WindowName(Quota quota) { return quota.Label == Parsers.CursorGrokLabel ? "Grok Bot 本周" : quota.Label.Contains("每周") ? "本周额度" : quota.Label.Contains("5 小时") ? "5 小时窗口" : "当前周期"; }
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
            Pricing.EnsureLoaded();
            return "费用 = 本机会话日志里每次请求的 Token × 该模型的官方 API 单价（价目表 pricing.json 整理自公开的 LiteLLM / models.dev 价目，每天与本项目仓库同步一次，断网时用内置价目；当前价目 " + Pricing.Day + (Pricing.Source == "synced" ? "，已同步" : "，随软件内置") + "）。\n" +
                "逐次请求计价：已计入缓存折扣、缓存写入（1 小时缓存按 2 倍输入价）、长上下文分档（例如单次请求输入超过 272K 时整次按长上下文价）和 Codex 优先 / fast 档的倍率。日志里自带费用的记录（Grok、部分 Claude、OpenCode）直接使用记录值；Cursor 没有本机日志，用量和每次调用的价格取自 Cursor 账户后台。\n" +
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
