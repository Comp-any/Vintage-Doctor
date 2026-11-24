using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageDoctor.API;

namespace VintageDoctor
{
    /// <summary>
    /// What a check writes to while a page is being built: the page's lines, who asked and in which language, and
    /// what was read from disk. The doctor's own checks and other mods' checks both use it.
    /// </summary>
    public sealed class DoctorContext : IDoctorContext
    {
        public VintageDoctorSystem Doctor;
        public ICoreAPI Api { get; set; }
        public EnumAppSide Side { get; set; }
        public IPlayer Player { get; set; }
        public string LanguageCode { get; set; } = "en";
        public string Topic { get; set; } = "overview";
        public string Argument { get; set; } = "";
        public bool Compact { get; set; }
        public ItemStack HeldItem { get; set; }
        public BlockPos LookedAt { get; set; }
        public CollectibleObject Subject { get; set; }
        /// <summary>What mods on the asker's game sent along (mod id → bytes); null when nothing came.</summary>
        public Vintagestory.API.Datastructures.ITreeAttribute Facts;
        /// <summary>A mod's page that will arrive later (it said so on the event bus): the page isn't finished until then.</summary>
        public Deferred Pending;
        /// <summary>The page is one of a mod's that only the other side of the game has: this side has nothing to add to it.</summary>
        public bool OtherSideHasTopic;
        /// <summary>The asker's game runs in the same program as this server (single player).</summary>
        public bool SameProcess;
        /// <summary>The page is the check-up, which lists every check with its result: one that found nothing says so too.</summary>
        public bool ListAll;
        public Evidence Ev;
        public DoctorReport R = new DoctorReport();
        /// <summary>While another mod's check runs: that mod's id, so its findings carry the mod's name.</summary>
        public string Owner;

        char SideChar => Side == EnumAppSide.Client ? 'c' : 's';

        /// <summary>One of the doctor's own texts, in the asker's language.</summary>
        public string T(string key, params object[] args)
        {
            try { return Vintagestory.API.Config.Lang.GetL(LanguageCode ?? "en", "vintagedoctor:" + key, args); }
            catch (Exception e) { return key + " (" + e.Message + ")"; }
        }

        public string Translate(string key, params object[] args)
        {
            try { return Vintagestory.API.Config.Lang.GetL(LanguageCode ?? "en", key, args); }
            catch { return key; }
        }

        /// <summary>A text that may be a lang key ("mymod:doctor-x") or plain words: translated when it is a key that exists.</summary>
        public string Words(string textOrKey)
        {
            if (string.IsNullOrEmpty(textOrKey)) return "";
            if (textOrKey.IndexOf(' ') < 0 && textOrKey.IndexOf(':') > 0)
            {
                try { if (Vintagestory.API.Config.Lang.HasTranslation(textOrKey, false, false)) return Translate(textOrKey); } catch { }
            }
            return textOrKey;
        }

        string Titled(string text)
        {
            if (Owner == null) return text;
            string name = Ev?.Mods?.NameOf(Owner) ?? Owner;
            return name + ": " + text;
        }

        public void Ok(string text) => Card(DoctorReport.OkMark, text);
        public void Info(string text) => Card(DoctorReport.InfoMark, text);
        public void Warn(string text) => Card(DoctorReport.WarnMark, text);
        public void Problem(string text) => Card(DoctorReport.ProblemMark, text);

        void Card(string mark, string text)
        {
            R.Add(mark + " " + Titled(text), DoctorLineStyle.Check);
            if (Owner != null) R.Lines[R.Lines.Count - 1].About = Owner;
        }

        /// <summary>
        /// Says which mods the card just written is about, where its words and buttons don't name them (a file's
        /// name, say): the check-up shows the card under those mods.
        /// </summary>
        public void About(params string[] modIds)
        {
            for (int i = R.Lines.Count - 1; i >= 0; i--)
            {
                DoctorLine l = R.Lines[i];
                if (l.Style == DoctorLineStyle.Heading) return;
                if (l.Style != DoctorLineStyle.Check || l.Indent != 0) continue;
                foreach (string id in modIds ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrEmpty(id)) l.About = string.IsNullOrEmpty(l.About) ? id : l.About + "," + id;
                }
                return;
            }
        }

        public void Result(DoctorLevel level, string text)
        {
            switch (level)
            {
                case DoctorLevel.Ok: Ok(text); break;
                case DoctorLevel.Info: Info(text); break;
                case DoctorLevel.Warning: Warn(text); break;
                default: Problem(text); break;
            }
        }

        public void Detail(string text) => R.Add(text, DoctorLineStyle.Note, 1);
        public void Detail(string text, int indent) => R.Add(text, DoctorLineStyle.Note, indent);
        public void Explain(string text) { if (!Compact && !string.IsNullOrEmpty(text)) R.Add(text, DoctorLineStyle.Note, 1); }
        public void Advice(string text) { if (!Compact && !string.IsNullOrEmpty(text)) R.Add(DoctorReport.AdviceMark + " " + text, DoctorLineStyle.Text, 1); }
        /// <summary>
        /// A note that stands by itself. Right under a card it is written as plain text instead: the window takes a
        /// note under a card for one of the card's details.
        /// </summary>
        public void Aside(string text)
        {
            int i = R.Lines.Count - 1;
            while (i >= 0 && R.Lines[i].Style != DoctorLineStyle.Heading && (R.Lines[i].Indent > 0 || R.Lines[i].Style == DoctorLineStyle.Note)) i--;
            bool underCard = i >= 0 && R.Lines[i].Style == DoctorLineStyle.Check && R.Lines[i].Indent == 0;
            R.Add(text, underCard ? DoctorLineStyle.Text : DoctorLineStyle.Note);
        }

        /// <summary>A plain line that is not part of a card.</summary>
        public void Line(string text, DoctorLineStyle style = DoctorLineStyle.Text, int indent = 0) => R.Add(text, style, indent);

        public void FixButton(string fixId, string argument, string label)
        {
            if (Compact || string.IsNullOrEmpty(fixId)) return;
            R.Add(DoctorReport.Fix(SideChar, fixId, argument, label), DoctorLineStyle.Text, 1);
        }

        public void LinkButton(string topic, string argument, string label)
        {
            if (Compact) return;
            R.Add(DoctorReport.Open(topic, argument, label), DoctorLineStyle.Text, 1);
        }

        public void UrlButton(string url, string label)
        {
            if (Compact || string.IsNullOrEmpty(url)) return;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return;
            R.Add(DoctorReport.Url(url, label), DoctorLineStyle.Text, 1);
        }

        public void CopyButton(string text, string label)
        {
            if (Compact || string.IsNullOrEmpty(text)) return;
            R.Add(DoctorReport.Copy(text, label), DoctorLineStyle.Text, 1);
        }

        /// <summary>Several buttons side by side on one line.</summary>
        public void Buttons(params string[] tokens)
        {
            if (Compact) return;
            string line = string.Join(" ", tokens.Where(t => !string.IsNullOrEmpty(t)));
            if (line.Length > 0) R.Add(line, DoctorLineStyle.Text, 1);
        }

        public string FixToken(string fixId, string argument, string label) => DoctorReport.Fix(SideChar, fixId, argument, label);

        public byte[] ClientFacts(string modId)
        {
            if (Facts == null || string.IsNullOrEmpty(modId)) return null;
            try { return Facts.GetBytes(modId, null); } catch { return null; }
        }

        public void Write(string text, int indent = 0) => R.Add(text ?? "", DoctorLineStyle.Text, indent);
        public string LinkText(string topic, string argument, string label) => Compact ? "" : DoctorReport.Open(topic, argument, label);
        public string FixText(string fixId, string argument, string label) => Compact || string.IsNullOrEmpty(fixId) ? "" : DoctorReport.Fix(SideChar, fixId, argument, label);
        /// <summary>'s' or 'c': the side a fix button made here runs on.</summary>
        public char SideLetter => SideChar;

        /// <summary>A fix that belongs on the player's own computer when the server is part of their game (single player): switching a mod off, say.</summary>
        public string PlayerFixToken(string fixId, string argument, string label) => DoctorReport.Fix(Side == EnumAppSide.Server && SameProcess ? 'c' : SideChar, fixId, argument, label);

        /// <summary>A button that opens a folder on the player's computer; nothing when the folder is on a server somewhere else.</summary>
        public string FolderToken(string which, string label) => OnPlayersComputer ? DoctorReport.Folder(which, label) : null;

        /// <summary>Whether this side's files are on the asker's own computer: their game, or the server inside it (single player).</summary>
        public bool OnPlayersComputer => !(Side == EnumAppSide.Server && !SameProcess);

        /// <summary>This side's log ("client-main.log", "server-main.log") and its crash reports.</summary>
        public string MainLogName => Side == EnumAppSide.Client ? "client-main.log" : "server-main.log";
        public string CrashLogName => Side == EnumAppSide.Client ? "client-crash.log" : "server-crash.log";

        /// <summary>
        /// A button that opens a file of the Logs folder on the player's computer (this side's log when no name is
        /// given), or the folder itself for "". Nothing when the logs are on a server somewhere else.
        /// </summary>
        public string LogToken(string file = null)
        {
            if (!OnPlayersComputer) return null;
            if (file == null) file = MainLogName;
            if (file.Length == 0 || !DoctorReport.OpenableFile(file)) return DoctorReport.Folder(DoctorReport.LogsFolder, T("btn-open-logs"));
            return DoctorReport.FileIn(DoctorReport.LogsFolder, file, T("btn-open-file", file));
        }

        public void LogButton(string file = null)
        {
            if (Compact) return;
            string token = LogToken(file);
            if (token != null) R.Add(token, DoctorLineStyle.Text, 1);
        }

        /// <summary>Added to each heading ("your game", "the server"), since the window shows both halves of a page one under the other.</summary>
        public string SideLabel;

        public void Section(string heading) => R.Section(string.IsNullOrEmpty(SideLabel) ? heading : heading + " · " + SideLabel);

        public string ItemRef(CollectibleObject collectible) => DoctorReport.Ref(collectible);

        public IReadOnlyList<DoctorLogIssue> LogIssues(string modId = null)
        {
            var result = new List<DoctorLogIssue>();
            if (Ev?.Log == null) return result;
            foreach (LogIssue i in Ev.Log.Issues)
            {
                if (modId != null && !string.Equals(i.Mod, modId, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new DoctorLogIssue { Level = i.Level, Message = i.Message, Stack = i.Stack.ToList(), Count = i.Count, ModId = i.Mod, FirstTime = i.FirstTime, LastTime = i.LastTime });
            }
            return result;
        }

        /// <summary>A mod's name as a reader knows it, with its id when the two differ much.</summary>
        public string ModName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            ModEntry m = Ev?.Mods?.Get(id);
            return m == null ? id : m.Name;
        }

        /// <summary>The number of ✖ and ⚠ cards so far.</summary>
        public int Problems => R.Problems;
        public int Warnings => R.Warnings;
    }

    /// <summary>What a fix gets.</summary>
    public sealed class DoctorFixContext : IDoctorFixContext
    {
        public VintageDoctorSystem Doctor;
        public ICoreAPI Api { get; set; }
        public EnumAppSide Side { get; set; }
        public IPlayer Player { get; set; }
        public string LanguageCode { get; set; } = "en";
        public string Argument { get; set; } = "";
        public Evidence Ev;

        public string Failed(string why) => DoctorReport.WarnMark + " " + why;

        public string T(string key, params object[] args)
        {
            try { return Vintagestory.API.Config.Lang.GetL(LanguageCode ?? "en", "vintagedoctor:" + key, args); }
            catch (Exception e) { return key + " (" + e.Message + ")"; }
        }

        public string Translate(string key, params object[] args)
        {
            try { return Vintagestory.API.Config.Lang.GetL(LanguageCode ?? "en", key, args); }
            catch { return key; }
        }
    }
}
