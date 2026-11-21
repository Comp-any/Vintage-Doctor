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
    }

    /// <summary>
    /// What a doctor page holds: lines. Blocks and items are written into the text as [[item:code]] / [[block:code]]
    /// (an icon and a name in the window), and buttons as {{kind|target|argument|label}}:
    /// open (another page), fix (do something, then show the page again), url, copy and folder.
    /// </summary>
    public sealed class DoctorReport
    {
        public const string OkMark = "✓", WarnMark = "⚠", ProblemMark = "✖", InfoMark = "•", AdviceMark = "→";

        public string Title = "";
        /// <summary>"topic|argument": what Run checks again asks for.</summary>
        public string Key = "overview|";
        /// <summary>Where the text was saved (null if it couldn't be).</summary>
        public string File;
        public readonly List<DoctorLine> Lines = new List<DoctorLine>();

        public void Section(string heading) => Add(heading, DoctorLineStyle.Heading);

        public void Add(string text, DoctorLineStyle style = DoctorLineStyle.Text, int indent = 0)
        {
            Lines.Add(new DoctorLine { Text = text ?? "", Style = style, Indent = Math.Max(0, indent) });
        }

        public int Count(string mark) => Lines.Count(l => l.Style == DoctorLineStyle.Check && l.Indent == 0 && l.Text.StartsWith(mark, StringComparison.Ordinal));
        public int Problems => Count(ProblemMark);
        public int Warnings => Count(WarnMark);

        // ---------------------------------------------------------------- tokens

        public static readonly Regex RefToken = new Regex(@"\[\[((?:item|block):[^\[\]]+)\]\]", RegexOptions.Compiled);
        public static readonly Regex ActionToken = new Regex(@"\{\{(open|fix|url|copy|folder)\|([^|{}]*)\|([^|{}]*)\|([^{}|]*)\}\}", RegexOptions.Compiled);

        /// <summary>Text made safe to sit inside a token (the separators taken out).</summary>
        public static string Safe(string s) => (s ?? "").Replace('{', '(').Replace('}', ')').Replace('|', '/').Replace("[[", "[").Replace("]]", "]");

        public static string Token(string kind, string target, string arg, string label) => "{{" + kind + "|" + Safe(target) + "|" + Safe(arg) + "|" + Safe(label) + "}}";
        public static string Open(string topic, string arg, string label) => Token("open", topic, arg, label);
        /// <summary>A fix button. <paramref name="side"/> is 's' (the server does it) or 'c' (the player's game does it).</summary>
        public static string Fix(char side, string fixId, string arg, string label) => Token("fix", side + ":" + fixId, arg, label);
        public static string Url(string url, string label) => Token("url", url, "", label);
        public static string Copy(string text, string label) => Token("copy", text, "", label);
        public static string Folder(string which, string label) => Token("folder", which, "", label);

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
            Styles = Lines.Select(l => (byte)l.Style).ToArray()
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
                    Indent = p.Indents != null && i < p.Indents.Length ? p.Indents[i] : 0
                });
            }
            return r;
        }
    }
}
