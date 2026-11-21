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
        public BlockPos LookedAt;
        /// <summary>The asker's game runs in the same program as this server (single player).</summary>
        public bool SameProcess;
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

        public void Ok(string text) => R.Add(DoctorReport.OkMark + " " + Titled(text), DoctorLineStyle.Check);
        public void Info(string text) => R.Add(DoctorReport.InfoMark + " " + Titled(text), DoctorLineStyle.Check);
        public void Warn(string text) => R.Add(DoctorReport.WarnMark + " " + Titled(text), DoctorLineStyle.Check);
        public void Problem(string text) => R.Add(DoctorReport.ProblemMark + " " + Titled(text), DoctorLineStyle.Check);

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

        /// <summary>A fix that belongs on the player's own computer when the server is part of their game (single player): switching a mod off, say.</summary>
        public string PlayerFixToken(string fixId, string argument, string label) => DoctorReport.Fix(Side == EnumAppSide.Server && SameProcess ? 'c' : SideChar, fixId, argument, label);

        /// <summary>A button that opens a folder on the player's computer; nothing when the folder is on a server somewhere else.</summary>
        public string FolderToken(string which, string label) => Side == EnumAppSide.Server && !SameProcess ? null : DoctorReport.Folder(which, label);

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
