using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CodeUsageMonit {
    // A usage period: a preset (today, last 24 h, 7 / 14 / 30 days) or a custom start and
    // end in local time with hour precision (the local index is hourly).
    public sealed class UsageRange {
        public string Preset = "30d";
        public DateTime Start, End;
        public bool FollowNow = true;
        public void Resolve(DateTime now, out DateTime start, out DateTime end) {
            end = now;
            switch (Preset) {
                case "today": start = now.Date; break;
                case "1d": start = now.AddHours(-24); break;
                case "7d": start = now.Date.AddDays(-6); break;
                case "14d": start = now.Date.AddDays(-13); break;
                case "custom": start = Start; end = FollowNow ? now : End; break;
                default: start = now.Date.AddDays(-29); break;
            }
            if (end > now) end = now;
            if (start >= end) start = end.AddHours(-1);
        }
        public string Label() {
            switch (Preset) {
                case "today": return "当天";
                case "1d": return "近 24 小时";
                case "7d": return "近 7 天";
                case "14d": return "近 14 天";
                case "custom": return Start.ToString("M/d HH:mm") + " – " + (FollowNow ? "现在" : End.ToString(End.Date == Start.Date ? "HH:mm" : "M/d HH:mm"));
                default: return "近 30 天";
            }
        }
        public UsageRange Copy() { return new UsageRange { Preset = Preset, Start = Start, End = End, FollowNow = FollowNow }; }
    }

    public sealed partial class MonitorPanel {
        private readonly Dictionary<string, UsageRange> ranges = new Dictionary<string, UsageRange>();
        private UsageRange RangeFor(string id) { UsageRange r; if (!ranges.TryGetValue(id, out r)) { r = new UsageRange(); ranges[id] = r; } return r; }
        // Hourly detail is kept for about a month; older days cannot be selected.
        private static DateTime OldestSelectable { get { return DateTime.Today.AddDays(-(LogReader.RetainDays - 1)); } }

        private Button RangeButton(string id, Action changed, bool compact) {
            UsageRange range = RangeFor(id);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = "", FontFamily = IconFont, FontSize = compact ? 10 : 11, Foreground = InkDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            content.Children.Add(Label(range.Label(), compact ? 10.5 : 11, Ink));
            content.Children.Add(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 8, Foreground = InkFaint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 1, 0, 0) });
            var button = new Button { Content = content, Padding = new Thickness(8, 3, 7, 4), Background = Brush("#12FFFFFF"), BorderBrush = Brush("#1FFFFFFF"), BorderThickness = new Thickness(1), ToolTip = "选择统计时间段" };
            System.Windows.Automation.AutomationProperties.SetName(button, "选择时间段");
            button.Click += delegate(object sender, RoutedEventArgs e) { e.Handled = true; ShowRangePicker(button, range, r => { ranges[id] = r; changed(); }); };
            return button;
        }

        private void ShowRangePicker(FrameworkElement anchor, UsageRange current, Action<UsageRange> apply) {
            var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = 6 };
            DateTime now = DateTime.Now, start, end; current.Resolve(now, out start, out end);
            // Editing state: whole hours, which endpoint the calendar edits, date or hour pane.
            start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0);
            end = current.Preset == "custom" && !current.FollowNow ? current.End : new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
            bool follow = current.Preset == "custom" ? current.FollowNow : true;
            string editing = "start", pane = "date";
            DateTime month = new DateTime(start.Year, start.Month, 1);
            string preset = current.Preset;
            Action rebuild = null;
            Action<string> pick = code => {
                var r = new UsageRange { Preset = code, FollowNow = true };
                popup.IsOpen = false; apply(r);
            };
            rebuild = () => {
                var root = new Grid();
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
                // Quick presets apply at once.
                var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                foreach (var option in new[] { new[] { "today", "当天" }, new[] { "1d", "1d" }, new[] { "7d", "7d" }, new[] { "14d", "14d" }, new[] { "30d", "30d" } }) {
                    string code = option[0]; bool active = preset == code;
                    var chip = new Button { Content = Label(option[1], 11.5, active ? Brush("#0B1216") : Ink), Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(0, 0, 8, 0), Background = active ? AccentBrush : Brush("#12FFFFFF"), BorderBrush = Brush(active ? "#5CC8E0" : "#26FFFFFF"), BorderThickness = new Thickness(1) };
                    if (active) ((TextBlock)chip.Content).FontWeight = FontWeights.SemiBold;
                    System.Windows.Automation.AutomationProperties.SetName(chip, "时间段 " + option[1]);
                    chip.Click += delegate { pick(code); };
                    chips.Children.Add(chip);
                }
                Grid.SetColumnSpan(chips, 3); root.Children.Add(chips);
                var left = new StackPanel(); Grid.SetRow(left, 1); root.Children.Add(left);
                left.Children.Add(Label("支持日期与时间 · 按小时统计", 10.5, InkFaint));
                left.Children.Add(TimeCard("开始时间", start, editing == "start", false, () => { editing = "start"; pane = "date"; month = new DateTime(start.Year, start.Month, 1); rebuild(); }, () => { editing = "start"; pane = "time"; rebuild(); }));
                left.Children.Add(TimeCard("结束时间", follow ? now : end, editing == "end" && !follow, follow, () => { if (follow) return; editing = "end"; pane = "date"; month = new DateTime(end.Year, end.Month, 1); rebuild(); }, () => { if (follow) return; editing = "end"; pane = "time"; rebuild(); }));
                var followBox = new CheckBox { IsChecked = follow, Margin = new Thickness(2, 10, 0, 0), Content = Label("结束时间跟随当前时刻", 11.5, InkDim), Foreground = InkDim };
                followBox.Checked += delegate { follow = true; if (editing == "end") editing = "start"; rebuild(); };
                followBox.Unchecked += delegate { follow = false; end = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0); rebuild(); };
                left.Children.Add(followBox);
                var actions = new Grid { Margin = new Thickness(0, 14, 0, 0) };
                actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); actions.ColumnDefinitions.Add(new ColumnDefinition());
                var cancel = new Button { Style = Styled("SecondaryButton"), Content = "取消", HorizontalAlignment = HorizontalAlignment.Stretch };
                cancel.Click += delegate { popup.IsOpen = false; };
                var ok = new Button { Style = Styled("PrimaryButton"), Content = "确定", HorizontalAlignment = HorizontalAlignment.Stretch };
                System.Windows.Automation.AutomationProperties.SetName(ok, "确定时间段");
                ok.Click += delegate {
                    DateTime a = start, b = follow ? now : end;
                    if (b <= a) { DateTime t = a; a = b; b = t; if (b == a) b = a.AddHours(1); }
                    popup.IsOpen = false; apply(new UsageRange { Preset = "custom", Start = a, End = b, FollowNow = follow });
                };
                actions.Children.Add(cancel); Grid.SetColumn(ok, 2); actions.Children.Add(ok);
                left.Children.Add(actions);
                // Right: month calendar, or the hour grid for the endpoint being edited.
                DateTime target = editing == "start" ? start : end;
                FrameworkElement right = pane == "time" ? HourGrid(target, h => {
                    if (editing == "start") start = start.Date.AddHours(h); else end = end.Date.AddHours(h);
                    rebuild();
                }) : Calendar(month, start, follow ? now : end, editing, m => { month = m; rebuild(); }, day => {
                    if (editing == "start") { start = day.AddHours(start.Hour); if (!follow) { editing = "end"; if (end < start) end = start.AddHours(1); } }
                    else { end = day.AddHours(end.Hour); if (end < start) { DateTime t = start; start = end; end = t; } }
                    rebuild();
                });
                Grid.SetRow(right, 1); Grid.SetColumn(right, 2); root.Children.Add(right);
                var frame = new Border { Background = PopupSurface(), BorderBrush = Brush("#33FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 14), Child = root };
                System.Windows.Documents.TextElement.SetFontFamily(frame, window.FontFamily);
                popup.Child = frame;
            };
            rebuild();
            I18n.Localize(popup.Child);
            popup.IsOpen = true;
        }
        private FrameworkElement TimeCard(string title, DateTime value, bool active, bool disabled, Action pickDate, Action pickTime) {
            var card = new Border { Background = Brush(active ? "#145CC8E0" : "#0AFFFFFF"), BorderBrush = Brush(active ? "#5CC8E0" : "#1FFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 8, 0, 0), Opacity = disabled ? .5 : 1 };
            var stack = new StackPanel();
            stack.Children.Add(Label(title, 11, InkDim));
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var date = new Button { Content = Label(disabled ? "现在" : value.ToString("yyyy/MM/dd"), 14, Ink), Padding = new Thickness(2, 1, 2, 1), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !disabled };
            date.Click += delegate { pickDate(); }; row.Children.Add(date);
            var cal = new Button { Style = Styled("IconButton"), Content = "", Width = 26, Height = 26, FontSize = 12, IsEnabled = !disabled, ToolTip = "选择日期" }; cal.Click += delegate { pickDate(); };
            Grid.SetColumn(cal, 1); row.Children.Add(cal);
            var time = new Button { Content = Label(disabled ? "" : value.ToString("HH:mm"), 14, Ink), Padding = new Thickness(2, 1, 2, 1), IsEnabled = !disabled };
            time.Click += delegate { pickTime(); }; Grid.SetColumn(time, 3); row.Children.Add(time);
            var clock = new Button { Style = Styled("IconButton"), Content = "", Width = 26, Height = 26, FontSize = 12, IsEnabled = !disabled, ToolTip = "选择时刻（整点）" }; clock.Click += delegate { pickTime(); };
            Grid.SetColumn(clock, 4); row.Children.Add(clock);
            System.Windows.Automation.AutomationProperties.SetName(date, title + "日期"); System.Windows.Automation.AutomationProperties.SetName(time, title + "时刻");
            stack.Children.Add(row);
            card.Child = stack;
            card.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (!disabled) { pickDate(); e.Handled = true; } };
            return card;
        }
        private FrameworkElement Calendar(DateTime month, DateTime start, DateTime end, string editing, Action<DateTime> changeMonth, Action<DateTime> pick) {
            var box = new Border { Background = Brush("#08FFFFFF"), BorderBrush = Brush("#14FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8) };
            var stack = new StackPanel();
            var head = new Grid(); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var prev = new Button { Style = Styled("IconButton"), Content = "", Width = 26, Height = 26, FontSize = 10, IsEnabled = month > new DateTime(OldestSelectable.Year, OldestSelectable.Month, 1), ToolTip = "上个月" };
            prev.Click += delegate { changeMonth(month.AddMonths(-1)); }; head.Children.Add(prev);
            var title = Label(month.ToString(I18n.T("yyyy年M月"), I18n.Culture), 13, Ink); title.FontWeight = FontWeights.SemiBold; title.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(title, 1); head.Children.Add(title);
            var next = new Button { Style = Styled("IconButton"), Content = "", Width = 26, Height = 26, FontSize = 10, IsEnabled = month.AddMonths(1) <= DateTime.Today, ToolTip = "下个月" };
            next.Click += delegate { changeMonth(month.AddMonths(1)); }; Grid.SetColumn(next, 2); head.Children.Add(next);
            stack.Children.Add(head);
            var grid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 6, 0, 0) };
            foreach (string d in I18n.English ? new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" } : new[] { "日", "一", "二", "三", "四", "五", "六" }) { var l = Label(d, 10.5, InkFaint); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 2, 0, 6); grid.Children.Add(l); }
            DateTime first = month.AddDays(-(int)month.DayOfWeek);
            for (int i = 0; i < 42; i++) {
                DateTime day = first.AddDays(i); bool inMonth = day.Month == month.Month;
                bool selectable = day >= OldestSelectable && day <= DateTime.Today;
                bool isStart = day == start.Date, isEnd = day == end.Date, inside = day > start.Date && day < end.Date;
                var text = Label(day.Day.ToString(), 12, isStart || isEnd ? Brush("#0B1216") : !selectable ? Brush("#40FFFFFF") : inMonth ? Ink : InkFaint);
                text.HorizontalAlignment = HorizontalAlignment.Center; if (isStart || isEnd) text.FontWeight = FontWeights.SemiBold;
                var cell = new Button { Content = text, Height = 30, Padding = new Thickness(0), Margin = new Thickness(1.5), IsEnabled = selectable, Background = isStart || isEnd ? AccentBrush : inside ? Brush("#1F5CC8E0") : Brush("#00FFFFFF"), ToolTip = day.ToString("M月d日 ddd", CultureInfo.GetCultureInfo("zh-CN")) + (selectable ? "" : " · 超出本机逐小时记录的范围") };
                if (day == DateTime.Today && !(isStart || isEnd)) { cell.BorderBrush = Brush("#595CC8E0"); cell.BorderThickness = new Thickness(1); }
                DateTime captured = day; cell.Click += delegate { pick(captured); };
                System.Windows.Automation.AutomationProperties.SetName(cell, day.ToString("yyyy-MM-dd"));
                grid.Children.Add(cell);
            }
            stack.Children.Add(grid);
            stack.Children.Add(Label(editing == "start" ? "点击日期设为开始" : "点击日期设为结束", 10, InkFaint));
            box.Child = stack;
            return box;
        }
        private FrameworkElement HourGrid(DateTime value, Action<int> pick) {
            var box = new Border { Background = Brush("#08FFFFFF"), BorderBrush = Brush("#14FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8) };
            var stack = new StackPanel();
            var title = Label(value.ToString(I18n.T("M月d日"), I18n.Culture) + " · 选择整点", 13, Ink); title.FontWeight = FontWeights.SemiBold; title.HorizontalAlignment = HorizontalAlignment.Center; title.Margin = new Thickness(0, 4, 0, 8); stack.Children.Add(title);
            var grid = new UniformGrid { Columns = 4 };
            DateTime now = DateTime.Now;
            for (int h = 0; h < 24; h++) {
                int captured = h; bool active = value.Hour == h; bool future = value.Date == now.Date && h > now.Hour;
                var cell = new Button { Content = Label(h.ToString("00") + ":00", 12, active ? Brush("#0B1216") : future ? Brush("#40FFFFFF") : Ink), Height = 30, Margin = new Thickness(2), Background = active ? AccentBrush : Brush("#0AFFFFFF"), IsEnabled = !future };
                ((TextBlock)cell.Content).HorizontalAlignment = HorizontalAlignment.Center;
                cell.Click += delegate { pick(captured); };
                grid.Children.Add(cell);
            }
            stack.Children.Add(grid);
            box.Child = stack;
            return box;
        }
    }
}
