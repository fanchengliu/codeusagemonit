using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CodeUsageMonit {
    public sealed partial class MonitorPanel {
        private sealed class PeriodSnapshot {
            public string Key; public UsageHistory History; public LogIndex Codex, Claude;
        }
        private readonly Dictionary<string, PeriodSnapshot> periodSnapshots = new Dictionary<string, PeriodSnapshot>();
        private sealed class PeriodCache { public string Key; public PeriodUsage Value; }
        private readonly Dictionary<string, PeriodCache> periodCache = new Dictionary<string, PeriodCache>();
        private readonly Dictionary<string, int> periodSerials = new Dictionary<string, int>();
        private readonly Dictionary<string, string> periodAttempts = new Dictionary<string, string>(), periodErrors = new Dictionary<string, string>();
        private readonly Dictionary<string, DateTime> periodAttemptAt = new Dictionary<string, DateTime>();
        private readonly HashSet<string> periodBusy = new HashSet<string>();
        private readonly SemaphoreSlim periodReadGate = new SemaphoreSlim(1, 1);
        private Popup rangePopup;
        private bool RangePickerOpen { get { return rangePopup != null && rangePopup.IsOpen; } }
        private UsageRangeChoice RangeChoice(string id) {
            UsageRangeChoice choice;
            if (config.UsageRanges.TryGetValue(id, out choice) && choice != null) {
                try { UsagePeriod.Resolve(choice, DateTime.UtcNow, TimeZoneInfo.Local); return choice; } catch (ArgumentException) { }
            }
            return new UsageRangeChoice();
        }
        private static string RangeKey(UsageRangeChoice c) { return c.Preset + "|" + c.StartUtc + "|" + c.EndUtc + "|" + c.FollowNow; }
        private PeriodUsage RangeUsage(string id) {
            DateTime now = DateTime.UtcNow; UsageRangeChoice choice = RangeChoice(id); string key = RangeKey(choice);
            UsagePeriod range = UsagePeriod.Resolve(choice, now, TimeZoneInfo.Local);
            PeriodSnapshot snapshot; bool loaded = periodSnapshots.TryGetValue(id, out snapshot) && snapshot.Key == key;
            string attempted; DateTime at;
            if (!demo && config.UsageRanges.ContainsKey(id) && !periodBusy.Contains(id) &&
                (!periodAttempts.TryGetValue(id, out attempted) || attempted != key || (choice.FollowNow && periodAttemptAt.TryGetValue(id, out at) && now - at > TimeSpan.FromMinutes(config.RefreshMinutes)))) {
                periodAttempts[id] = key; periodAttemptAt[id] = now;
                app.Dispatcher.BeginInvoke(new Action(() => { if (!periodBusy.Contains(id)) { var ignored = LoadPeriodHistory(id); } }));
            }
            UsageHistory source = loaded ? snapshot.History : history;
            LogIndex codex = loaded ? snapshot.Codex : codexLogs, claude = loaded ? snapshot.Claude : claudeLogs;
            string cacheKey = key + "|" + now.Ticks / TimeSpan.TicksPerMinute + "|" + loaded + "|" + source.Updated + "|" + (codex == null ? "" : codex.Updated) + "|" + (claude == null ? "" : claude.Updated) + "|" + String.Join(",", config.Enabled);
            PeriodCache cached; if (periodCache.TryGetValue(id, out cached) && cached.Key == cacheKey) return cached.Value;
            Func<string, PeriodUsage> calculate = provider => PeriodCalculator.Calculate(provider, range, source, provider == "codex" ? codex : provider == "claude" ? claude : null, now, TimeZoneInfo.Local, loaded);
            var result = id == "overview" ? PeriodCalculator.Combine(range, EnabledIds().Select(calculate)) : calculate(id);
            periodCache[id] = new PeriodCache { Key = cacheKey, Value = result }; return result;
        }
        private async Task LoadPeriodHistory(string id) {
            if (demo) return;
            string key = RangeKey(RangeChoice(id)); int serial; periodSerials.TryGetValue(id, out serial); periodSerials[id] = ++serial;
            periodAttempts[id] = key; periodAttemptAt[id] = DateTime.UtcNow; periodBusy.Add(id); periodErrors.Remove(id); Render();
            await periodReadGate.WaitAsync();
            try {
                if (quitting || periodSerials[id] != serial) return;
                DateTime now = DateTime.UtcNow; UsagePeriod range = UsagePeriod.Resolve(RangeChoice(id), now, TimeZoneInfo.Local);
                int horizon = Math.Max(31, (int)Math.Ceiling((now - range.StartUtc).TotalDays) + 1);
                Task<UsageHistory> daily = HistoryService.ReadRange(TimeZoneInfo.ConvertTimeFromUtc(range.StartUtc, TimeZoneInfo.Local).Date, TimeZoneInfo.ConvertTimeFromUtc(range.EndUtc, TimeZoneInfo.Local).Date);
                Task<LogIndex> codex = id == "codex" || id == "overview" && config.Enabled.Contains("codex") ? Task.Run(() => CodexLogs.Scan(null, now, horizon)) : Task.FromResult<LogIndex>(null);
                Task<LogIndex> claude = id == "claude" || id == "overview" && config.Enabled.Contains("claude") ? Task.Run(() => ClaudeLogs.Scan(null, now, horizon)) : Task.FromResult<LogIndex>(null);
                await Task.WhenAll(daily, (Task)codex, claude);
                if (periodSerials[id] != serial || quitting) return;
                if (daily.Result.Error.Length > 0) { periodErrors[id] = daily.Result.Error; return; }
                periodSnapshots[id] = new PeriodSnapshot { Key = key, History = daily.Result, Codex = codex.Result, Claude = claude.Result };
            } catch (Exception e) { if (periodSerials[id] == serial) periodErrors[id] = e is ArgumentException ? e.Message : "本机历史读取失败，请重新选择时间后重试。"; }
            finally { periodReadGate.Release(); if (periodSerials[id] == serial) periodBusy.Remove(id); Render(); }
        }
        private void ApplyUsageRange(string id, UsageRangeChoice choice) {
            UsagePeriod.Resolve(choice, DateTime.UtcNow, TimeZoneInfo.Local);
            config.UsageRanges[id] = choice.Copy(); SaveConfig(); compactScrollKey = "";
            periodAttempts[id] = RangeKey(choice); periodAttemptAt[id] = DateTime.UtcNow;
            if (IsCompact && activeSize == "small" && id != "overview") { config.CompactProvider = id; compactPage = "period"; }
            CloseRangePicker(); Render(); var ignored = LoadPeriodHistory(id);
        }
        private Button RangeButton(string id) {
            var choice = RangeChoice(id); string label = choice.Preset == "today" ? "当天" : choice.Preset == "custom" ? "自定义" : choice.Preset;
            var b = new Button { Style = Styled("SecondaryButton"), Content = (IsCompact && activeSize == "small" ? "日期 · " : "时间范围 · ") + label + " ▾", Padding = new Thickness(9, 5, 9, 5), HorizontalAlignment = HorizontalAlignment.Right };
            System.Windows.Automation.AutomationProperties.SetName(b, ProviderCatalog.Name(id) + " 时间范围");
            b.Click += delegate { OpenRangePicker(id, b); }; return b;
        }
        private static string PeriodTokens(PeriodUsage result) {
            if (!result.HasData || result.ExcludedPartialDays && result.Days.Count == 0) return "—";
            return (result.EstimatedTokens ? "≈" : result.ExcludedPartialDays || result.CoveragePartial ? "≥" : "") + Compact(result.Tokens);
        }
        private static string PeriodCost(PeriodUsage result) {
            if (!result.HasData || result.ExcludedPartialDays && result.Days.Count == 0) return "—";
            return result.HasCost ? "≈$" + result.Cost.ToString("N2", CultureInfo.InvariantCulture) : "未定价";
        }
        private static string PeriodCaption(PeriodUsage result) {
            return result.Period.StartUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm") + " — " + result.Period.EndUtc.ToLocalTime().ToString("MM/dd HH:mm");
        }
        private UIElement PeriodNote(string id, PeriodUsage result) {
            string error;
            if (periodBusy.Contains(id)) return Hint("正在读取所选时段的本机历史…", 6);
            if (periodErrors.TryGetValue(id, out error)) { var warning = Hint(error, 6); warning.Foreground = WarnBrush; return warning; }
            string text = !result.HasData ? "该时段未记录到本机用量；当前账户额度或余额不是区间消耗。" : result.Hourly ? (result.EstimatedTokens ? "按小时索引估算，分钟边界按重叠比例分配。" : "本机按小时记录的小计。") : "按日汇总统计。";
            if (result.ExcludedPartialDays) text += result.Days.Count == 0 ? "该时段没有可计入的完整日期，无法给出用量。" : "跨越起止边界的部分日期不计入，显示已记录小计。";
            if (result.CoveragePartial) text += "现有缓存可能未覆盖全部日期，确认时间后会重新读取。";
            if (!result.CostComplete) text += "部分 Token 无可用价格。";
            if (result.AsOf.HasValue) text += " 统计截至 " + result.AsOf.Value.ToLocalTime().ToString("MM/dd HH:mm") + "。";
            text += " 费用为 API 等价估算。";
            return Hint(text, 6);
        }
        private UIElement PeriodBlock(string id, bool detail) {
            PeriodUsage result = RangeUsage(id); var panel = new StackPanel();
            var head = Row(); AddRow(head, Label("区间用量", 12, Ink), RangeButton(id)); panel.Children.Add(head);
            panel.Children.Add(Hint(PeriodCaption(result), 8));
            var metrics = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
            metrics.Children.Add(Metric("区间估算费用", PeriodCost(result))); metrics.Children.Add(Metric("区间 Token", PeriodTokens(result))); panel.Children.Add(metrics);
            panel.Children.Add(PeriodChart(result, id, detail ? 54 : 32)); panel.Children.Add(PeriodNote(id, result));
            if (detail && result.Days.Count > 0) {
                string model = UsageDetails.MainModel(result.Days); if (model.Length > 0) panel.Children.Add(Hint("最常用模型 · " + model, 8));
                foreach (var day in result.Days.GroupBy(d => d.Day).OrderByDescending(d => d.Key)) { var row = Row(); row.Margin = new Thickness(0, 8, 0, 0); AddRow(row, Label(day.Key, 10.5, InkDim), Label((day.Any(d => d.CostKnown) ? "≈$" + day.Where(d => d.CostKnown).Sum(d => d.Cost).ToString("N2") : "未定价") + " · " + Compact(day.Sum(d => d.Tokens)), 10.5, Ink)); panel.Children.Add(row); }
            }
            return panel;
        }
        private UIElement PeriodChart(PeriodUsage result, string id, double height) {
            var from = result.Period.StartUtc.ToLocalTime().Date; var last = result.Period.EndUtc.AddTicks(-1).ToLocalTime().Date;
            int count = Math.Max(1, (last - from).Days + 1), groupSize = Math.Max(1, (int)Math.Ceiling(count / 45.0));
            var series = Enumerable.Range(0, (int)Math.Ceiling(count / (double)groupSize)).Select(i => {
                DateTime start = from.AddDays(i * groupSize), end = start.AddDays(groupSize); string a = HistoryService.DayKey(start), b = HistoryService.DayKey(end);
                var rows = result.Days.Where(d => String.CompareOrdinal(d.Day, a) >= 0 && String.CompareOrdinal(d.Day, b) < 0).ToList();
                return new { Start = start, Rows = rows, Value = rows.Where(d => d.CostKnown).Sum(d => d.Cost) };
            }).ToList();
            double max = Math.Max(.01, series.Max(s => s.Value)); var columns = new UniformGrid { Columns = series.Count, Height = height, Margin = new Thickness(0, 8, 0, 3) };
            foreach (var item in series) {
                var cell = new Grid { Margin = new Thickness(1, 0, 1, 0), Background = Brushes.Transparent, ToolTip = item.Start.ToString("yyyy/MM/dd") + (groupSize > 1 ? " 起 " + groupSize + " 天" : "") + " · ≈$" + item.Value.ToString("N2") + " · " + Compact(item.Rows.Sum(d => d.Tokens)) + " Token" };
                var segments = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                foreach (var group in item.Rows.Where(d => d.CostKnown && d.Cost > 0).GroupBy(d => d.Agent)) segments.Children.Add(new Border { Height = height * group.Sum(d => d.Cost) / max, Background = Brush(ProviderCatalog.Color(group.Key)), Opacity = .8 });
                if (segments.Children.Count == 0) segments.Children.Add(new Border { Height = 1, Background = Hairline }); cell.Children.Add(segments); columns.Children.Add(cell);
            }
            var wrapper = new StackPanel(); wrapper.Children.Add(columns); var axis = Row(); AddRow(axis, Label(from.ToString("M/d"), 9.5, InkFaint), Label(last.ToString("M/d"), 9.5, InkFaint)); wrapper.Children.Add(axis); return wrapper;
        }
        private void CloseRangePicker() { if (rangePopup != null) { rangePopup.IsOpen = false; rangePopup = null; } }

        // Calendar popover: edits a draft. Only Confirm commits the provider's choice.
        private void OpenRangePicker(string id, FrameworkElement anchor) {
            CloseRangePicker(); var draft = RangeChoice(id).Copy(); var span = UsagePeriod.Resolve(draft, DateTime.UtcNow, TimeZoneInfo.Local);
            DateTime startDate = span.StartUtc.ToLocalTime().Date, endDate = span.EndUtc.ToLocalTime().Date, month = new DateTime(startDate.Year, startDate.Month, 1);
            bool startActive = true, updating = true;
            var container = new Grid { Margin = new Thickness(16), MinWidth = 540 };
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; container.Children.Add(quick);
            var split = new Grid(); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(222) }); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); split.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(split, 1); container.Children.Add(split);
            var inputs = new StackPanel(); split.Children.Add(inputs);
            var calendar = new StackPanel(); Grid.SetColumn(calendar, 2); split.Children.Add(calendar);
            var startTime = new TextBox { Text = span.StartUtc.ToLocalTime().ToString("HH:mm"), Width = 57, FontSize = 13, Padding = new Thickness(5), VerticalContentAlignment = VerticalAlignment.Center };
            var endTime = new TextBox { Text = span.EndUtc.ToLocalTime().ToString("HH:mm"), Width = 57, FontSize = 13, Padding = new Thickness(5), VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(startTime, "开始时间"); System.Windows.Automation.AutomationProperties.SetName(endTime, "结束时间");
            var startButton = new Button { Content = startDate.ToString("yyyy/MM/dd") + " ▦", Padding = new Thickness(0, 5, 6, 5), FontSize = 13, HorizontalContentAlignment = HorizontalAlignment.Left };
            var endButton = new Button { Content = endDate.ToString("yyyy/MM/dd") + " ▦", Padding = new Thickness(0, 5, 6, 5), FontSize = 13, HorizontalContentAlignment = HorizontalAlignment.Left };
            System.Windows.Automation.AutomationProperties.SetName(startButton, "开始日期"); System.Windows.Automation.AutomationProperties.SetName(endButton, "结束日期");
            Func<string, Button, TextBox, Border> field = (label, button, time) => {
                var p = new StackPanel(); p.Children.Add(Label(label, 11, InkDim)); var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; row.Children.Add(button); row.Children.Add(time); p.Children.Add(row);
                return new Border { Child = p, CornerRadius = new CornerRadius(10), Padding = new Thickness(11), BorderThickness = new Thickness(1), Background = CardFill, Margin = new Thickness(0, 0, 0, 9) };
            };
            var startField = field("开始日期与时间", startButton, startTime); var endField = field("结束日期与时间", endButton, endTime); inputs.Children.Add(startField); inputs.Children.Add(endField);
            var follow = new CheckBox { Content = "结束时间跟随当前时刻", IsChecked = draft.FollowNow, Foreground = InkDim, FontSize = 11, Margin = new Thickness(0, 4, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetName(follow, "结束时间跟随当前时刻"); inputs.Children.Add(follow);
            inputs.Children.Add(Hint("支持过去 366 天 · 本机时区\n按日汇总平台无法精确统计分钟边界。", 3));
            var error = Hint("", 8); error.Foreground = WarnBrush;
            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.Children.Add(error);
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(actions, 1); footer.Children.Add(actions); Grid.SetRow(footer, 2); container.Children.Add(footer);
            var cancel = new Button { Style = Styled("SecondaryButton"), Content = "取消", Margin = new Thickness(0, 0, 8, 0) };
            var confirm = new Button { Style = Styled("PrimaryButton"), Content = "确定", MinWidth = 90 }; actions.Children.Add(cancel); actions.Children.Add(confirm);
            System.Windows.Automation.AutomationProperties.SetName(confirm, "确定时间范围");
            var quickButtons = new Dictionary<string, Button>(); Action refreshCalendar = null;
            Action draw = () => {
                startField.BorderBrush = startActive ? AccentBrush : Hairline; endField.BorderBrush = startActive ? Hairline : AccentBrush;
                startButton.Content = startDate.ToString("yyyy/MM/dd") + " ▦"; endButton.Content = endDate.ToString("yyyy/MM/dd") + " ▦"; endTime.IsEnabled = follow.IsChecked != true;
                foreach (var b in quickButtons) { b.Value.Background = draft.Preset == b.Key ? AccentBrush : CardFill; b.Value.Foreground = draft.Preset == b.Key ? Brush("#102126") : InkDim; }
                calendar.Children.Clear();
                var previous = IconAction("", "上个月", () => { month = month.AddMonths(-1); refreshCalendar(); });
                var next = IconAction("", "下个月", () => { month = month.AddMonths(1); refreshCalendar(); });
                previous.IsEnabled = month > new DateTime(DateTime.Today.AddDays(-366).Year, DateTime.Today.AddDays(-366).Month, 1); next.IsEnabled = month.AddMonths(1) <= DateTime.Today;
                var title = Label(month.ToString("yyyy年M月"), 15, Ink); title.FontWeight = FontWeights.SemiBold; title.HorizontalAlignment = HorizontalAlignment.Center;
                var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.Children.Add(previous); Grid.SetColumn(title, 1); header.Children.Add(title); Grid.SetColumn(next, 2); header.Children.Add(next); calendar.Children.Add(header);
                var days = new UniformGrid { Columns = 7, Margin = new Thickness(0, 7, 0, 0) };
                foreach (string day in new[] { "日", "一", "二", "三", "四", "五", "六" }) { var label = Label(day, 10.5, InkFaint); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 4, 0, 8); days.Children.Add(label); }
                DateTime first = month.AddDays(-(int)month.DayOfWeek);
                for (int i = 0; i < 42; i++) {
                    DateTime day = first.AddDays(i); bool selected = day == (startActive ? startDate : endDate);
                    var b = new Button { Content = day.Day.ToString(), Height = 31, Padding = new Thickness(0), Margin = new Thickness(1), FontSize = 12, Foreground = selected ? Brush("#102126") : day.Month == month.Month ? Ink : InkFaint, Background = selected ? AccentBrush : day >= startDate && day <= endDate ? Brush("#185CC8E0") : Brushes.Transparent, IsEnabled = day >= DateTime.Today.AddDays(-366) && day <= DateTime.Today };
                    System.Windows.Automation.AutomationProperties.SetName(b, "选择日期 " + day.ToString("yyyy-MM-dd"));
                    b.Click += delegate { if (startActive) startDate = day; else { endDate = day; follow.IsChecked = false; } draft.Preset = "custom"; month = new DateTime(day.Year, day.Month, 1); refreshCalendar(); }; days.Children.Add(b);
                }
                calendar.Children.Add(days);
            }; refreshCalendar = draw;
            foreach (string code in UsagePeriod.Presets) {
                string captured = code; var b = new Button { Style = Styled("SecondaryButton"), Content = code == "today" ? "当天" : code, MinWidth = 46, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 7, 0) }; quickButtons[code] = b;
                System.Windows.Automation.AutomationProperties.SetName(b, "快捷范围 " + code);
                b.Click += delegate { updating = true; draft = new UsageRangeChoice { Preset = captured }; var chosen = UsagePeriod.Resolve(draft, DateTime.UtcNow, TimeZoneInfo.Local); startDate = chosen.StartUtc.ToLocalTime().Date; endDate = chosen.EndUtc.ToLocalTime().Date; startTime.Text = chosen.StartUtc.ToLocalTime().ToString("HH:mm"); endTime.Text = chosen.EndUtc.ToLocalTime().ToString("HH:mm"); follow.IsChecked = true; month = new DateTime(startDate.Year, startDate.Month, 1); updating = false; error.Text = ""; refreshCalendar(); }; quick.Children.Add(b);
            }
            startButton.Click += delegate { startActive = true; month = new DateTime(startDate.Year, startDate.Month, 1); refreshCalendar(); };
            endButton.Click += delegate { startActive = false; follow.IsChecked = false; month = new DateTime(endDate.Year, endDate.Month, 1); refreshCalendar(); };
            startTime.TextChanged += delegate { if (!updating) { draft.Preset = "custom"; refreshCalendar(); } };
            endTime.TextChanged += delegate { if (!updating) { draft.Preset = "custom"; refreshCalendar(); } };
            follow.Checked += delegate { if (!updating) { draft.FollowNow = true; refreshCalendar(); } };
            follow.Unchecked += delegate { if (!updating) { draft.FollowNow = false; draft.Preset = "custom"; refreshCalendar(); } };
            Action apply = () => {
                try {
                    var chosen = draft.Copy(); chosen.FollowNow = follow.IsChecked == true;
                    if (chosen.Preset == "custom" || !chosen.FollowNow) {
                        DateTime a, b;
                        if (!DateTime.TryParseExact(startTime.Text.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out a)) throw new ArgumentException("开始时间请使用 HH:mm 格式。");
                        if (!DateTime.TryParseExact(endTime.Text.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out b)) throw new ArgumentException("结束时间请使用 HH:mm 格式。");
                        chosen.Preset = "custom"; chosen.StartUtc = UsagePeriod.LocalUtc(startDate.Add(a.TimeOfDay), TimeZoneInfo.Local).ToString("o"); chosen.EndUtc = UsagePeriod.LocalUtc(endDate.Add(b.TimeOfDay), TimeZoneInfo.Local).ToString("o");
                    }
                    ApplyUsageRange(id, chosen);
                } catch (ArgumentException ex) { error.Text = ex.Message; }
            };
            cancel.Click += delegate { CloseRangePicker(); }; confirm.Click += delegate { apply(); };
            var surface = new Border { Child = container, Width = 620, CornerRadius = new CornerRadius(12), BorderBrush = Brush("#52616B"), BorderThickness = new Thickness(1), Background = Brush("#FF202329"), Resources = window.Resources, SnapsToDevicePixels = true };
            System.Windows.Documents.TextElement.SetFontFamily(surface, window.FontFamily); System.Windows.Documents.TextElement.SetForeground(surface, Ink);
            surface.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { CloseRangePicker(); e.Handled = true; } else if (e.Key == Key.Enter) { apply(); e.Handled = true; } };
            rangePopup = new Popup { Child = surface, PlacementTarget = anchor, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = false, PopupAnimation = PopupAnimation.None };
            rangePopup.Closed += delegate { app.Dispatcher.BeginInvoke(new Action(Render)); };
            updating = false; refreshCalendar(); rangePopup.IsOpen = true;
        }
    }
}
