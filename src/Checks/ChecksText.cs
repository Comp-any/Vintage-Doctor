using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
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

        // ---------------------------------------------------------------- the chat window (1.7.0)

        /// <summary>The tags that open and must be closed again; a line that leaves one open styles everything after it.</summary>
        static readonly HashSet<string> PairTags = new HashSet<string>(StringComparer.Ordinal) { "font", "strong", "i", "a", "hk", "code" };

        /// <summary>A line of the chat that upsets the chat window, and how.</summary>
        internal sealed class ChatBreak
        {
            public int Group, Line;
            public string Text, Message;
            public List<string> Open = new List<string>();
        }

        /// <summary>
        /// The chat's lines that break it. The game keeps each chat tab's lines as text with formatting tags and draws
        /// them as ONE text, joined (ClientMain.ChatHistoryByPlayerGroup, HudDialogChat.UpdateText, 1.22.7): a line
        /// with a tag it never closes changes every line after it, and a stray closing tag or '>' makes the game write
        /// an error each time the chat is drawn, until the line scrolls away or the chat is cleared (.clearchat).
        /// </summary>
        internal static List<ChatBreak> ChatBreaks(IEnumerable<(int group, IList<string> lines)> history)
        {
            var found = new List<ChatBreak>();
            foreach (var (group, lines) in history ?? Enumerable.Empty<(int, IList<string>)>())
            {
                for (int i = 0; i < (lines?.Count ?? 0); i++)
                {
                    string line = lines[i] ?? "";
                    if (line.IndexOf('<') < 0 && line.IndexOf('>') < 0) continue;
                    var errors = TextTags.Errors(line);
                    List<string> open = TextTags.Unclosed(line).Where(PairTags.Contains).ToList();
                    if (errors.Count == 0 && open.Count == 0) continue;
                    found.Add(new ChatBreak { Group = group, Line = i, Text = line, Message = errors.Count > 0 ? errors[0].message : null, Open = open });
                }
            }
            return found;
        }

        /// <summary>The chat's lines as the game keeps them, read from the game by reflection; null when they can't be read.</summary>
        static List<(int group, IList<string> lines)> ChatHistory(ICoreAPI api)
        {
            try
            {
                object game = (api as Vintagestory.API.Client.ICoreClientAPI)?.World;
                if (game == null) return null;
                const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                object value = game.GetType().GetField("ChatHistoryByPlayerGroup", all)?.GetValue(game) ?? game.GetType().GetProperty("ChatHistoryByPlayerGroup", all)?.GetValue(game);
                if (!(value is System.Collections.IDictionary dict)) return null;
                var list = new List<(int, IList<string>)>();
                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    if (!(kv.Value is System.Collections.IEnumerable lines)) continue;
                    list.Add((kv.Key is int g ? g : 0, lines.Cast<object>().Select(o => o as string ?? "").ToList()));
                }
                return list;
            }
            catch { return null; }
        }

        /// <summary>On the player's game: lines in the chat whose tags break the chat window, with a button that clears the chat.</summary>
        public static void Chat(DoctorContext x, bool full)
        {
            if (x.Side != EnumAppSide.Client) return;
            List<(int group, IList<string> lines)> history = ChatHistory(x.Api);
            if (history == null) return;
            Chat(x, full, ChatBreaks(history));
        }

        internal static void Chat(DoctorContext x, bool full, List<ChatBreak> broken)
        {
            if (broken.Count == 0)
            {
                if (Listed(x, full)) x.Ok(x.T("chat-ok"));
                return;
            }
            x.Warn(x.T("chat-broken", broken.Select(b => b.Text).Distinct().Count()));
            x.About(DoctorReport.GameSection);
            foreach (ChatBreak b in broken.GroupBy(b => b.Text).Select(g => g.First()).Take(full ? 12 : 4))
            {
                string what = b.Message != null ? TagWhat(x, b.Message, null) : x.T("tags-what-open", string.Join("> <", b.Open));
                x.Detail(x.T("chat-line", what, TextTags.Around(b.Text, b.Message != null ? TextTags.Position(b.Message) : b.Text.IndexOf("<" + b.Open[0], StringComparison.Ordinal), 50, 50)));
            }
            x.Explain(x.T("chat-why"));
            x.Advice(x.T("chat-advice"));
            x.FixButton("doctor:clear-chat", "", x.T("btn-clear-chat"));
            x.CopyButton(string.Join("\n", broken.Select(b => b.Text).Distinct()), x.T("btn-copy-chat"));
        }
    }
}
