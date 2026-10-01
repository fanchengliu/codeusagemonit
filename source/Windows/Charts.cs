using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodeUsageMonit {
    // One bar of a usage chart: a day or an hour, optionally split into coloured parts
    // (providers in the overview, models on a provider page).
    public sealed class ChartBar {
        public DateTime Start; public bool Hourly, Current;
        public double Cost, Tokens, Requests, TimedOutput, TimedSeconds; public bool CostPartial;
        public List<ChartPart> Parts = new List<ChartPart>();
        public double? Speed { get { return OutputTiming.Speed(TimedOutput, TimedSeconds); } }
    }
    public sealed class ChartPart {
        public string Name = "", Color = "#FFFFFF"; public double Cost, Tokens, TimedOutput, TimedSeconds;
        public double? Speed { get { return OutputTiming.Speed(TimedOutput, TimedSeconds); } }
    }

    public sealed partial class MonitorPanel {
        // Chart state survives page rebuilds: the metric shown and the pinned bar.
        private readonly Dictionary<string, string> chartMetric = new Dictionary<string, string>();
        private readonly Dictionary<string, DateTime> chartPinned = new Dictionary<string, DateTime>();
        private static CultureInfo Zh { get { return I18n.Culture; } }

        // Interactive bar chart: hover a bar to highlight it, click to pin its details below
        // (click again or × to unpin); the chip at the top switches between cost and tokens,
        // and — for one agent (speedColor set) — its output speed per bar (never stacked).
        private FrameworkElement UsageChart(string id, string title, List<ChartBar> bars, double height, bool stacked, bool compact, Action rerender, bool showDetail = true, string speedColor = null) {
            string metric; if (!chartMetric.TryGetValue(id, out metric) || (metric == "speed" && speedColor == null)) metric = "cost";
            bool cost = metric == "cost", speed = metric == "speed";
            if (speed) stacked = false;
            Func<ChartBar, double> value = b => speed ? (b.Speed ?? 0) : cost ? b.Cost : b.Tokens;
            double max = Math.Max(cost ? .01 : 1, bars.Count == 0 ? 0 : bars.Max(value));
            DateTime pinned; bool hasPin = chartPinned.TryGetValue(id, out pinned) && bars.Any(b => b.Start == pinned);
            var wrapper = new StackPanel { Margin = new Thickness(0, compact ? 6 : 14, 0, 0) };
            if (!compact) {
                var head = new Grid();
                head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.Children.Add(Label(title, 10.5, InkFaint));
                var peak = Label(speed ? "最高 " + OutputTiming.Text(bars.Any(b => b.Speed.HasValue) ? max : (double?)null) : "峰值 " + (cost ? Usd(max, max < 10) : Compact(max)), 10.5, InkFaint); peak.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(peak, 1); head.Children.Add(peak);
                var toggle = MetricToggle(id, metric, rerender, speedColor != null); Grid.SetColumn(toggle, 2); head.Children.Add(toggle);
                wrapper.Children.Add(head);
            }
            var columns = new UniformGrid { Columns = Math.Max(1, bars.Count), Rows = 1, Height = height, Margin = new Thickness(0, compact ? 0 : 6, 0, 0) };
            foreach (ChartBar bar in bars) {
                ChartBar captured = bar;
                bool selected = hasPin && bar.Start == pinned;
                var column = new Grid { Background = Brushes.Transparent, Height = height };
                double v = value(bar);
                if (v <= 0) column.Children.Add(new Border { Height = 1, Background = Brush("#FFFFFF"), Opacity = .14, VerticalAlignment = VerticalAlignment.Bottom });
                else {
                    column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(0, max - v), GridUnitType.Star) });
                    var parts = stacked && bar.Parts.Count > 0 ? bar.Parts.Where(p => (cost ? p.Cost : p.Tokens) > 0).Reverse().ToList() : new List<ChartPart> { new ChartPart { Color = speed ? speedColor : bar.Parts.Count > 0 ? bar.Parts[0].Color : "#5CC8E0", Cost = bar.Cost, Tokens = bar.Tokens } };
                    foreach (ChartPart part in parts) {
                        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(speed ? v : cost ? part.Cost : part.Tokens, GridUnitType.Star), MinHeight = 1.5 });
                        var segment = new Border { Background = Brush(part.Color), Opacity = selected ? 1 : hasPin ? .35 : bar.Current ? 1 : .62, CornerRadius = parts.IndexOf(part) == 0 ? new CornerRadius(1.5, 1.5, 0, 0) : new CornerRadius(0) };
                        Grid.SetRow(segment, column.RowDefinitions.Count - 1); column.Children.Add(segment);
                    }
                }
                // Each bar is a button: hover highlights it, click (or Enter) pins its details.
                var hit = new Button { Content = column, Padding = new Thickness(0), Margin = new Thickness(bars.Count > 40 ? .5 : 1, 0, bars.Count > 40 ? .5 : 1, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Background = Brush(selected ? "#1FFFFFFF" : "#00FFFFFF"), ToolTip = BarTip(bar, stacked) };
                hit.Click += delegate(object sender, RoutedEventArgs e) {
                    e.Handled = true;
                    DateTime current; if (chartPinned.TryGetValue(id, out current) && current == captured.Start) chartPinned.Remove(id); else chartPinned[id] = captured.Start;
                    rerender();
                };
                System.Windows.Automation.AutomationProperties.SetName(hit, BarLabel(bar) + " 用量");
                columns.Children.Add(hit);
            }
            wrapper.Children.Add(columns);
            wrapper.Children.Add(ChartAxis(bars, compact));
            if (hasPin && showDetail) wrapper.Children.Add(BarDetail(id, bars.First(b => b.Start == pinned), stacked, compact, rerender));
            return wrapper;
        }
        private FrameworkElement MetricToggle(string id, string metric, Action rerender, bool withSpeed) {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var options = new List<string[]> { new[] { "cost", "费用" }, new[] { "tokens", "Token" } };
            if (withSpeed) options.Add(new[] { "speed", "速度" });
            foreach (var option in options) {
                string code = option[0]; bool active = metric == code;
                var chip = new Button { Content = Label(option[1], 10, active ? Ink : InkFaint), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(2, 0, 0, 0), Background = Brush(active ? "#1CFFFFFF" : "#00FFFFFF"), ToolTip = code == "cost" ? "按 API 等价费用显示" : code == "tokens" ? "按 Token 数显示" : "按输出速度显示（输出 Token ÷ 请求耗时，含首字延迟）" };
                System.Windows.Automation.AutomationProperties.SetName(chip, "图表显示" + option[1]);
                chip.Click += delegate { chartMetric[id] = code; rerender(); };
                row.Children.Add(chip);
            }
            return row;
        }
        // Date ticks: about six evenly spaced labels, always the first and the last bar.
        private static FrameworkElement ChartAxis(List<ChartBar> bars, bool compact) {
            var axis = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            if (bars.Count == 0) return axis;
            int step = Math.Max(1, (int)Math.Ceiling(bars.Count / (compact ? 4.0 : 6.0)));
            var ticks = new List<int>(); for (int i = 0; i < bars.Count; i += step) ticks.Add(i);
            if (bars.Count - 1 - ticks.Last() < step / 2.0 && ticks.Count > 1) ticks[ticks.Count - 1] = bars.Count - 1; else if (ticks.Last() != bars.Count - 1) ticks.Add(bars.Count - 1);
            for (int i = 0; i < bars.Count; i++) axis.ColumnDefinitions.Add(new ColumnDefinition());
            foreach (int i in ticks) {
                ChartBar bar = bars[i];
                string text = bar.Current ? (bar.Hourly ? "现在" : "今天") : bar.Hourly ? bar.Start.ToString(bar.Start.Hour == 0 ? "M/d" : "HH:mm", Zh) : bar.Start.ToString("M/d", Zh);
                var label = new TextBlock { Text = text, FontSize = compact ? 9 : 9.5, Foreground = InkFaint, TextAlignment = i == 0 ? TextAlignment.Left : i == bars.Count - 1 ? TextAlignment.Right : TextAlignment.Center };
                int span = Math.Min(step, 4), from = i == 0 ? 0 : i == bars.Count - 1 ? Math.Max(0, i - span + 1) : Math.Max(0, i - span / 2);
                label.HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == bars.Count - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center;
                Grid.SetColumn(label, from); Grid.SetColumnSpan(label, Math.Min(span, bars.Count - from)); axis.Children.Add(label);
            }
            return axis;
        }
        // The pinned bar of a chart, if any (compact sizes show it in their footer).
        private ChartBar PinnedBar(string id, List<ChartBar> bars) { DateTime at; return chartPinned.TryGetValue(id, out at) ? bars.FirstOrDefault(b => b.Start == at) : null; }
        private static string BarSummary(ChartBar bar) { return (bar.Hourly ? bar.Start.ToString("M/d HH:00") : bar.Start.ToString("M/d ddd", Zh)) + " · " + Usd(bar.Cost) + " · " + Compact(bar.Tokens) + " Token" + (bar.Speed.HasValue ? " · " + OutputTiming.Text(bar.Speed) : ""); }
        private static string BarLabel(ChartBar bar) { return bar.Hourly ? bar.Start.ToString(I18n.T("M月d日 HH:00"), Zh) : bar.Start.ToString(I18n.T("M月d日 ddd"), Zh); }
        private static string BarTip(ChartBar bar, bool stacked) {
            string tip = BarLabel(bar) + "\n" + (bar.Cost > 0 || !bar.CostPartial ? Usd(bar.Cost) : "费用未知") + " · " + Compact(bar.Tokens) + " Token" + (bar.Requests > 0 ? " · " + bar.Requests.ToString("N0", CultureInfo.InvariantCulture) + " 次请求" : "") + (bar.Speed.HasValue && !stacked ? "\n输出速度 " + OutputTiming.Text(bar.Speed) : "");
            if (stacked) tip += String.Concat(bar.Parts.Where(p => p.Tokens > 0).OrderByDescending(p => p.Cost).Take(6).Select(p => "\n" + p.Name + "  " + Usd(p.Cost)));
            return tip + "\n点击固定明细";
        }
        private FrameworkElement BarDetail(string id, ChartBar bar, bool stacked, bool compact, Action rerender) {
            var box = new Border { Background = Brush("#0CFFFFFF"), BorderBrush = Brush("#14FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 6, 8), Margin = new Thickness(0, 8, 0, 0) };
            var stack = new StackPanel();
            var head = new Grid(); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { FontSize = compact ? 10.5 : 11.5, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
            title.Inlines.Add(new Run(BarLabel(bar)) { FontWeight = FontWeights.SemiBold });
            title.Inlines.Add(new Run("   " + (bar.Cost > 0 || !bar.CostPartial ? Usd(bar.Cost) : "费用未知") + " · " + Compact(bar.Tokens) + " Token" + (bar.Requests > 0 ? " · " + bar.Requests.ToString("N0", CultureInfo.InvariantCulture) + " 次" : "") + (bar.Speed.HasValue && !stacked ? " · " + OutputTiming.Text(bar.Speed) : "")) { Foreground = InkDim });
            head.Children.Add(title);
            var close = new Button { Style = Styled("IconButton"), Content = "", Width = 20, Height = 20, FontSize = 8, ToolTip = "取消固定" };
            close.Click += delegate(object sender, RoutedEventArgs e) { e.Handled = true; chartPinned.Remove(id); rerender(); };
            Grid.SetColumn(close, 1); head.Children.Add(close);
            stack.Children.Add(head);
            double total = Math.Max(1e-9, bar.Parts.Sum(p => p.Cost) > 0 ? bar.Parts.Sum(p => p.Cost) : bar.Parts.Sum(p => p.Tokens));
            bool byCost = bar.Parts.Sum(p => p.Cost) > 0;
            foreach (ChartPart part in bar.Parts.Where(p => p.Tokens > 0 || p.Cost > 0).OrderByDescending(p => p.Cost).ThenByDescending(p => p.Tokens).Take(compact ? 3 : 6)) {
                var row = new Grid { Margin = new Thickness(0, 5, 4, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 50 : 72) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = Brush(part.Color), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
                var name = Label(part.Name, 10.5, InkDim); Grid.SetColumn(name, 1); row.Children.Add(name);
                double share = (byCost ? part.Cost : part.Tokens) / total;
                var track = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0), Background = Brush("#14FFFFFF") };
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, share), GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, 1 - share), GridUnitType.Star) });
                track.Children.Add(new Border { Background = Brush(part.Color), CornerRadius = new CornerRadius(2) });
                Grid.SetColumn(track, 2); row.Children.Add(track);
                var figures = Label(Usd(part.Cost) + " · " + Compact(part.Tokens) + (part.Speed.HasValue ? " · " + OutputTiming.Text(part.Speed) : ""), 10.5, Ink); Tabular(figures); Grid.SetColumn(figures, 3); row.Children.Add(figures);
                stack.Children.Add(row);
            }
            box.Child = stack;
            return box;
        }

        // ── Series from the hourly indexes ────────────────────────────────
        // Bars for [start, end) in local time: hourly when the span is two days or less.
        private List<ChartBar> SeriesFromIndexes(IEnumerable<KeyValuePair<string, LogIndex>> sources, DateTime startLocal, DateTime endLocal, Func<string, string, string> partKey, Func<string, string> partColor, Func<string, bool> provider = null, bool dailyOnly = false) {
            bool hourly = !dailyOnly && (endLocal - startLocal).TotalHours <= 48;
            var bars = new List<ChartBar>();
            DateTime first = hourly ? new DateTime(startLocal.Year, startLocal.Month, startLocal.Day, startLocal.Hour, 0, 0) : startLocal.Date;
            DateTime nowLocal = DateTime.Now;
            for (DateTime t = first; t < endLocal; t = hourly ? t.AddHours(1) : t.AddDays(1)) bars.Add(new ChartBar { Start = t, Hourly = hourly, Current = hourly ? t.Date == nowLocal.Date && t.Hour == nowLocal.Hour : t == nowLocal.Date });
            if (bars.Count == 0) return bars;
            var byStart = bars.ToDictionary(b => b.Start);
            DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, TimeZoneInfo.Local), endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, TimeZoneInfo.Local), nowUtcForWeights = DateTime.UtcNow;
            foreach (var source in sources) {
                if (source.Value == null) continue;
                foreach (HourUsage hour in source.Value.Entries()) {
                    if (provider != null && !provider(hour.Provider)) continue;
                    double w = LogIndex.Weight(hour.Hour, startUtc, endUtc, nowUtcForWeights);
                    if (w <= 0) continue;
                    DateTime local = TimeZoneInfo.ConvertTimeFromUtc(hour.Hour, TimeZoneInfo.Local);
                    DateTime slot = hourly ? new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0) : local.Date;
                    ChartBar bar; if (!byStart.TryGetValue(slot, out bar)) continue;
                    bar.Cost += hour.Cost * w; bar.Tokens += hour.Tokens * w; bar.Requests += hour.Requests * w; if (hour.Unpriced > 0) bar.CostPartial = true;
                    bar.TimedOutput += hour.TimedOutput * w; bar.TimedSeconds += hour.TimedSeconds * w;
                    string key = partKey(source.Key, hour.Model); if (key.Length == 0) continue;
                    ChartPart part = bar.Parts.FirstOrDefault(p => p.Name == key);
                    if (part == null) { part = new ChartPart { Name = key, Color = partColor(key) }; bar.Parts.Add(part); }
                    part.Cost += hour.Cost * w; part.Tokens += hour.Tokens * w; part.TimedOutput += hour.TimedOutput * w; part.TimedSeconds += hour.TimedSeconds * w;
                }
            }
            foreach (ChartBar bar in bars) { bar.Requests = Math.Round(bar.Requests); bar.Parts = bar.Parts.OrderBy(p => p.Name, StringComparer.Ordinal).ToList(); }
            return bars;
        }
        // Usage of some agents over a period: totals, per-part totals (agents or models) and
        // bars. Agents with an hourly index are exact to the hour; otherwise (demo data, a
        // provider without local logs) whole days come from the daily history.
        private sealed class RangeData { public Bucket Total = new Bucket(); public List<KeyValuePair<string, Bucket>> Parts = new List<KeyValuePair<string, Bucket>>(); public List<ChartBar> Bars = new List<ChartBar>(); public bool Hourly, FromLogs; }
        private RangeData RangeUsage(List<string> agents, UsageRange range, bool partsByAgent, string colorProvider) {
            var data = new RangeData(); DateTime a, b; range.Resolve(DateTime.Now, out a, out b);
            var sources = agents.Where(id => usageIndexes.ContainsKey(id)).Select(id => new KeyValuePair<string, LogIndex>(id, usageIndexes[id])).ToList();
            data.FromLogs = sources.Count > 0;
            if (!data.FromLogs) { a = a.Date; if (b.TimeOfDay > TimeSpan.Zero || b.Date == a) b = b.Date.AddDays(1); }
            var parts = new Dictionary<string, Bucket>();
            Func<string, string, string> key = (agent, model) => partsByAgent ? agent : model;
            DateTime aUtc = TimeZoneInfo.ConvertTimeToUtc(a, TimeZoneInfo.Local), bUtc = TimeZoneInfo.ConvertTimeToUtc(b, TimeZoneInfo.Local), nowUtc = DateTime.UtcNow;
            foreach (var source in sources) foreach (HourUsage hour in source.Value.Entries()) {
                // The running hour counts in full up to now (LogIndex.Weight), not by the share of the clock hour.
                double w = LogIndex.Weight(hour.Hour, aUtc, bUtc, nowUtc); if (w <= 0) continue;
                string k = key(source.Key, hour.Model); if (k.Length == 0) k = "（未标注模型）";
                Bucket p; if (!parts.TryGetValue(k, out p)) parts[k] = p = new Bucket();
                foreach (Bucket t in new[] { p, data.Total }) { t.I += hour.Input * w; t.C += hour.Cached * w; t.W += hour.CacheWrite * w; t.O += hour.Output * w; t.X += hour.Other * w; t.R += hour.Requests * w; t.D += hour.Cost * w; t.U += hour.Unpriced * w; t.TN += hour.TimedRequests * w; t.TO += hour.TimedOutput * w; t.TS += hour.TimedSeconds * w; }
            }
            // Days from the history for agents without an hourly index.
            var dayRows = history.Days.Where(d => agents.Contains(d.Agent) && (!data.FromLogs || !usageIndexes.ContainsKey(d.Agent))).ToList();
            foreach (DayUsage row in dayRows) {
                DateTime day; if (!DateTime.TryParseExact(row.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) || day < a.Date || day >= b) continue;
                data.Total.I += row.InputTokens; data.Total.C += row.CachedTokens; data.Total.W += row.CacheCreationTokens; data.Total.O += row.OutputTokens; data.Total.X += Math.Max(0, row.Tokens - row.InputTokens - row.CachedTokens - row.CacheCreationTokens - row.OutputTokens); data.Total.R += row.Requests; data.Total.D += row.Cost; if (!row.CostKnown) data.Total.U += row.Tokens; data.Total.TN += row.TimedRequests; data.Total.TO += row.TimedOutput; data.Total.TS += row.TimedSeconds;
                if (partsByAgent) { Bucket p; if (!parts.TryGetValue(row.Agent, out p)) parts[row.Agent] = p = new Bucket(); p.I += row.Tokens; p.D += row.Cost; p.R += row.Requests; p.TN += row.TimedRequests; p.TO += row.TimedOutput; p.TS += row.TimedSeconds; }
                else foreach (ModelUsage m in ModelsOf(row)) { Bucket p; if (!parts.TryGetValue(m.Model, out p)) parts[m.Model] = p = new Bucket(); p.I += m.Tokens; p.D += m.Cost; p.TO += m.TimedOutput; p.TS += m.TimedSeconds; }
            }
            data.Parts = parts.OrderByDescending(p => p.Value.D).ThenByDescending(p => p.Value.Tokens()).ToList();
            List<string> order = data.Parts.Select(p => p.Key).ToList();
            Func<string, string> color = name => partsByAgent ? ProviderCatalog.Color(name) : ModelColor(colorProvider, name, order);
            data.Bars = SeriesFromIndexes(sources, a, b, (agent, model) => { string k = key(agent, model); return k.Length == 0 ? "（未标注模型）" : k; }, color, null, !data.FromLogs);
            data.Hourly = data.Bars.Count > 0 && data.Bars[0].Hourly;
            if (!data.Hourly) {
                var byDay = data.Bars.ToDictionary(x => x.Start.Date);
                foreach (DayUsage row in dayRows) {
                    DateTime day; ChartBar bar; if (!DateTime.TryParseExact(row.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) || !byDay.TryGetValue(day, out bar)) continue;
                    bar.Cost += row.Cost; bar.Tokens += row.Tokens; bar.Requests += row.Requests; if (!row.CostKnown) bar.CostPartial = true; bar.TimedOutput += row.TimedOutput; bar.TimedSeconds += row.TimedSeconds;
                    if (partsByAgent) bar.Parts.Add(new ChartPart { Name = row.Agent, Color = color(row.Agent), Cost = row.Cost, Tokens = row.Tokens, TimedOutput = row.TimedOutput, TimedSeconds = row.TimedSeconds });
                    else foreach (ModelUsage m in ModelsOf(row)) bar.Parts.Add(new ChartPart { Name = m.Model, Color = color(m.Model), Cost = m.Cost, Tokens = m.Tokens, TimedOutput = m.TimedOutput, TimedSeconds = m.TimedSeconds });
                }
                foreach (ChartBar bar in data.Bars) bar.Parts = bar.Parts.OrderBy(p => partsByAgent ? Array.IndexOf(ProviderCatalog.Ids, p.Name) : order.IndexOf(p.Name)).ToList();
            }
            data.Total.R = Math.Round(data.Total.R);
            return data;
        }
        // A day's models; when the row carries no per-model cost, its cost is split by tokens.
        private static List<ModelUsage> ModelsOf(DayUsage row) {
            var models = (row.Models ?? new List<ModelUsage>()).Where(m => m.Tokens > 0 || m.Cost > 0).ToList();
            if (models.Count == 0) return row.Tokens > 0 || row.Cost > 0 ? new List<ModelUsage> { new ModelUsage { Model = "（未标注模型）", Tokens = row.Tokens, Cost = row.Cost, TimedOutput = row.TimedOutput, TimedSeconds = row.TimedSeconds } } : models;
            double tokens = models.Sum(m => m.Tokens);
            if (models.Sum(m => m.Cost) <= 0 && row.Cost > 0 && tokens > 0) return models.Select(m => new ModelUsage { Model = m.Model, Tokens = m.Tokens, Cost = row.Cost * m.Tokens / tokens, TimedOutput = m.TimedOutput, TimedSeconds = m.TimedSeconds }).ToList();
            return models;
        }
        // Model colours on a provider page: the provider hue, then fixed companions.
        private static readonly string[] ModelPalette = { "#8FD1E8", "#F2B36B", "#7AD3A8", "#D6C1F9", "#E58FD0", "#F4C95D", "#A9B4C6" };
        private static string ModelColor(string provider, string model, List<string> order) {
            int i = order.IndexOf(model); if (i <= 0) return ProviderCatalog.Color(provider);
            return ModelPalette[(i - 1) % ModelPalette.Length];
        }
    }
}
