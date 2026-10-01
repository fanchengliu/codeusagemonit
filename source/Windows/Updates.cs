using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // Settings › About: is there a newer release on GitHub? One GET to the public releases
    // API (no account, nothing uploaded); at most once a day when automatic checks are on.
    public sealed class UpdateState { public string Checked = "", Latest = "", Url = "", Published = "", Error = ""; }
    public static class Updates {
        public const string Api = "https://api.github.com/repos/fanchengliu/codeusagemonit/releases/latest";
        public const string Releases = "https://github.com/fanchengliu/codeusagemonit/releases/latest";

        public static UpdateState State() { return Store.Read<UpdateState>("update-check.json"); }
        public static bool Newer(UpdateState state) { return state != null && state.Latest.Length > 0 && Compare(state.Latest, AppInfo.Version) > 0; }
        public static bool Due(AppConfig config, DateTime nowUtc) {
            if (!config.UpdateCheck) return false;
            DateTime last; return !LogIndex.Parse(State().Checked, out last) || nowUtc - last >= TimeSpan.FromHours(24);
        }
        public static async Task<UpdateState> Check(AppConfig config, DateTime nowUtc) {
            var state = new UpdateState { Checked = nowUtc.ToString("o") };
            try {
                object release;
                using (var service = new ProviderService(config)) release = await service.GetJson(Api).ConfigureAwait(false);
                string tag = J.Str(release, "tag_name").Trim();
                if (tag.Length == 0) throw new ProviderException(I18n.T("GitHub 没有返回版本号。"), "error");
                state.Latest = tag.TrimStart('v', 'V');
                string url = J.Str(release, "html_url"); state.Url = url.StartsWith("https://github.com/fanchengliu/codeusagemonit/", StringComparison.Ordinal) ? url : Releases;
                state.Published = J.Str(release, "published_at");
            } catch (Exception e) {
                // Keep the last known release.
                UpdateState previous = State(); state.Latest = previous.Latest; state.Url = previous.Url; state.Published = previous.Published;
                state.Error = e is ProviderException ? e.Message : I18n.T("无法连接 GitHub，请检查网络或代理设置。");
            }
            try { Store.Write("update-check.json", state); } catch { }
            return state;
        }
        // "1.10.0" > "1.9.2"; missing parts count as 0; anything after "-" is ignored.
        public static int Compare(string a, string b) {
            int[] x = Parts(a), y = Parts(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++) {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }
        private static int[] Parts(string version) {
            string core = (version ?? "").Trim().TrimStart('v', 'V').Split('-', '+')[0];
            return core.Split('.').Select(s => { int n; return Int32.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n) ? n : 0; }).ToArray();
        }
    }
}
