using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;

namespace VintageDoctor
{
    /// <summary>
    /// Check: the one-line result of a test ("✓ ...", "⚠ ...", "✖ ..." or "• ..."). In the window the lines after it
    /// that are indented or notes are its details, hidden until the card is opened; the file and the console show
    /// everything.
    /// </summary>
    public enum DoctorLineStyle : byte { Text = 0, Heading = 1, Total = 2, Note = 3, Warn = 4, Check = 5 }

    public sealed class DoctorLine
    {
        public int Indent;
        public DoctorLineStyle Style;
        public string Text;
        /// <summary>On a card: the mods it is about beyond the ones its words name (ids, comma-separated). Not sent.</summary>
        public string About;
        /// <summary>
        /// On the check-up: the section the line is in, which is a mod's id or <see cref="DoctorReport.GameSection"/>
        /// for what isn't about one mod. A half of the check-up (one side's cards, before the two are put together)
        /// may name several, comma-separated: the card is shown in each. Null on other pages.
        /// </summary>
        public string Section;

        public DoctorLine Copy(string section) => new DoctorLine { Indent = Indent, Style = Style, Text = Text, About = About, Section = section };
    }

    /// <summary>
    /// What a doctor page holds: lines. Blocks and items are written into the text as [[item:code]] / [[block:code]]
    /// (an icon and a name in the window), and buttons as {{kind|target|argument|label}}:
    /// open (another page), fix (do something, then show the page again), url, copy and folder.
    /// </summary>
    public sealed class DoctorReport
    {
        // The marks are how a line says what it is, to the window, to the other side and to other mods. They are never
        // drawn as they are: the game's font can't be counted on for them (see GameFont).
        public const string OkMark = "✓", WarnMark = "⚠", ProblemMark = "✖", InfoMark = "•", AdviceMark = "→";   // font-ok: never drawn as text
        /// <summary>The check-up's section for what isn't about one mod, and the one for the mods nothing was found against.</summary>
        public const string GameSection = "game", RestSection = "-";

        public string Title = "";
        /// <summary>"topic|argument": what Run checks again asks for.</summary>
        public string Key = "overview|";
        /// <summary>Where the text was saved (null if it couldn't be).</summary>
        public string File;
        public readonly List<DoctorLine> Lines = new List<DoctorLine>();
        /// <summary>On the check-up: the heading of each section (a mod's name and version), by its key.</summary>
        public readonly Dictionary<string, string> SectionTitles = new Dictionary<string, string>(StringComparer.Ordinal);

        public void Section(string heading) => Add(heading, DoctorLineStyle.Heading);

        public void Add(string text, DoctorLineStyle style = DoctorLineStyle.Text, int indent = 0)
        {
            Lines.Add(new DoctorLine { Text = text ?? "", Style = style, Indent = Math.Max(0, indent) });
        }

        /// <summary>How many cards have this mark. A card shown under several mods on the check-up counts once.</summary>
        public int Count(string mark) => Lines.Where(l => l.Style == DoctorLineStyle.Check && l.Indent == 0 && l.Text.StartsWith(mark, StringComparison.Ordinal)).Select(l => l.Text).Distinct(StringComparer.Ordinal).Count();
        public int Problems => Count(ProblemMark);
        public int Warnings => Count(WarnMark);

        // ---------------------------------------------------------------- tokens

        public static readonly Regex RefToken = new Regex(@"\[\[((?:item|block):[^\[\]]+)\]\]", RegexOptions.Compiled);
        public static readonly Regex ActionToken = new Regex(@"\{\{(open|fix|url|copy|folder|graph)\|([^|{}]*)\|([^|{}]*)\|([^{}|]*)\}\}", RegexOptions.Compiled);

        /// <summary>Text made safe to sit inside a token (the separators taken out).</summary>
        public static string Safe(string s) => (s ?? "").Replace('{', '(').Replace('}', ')').Replace('|', '/').Replace("[[", "[").Replace("]]", "]");

        public static string Token(string kind, string target, string arg, string label) => "{{" + kind + "|" + Safe(target) + "|" + Safe(arg) + "|" + Safe(label) + "}}";
        public static string Open(string topic, string arg, string label) => Token("open", topic, arg, label);
        /// <summary>A fix button. <paramref name="side"/> is 's' (the server does it) or 'c' (the player's game does it).</summary>
        public static string Fix(char side, string fixId, string arg, string label) => Token("fix", side + ":" + fixId, arg, label);
        public static string Url(string url, string label) => Token("url", url, "", label);
        public static string Copy(string text, string label) => Token("copy", text, "", label);

        /// <summary>
        /// A graph (1.9.0): one or two lines of numbers over time, oldest first, drawn by the window; elsewhere written
        /// as the newest, the average and the highest. <paramref name="unit"/> is "bytes" (shown as B/s, KB/s...) or
        /// any word written after each number.
        /// </summary>
        public static string Graph(string label, string unit, IList<double> first, IList<double> second = null)
        {
            string Join(IList<double> v) => string.Join(",", v.Select(d => Math.Round(d, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return Token("graph", Join(first) + (second != null && second.Count > 0 ? ";" + Join(second) : ""), unit, label);
        }

        /// <summary>A graph token's lines of numbers.</summary>
        public static List<List<double>> GraphValues(string target)
        {
            var all = new List<List<double>>();
            foreach (string part in (target ?? "").Split(';'))
            {
                var v = new List<double>();
                foreach (string n in part.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    if (double.TryParse(n, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) v.Add(d);
                all.Add(v);
            }
            return all;
        }

        /// <summary>A number in a graph's unit: bytes a second as B/s, KB/s, MB/s.</summary>
        public static string GraphNumber(double v, string unit)
        {
            if (unit != "bytes") return Math.Round(v, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(unit) ? "" : " " + unit);
            return Rate(v);
        }

        public static string Rate(double bytesPerSecond)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            if (bytesPerSecond < 1024) return Math.Round(bytesPerSecond).ToString(c) + " B/s";
            if (bytesPerSecond < 1024 * 1024) return (bytesPerSecond / 1024).ToString(bytesPerSecond < 10240 ? "0.0" : "0", c) + " KB/s";
            return (bytesPerSecond / 1024 / 1024).ToString("0.00", c) + " MB/s";
        }
        public static string Folder(string which, string label) => Token("folder", which, "", label);
        /// <summary>
        /// A button that opens one file of a folder on the player's computer ("logs", "client-main.log"). It is a
        /// folder button with the file as its argument, so a window from before 1.4.0 opens the folder instead.
        /// </summary>
        public static string FileIn(string which, string file, string label) => Token("folder", which, file ?? "", label);
        /// <summary>The Logs folder's key in a folder button.</summary>
        public const string LogsFolder = "logs";

        /// <summary>
        /// Whether a name is a file the window may open from one of the game's folders: a plain name (no folder in
        /// it) of a log or text file. The name can come from a server or another mod, so nothing else is opened.
        /// </summary>
        public static bool OpenableFile(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 80) return false;
            return SafeFileName.IsMatch(name) && name.IndexOf("..", StringComparison.Ordinal) < 0;
        }
        static readonly Regex SafeFileName = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\.(log|txt)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Ref(CollectibleObject c)
        {
            if (c?.Code == null) return "?";
            return "[[" + (c is Block ? "block:" : "item:") + c.Code + "]]";
        }

        public static CollectibleObject Resolve(IWorldAccessor world, string key)
        {
            if (world == null || string.IsNullOrEmpty(key)) return null;
            try
            {
                if (key.StartsWith("block:", StringComparison.Ordinal)) return world.GetBlock(new AssetLocation(key.Substring(6)));
                if (key.StartsWith("item:", StringComparison.Ordinal)) return world.GetItem(new AssetLocation(key.Substring(5)));
            }
            catch { }
            return null;
        }

        public static string NameOf(CollectibleObject c)
        {
            if (c?.Code == null) return "?";
            try { return c.GetHeldItemName(new ItemStack(c)); }
            catch { return c.Code.ToShortString(); }
        }

        /// <summary>A button as text: its label and, where there is one, the command that does the same.</summary>
        public static string PlainAction(string kind, string target, string arg, string label)
        {
            switch (kind)
            {
                case "open": return label + " (/doctor" + (target.Length > 0 && target != "overview" ? " " + target : "") + (arg.Length > 0 ? " " + arg : "") + ")";
                case "fix":
                    {
                        string id = target.Length > 2 && target[1] == ':' ? target.Substring(2) : target;
                        bool client = target.StartsWith("c:", StringComparison.Ordinal);
                        return label + " (" + (client ? ".doctor" : "/doctor") + " fix " + id + (arg.Length > 0 ? " " + arg : "") + ")";
                    }
                case "url": return label + " (" + target + ")";
                case "copy": return label + ": " + target;
                case "graph":
                    {
                        var lines = GraphValues(target).Where(v => v.Count > 0).ToList();
                        if (lines.Count == 0) return label;
                        return label + ": " + string.Join(" / ", lines.Select(v => GraphNumber(v[v.Count - 1], arg) + " (avg " + GraphNumber(v.Average(), arg) + ", top " + GraphNumber(v.Max(), arg) + ")"));
                    }
                default: return label;
            }
        }

        /// <summary>The text with each item ref written out as "Name (code)" and each button as its label and command.</summary>
        public static string Plain(IWorldAccessor world, string text, bool labelsOnly = false)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.IndexOf("{{", StringComparison.Ordinal) >= 0)
                text = ActionToken.Replace(text, m => labelsOnly ? m.Groups[4].Value : PlainAction(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value));
            if (text.IndexOf("[[", StringComparison.Ordinal) >= 0)
                text = RefToken.Replace(text, m =>
                {
                    CollectibleObject c = Resolve(world, m.Groups[1].Value);
                    string code = m.Groups[1].Value.Substring(m.Groups[1].Value.IndexOf(':') + 1);
                    return c?.Code == null ? code : NameOf(c) + " (" + code + ")";
                });
            return text;
        }

        /// <summary>The whole page as plain text, for the file and the console.</summary>
        public string PlainText(IWorldAccessor world, bool labelsOnly = false)
        {
            var sb = new StringBuilder();
            foreach (DoctorLine line in Lines)
            {
                if (line.Style == DoctorLineStyle.Heading && sb.Length > 0) sb.Append('\n');
                string text = Plain(world, line.Text, labelsOnly);
                if (line.Style == DoctorLineStyle.Heading) { sb.Append("== ").Append(text).Append(" ==\n"); continue; }
                foreach (string part in text.Split('\n')) sb.Append(' ', line.Indent * 2).Append(part).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        public DoctorReportPacket ToPacket(int id = 0) => new DoctorReportPacket
        {
            Id = id,
            Title = Title,
            Key = Key,
            File = File,
            Texts = Lines.Select(l => l.Text).ToArray(),
            Indents = Lines.Select(l => (byte)Math.Min(l.Indent, 20)).ToArray(),
            Styles = Lines.Select(l => (byte)l.Style).ToArray(),
            Sections = Lines.Any(l => l.Section != null) ? Lines.Select(l => l.Section ?? "").ToArray() : null,
            SectionTitles = SectionTitles.Count > 0 ? SectionTitles.Select(kv => kv.Key + "|" + kv.Value).ToArray() : null
        };

        public static DoctorReport FromPacket(DoctorReportPacket p)
        {
            var r = new DoctorReport { Title = p?.Title ?? "", Key = p?.Key ?? "overview|", File = p?.File };
            string[] texts = p?.Texts ?? Array.Empty<string>();
            for (int i = 0; i < texts.Length; i++)
            {
                r.Lines.Add(new DoctorLine
                {
                    Text = texts[i] ?? "",
                    Style = p.Styles != null && i < p.Styles.Length ? (DoctorLineStyle)p.Styles[i] : DoctorLineStyle.Text,
                    Indent = p.Indents != null && i < p.Indents.Length ? p.Indents[i] : 0,
                    Section = p.Sections != null && i < p.Sections.Length ? p.Sections[i] : null
                });
            }
            foreach (string s in p?.SectionTitles ?? Array.Empty<string>())
            {
                int bar = s?.IndexOf('|') ?? -1;
                if (bar > 0) r.SectionTitles[s.Substring(0, bar)] = s.Substring(bar + 1);
            }
            return r;
        }
    }
}
