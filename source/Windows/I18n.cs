using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeUsageMonit {
    // Interface language. The Chinese text in the code is the key and English comes from
    // I18nTable (gettext-style), so a string without a translation simply stays Chinese.
    // Text with numbers or names uses {0}-style placeholders: T("近 {0} 天", days).
    public static class I18n {
        private static string language = "zh";
        // "zh" or "en"; settings store "auto" | "zh" | "en".
        public static string Language { get { return language; } }
        public static bool English { get { return language == "en"; } }
        public static void Use(string setting) { language = Resolve(setting); }
        public static string Resolve(string setting) {
            if (setting == "zh" || setting == "en") return setting;
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
        }
        public static string T(string zh) {
            if (!English || zh == null) return zh;
            string en; return I18nTable.En.TryGetValue(zh, out en) ? en : zh;
        }
        public static string T(string zh, params object[] args) {
            try { return String.Format(CultureInfo.InvariantCulture, T(zh), args); } catch (FormatException) { return String.Format(CultureInfo.InvariantCulture, zh, args); }
        }
        // Culture for dates and month / weekday names.
        public static CultureInfo Culture { get { return CultureInfo.GetCultureInfo(English ? "en-US" : "zh-CN"); } }
    }
}
