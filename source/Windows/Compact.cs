using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodeUsageMonit {
    // One window, four display sizes: small / medium / large render here into
    // CompactRoot; "full" is the panel. Compact sizes are fixed-size, draggable from
    // anywhere, and sit on the desktop layer unless "always on top" is on.
    public sealed partial class MonitorPanel {
        private Border compactRoot;
        private ScrollViewer compactScroll;
        private string activeSize = "full";
        private bool IsCompact { get { return activeSize != "full"; } }
        public static readonly string[] DisplaySizes = { "small", "medium", "large", "full" };
        private static string SizeName(string size) { return size == "small" ? "小" : size == "medium" ? "中" : size == "large" ? "大" : "完整"; }
        private static Size CompactDimensions(string size) { return size == "small" ? new Size(172, 172) : size == "large" ? new Size(360, 390) : new Size(360, 176); }

        // Switches what the window shows. Callers decide whether this is the saved size
        // (SetDisplaySize) or a temporary full view (settings opened from a compact size).
        private void ApplyDisplaySize(string size) {
            activeSize = size;
            bool compact = IsCompact;
            scaleRoot.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            compactRoot.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            frame.SetCompact(compact, CompactDimensions(size));
            if (compact) { frame.PlaceCompact(); RenderCompact(); } else Render();
        }
        private void SetDisplaySize(string size) {
            if (!DisplaySizes.Contains(size)) return;
            config.DisplaySize = size; SaveConfig();
            if (settingsView != null) CloseSettings(); // applies config.DisplaySize
            else if (activeSize != size) ApplyDisplaySize(size);
            if (!window.IsVisible) Reveal();
        }
        private List<string> CompactProviders() { return EnabledIds().ToList(); }
        private void CycleCompact(int step) {
            var ids = CompactProviders(); if (activeSize == "large") ids.Insert(0, "overview");
            if (ids.Count == 0) return;
            int index = ids.IndexOf(config.CompactProvider);
            SelectCompact(ids[((index < 0 ? 0 : index) + step + ids.Count) % ids.Count]);
        }
        private void SelectCompact(string id) { if (config.CompactProvider == id) return; config.CompactProvider = id; SaveConfig(); RenderCompact(); }

        // Right-click menu of the compact sizes (and of the panel's title area).
        private ContextMenu SizeMenu() {
            var menu = new ContextMenu();
            menu.Opened += delegate {
                menu.Items.Clear();
                foreach (string size in DisplaySizes) {
                    string captured = size;
                    var item = new MenuItem { Header = SizeName(size) + (size == "full" ? "面板" : "尺寸"), IsChecked = config.DisplaySize == size };
                    item.Click += delegate { SetDisplaySize(captured); };
                    menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
                var refresh = new MenuItem { Header = "刷新全部" }; refresh.Click += delegate { var ignored = Refresh(); }; menu.Items.Add(refresh);
                var top = new MenuItem { Header = "总在最前", IsChecked = config.AlwaysOnTop };
                top.Click += delegate { config.AlwaysOnTop = !config.AlwaysOnTop; SaveConfig(); UpdatePin(); if (IsCompact) frame.SetCompact(true, CompactDimensions(activeSize)); };
                menu.Items.Add(top);
                var settings = new MenuItem { Header = "设置…" }; settings.Click += delegate { OpenSettings(); }; menu.Items.Add(settings);
                var hide = new MenuItem { Header = "收起到托盘" }; hide.Click += delegate { window.Hide(); }; menu.Items.Add(hide);
            };
            menu.Items.Add(new MenuItem { Header = "…" });
            return menu;
        }

        private void RenderCompact() {
            if (quitting || !IsCompact) return;
            compactScroll = null;
            List<string> ids = CompactProviders();
            string id = config.CompactProvider;
            bool overview = id == "overview" && activeSize == "large";
            if (!overview && !ids.Contains(id)) id = ids.FirstOrDefault() ?? "codex";
            FrameworkElement content;
            if (overview) content = CompactOverview(ids);
            else {
                ProviderState state; if (!states.TryGetValue(id, out state)) state = new ProviderState { Id = id };
                List<DayUsage> days = history.Days.Where(d => d.Agent == id).ToList();
                content = activeSize == "small" ? CompactSmall(state, days, ids) : activeSize == "large" ? CompactLarge(state, days, ids) : CompactMedium(state, days, ids);
            }
            compactRoot.Child = content;
        }

        // ── Small: the most constrained window, its meter and countdown ─────
        private FrameworkElement CompactSmall(ProviderState state, List<DayUsage> days, List<string> ids) {
            var root = new Grid { Margin = new Thickness(14, 11, 12, 10) };
            for (int i = 0; i < 3; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var head = CompactHeader(state, 14, 12.5, false);
            AddHeaderButton(head, ProviderRefreshButton(state.Id, 22));
            root.Children.Add(head);
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Quota main = Headline(state);
            if (main != null) {
                body.Children.Add(BigValue(main.Remaining.ToString("0") + "%", 30));
                body.Children.Add(Label(main.Label + "剩余", 10.5, InkDim));
                var bar = SegmentBar(main.Remaining, ProviderCatalog.Color(state.Id), 16, 6); bar.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(bar);
                var reset = Label(Countdown(main.ResetUtc), 10, InkFaint); reset.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(reset);
            } else AlternateBody(body, state, days, 26);
            Grid.SetRow(body, 1); root.Children.Add(body);
            FrameworkElement footer = NeedsConnect(state) ? (FrameworkElement)ConnectLink(state.Id) : Dots(ids, state.Id);
            Grid.SetRow(footer, 2); root.Children.Add(footer);
            return root;
        }
        // ── Medium: headline on the left, next window (or 30-day chart) on the right ─
        private FrameworkElement CompactMedium(ProviderState state, List<DayUsage> days, List<string> ids) {
            var root = new Grid { Margin = new Thickness(14, 10, 12, 10) };
            for (int i = 0; i < 3; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var head = CompactHeader(state, 15, 13, false);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(Switcher(ids, state.Id, 5, 18, false)); right.Children.Add(ProviderRefreshButton(state.Id, 22));
            AddHeaderButton(head, right);
            root.Children.Add(head);
            var body = new Grid { Margin = new Thickness(0, 8, 0, 6) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) }); body.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Quota main = Headline(state);
            List<Quota> others = state.Quotas.Where(q => q != main).ToList();
            if (main != null) {
                left.Children.Add(BigValue(main.Remaining.ToString("0") + "%", 30));
                left.Children.Add(Label(main.Label + "剩余", 10.5, InkDim));
                var reset = Label(Countdown(main.ResetUtc), 10, InkFaint); reset.Margin = new Thickness(0, 3, 0, 0); left.Children.Add(reset);
            } else AlternateBody(left, state, days, 24);
            body.Children.Add(left);
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            if (main != null) {
                column.Children.Add(CompactRow(main, state.Id, 20, false));
                if (others.Count > 0) column.Children.Add(CompactRow(others[0], state.Id, 20, true));
                else if (days.Count > 0) { var chart = MiniChart(days, state.Id, 34); chart.Margin = new Thickness(0, 12, 0, 0); column.Children.Add(chart); }
            } else if (days.Count > 0) column.Children.Add(MiniChart(days, state.Id, 44));
            Grid.SetColumn(column, 1); body.Children.Add(column);
            Grid.SetRow(body, 1); root.Children.Add(body);
            var foot = CompactFooter(state, days); Grid.SetRow(foot, 2); root.Children.Add(foot);
            return root;
        }
        // ── Large: full windows with pace, spend and chart; or the overview ──
        private FrameworkElement CompactLarge(ProviderState state, List<DayUsage> days, List<string> ids) {
            var root = new DockPanel { Margin = new Thickness(16, 12, 14, 12), LastChildFill = true };
            var head = CompactHeader(state, 18, 14.5, true);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(Label(state.Status == "ready" ? UpdatedAgo(state.LastSuccess) : StatusWord(state), 10, InkFaint)); right.Children.Add(ProviderRefreshButton(state.Id, 24));
            AddHeaderButton(head, right);
            DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
            var switcher = Switcher(ids, state.Id, 11, 20, true); switcher.HorizontalAlignment = HorizontalAlignment.Left; switcher.Margin = new Thickness(-4, 8, 0, 2);
            DockPanel.SetDock(switcher, Dock.Top); root.Children.Add(switcher);
            string account = state.Account ?? "";
            if (config.HideAccounts && account.Contains("@")) account = account.Substring(0, Math.Min(2, account.IndexOf('@'))) + "•••" + account.Substring(account.IndexOf('@'));
            var foot = Label(account, 10, InkFaint); DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot);
            var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            if (NeedsConnect(state)) {
                body.Children.Add(Label(state.Stale ? "上次读数 · 登录或连接需要处理" : "登录或连接需要处理", 10, WarnBrush));
                body.Children.Add(ConnectLink(state.Id));
            }
            if (state.Quotas.Count > 0) foreach (Quota quota in state.Quotas.Take(3)) body.Children.Add(FullRow(quota, state.Id));
            else if (state.Balances.Count == 0 && days.Count > 0) {
                var note = Label(ProviderCatalog.LocalOnly(state.Id) ? "没有账户额度 · 只统计本机日志" : StatusWord(state) + " · 下面是本机用量", 11, InkDim); note.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(note);
            } else { var alt = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; AlternateBody(alt, state, days, 28); body.Children.Add(alt); }
            if (state.ResetCreditsAvailable.HasValue) {
                var credits = Row(); credits.Margin = new Thickness(0, 10, 0, 0);
                AddRow(credits, Label("限额重置额度", 11, InkDim), Label(state.ResetCreditsAvailable + " 次可用", 11, Ink)); body.Children.Add(credits);
            }
            if (days.Count > 0) {
                body.Children.Add(new Border { Height = 1, Background = Hairline, Margin = new Thickness(0, 12, 0, 10) });
                var grid = new UniformGrid { Columns = 3 };
                grid.Children.Add(Metric("今日", TodayMoney(days))); grid.Children.Add(Metric("近 30 天", Money(days))); grid.Children.Add(Metric("30 天 Token", Compact(days.Sum(d => d.Tokens))));
                body.Children.Add(grid);
                var chart = MiniChart(days, state.Id, state.Quotas.Count > 1 ? 30 : 46); chart.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(chart);
                DayUsage today = days.FirstOrDefault(d => d.Day == HistoryService.DayKey(DateTime.Today));
                if (today != null && today.InputTokens + today.OutputTokens + today.CachedTokens > 0 && state.Quotas.Count < 3) {
                    var mix = Label("今日构成 · 新增输入 " + Compact(today.InputTokens) + " · 输出 " + Compact(today.OutputTokens) + " · 缓存命中 " + Compact(today.CachedTokens), 10, InkFaint);
                    mix.Margin = new Thickness(0, 8, 0, 0); Tabular(mix); body.Children.Add(mix);
                }
            }
            root.Children.Add(CompactScroller(body));
            return root;
        }
        // Large "概览": 30-day total and chart, then every enabled provider's headline window.
        private FrameworkElement CompactOverview(List<string> ids) {
            var root = new DockPanel { Margin = new Thickness(16, 12, 14, 12), LastChildFill = true };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement icon = Icon("overview", 18); icon.Margin = new Thickness(0, 0, 8, 0); head.Children.Add(icon);
            var title = Label("概览", 14.5, Ink); title.FontWeight = FontWeights.SemiBold; Grid.SetColumn(title, 1); head.Children.Add(title);
            var refresh = RefreshAllButton(24); Grid.SetColumn(refresh, 2); head.Children.Add(refresh);
            DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
            var switcher = Switcher(ids, "overview", 11, 20, true); switcher.HorizontalAlignment = HorizontalAlignment.Left; switcher.Margin = new Thickness(-4, 8, 0, 4);
            DockPanel.SetDock(switcher, Dock.Top); root.Children.Add(switcher);
            int connected = ids.Count(id => states.ContainsKey(id) && states[id].Status == "ready");
            var foot = Label(connected + " / " + ids.Count + " 已连接" + (lastRefresh == DateTime.MinValue ? "" : " · " + lastRefresh.ToString("HH:mm") + " 更新"), 10, InkFaint);
            DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot);
            var body = new StackPanel();
            List<DayUsage> all = history.Days.Where(d => ids.Contains(d.Agent)).ToList();
            var hero = Row();
            var amount = new StackPanel();
            amount.Children.Add(Label("近 30 天 · API 等价", 10.5, InkDim));
            amount.Children.Add(BigValue(all.Any(d => d.CostKnown) ? Money(all) : "—", 24));
            var todayText = Label("今日 " + TodayMoney(all) + " · " + Compact(all.Sum(d => d.Tokens)) + " Token", 10.5, InkFaint); todayText.Margin = new Thickness(0, 3, 0, 0); amount.Children.Add(todayText);
            AddRow(hero, amount, new Border());
            body.Children.Add(hero);
            if (all.Count > 0) { var chart = StackedChart(all) as FrameworkElement; if (chart != null) { chart.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(chart); } }
            body.Children.Add(new Border { Height = 1, Background = Hairline, Margin = new Thickness(0, 8, 0, 4) });
            foreach (string id in ids) body.Children.Add(OverviewLine(id));
            root.Children.Add(CompactScroller(body));
            return root;
        }
        private ScrollViewer CompactScroller(UIElement content) {
            compactScroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0) };
            return compactScroll;
        }
        private FrameworkElement OverviewLine(string id) {
            ProviderState state; if (!states.TryGetValue(id, out state)) state = new ProviderState { Id = id };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            FrameworkElement icon = Icon(id, 13); icon.Margin = new Thickness(0, 0, 7, 0); grid.Children.Add(icon);
            Quota main = Headline(state);
            var name = Label(ProviderCatalog.Name(id), 11.5, Ink); Grid.SetColumn(name, 1); grid.Children.Add(name);
            if (main != null) {
                var bar = SegmentBar(main.Remaining, ProviderCatalog.Color(id), 12, 5); bar.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(bar, 2); grid.Children.Add(bar);
                var pct = Label(main.Remaining.ToString("0") + "%", 11.5, Ink); pct.FontWeight = FontWeights.SemiBold; pct.HorizontalAlignment = HorizontalAlignment.Right; Tabular(pct); Grid.SetColumn(pct, 3); grid.Children.Add(pct);
                grid.ToolTip = main.Label + " 剩余 " + main.Remaining.ToString("0.#") + "% · " + Countdown(main.ResetUtc);
            } else {
                string text = state.Balances.Count > 0 ? state.Balances[0].Currency + " " + state.Balances[0].Amount.ToString("N2", CultureInfo.InvariantCulture) : state.Status == "ready" ? "本机用量" : StatusWord(state);
                var note = Label(text, 10.5, state.Status == "ready" ? InkDim : InkFaint); note.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(note, 2); Grid.SetColumnSpan(note, 2); grid.Children.Add(note);
            }
            var button = new Button { Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(-6, 1, -6, 1) };
            System.Windows.Automation.AutomationProperties.SetName(button, "查看 " + ProviderCatalog.Name(id));
            button.Click += delegate { SelectCompact(id); };
            return button;
        }

        // ── Pieces ────────────────────────────────────────────────────────
        private Grid CompactHeader(ProviderState state, double icon, double size, bool plan) {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            FrameworkElement glyph = Icon(state.Id, icon); glyph.Margin = new Thickness(0, 0, 7, 0); glyph.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(glyph);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var name = Label(ProviderCatalog.Name(state.Id), size, Ink); name.FontWeight = FontWeights.SemiBold; title.Children.Add(name);
            if (plan && state.Plan.Length > 0 && state.Plan != ProviderCatalog.Name(state.Id)) { Border pill = Pill(state.Plan); pill.Margin = new Thickness(6, 1, 0, 0); ((TextBlock)pill.Child).FontSize = 10; title.Children.Add(pill); }
            Grid.SetColumn(title, 1); grid.Children.Add(title);
            return grid;
        }
        private static void AddHeaderButton(Grid head, FrameworkElement element) {
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetColumn(element, head.ColumnDefinitions.Count - 1); head.Children.Add(element);
        }
        private static Quota Headline(ProviderState state) { return state.Quotas.OrderBy(q => q.Remaining).FirstOrDefault(); }
        private static TextBlock BigValue(string text, double size) { var value = Label(text, size, Ink); value.FontWeight = FontWeights.SemiBold; Tabular(value); value.Margin = new Thickness(0, 0, 0, -2); return value; }
        private void AlternateBody(StackPanel body, ProviderState state, List<DayUsage> days, double size) {
            Balance balance = state.Balances.FirstOrDefault();
            if (balance != null) { body.Children.Add(BigValue(balance.Amount.ToString("N2", CultureInfo.InvariantCulture), size)); body.Children.Add(Label(balance.Currency + " 可用余额", 10.5, InkDim)); }
            else if (days.Count > 0) { body.Children.Add(BigValue(Money(days), size)); body.Children.Add(Label("近 30 天 · 今日 " + TodayMoney(days), 10.5, InkDim)); }
            else {
                body.Children.Add(BigValue("—", size));
                body.Children.Add(new TextBlock { Text = state.Status == "loading" ? "正在读取…" : state.Status == "ready" ? "暂无额度数据" : StatusWord(state), FontSize = 10.5, Foreground = InkDim, TextWrapping = TextWrapping.Wrap });
            }
        }
        private static bool NeedsConnect(ProviderState state) { return state.Status == "setup" || state.Status == "expired" || state.Status == "error"; }
        // Connecting needs room (key box, device code): it opens the full panel on that provider.
        private Button ConnectLink(string id) {
            var link = new Button { Style = Styled("LinkButton"), Content = "去连接 ›", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 2, 0, 0), ToolTip = "切换到完整面板，在 " + ProviderCatalog.Name(id) + " 页面登录或填写密钥" };
            System.Windows.Automation.AutomationProperties.SetName(link, "连接 " + ProviderCatalog.Name(id));
            link.Click += delegate(object sender, RoutedEventArgs e) { e.Handled = true; SetDisplaySize("full"); SelectProvider(id); };
            return link;
        }
        private static FrameworkElement CompactRow(Quota quota, string id, int cells, bool gap) {
            var stack = new StackPanel { Margin = new Thickness(0, gap ? 10 : 0, 0, 0) };
            var row = Row();
            var label = new TextBlock { FontSize = 11, Foreground = InkDim, TextTrimming = TextTrimming.CharacterEllipsis };
            label.Inlines.Add(quota.Label + " "); label.Inlines.Add(new System.Windows.Documents.Run(quota.Remaining.ToString("0") + "%") { Foreground = Ink, FontWeight = FontWeights.SemiBold });
            string reset = Countdown(quota.ResetUtc); if (reset.EndsWith("后重置")) reset = reset.Substring(0, reset.Length - 3);
            AddRow(row, label, Label(reset, 10, InkFaint)); stack.Children.Add(row);
            var bar = SegmentBar(quota.Remaining, ProviderCatalog.Color(id), cells, 6); bar.Margin = new Thickness(0, 5, 0, 0); stack.Children.Add(bar);
            return stack;
        }
        private static FrameworkElement FullRow(Quota quota, string id) {
            var stack = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var row = Row();
            var heading = new TextBlock { FontSize = 12.5, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis };
            heading.Inlines.Add(quota.Label + " "); heading.Inlines.Add(new System.Windows.Documents.Run(quota.Remaining.ToString("0.#") + "%") { FontSize = 16, FontWeight = FontWeights.SemiBold }); heading.Inlines.Add(" 剩余");
            AddRow(row, heading, Label(Countdown(quota.ResetUtc), 10, InkDim)); stack.Children.Add(row);
            var bar = SegmentBar(quota.Remaining, ProviderCatalog.Color(id), 24, 7); bar.Margin = new Thickness(0, 6, 0, 0); stack.Children.Add(bar);
            PaceInfo pace = UsageDetails.Pace(quota, DateTime.UtcNow);
            if (pace != null) {
                bool even = Math.Abs(pace.Reserve) < 1;
                var line = new TextBlock { FontSize = 10, Foreground = InkFaint, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                line.Inlines.Add(new System.Windows.Documents.Run(even ? "进度均衡" : pace.Reserve >= 0 ? "余量 " + pace.Reserve.ToString("0") + "%" : "超前消耗 " + (-pace.Reserve).ToString("0") + "%") { Foreground = even ? InkDim : pace.Reserve >= 0 ? GoodBrush : WarnBrush });
                line.Inlines.Add(pace.Lasts ? " · 可持续到重置" : pace.SecondsUntilEmpty.HasValue ? " · 约 " + Duration(pace.SecondsUntilEmpty.Value) + "后用尽" : "");
                stack.Children.Add(line);
            }
            return stack;
        }
        private static FrameworkElement Metric(string title, string value) {
            var stack = new StackPanel(); stack.Children.Add(Label(title, 10, InkFaint));
            var number = Label(value, 14, Ink); number.FontWeight = FontWeights.SemiBold; Tabular(number); number.Margin = new Thickness(0, 2, 0, 0); stack.Children.Add(number);
            return stack;
        }
        private static FrameworkElement MiniChart(List<DayUsage> days, string id, double height) {
            var series = Enumerable.Range(0, 30).Select(i => HistoryService.DayKey(DateTime.Today.AddDays(i - 29))).Select(key => days.Where(d => d.Day == key && d.CostKnown).Sum(d => d.Cost)).ToList();
            double max = Math.Max(.01, series.Max());
            var columns = new UniformGrid { Columns = 30, Height = height };
            for (int i = 0; i < 30; i++) {
                var column = new Grid { Margin = new Thickness(.75, 0, .75, 0) };
                column.Children.Add(new Border { Height = series[i] > 0 ? Math.Max(2, height * series[i] / max) : 1, Background = Brush(series[i] > 0 ? ProviderCatalog.Color(id) : "#FFFFFF"), Opacity = series[i] <= 0 ? .12 : i == 29 ? 1 : .55, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1, 1, 0, 0) });
                columns.Children.Add(column);
            }
            return columns;
        }
        private FrameworkElement CompactFooter(ProviderState state, List<DayUsage> days) {
            var row = Row();
            string spend = days.Count > 0 ? "今日 " + TodayMoney(days) + " · 30 天 " + Money(days) : state.Balances.Count > 0 ? "余额 " + state.Balances[0].Currency + " " + state.Balances[0].Amount.ToString("N2", CultureInfo.InvariantCulture) : "";
            var left = Label(spend, 10.5, InkDim); Tabular(left);
            AddRow(row, NeedsConnect(state) ? (UIElement)ConnectLink(state.Id) : left, Label(state.Status == "ready" ? UpdatedAgo(state.LastSuccess) : StatusWord(state), 10, InkFaint));
            return row;
        }
        private FrameworkElement Switcher(List<string> ids, string current, int max, double size, bool overview) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            List<string> shown = (overview ? new[] { "overview" }.Concat(ids) : ids).Take(max).ToList();
            if (!shown.Contains(current) && (ids.Contains(current) || current == "overview") && shown.Count > 0) shown[shown.Count - 1] = current;
            foreach (string id in shown) {
                string captured = id; bool active = id == current;
                FrameworkElement glyph = Icon(id, size - 8); glyph.Opacity = active ? 1 : .55;
                var button = new Button { Content = glyph, Width = size + 4, Height = size + 4, Padding = new Thickness(0), Margin = new Thickness(1, 0, 1, 0), Background = Brush(active ? "#1FFFFFFF" : "#00FFFFFF"), ToolTip = id == "overview" ? "概览" : ProviderCatalog.Name(id) };
                System.Windows.Automation.AutomationProperties.SetName(button, "切换到 " + (id == "overview" ? "概览" : ProviderCatalog.Name(id)));
                button.Click += delegate { SelectCompact(captured); };
                row.Children.Add(button);
            }
            if ((overview ? ids.Count + 1 : ids.Count) > max) {
                var more = new Button { Style = Styled("IconButton"), Content = "", Width = 22, Height = 22, FontSize = 10, ToolTip = "更多平台（滚动鼠标滚轮切换）" };
                more.Click += delegate { CycleCompact(1); }; row.Children.Add(more);
            }
            return row;
        }
        private FrameworkElement Dots(List<string> ids, string current) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (string id in ids.Take(12)) {
                string captured = id; bool active = id == current;
                var dot = new Ellipse { Width = active ? 6 : 5, Height = active ? 6 : 5, Fill = Brush(active ? ProviderCatalog.Color(id) : "#40FFFFFF") };
                var hit = new Button { Content = dot, Width = 12, Height = 14, Padding = new Thickness(0), ToolTip = ProviderCatalog.Name(id) };
                System.Windows.Automation.AutomationProperties.SetName(hit, "切换到 " + ProviderCatalog.Name(id));
                hit.Click += delegate { SelectCompact(captured); };
                row.Children.Add(hit);
            }
            return row;
        }
    }
}
