using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CodeUsageMonit {
    public sealed partial class MonitorPanel {
        private Button RangeIcon(string id) {
            var button = IconAction("", ProviderCatalog.Name(id) + " 时间范围", () => { }); button.Width = 22; button.Height = 24;
            var range = UsagePeriod.Resolve(RangeChoice(id), DateTime.UtcNow, TimeZoneInfo.Local);
            button.ToolTip = "选择日期与时间范围\n" + range.StartUtc.ToLocalTime().ToString("MM/dd HH:mm") + " — " + range.EndUtc.ToLocalTime().ToString("MM/dd HH:mm"); button.Click += delegate { OpenRangePicker(id, button); }; return button;
        }
        private FrameworkElement CoinStrip(List<string> ids, string current, int max, bool dots) {
            var strip = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var shown = ids.Take(max).ToList(); if (!shown.Contains(current) && shown.Count > 0) shown[shown.Count - 1] = current;
            foreach (string id in shown) {
                string captured = id; bool active = current == id;
                UIElement glyph = dots ? (UIElement)new System.Windows.Shapes.Ellipse { Width = active ? 6 : 4, Height = active ? 6 : 4, Fill = Brush(active ? ProviderCatalog.Color(id) : "#60FFFFFF") } : Icon(id, 13);
                var button = new Button { Content = glyph, Width = dots ? 10 : 23, Height = dots ? 12 : 24, Padding = new Thickness(0), Margin = new Thickness(dots ? 0 : 1, 0, 0, 0), Background = !dots && active ? CardFill : Brushes.Transparent, ToolTip = ProviderCatalog.Name(id) };
                System.Windows.Automation.AutomationProperties.SetName(button, "切换到 " + ProviderCatalog.Name(id)); button.Click += delegate { SelectCompact(captured); }; strip.Children.Add(button);
            }
            if (ids.Count > max) {
                var more = new Button { Content = "…", Width = dots ? 16 : 22, Height = dots ? 14 : 24, Padding = new Thickness(0), ToolTip = "更多平台" };
                System.Windows.Automation.AutomationProperties.SetName(more, "更多平台");
                more.Click += delegate { var menu = new ContextMenu(); foreach (string id in ids) { string captured = id; AddMenuAction(menu, ProviderCatalog.Name(id), () => SelectCompact(captured)); } more.ContextMenu = menu; menu.PlacementTarget = more; menu.IsOpen = true; }; strip.Children.Add(more);
            }
            return strip;
        }
        private Grid CardHeader(ProviderState state, bool plan, bool dense) {
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var identity = new Grid(); identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); identity.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Icon(state.Id, dense ? 14 : 17); icon.Margin = new Thickness(0, 0, 6, 0); identity.Children.Add(icon);
            var name = Label(ProviderCatalog.Name(state.Id), dense ? 12 : 14, Ink); name.FontWeight = FontWeights.SemiBold; Grid.SetColumn(name, 1); identity.Children.Add(name);
            if (plan && state.Plan.Length > 0) { identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); var pill = Pill(state.Plan); pill.MaxWidth = 95; pill.ToolTip = state.Plan; ((TextBlock)pill.Child).FontSize = 9.5; Grid.SetColumn(pill, 2); identity.Children.Add(pill); }
            var title = new Button { Content = identity, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0, 3, 3, 3), ToolTip = "查看用量详情" };
            System.Windows.Automation.AutomationProperties.SetName(title, "查看 " + ProviderCatalog.Name(state.Id) + " 用量详情"); title.Click += delegate { OpenCompactPage(state.Id, "details"); }; header.Children.Add(title);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(ProviderRefreshButton(state.Id, 22)); actions.Children.Add(RangeIcon(state.Id));
            var menu = ProviderActionsButton(state.Id); menu.Width = 22; menu.Height = 24; actions.Children.Add(menu);
            Grid.SetColumn(actions, 1); header.Children.Add(actions); return header;
        }
        private FrameworkElement SmallCard(ProviderState state, List<string> ids) {
            var root = new Grid { Margin = new Thickness(12, 8, 10, 8) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(CardHeader(state, false, true)); var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Quota main = Headline(state);
            if (main != null) {
                var value = new StackPanel(); value.Children.Add(BigValue(main.Remaining.ToString("0") + "%", 32)); value.Children.Add(Label(main.Label + "剩余", 10.5, InkDim));
                var bar = SegmentBar(main.Remaining, ProviderCatalog.Color(state.Id), 16, 6); bar.Margin = new Thickness(0, 8, 0, 6); value.Children.Add(bar); value.Children.Add(Label(Countdown(main.ResetUtc), 9.5, InkFaint));
                var hit = new Button { Content = value, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0) }; System.Windows.Automation.AutomationProperties.SetName(hit, "查看 " + main.Label + " 明细"); hit.Click += delegate { OpenCompactPage(state.Id, "details"); }; content.Children.Add(hit);
            } else if (state.Balances.Count > 0) { content.Children.Add(BigValue(state.Balances[0].Amount.ToString("N2"), 28)); content.Children.Add(Label(state.Balances[0].Currency + " 余额", 10.5, InkDim)); }
            else { var usage = RangeUsage(state.Id); content.Children.Add(BigValue(PeriodTokens(usage), 27)); content.Children.Add(Label(usage.HasData ? "区间 Token" : StatusWord(state), 10.5, InkDim)); }
            if (NeedsConnect(state)) { var connect = ConnectionButton(state.Id, true); connect.Padding = new Thickness(8, 3, 8, 3); connect.Margin = new Thickness(0, 4, 0, 0); connect.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(connect); }
            Grid.SetRow(content, 1); root.Children.Add(content);
            FrameworkElement footer = copyNotice.Length > 0 ? (FrameworkElement)Label(copyNotice, 9, AccentBrush) : CoinStrip(ids, state.Id, 13, true); Grid.SetRow(footer, 2); root.Children.Add(footer); return root;
        }
        private FrameworkElement MediumCard(ProviderState state, List<string> ids) {
            var root = new Grid { Margin = new Thickness(14, 8, 12, 9) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new Grid(); head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) }); head.ColumnDefinitions.Add(new ColumnDefinition());
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; var icon = Icon(state.Id, 16); icon.Margin = new Thickness(0, 0, 7, 0); name.Children.Add(icon); var label = Label(ProviderCatalog.Name(state.Id), 13, Ink); label.FontWeight = FontWeights.SemiBold; name.Children.Add(label);
            var title = new Button { Content = name, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left }; System.Windows.Automation.AutomationProperties.SetName(title, "查看 " + ProviderCatalog.Name(state.Id) + " 用量详情"); title.Click += delegate { OpenCompactPage(state.Id, "details"); }; head.Children.Add(title);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; actions.Children.Add(CoinStrip(ids, state.Id, 5, false)); actions.Children.Add(RangeIcon(state.Id)); actions.Children.Add(ProviderRefreshButton(state.Id, 22)); Grid.SetColumn(actions, 1); head.Children.Add(actions); root.Children.Add(head);
            var body = new Grid { Margin = new Thickness(0, 6, 0, 5) }; body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) }); body.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Quota main = Headline(state); PeriodUsage usage = RangeUsage(state.Id);
            left.Children.Add(BigValue(main != null ? main.Remaining.ToString("0") + "%" : state.Balances.Count > 0 ? state.Balances[0].Amount.ToString("N2") : PeriodTokens(usage), 30));
            left.Children.Add(Label(main != null ? main.Label + "剩余" : state.Balances.Count > 0 ? state.Balances[0].Currency + " 余额" : "区间 Token", 10.5, InkDim)); if (main != null) left.Children.Add(Label(Countdown(main.ResetUtc), 9.5, InkFaint)); body.Children.Add(left);
            var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var quota in state.Quotas.OrderBy(q => q == main ? 0 : 1).Take(2)) right.Children.Add(DenseQuota(quota, state.Id));
            if (state.Quotas.Count < 2) right.Children.Add(PeriodChart(usage, state.Id, 30)); Grid.SetColumn(right, 1); body.Children.Add(right); Grid.SetRow(body, 1); root.Children.Add(body);
            var footer = Row(); AddRow(footer, Label(copyNotice.Length > 0 ? copyNotice : "所选时段 " + PeriodCost(usage) + " · " + PeriodTokens(usage), 10, InkDim), NeedsConnect(state) ? (UIElement)ConnectionButton(state.Id, true) : Label(UpdatedAgo(state.LastSuccess), 9.5, InkFaint)); Grid.SetRow(footer, 2); root.Children.Add(footer); return root;
        }
        private FrameworkElement DenseQuota(Quota quota, string id) {
            var stack = new StackPanel { Margin = new Thickness(0, 3, 0, 6) }; var head = Row(); AddRow(head, Label(quota.Label + " " + quota.Remaining.ToString("0") + "%", 10.5, Ink), Label(Countdown(quota.ResetUtc).Replace("后重置", ""), 9, InkFaint)); stack.Children.Add(head);
            var bar = SegmentBar(quota.Remaining, ProviderCatalog.Color(id), 20, 6); bar.Margin = new Thickness(0, 5, 0, 0); stack.Children.Add(bar);
            var button = new Button { Content = stack, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = "查看所有额度" }; button.Click += delegate { OpenCompactPage(id, "details"); }; return button;
        }
        private FrameworkElement LargeCard(ProviderState state, List<string> ids) {
            var root = new DockPanel { Margin = new Thickness(15, 10, 13, 10) };
            var header = CardHeader(state, true, false); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            var strip = CoinStrip(ids, state.Id, 12, false); strip.Margin = new Thickness(-3, 7, 0, 4); DockPanel.SetDock(strip, Dock.Top); root.Children.Add(strip);
            var footer = Label(copyNotice.Length > 0 ? copyNotice : VisibleAccount(state), 9.5, InkFaint); footer.Margin = new Thickness(0, 7, 0, 0); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var content = new StackPanel();
            foreach (var quota in state.Quotas.Take(state.Id == "cursor" ? 4 : 3)) content.Children.Add(LargeQuota(quota, state.Id));
            if (!String.IsNullOrWhiteSpace(state.GrokBotError)) content.Children.Add(Hint(state.GrokBotError, 6));
            if (NeedsConnect(state)) content.Children.Add(ConnectionButton(state.Id, true));
            foreach (var balance in state.Balances) { content.Children.Add(BigValue(balance.Amount.ToString("N2"), 26)); content.Children.Add(Label(balance.Currency + " 余额", 10.5, InkDim)); }
            if (state.ResetCreditsAvailable.HasValue) content.Children.Add(Hint("限额重置额度 · " + state.ResetCreditsAvailable + " 次可用", 6));
            content.Children.Add(Separator()); PeriodUsage usage = RangeUsage(state.Id);
            var metrics = new UniformGrid { Columns = 3 }; var today = history.Days.Where(d => d.Agent == state.Id).ToList(); metrics.Children.Add(Metric("今日", TodayMoney(today))); metrics.Children.Add(Metric("所选时段", PeriodCost(usage))); metrics.Children.Add(Metric("区间 Token", PeriodTokens(usage))); content.Children.Add(metrics);
            content.Children.Add(PeriodChart(usage, state.Id, 42)); if (RangeChoice(state.Id).Preset == "custom") content.Children.Add(Hint(PeriodCaption(usage), 5));
            if (periodBusy.Contains(state.Id) || periodErrors.ContainsKey(state.Id)) content.Children.Add(PeriodNote(state.Id, usage));
            else if (usage.ExcludedPartialDays || usage.EstimatedTokens || usage.CoveragePartial) { var note = Label(usage.Hourly ? "按小时估算 ⓘ" : "按日汇总 · 已记录小计 ⓘ", 9.5, InkFaint); note.ToolTip = ((TextBlock)PeriodNote(state.Id, usage)).Text; content.Children.Add(note); }
            var day = today.FirstOrDefault(d => d.Day == HistoryService.DayKey(DateTime.Today)); if (day != null) content.Children.Add(Hint("今日构成 · 输入 " + Compact(day.InputTokens) + " · 输出 " + Compact(day.OutputTokens) + " · 缓存 " + Compact(day.CachedTokens), 6));
            compactScroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 3, 0) }; root.Children.Add(compactScroll); return root;
        }
        private FrameworkElement LargeQuota(Quota quota, string id) {
            var body = new StackPanel { Margin = new Thickness(0, 10, 0, 4) }; var row = Row();
            var heading = Label("", 12, Ink); heading.Inlines.Add(quota.Label + " "); heading.Inlines.Add(new System.Windows.Documents.Run(quota.Remaining.ToString("0.#") + "%") { FontSize = 16, FontWeight = FontWeights.SemiBold }); heading.Inlines.Add(" 剩余");
            AddRow(row, heading, Label(Countdown(quota.ResetUtc), 9.5, InkDim)); body.Children.Add(row);
            var bar = SegmentBar(quota.Remaining, ProviderCatalog.Color(id), 24, 7); bar.Margin = new Thickness(0, 6, 0, 5); body.Children.Add(bar);
            var pace = UsageDetails.Pace(quota, DateTime.UtcNow); if (pace != null) body.Children.Add(Label((pace.Reserve >= 0 ? "余量 " : "超前消耗 ") + Math.Abs(pace.Reserve).ToString("0") + "%" + (pace.Lasts ? " · 可持续到重置" : pace.SecondsUntilEmpty.HasValue ? " · 约 " + Duration(pace.SecondsUntilEmpty.Value) + "后用尽" : ""), 10, pace.Reserve >= 0 ? GoodBrush : WarnBrush));
            var button = new Button { Content = body, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = "查看全部额度和用量详情" }; System.Windows.Automation.AutomationProperties.SetName(button, "查看 " + quota.Label + " 明细"); button.Click += delegate { OpenCompactPage(id, "details"); }; return button;
        }
    }
}
