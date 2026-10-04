using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NepFix
{
    /// Перевод интерфейса. Исходные строки мода русские; в английском режиме они переводятся при выводе
    /// по словарю LocData: точные совпадения, шаблоны с {0} для строк с числами и составные строки по частям.
    /// Внутренняя логика и лог остаются на русском, переводится только то, что видит игрок.
    internal static class Loc
    {
        public static bool En { get; private set; }
        static readonly Dictionary<string, string> exact = new();
        class Tpl { public Regex Full, Seg; public string En, Key; public int Lit; }
        static readonly List<Tpl> full = new(), seg = new();
        static readonly Dictionary<string, string> cache = new();
        static bool built;

        public static void Init(string lang)
        {
            lang = (lang ?? "auto").Trim().ToLowerInvariant();
            if (lang == "ru") En = false;
            else if (lang == "en") En = true;
            else
            {
                string two = "en";
                try { two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName; } catch { }
                En = !(two == "ru" || two == "uk" || two == "be" || two == "kk");
            }
            cache.Clear();
        }

        static bool Cyr(string s) { foreach (char c in s) if (c >= 'А' && c <= 'я' || c == 'ё' || c == 'Ё') return true; return false; }

        static void Build()
        {
            built = true;
            var d = LocData.Pairs;
            for (int i = 0; i + 1 < d.Length; i += 2)
            {
                string ru = d[i], en = d[i + 1];
                if (ru.IndexOf("{0}", StringComparison.Ordinal) < 0) exact[ru] = en;
                var parts = Regex.Split(ru, @"\{\d+\}");
                var holes = Regex.Matches(ru, @"\{\d+\}");
                int lit = 0; string key = "";
                foreach (var p in parts) { lit += p.Length; if (p.Length > key.Length) key = p; }
                var t = new Tpl { En = en, Key = key, Lit = lit };
                if (holes.Count > 0)
                {
                    var sb = new StringBuilder("^");
                    for (int k = 0; k < parts.Length; k++)
                    {
                        sb.Append(Regex.Escape(parts[k]));
                        if (k < holes.Count) sb.Append("(.*?)");
                    }
                    sb.Append('$');
                    t.Full = new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
                    full.Add(t);
                }
                if (lit >= 8 && Cyr(key))
                {
                    var sb = new StringBuilder(@"(?<![А-Яа-яЁё])");
                    for (int k = 0; k < parts.Length; k++)
                    {
                        sb.Append(Regex.Escape(parts[k]));
                        if (k < holes.Count)
                        {
                            if (k == 0 && parts[0].Length == 0) sb.Append(@"(\S+)");
                            else if (k == holes.Count - 1 && parts[k + 1].Length == 0) sb.Append(@"(.*?)(?=,\s|\.\s|\.$|;|\)|$)");
                            else sb.Append("(.*?)");
                        }
                    }
                    if (parts[parts.Length - 1].Length > 0) sb.Append(@"(?![А-Яа-яЁё])");
                    t.Seg = new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
                    seg.Add(t);
                }
            }
            full.Sort((a, b) => b.Lit.CompareTo(a.Lit));
            seg.Sort((a, b) => b.Lit.CompareTo(a.Lit));
        }

        /// Строка интерфейса на выбранном языке.
        public static string T(string s)
        {
            if (!En || string.IsNullOrEmpty(s)) return s;
            if (!Cyr(s)) return s.IndexOf(',') >= 0 ? decComma.Replace(s, ".") : s;
            if (!built) Build();
            lock (cache)
            {
                if (cache.TryGetValue(s, out var r)) return r;
                try { r = Translate(s, 0); } catch { r = s; }
                if (cache.Count > 4000) cache.Clear();
                cache[s] = r;
                return r;
            }
        }

        static readonly Regex decComma = new(@"(?<=\d),(?=\d)", RegexOptions.CultureInvariant);

        static string Translate(string s, int depth)
        {
            string r = TranslateCore(s, depth);
            return depth == 0 ? decComma.Replace(r, ".") : r;
        }

        static string TranslateCore(string s, int depth)
        {
            if (exact.TryGetValue(s, out var e)) return e;
            if (depth > 3) return s;
            string trimmed = s.Trim();
            if (trimmed.Length != s.Length && exact.TryGetValue(trimmed, out e)) return s.Replace(trimmed, e);
            // сначала известные длинные фразы и шаблоны внутри строки: составные строки собираются из нескольких
            string r = s;
            foreach (var t in seg)
            {
                if (r.IndexOf(t.Key, StringComparison.Ordinal) < 0) continue;
                r = t.Seg.Replace(r, m => Fill(t, m, depth));
                if (!Cyr(r)) return r;
            }
            if (exact.TryGetValue(r, out e)) return e;
            foreach (var t in full)
            {
                if (t.Key.Length > 0 && r.IndexOf(t.Key, StringComparison.Ordinal) < 0) continue;
                var m = t.Full.Match(r);
                if (m.Success) return Fill(t, m, depth);
            }
            return r;
        }

        static string Fill(Tpl t, Match m, int depth)
        {
            var args = new object[m.Groups.Count - 1];
            for (int i = 1; i < m.Groups.Count; i++)
            {
                string g = m.Groups[i].Value;
                args[i - 1] = Cyr(g) ? TranslateCore(g, depth + 1) : g;
            }
            try { return string.Format(t.En, args); } catch { return t.En; }
        }
    }
}
