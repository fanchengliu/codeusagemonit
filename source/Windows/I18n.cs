using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace CodeUsageMonit {
    // Interface language. The Chinese text in the code is the key and English comes from
    // I18nTable (gettext-style). Text the code builds from pieces ("近 " + n + " 天") is
    // translated piece by piece: every table key of two or more characters found in it is
    // replaced, longest first. Pages are built in Chinese and Localize translates a finished
    // element tree in place, remembering each original so switching back restores it.
    // Stored data (quota labels in the cache, logs) stays Chinese; only what is shown changes.
    public static class I18n {
        private static string language = "zh";
        // "zh" or "en"; settings store "auto" | "zh" | "en".
        public static string Language { get { return language; } }
        public static bool English { get { return language == "en"; } }
        public static void Use(string setting) { language = Resolve(setting); if (English) everEnglish = true; }
        // Chinese trees need no pass until English has been shown once (then they are restored).
        private static bool everEnglish;
        public static string Resolve(string setting) {
            if (setting == "zh" || setting == "en") return setting;
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
        }
        // Culture for dates and month / weekday names.
        public static CultureInfo Culture { get { return CultureInfo.GetCultureInfo(English ? "en-US" : "zh-CN"); } }

        public static string T(string zh) {
            if (!English || String.IsNullOrEmpty(zh) || !HasChinese(zh)) return zh;
            string en; if (I18nTable.En.TryGetValue(zh, out en)) return en;
            lock (cache) {
                if (cache.TryGetValue(zh, out en)) return en;
                en = zh;
                foreach (string key in Fragments) if (en.IndexOf(key, StringComparison.Ordinal) >= 0) en = en.Replace(key, I18nTable.En[key]);
                if (cache.Count > 4000) cache.Clear();
                cache[zh] = en;
                return en;
            }
        }
        public static string T(string zh, params object[] args) {
            try { return String.Format(CultureInfo.InvariantCulture, T(zh), args); } catch (FormatException) { return String.Format(CultureInfo.InvariantCulture, zh, args); }
        }
        public static bool HasChinese(string s) { foreach (char c in s) if (c >= '㐀' && c <= '鿿') return true; return false; }
        private static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string[] fragments;
        private static string[] Fragments {
            get { return fragments ?? (fragments = I18nTable.En.Keys.Where(k => k.Length >= 2 && HasChinese(k)).OrderByDescending(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).ToArray()); }
        }

        // ── Translating a built element tree ──────────────────────────────
        // Per element and property: the text it was built with and what was shown last.
        private sealed class Shown { public string Source, Text; }
        private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Shown>> shown = new ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Shown>>();

        public static void Localize(DependencyObject root) {
            if (root == null || (!English && !everEnglish)) return;
            var visited = new HashSet<DependencyObject>();
            Visit(root, visited);
        }
        private static void Visit(DependencyObject node, HashSet<DependencyObject> visited) {
            if (node == null || !visited.Add(node)) return;
            var text = node as TextBlock;
            // Text set directly leaves Inlines empty; built-up text is a list of runs.
            // (A label followed by added runs, e.g. a bold percentage, keeps its runs.)
            if (text != null && text.Inlines.Count <= 1 && text.ReadLocalValue(TextBlock.TextProperty) != DependencyProperty.UnsetValue) Swap(text, TextBlock.TextProperty);
            else if (text != null) {
                foreach (Inline inline in text.Inlines.ToList()) {
                    var run = inline as Run; if (run != null) Swap(run, Run.TextProperty);
                    var span = inline as Span; if (span != null) foreach (Inline inner in span.Inlines) { var r = inner as Run; if (r != null) Swap(r, Run.TextProperty); }
                }
            }
            var window = node as Window; if (window != null) Swap(window, Window.TitleProperty);
            var content = node as ContentControl; if (content != null && content.Content is string) Swap(content, ContentControl.ContentProperty);
            var header = node as HeaderedItemsControl; if (header != null && header.Header is string) Swap(header, HeaderedItemsControl.HeaderProperty);
            var element = node as FrameworkElement;
            if (element != null) {
                if (element.ToolTip is string) Swap(element, FrameworkElement.ToolTipProperty);
                else { var tip = element.ToolTip as DependencyObject; if (tip != null) Visit(tip, visited); }
                if (element.ContextMenu != null) Visit(element.ContextMenu, visited);
            }
            if (node is UIElement || node is ContentElement) {
                string name = AutomationProperties.GetName(node);
                if (!String.IsNullOrEmpty(name)) Swap(node, AutomationProperties.NameProperty);
            }
            if (text != null) return;
            foreach (object child in LogicalTreeHelper.GetChildren(node)) { var d = child as DependencyObject; if (d != null) Visit(d, visited); }
        }
        private static void Swap(DependencyObject d, DependencyProperty property) {
            string current = d.GetValue(property) as string; if (current == null) return;
            Dictionary<DependencyProperty, Shown> map = shown.GetOrCreateValue(d);
            Shown last;
            // Text changed by the code since the last pass is the new original.
            if (!map.TryGetValue(property, out last) || last.Text != current) last = new Shown { Source = current };
            string wanted = T(last.Source);
            if (wanted != current) d.SetValue(property, wanted);
            last.Text = wanted; map[property] = last;
        }
    }
}
