using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VintageDoctor
{
    /// <summary>A text with formatting tags out of place: whose it is (its lang key), and the first error the game would write about it.</summary>
    public sealed class BrokenText
    {
        public string Key, Domain, Text, Message;
        public int Position;
    }

    /// <summary>
    /// The game's formatting tags in texts (VTML: item descriptions, chat, the handbook, tooltips). When it shows a
    /// text with a '&gt;' that isn't part of a tag, or a closing tag that doesn't match, the game writes an error to the
    /// log ("Found closing tag char &gt; but no tag was opened at 12...") and the whole text only to its debug log. That
    /// says nothing about whose text it was; this finds out, by reading the texts the way the game does
    /// (<see cref="Errors"/> is the game's own VtmlParser.Tokenize, minus the building) and matching the errors word
    /// for word against every mod's texts and whatever the debug and chat logs held.
    /// </summary>
    public static class TextTags
    {
        const string SeeDebug = " See debug log for full text.";
        static readonly Regex ErrorRx = new Regex(@"^Found closing tag (?:char > but no tag was opened at (\d+)\.|<([^>]*)> at position (\d+) but (?:it was never opened|<([^>]*)> should be closed first)\.)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsTagError(string message) => message != null && message.StartsWith("Found closing tag ", StringComparison.Ordinal) && ErrorRx.IsMatch(message);

        /// <summary>
        /// The errors the game's parser writes for a text, word for word and in order, with where in the text each
        /// is. Mirrors Vintagestory.API.Common.VtmlParser.Tokenize (1.22, and the public source of 1.23): '&lt;' starts
        /// a tag, '&gt;' ends one; a '&gt;' outside a tag is an error (and, as in the game, then counts as opening a tag
        /// without a name); "&lt;/x&gt;" must close the tag opened last; "&lt;br&gt;" and "&lt;x/&gt;" open nothing.
        /// </summary>
        public static List<(string message, int pos)> Errors(string vtml)
        {
            var found = new List<(string, int)>();
            if (string.IsNullOrEmpty(vtml) || vtml.IndexOf('>') < 0) return found;
            var stack = new Stack<string>();
            var tag = new StringBuilder();
            bool inside = false;
            for (int pos = 0; pos < vtml.Length; pos++)
            {
                char c = vtml[pos];
                if (c == '<') { inside = true; continue; }
                if (c == '>')
                {
                    if (!inside) found.Add(("Found closing tag char > but no tag was opened at " + pos + ". Use &gt;/&lt; if you want to display them as plain characters." + SeeDebug, pos));
                    inside = false;
                    string t = tag.ToString();
                    tag.Clear();
                    if (t.Length > 0 && t[0] == '/')
                    {
                        string name = t.Substring(1);
                        if (stack.Count == 0) found.Add(("Found closing tag <" + name + "> at position " + pos + " but it was never opened." + SeeDebug, pos));
                        else if (stack.Peek() != name) found.Add(("Found closing tag <" + name + "> at position " + pos + " but <" + stack.Peek() + "> should be closed first." + SeeDebug, pos));
                        if (stack.Count > 0) stack.Pop();
                        continue;
                    }
                    if (t == "br") continue;
                    if (t.Length > 0 && pos > 0 && vtml[pos - 1] == '/') continue;
                    // The tag's name is what comes before the first space (none for a '>' on its own).
                    int end = t.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                    stack.Push(end < 0 ? t : t.Substring(0, end));
                    continue;
                }
                if (inside) tag.Append(c);
            }
            return found;
        }

        /// <summary>
        /// What kind of mistake an error is, without where it was: the same text shown with something in front of it
        /// (a name, a heading) moves the position, but not this.
        /// </summary>
        public static string Kind(string message)
        {
            Match m = ErrorRx.Match(message ?? "");
            if (!m.Success) return null;
            if (m.Groups[1].Success) return ">";
            return "</" + m.Groups[2].Value + ">" + (m.Groups[4].Success ? "<" + m.Groups[4].Value + ">" : "");
        }

        /// <summary>The place in the text an error is at.</summary>
        public static int Position(string message)
        {
            Match m = ErrorRx.Match(message ?? "");
            if (!m.Success) return -1;
            return int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value, out int p) ? p : -1;
        }

        /// <summary>What is wrong, in a few words: the tag's names and the character it is at.</summary>
        public static (string what, string a, string b) Describe(string message)
        {
            Match m = ErrorRx.Match(message ?? "");
            if (!m.Success) return (null, null, null);
            if (m.Groups[1].Success) return ("stray", null, null);
            return m.Groups[4].Success ? ("first", m.Groups[2].Value, m.Groups[4].Value) : ("never", m.Groups[2].Value, null);
        }

        /// <summary>A short piece of a text around a place in it, on one line.</summary>
        public static string Around(string text, int pos, int before = 32, int after = 24)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ");
            if (pos < 0 || pos >= text.Length) return text.Length <= before + after ? text : text.Substring(0, before + after) + "...";
            int from = Math.Max(0, pos - before), to = Math.Min(text.Length, pos + after);
            return (from > 0 ? "..." : "") + text.Substring(from, to - from) + (to < text.Length ? "..." : "");
        }

        // ---------------------------------------------------------------- every text the game knows

        static readonly object gate = new object();
        static string scannedFor;
        static List<BrokenText> scanned;

        /// <summary>
        /// Every text in the game's lang files (all mods', in the language shown, with English where it has none) that
        /// would make the game write an error when it is shown as formatted text. Read once per language: texts
        /// don't change while the game runs.
        /// </summary>
        public static List<BrokenText> LangProblems()
        {
            string locale;
            IDictionary<string, string> entries;
            try
            {
                locale = Vintagestory.API.Config.Lang.CurrentLocale ?? "en";
                lock (gate) if (scannedFor == locale && scanned != null) return scanned;
                entries = Vintagestory.API.Config.Lang.GetAllEntries();
            }
            catch { return new List<BrokenText>(); }
            List<BrokenText> list = Scan(entries);
            lock (gate) { scannedFor = locale; scanned = list; }
            return list;
        }

        /// <summary>The texts among these that the game would write an error about, with the first error each.</summary>
        public static List<BrokenText> Scan(IEnumerable<KeyValuePair<string, string>> entries)
        {
            var list = new List<BrokenText>();
            foreach (var kv in entries ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrEmpty(kv.Value) || kv.Value.IndexOf('>') < 0) continue;
                List<(string message, int pos)> errors = Errors(kv.Value);
                if (errors.Count == 0) continue;
                int colon = (kv.Key ?? "").IndexOf(':');
                list.Add(new BrokenText { Key = kv.Key, Domain = colon > 0 ? kv.Key.Substring(0, colon) : "game", Text = kv.Value, Message = errors[0].message, Position = errors[0].pos });
            }
            return list;
        }

        // ---------------------------------------------------------------- what the logs held

        /// <summary>
        /// The texts the game showed when it wrote these errors, by error: from the debug log (the game writes the
        /// whole text there, right after the error) and from the chat log (a chat line that makes the very same
        /// error). Files that aren't there are passed over; never throws.
        /// </summary>
        public static Dictionary<string, string> ShownTexts(string logsFolder, string side, ICollection<string> messages)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            if (messages == null || messages.Count == 0 || string.IsNullOrEmpty(logsFolder)) return texts;
            var wanted = new HashSet<string>(messages, StringComparer.Ordinal);
            try
            {
                foreach (string file in Directory.GetFiles(logsFolder, side + "-*.log").OrderBy(f => f, StringComparer.Ordinal))
                {
                    string name = Path.GetFileName(file);
                    if (name.EndsWith("-main.log", StringComparison.Ordinal) || name.Contains("-crash")) continue;
                    List<string> lines = Tail(file, 24L * 1024 * 1024);
                    bool chat = name.Contains("-chat");
                    for (int i = 0; i < lines.Count; i++)
                    {
                        string message = Message(lines[i]);
                        if (message == null) continue;
                        if (chat)
                        {
                            foreach (var (error, _) in Errors(message)) if (wanted.Contains(error) && !texts.ContainsKey(error)) texts[error] = message;
                            continue;
                        }
                        if (!wanted.Contains(message) || texts.ContainsKey(message)) continue;
                        // The whole text is the next line written as VerboseDebug, and the lines after it without a
                        // time of their own (a text with line breaks in it).
                        for (int k = i + 1; k < Math.Min(lines.Count, i + 4); k++)
                        {
                            if (lines[k].IndexOf("[VerboseDebug]", StringComparison.Ordinal) < 0) continue;
                            var sb = new StringBuilder(Message(lines[k]) ?? "");
                            for (int m = k + 1; m < lines.Count && m < k + 40 && Message(lines[m]) == null; m++) sb.Append('\n').Append(lines[m]);
                            texts[message] = sb.ToString();
                            break;
                        }
                    }
                }
            }
            catch { }
            return texts;
        }

        static readonly Regex Stamp = new Regex(@"^\d{1,2}\.\d{1,2}\.\d{4} \d{2}:\d{2}:\d{2}(?:\.\d+)? \[[^\]]+\] ?", RegexOptions.Compiled);

        /// <summary>A log line's message without its time and kind; null for a line that goes on from the one above.</summary>
        static string Message(string line)
        {
            Match m = Stamp.Match(line ?? "");
            return m.Success ? line.Substring(m.Length) : null;
        }

        /// <summary>The lines of a file, only the last part of it when it is big; the game may be writing to it.</summary>
        static List<string> Tail(string file, long most)
        {
            var lines = new List<string>();
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length > most) fs.Seek(fs.Length - most, SeekOrigin.Begin);
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    if (fs.Position > 0) sr.ReadLine();
                    string l;
                    while ((l = sr.ReadLine()) != null) lines.Add(l);
                }
            }
            return lines;
        }
    }
}
