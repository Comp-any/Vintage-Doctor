using System;
using System.Collections.Generic;
using System.Linq;
using VintageDoctor.API;

namespace VintageDoctor
{
    public static partial class Checks
    {
        // ---------------------------------------------------------------- formatting tags in texts (1.5.0)

        /// <summary>One error of the log about a text's tags, and whose text it most likely was.</summary>
        sealed class TagSuspect
        {
            public string Message, Shown;
            public int Times;
            /// <summary>The texts it fits: word for word (the same error at the same place), or by kind only (the place moved).</summary>
            public List<BrokenText> Exact = new List<BrokenText>(), Alike = new List<BrokenText>();
            public string Mod;
            public bool Sure;
        }

        static string TagOwner(DoctorContext x, string domain)
        {
            if (string.IsNullOrEmpty(domain) || ModSnapshot.IsGameMod(domain)) return DoctorReport.GameSection;
            return x.Ev?.Mods?.Get(domain)?.Id ?? domain;
        }

        /// <summary>What is wrong with a text, in words, with a piece of it around the place.</summary>
        /// <summary>A text's name: its lang key, or "the help of the command .doctor".</summary>
        static string TagName(DoctorContext x, BrokenText b) => b.Command != null ? x.T("tags-command", b.Command) : b.Key;

        static string TagWhat(DoctorContext x, BrokenText b) => b.Message != null ? TagWhat(x, b.Message, b.Text) : x.T("tags-what-open", string.Join("> <", b.Unclosed)) + ": " + TextTags.Around(b.Text, b.Text.IndexOf("<" + b.Unclosed[0], StringComparison.Ordinal));

        static string TagWhat(DoctorContext x, string message, string text)
        {
            var (what, a, b) = TextTags.Describe(message);
            int pos = TextTags.Position(message);
            string words = what == "stray" ? x.T("tags-what-stray", pos) : what == "never" ? x.T("tags-what-never", a, pos) : x.T("tags-what-first", a, b, pos);
            return text == null ? words : words + ": " + TextTags.Around(text, pos);
        }

        /// <summary>
        /// Texts with formatting tags out of place, and whose they are. The game writes an error to the log each time
        /// it shows one, but never says whose text it was; the doctor reads every mod's texts the way the game does
        /// and matches the errors word for word (the same error at the same place), then by kind (the same tags,
        /// the place moved by something put in front), and reads the debug and chat logs for the text itself.
        /// <paramref name="onlyMod"/> keeps to one mod's (its page).
        /// </summary>
        public static void TextTagsCheck(DoctorContext x, bool full, string onlyMod = null)
        {
            if (full && onlyMod == null) x.Section(x.T("sec-tags"));
            List<BrokenText> broken = TextTags.LangProblems().Concat(TextTags.CommandProblems(x.Api)).ToList();
            TextTagsCheck(x, full, onlyMod, broken);
        }

        internal static void TextTagsCheck(DoctorContext x, bool full, string onlyMod, List<BrokenText> broken)
        {
            List<(string time, string message)> logged = x.Ev?.Log?.TagErrors ?? new List<(string, string)>();
            var suspects = new List<TagSuspect>();
            foreach (var g in logged.GroupBy(t => t.message))
            {
                var s = new TagSuspect { Message = g.Key, Times = g.Count() };
                x.Ev.TagTexts.TryGetValue(g.Key, out s.Shown);
                string kind = TextTags.Kind(g.Key);
                foreach (BrokenText b in broken)
                {
                    bool same = s.Shown != null ? s.Shown == b.Text || (b.Text.Length >= 8 && s.Shown.Contains(b.Text)) : TextTags.Errors(b.Text).Any(e => e.message == g.Key);
                    // A text that leaves <y> open makes "</x> while <y> is still open" once the game wraps it in its own formatting.
                    var (what, _, top) = TextTags.Describe(g.Key);
                    if (same) s.Exact.Add(b);
                    else if (TextTags.Errors(b.Text).Any(e => TextTags.Kind(e.message) == kind) || (what == "first" && b.Unclosed.Contains(top))) s.Alike.Add(b);
                }
                // Whose: the texts it fits word for word, if they are one mod's; else the ones alike, if one mod's;
                // else a mod the shown text names.
                var exactMods = s.Exact.Select(b => TagOwner(x, b.Domain)).Distinct().ToList();
                var alikeMods = s.Alike.Select(b => TagOwner(x, b.Domain)).Distinct().ToList();
                if (exactMods.Count == 1) { s.Mod = exactMods[0]; s.Sure = true; }
                else if (exactMods.Count == 0 && alikeMods.Count == 1) s.Mod = alikeMods[0];
                else if (exactMods.Count == 0 && alikeMods.Count == 0 && s.Shown != null)
                {
                    ModEntry named = x.Ev.Mods?.UserMods.FirstOrDefault(m => Pages.Names(s.Shown, m.Id, m.Name, true));
                    if (named != null) s.Mod = named.Id;
                }
                suspects.Add(s);
            }

            var cardsFor = new HashSet<string>(StringComparer.Ordinal);
            // The errors in the log, by the mod they are most likely from.
            foreach (var g in suspects.Where(s => s.Mod != null && s.Mod != DoctorReport.GameSection).GroupBy(s => s.Mod))
            {
                if (onlyMod != null && !string.Equals(g.Key, onlyMod, StringComparison.OrdinalIgnoreCase)) continue;
                cardsFor.Add(g.Key);
                int times = g.Sum(s => s.Times);
                bool sure = g.All(s => s.Sure);
                x.Warn(sure ? x.T("tags-mod", x.ModName(g.Key), times) : x.T("tags-mod-maybe", x.ModName(g.Key), times));
                x.About(g.Key);
                TagDetails(x, g.ToList(), full, g.Key);
                foreach (BrokenText b in broken.Where(b => b.Message != null && TagOwner(x, b.Domain) == g.Key && !g.Any(s => s.Exact.Contains(b) || s.Alike.Contains(b))).Take(full ? 12 : 3))
                    x.Detail(x.T("tags-also", TagName(x, b), TagWhat(x, b)));
                x.Explain(x.T("tags-why", x.Side == Vintagestory.API.Common.EnumAppSide.Client ? "client-debug.log" : "server-debug.log"));
                x.Advice(x.T("tags-advice", x.ModName(g.Key)));
                if (x.Ev.Mods?.Get(g.Key) != null) ModButtons(x, g.Key, false);
            }
            // The ones no mod could be named for (or the game's own texts).
            var unknown = suspects.Where(s => s.Mod == null || s.Mod == DoctorReport.GameSection).ToList();
            if (unknown.Count > 0 && onlyMod == null)
            {
                x.Warn(x.T("tags-nomod", unknown.Sum(s => s.Times)));
                x.About(DoctorReport.GameSection);
                TagDetails(x, unknown, full, null);
                x.Explain(x.T("tags-why", x.Side == Vintagestory.API.Common.EnumAppSide.Client ? "client-debug.log" : "server-debug.log"));
                x.Advice(x.T("tags-advice-nomod", x.Side == Vintagestory.API.Common.EnumAppSide.Client ? "client-debug.log" : "server-debug.log"));
            }
            // Texts that would make the same errors, from mods nothing in the log was traced to: to know.
            foreach (var g in broken.Where(b => b.Message != null && b.Command == null).GroupBy(b => TagOwner(x, b.Domain)).OrderBy(g => x.ModName(g.Key), StringComparer.OrdinalIgnoreCase))
            {
                if (g.Key == DoctorReport.GameSection || cardsFor.Contains(g.Key)) continue;
                if (onlyMod != null && !string.Equals(g.Key, onlyMod, StringComparison.OrdinalIgnoreCase)) continue;
                x.Info(x.T("tags-lang", x.ModName(g.Key), g.Count()));
                x.About(g.Key);
                foreach (BrokenText b in g.Take(full ? 12 : 3)) x.Detail(x.T("tags-also", TagName(x, b), TagWhat(x, b)));
                if (g.Count() > (full ? 12 : 3)) x.Detail(x.T("more-lines", g.Count() - (full ? 12 : 3)));
                x.Explain(x.T("tags-lang-why"));
                x.Advice(x.T("tags-advice", x.ModName(g.Key)));
            }
            if (logged.Count == 0 && cardsFor.Count == 0 && !broken.Any(b => b.Message != null && b.Command == null && TagOwner(x, b.Domain) != DoctorReport.GameSection) && onlyMod == null && Listed(x, full))
                x.Ok(x.T("tags-ok"));
        }

        static void TagDetails(DoctorContext x, List<TagSuspect> list, bool full, string mod)
        {
            foreach (TagSuspect s in list.OrderByDescending(s => s.Times).Take(full ? 12 : 4))
            {
                BrokenText text = s.Exact.FirstOrDefault(b => mod == null || TagOwner(x, b.Domain) == mod) ?? s.Alike.FirstOrDefault(b => mod == null || TagOwner(x, b.Domain) == mod);
                string times = s.Times > 1 ? "×" + s.Times + " " : "";
                if (text != null) x.Detail(times + x.T("tags-text", TagName(x, text), text.Message == null ? TagWhat(x, text) : TagWhat(x, s.Message, text.Text)) + (s.Exact.Contains(text) ? "" : " " + x.T("tags-likely")));
                else x.Detail(times + TagWhat(x, s.Message, s.Shown));
                if (s.Shown != null && text == null) x.Detail(x.T("tags-shown", TextTags.Around(s.Shown, TextTags.Position(s.Message), 60, 60)), 2);
            }
            if (list.Count > (full ? 12 : 4)) x.Detail(x.T("more-lines", list.Count - (full ? 12 : 4)));
        }
    }
}
