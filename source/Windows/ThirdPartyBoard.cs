using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace CodeUsageMonit {
    // The third-party page as a dashboard: for a chosen period, which relay carried how
    // much (tokens, API-equivalent cost, requests), how fast it answered, and what each
    // relay was used for. Relays set their own limits and do not publish them, so there is
    // deliberately no quota meter here.
    public sealed partial class MonitorPanel {
        private string boardMetric = "tokens";   // ranking: tokens / cost / requests / speed
        private string boardOpen;                 // endpoint whose details are unfolded

        private void RenderThirdParty() {
            UsageRange range = RangeFor(ProviderCatalog.ThirdParty);
            DateTime a, b; range.Resolve(DateTime.Now, out a, out b);
            ThirdPartyPeriod period = ThirdPartyReport.Period(codexLogs, claudeLogs, endpointLog ?? new EndpointLog(), a, b, DateTime.UtcNow, TimeZoneInfo.Local);

            // Head: title and period, then the endpoint each client is configured for now.
            var intro = new StackPanel();
            var head = new Grid(); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(ThirdPartyHeader(true));
            var picker = RangeButton(ProviderCatalog.ThirdParty, Render, false); picker.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(picker, 1); head.Children.Add(picker);
            intro.Children.Add(head);
            intro.Children.Add(new TextBlock { Text = "本机 Claude Code / Codex 经第三方接口（中转站）发出的请求：哪个接口用了多少、按官方价折合多少、输出有多快。各服务商的周、月限额不公开，这里只统计用量。", FontSize = 11, Foreground = InkDim, TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 10, 0, 4) });
            foreach (var current in new[] { new { App = "Claude Code", Mark = thirdParty.ClaudeNow }, new { App = "Codex", Mark = thirdParty.CodexNow } }) {
                var row = Row(); row.Margin = new Thickness(0, 8, 0, 0);
                var value = Label(CurrentLabel(current.Mark), 11.5, current.Mark != null && !current.Mark.Official ? Ink : InkDim);
                value.MaxWidth = 250; value.ToolTip = current.Mark != null && current.Mark.Key.Length > 0 ? "密钥指纹 " + current.Mark.Key + "（只保存指纹，不保存密钥）" : null;
                AddRow(row, Label("当前 " + current.App, 11.5, InkDim), value); intro.Children.Add(row);
            }
            if (thirdParty.ClaudeUnattributed > 0) {
                DateTime since; string when = LogIndex.Parse(thirdParty.ClaudeTrackedSince, out since) ? since.ToLocalTime().ToString("M月d日 HH:mm") : "开始记录";
                intro.Children.Add(Notice("Claude Code 的日志不记录接口地址，本软件从 " + when + " 起记录切换；在此之前带官方 request-id 的 " + Compact(thirdParty.ClaudeUnattributed) + " Token 无法判断是官方还是透传型中转，未计入任何接口。", false));
            }
            body.Children.Add(Card(intro));
            if (period.Endpoints.Count == 0) return;

            // Totals over the period, and the bars split by endpoint.
            var totals = new StackPanel();
            int active = period.Endpoints.Count(e => e.Total.Tokens() > 0);
            var caption = Row(); AddRow(caption, BoardTitle("合计 · " + range.Label()), Label(active + " 个接口有用量", 10.5, InkFaint)); totals.Children.Add(caption);
            Bucket all = period.Total;
            var figures = new UniformGrid { Columns = 4, Margin = new Thickness(0, 10, 0, 0) };
            figures.Children.Add(BigFigure("官方价参考", all.Tokens() > 0 ? (all.U > 0 ? "≥" : "≈") + Usd(all.D, all.D < 100) : "—", Usd(all.D) + "\n" + CostTip));
            figures.Children.Add(BigFigure("Token", Compact(all.Tokens()), "新增输入 + 输出 + 缓存命中 + 缓存写入"));
            figures.Children.Add(BigFigure("请求", all.R > 0 ? all.R.ToString("N0", CultureInfo.InvariantCulture) : "—", "本机日志里经第三方接口的模型请求次数"));
            figures.Children.Add(BigFigure("输出速度", OutputTiming.Text(all.Speed()), SpeedTip(all)));
            totals.Children.Add(figures);
            if (all.Tokens() > 0) {
                totals.Children.Add(UsageChart("usage|" + ProviderCatalog.ThirdParty, (period.Hourly ? "每小时" : "每日") + " · 按接口堆叠", period.Bars, 64, true, false, Render, true, "#5CC8E0"));
                var legend = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (EndpointPeriod e in period.Endpoints.Where(x => x.Total.Tokens() > 0)) {
                    var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 4) };
                    item.Children.Add(Dot(e.Color)); item.Children.Add(Label(e.Label, 10.5, InkDim));
                    legend.Children.Add(item);
                }
                totals.Children.Add(legend);
            }
            body.Children.Add(Card(totals));

            body.Children.Add(Card(Ranking(period)));
            UIElement speeds = SpeedByModel(period);
            if (speeds != null) body.Children.Add(Card(speeds));
        }

        private const string CostTip = "按官方 API 单价逐次请求估算的参考值。中转站按自己的倍率或套餐扣费，实际花费以服务商后台为准。";
        private static string SpeedTip(Bucket b) {
            return (b.Speed().HasValue ? Math.Round(b.TN).ToString("N0", CultureInfo.InvariantCulture) + " 次请求计时，" + Compact(b.TO) + " 输出 Token ÷ " + Duration(b.TS) + "\n" : "这一时间段没有可计时的请求\n") + OutputTiming.Definition;
        }
        private static TextBlock BoardTitle(string text) { var t = Label(text, 12, Ink); t.FontWeight = FontWeights.SemiBold; return t; }
        private static Border Dot(string color) { return new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = Brush(color), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }; }
        private static double MetricOf(Bucket b, string metric) { return metric == "cost" ? b.D : metric == "requests" ? b.R : metric == "speed" ? (b.Speed() ?? 0) : b.Tokens(); }
        private static string MetricText(Bucket b, string metric) {
            if (metric == "cost") return b.Tokens() > 0 ? (b.U > 0 ? "≥" : "≈") + Usd(b.D) : "—";
            if (metric == "requests") return b.R > 0 ? Math.Round(b.R).ToString("N0", CultureInfo.InvariantCulture) + " 次" : "—";
            if (metric == "speed") return OutputTiming.Text(b.Speed());
            return b.Tokens() > 0 ? Compact(b.Tokens()) : "—";
        }

        // Endpoints side by side, ranked by the chosen measure; click one for its details.
        private UIElement Ranking(ThirdPartyPeriod period) {
            var stack = new StackPanel();
            var head = Row();
            var chips = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (string[] option in new[] { new[] { "tokens", "Token" }, new[] { "cost", "费用" }, new[] { "requests", "请求" }, new[] { "speed", "速度" } }) {
                string code = option[0]; bool on = boardMetric == code;
                var chip = new Button { Content = Label(option[1], 10, on ? Ink : InkFaint), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(2, 0, 0, 0), Background = Brush(on ? "#1CFFFFFF" : "#00FFFFFF"), ToolTip = "按" + option[1] + "排序" };
                System.Windows.Automation.AutomationProperties.SetName(chip, "接口对比按" + option[1]);
                chip.Click += delegate { boardMetric = code; Render(); };
                chips.Children.Add(chip);
            }
            AddRow(head, BoardTitle("接口对比"), chips); stack.Children.Add(head);
            List<EndpointPeriod> order = period.Endpoints.OrderByDescending(e => MetricOf(e.Total, boardMetric)).ThenByDescending(e => e.Total.Tokens()).ToList();
            double max = Math.Max(1e-9, order.Max(e => MetricOf(e.Total, boardMetric)));
            double sum = boardMetric == "speed" ? 0 : order.Sum(e => MetricOf(e.Total, boardMetric));
            foreach (EndpointPeriod e in order) {
                EndpointPeriod captured = e; bool open = boardOpen == e.Id;
                var row = new StackPanel();
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(Dot(e.Color));
                var title = new TextBlock { FontSize = 12, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                title.Inlines.Add(e.Label);
                if (e.Current) title.Inlines.Add(new Run("  使用中") { FontSize = 10, Foreground = AccentBrush });
                Grid.SetColumn(title, 1); line.Children.Add(title);
                double v = MetricOf(e.Total, boardMetric);
                var value = Label(MetricText(e.Total, boardMetric) + (sum > 0 && v > 0 ? "  " + (v / sum * 100).ToString(v / sum < .1 ? "0.0" : "0", CultureInfo.InvariantCulture) + "%" : ""), 11.5, Ink); Tabular(value); value.Margin = new Thickness(8, 0, 6, 0);
                Grid.SetColumn(value, 2); line.Children.Add(value);
                var chevron = new TextBlock { Text = open ? "" : "", FontFamily = IconFont, FontSize = 9, Foreground = InkFaint, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(chevron, 3); line.Children.Add(chevron);
                row.Children.Add(line);
                var track = new Grid { Height = 5, Margin = new Thickness(14, 6, 0, 0), Background = Brush("#12FFFFFF") };
                double share = v / max;
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, share), GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, 1 - share), GridUnitType.Star) });
                if (v > 0) track.Children.Add(new Border { Background = Brush(e.Color), CornerRadius = new CornerRadius(2) });
                row.Children.Add(track);
                var sub = Label(AppName(e.App) + " · " + (e.Total.Tokens() > 0 ? Compact(e.Total.Tokens()) + " Token · " + MetricText(e.Total, "cost") + " · " + MetricText(e.Total, "requests") + (e.Total.Speed().HasValue ? " · " + OutputTiming.Text(e.Total.Speed()) : "") : "这段时间没有用量"), 10.5, InkFaint);
                sub.Margin = new Thickness(14, 4, 0, 0); row.Children.Add(sub);
                var hit = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(7, 6, 7, 6), Margin = new Thickness(-7, 6, -7, 0), ToolTip = open ? "收起" : "展开模型和明细" };
                System.Windows.Automation.AutomationProperties.SetName(hit, e.Label);
                hit.Click += delegate { boardOpen = boardOpen == captured.Id ? null : captured.Id; Render(); };
                stack.Children.Add(hit);
                if (open) stack.Children.Add(EndpointDetail(e));
            }
            var foot = Label("点接口展开模型和明细 · 官方价参考不是中转站的实际扣费 · 限额未知", 10.5, InkFaint); foot.TextWrapping = TextWrapping.Wrap;
            foot.Margin = new Thickness(0, 12, 0, 0); stack.Children.Add(foot);
            return stack;
        }

        // One endpoint unfolded: where it is, token composition, its models.
        private UIElement EndpointDetail(EndpointPeriod e) {
            var box = new StackPanel { Margin = new Thickness(14, 2, 0, 8) };
            string origin = e.Host.Length > 0 ? e.Host : e.Key == ClaudeLogs.NoRequestId ? "记录缺少官方 request-id，无法确定地址" : "本软件开始记录之前的第三方会话";
            var where = Label(AppName(e.App) + " · " + origin + (e.Key.Length > 0 && e.Host.Length > 0 ? " · 密钥 …" + e.Key : "") + " · " + (e.LastUsed.Length == 0 ? "近一个月未使用" : "最后使用 " + Ago(e.LastUsed)), 10.5, InkDim);
            where.TextWrapping = TextWrapping.Wrap;
            if (e.Key == ClaudeLogs.NoRequestId) where.ToolTip = "Anthropic 官方接口的每次响应都带 request-id，Claude Code 会把它写进日志。这些记录没有 request-id，通常是经中转站转发（中转站往往不透传这个响应头），但无法确定是哪一家。";
            box.Children.Add(where);
            if (e.Total.Tokens() <= 0) return box;
            UIElement composition = Composition(e.Total.I, e.Total.O, e.Total.C, e.Total.W, "Token 构成");
            if (composition != null) box.Children.Add(composition);
            List<KeyValuePair<string, Bucket>> models = e.Models.Where(p => p.Value.Tokens() > 0).OrderByDescending(p => p.Value.Tokens()).ToList();
            if (models.Count == 0) return box;
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var caption = Row(); AddRow(caption, Label("模型", 10.5, InkFaint), Label("Token · 官方价参考 · 速度", 10.5, InkFaint)); list.Children.Add(caption);
            double total = Math.Max(1, models.Sum(p => p.Value.Tokens()));
            foreach (var pair in models.Take(6)) {
                var line = new Grid { Margin = new Thickness(0, 6, 0, 0), ToolTip = pair.Key + "\n" + Compact(pair.Value.Tokens()) + " Token · " + MetricText(pair.Value, "cost") + " · " + MetricText(pair.Value, "requests") + (pair.Value.Speed().HasValue ? "\n输出速度 " + OutputTiming.Text(pair.Value.Speed()) + "（" + Math.Round(pair.Value.TN).ToString("N0", CultureInfo.InvariantCulture) + " 次请求计时）" : "") };
                line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(Label(pair.Key, 11, Ink));
                double share = pair.Value.Tokens() / total;
                var track = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0), Background = Brush("#14FFFFFF") };
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, share), GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, 1 - share), GridUnitType.Star) });
                track.Children.Add(new Border { Background = Brush(e.Color), CornerRadius = new CornerRadius(2) });
                Grid.SetColumn(track, 1); line.Children.Add(track);
                var value = Label(Compact(pair.Value.Tokens()) + " · " + MetricText(pair.Value, "cost") + " · " + OutputTiming.Text(pair.Value.Speed()), 10.5, InkDim); Tabular(value);
                Grid.SetColumn(value, 2); line.Children.Add(value);
                list.Children.Add(line);
            }
            if (models.Count > 6) { var more = Label("另有 " + (models.Count - 6) + " 个模型", 10.5, InkFaint); more.Margin = new Thickness(0, 6, 0, 0); list.Children.Add(more); }
            box.Children.Add(list);
            return box;
        }

        // Output speed of the same model through different endpoints — the fair comparison,
        // since models differ far more from each other than relays do.
        private UIElement SpeedByModel(ThirdPartyPeriod period) {
            var groups = period.Endpoints
                .SelectMany(e => e.Models.Where(p => p.Value.Speed().HasValue).Select(p => new { Endpoint = e, Model = p.Key, Usage = p.Value }))
                .GroupBy(x => x.Model).OrderByDescending(g => g.Sum(x => x.Usage.TN)).Take(5).ToList();
            if (groups.Count == 0) return null;
            var stack = new StackPanel();
            var head = Row(); var title = BoardTitle("输出速度 · 同一模型在各接口"); title.ToolTip = OutputTiming.Definition;
            AddRow(head, title, Label("t/s · 计时请求数", 10.5, InkFaint)); stack.Children.Add(head);
            foreach (var group in groups) {
                var name = Label(group.Key, 11, InkDim); name.Margin = new Thickness(0, 12, 0, 2); stack.Children.Add(name);
                double max = Math.Max(1e-9, group.Max(x => x.Usage.Speed() ?? 0));
                foreach (var x in group.OrderByDescending(x => x.Usage.Speed() ?? 0)) {
                    var line = new Grid { Margin = new Thickness(0, 4, 0, 0), ToolTip = x.Endpoint.Label + " · " + group.Key + "\n" + SpeedTip(x.Usage) };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                    var who = new StackPanel { Orientation = Orientation.Horizontal }; who.Children.Add(Dot(x.Endpoint.Color)); who.Children.Add(Label(x.Endpoint.Label, 11, Ink));
                    line.Children.Add(who);
                    double share = (x.Usage.Speed() ?? 0) / max;
                    var track = new Grid { Height = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), Background = Brush("#12FFFFFF") };
                    track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, share), GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, 1 - share), GridUnitType.Star) });
                    track.Children.Add(new Border { Background = Brush(x.Endpoint.Color), CornerRadius = new CornerRadius(2) });
                    Grid.SetColumn(track, 1); line.Children.Add(track);
                    var value = Label(OutputTiming.Text(x.Usage.Speed()) + " · " + Math.Round(x.Usage.TN).ToString("N0", CultureInfo.InvariantCulture) + " 次", 10.5, InkDim); Tabular(value); value.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetColumn(value, 2); line.Children.Add(value);
                    stack.Children.Add(line);
                }
            }
            var foot = Label("计时请求少时数值波动大；速度含首字延迟和网络，详见悬停说明", 10.5, InkFaint); foot.TextWrapping = TextWrapping.Wrap; foot.Margin = new Thickness(0, 12, 0, 0); stack.Children.Add(foot);
            return stack;
        }
    }
}
