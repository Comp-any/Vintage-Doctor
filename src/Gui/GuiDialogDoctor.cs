using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageDoctor
{
    /// <summary>
    /// Shapes drawn with Cairo (a card, a bar, a dot) as an interactive element: it keeps its own texture and draws it
    /// where its bounds are each frame, so it scrolls with the list. (A static draw is baked into the dialog's
    /// background once and stays put.)
    /// </summary>
    public class GuiElementCanvas : GuiElement
    {
        readonly Action<Context, ElementBounds> draw;
        LoadedTexture texture;

        public GuiElementCanvas(ICoreClientAPI capi, ElementBounds bounds, Action<Context, ElementBounds> draw) : base(capi, bounds)
        {
            this.draw = draw;
            texture = new LoadedTexture(capi);
        }

        public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
        {
            Bounds.CalcWorldBounds();
            int w = Math.Max(1, (int)Math.Ceiling(Bounds.OuterWidth)), h = Math.Max(1, (int)Math.Ceiling(Bounds.OuterHeight));
            using var surface = new ImageSurface(Format.Argb32, w, h);
            using var ctx = new Context(surface);
            ctx.SetSourceRGBA(0, 0, 0, 0);
            ctx.Paint();
            // The drawing is at the origin of its own surface; the delegate only reads the bounds' size.
            draw(ctx, Bounds);
            generateTexture(surface, ref texture);
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, Bounds);
        }

        // A drawing is not clickable: the base element marks every click on it as handled, which would stop
        // the click reaching the hotspot behind the card or chip.
        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
        public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args) { }

        public override void Dispose()
        {
            base.Dispose();
            texture?.Dispose();
        }
    }

    /// <summary>An invisible area that runs an action when clicked, so a whole card header can open the card without being a link.</summary>
    public class GuiElementHotspot : GuiElement
    {
        readonly Action onClick;
        public GuiElementHotspot(ICoreClientAPI capi, ElementBounds bounds, Action onClick) : base(capi, bounds) { this.onClick = onClick; }
        public override void ComposeElements(Context ctx, ImageSurface surface) => Bounds.CalcWorldBounds();
        public override void RenderInteractiveElements(float deltaTime) { }
        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseDownOnElement(api, args);
            args.Handled = true;
            onClick();
        }
    }

    /// <summary>
    /// The doctor's window (Ctrl+Shift+D, /doctor): a checklist. At the top, how many checks passed, warned, failed or
    /// are just information (click one to hide or show that kind), the list that shows one mod's part alone, and "Run checks again"; then one
    /// card per check with a coloured bar and dot, the check in words and a badge, which opens to show the details,
    /// what to do, and buttons for the fixes and the pages it links to; headings and questions in between; at the
    /// bottom Check-up, Back, when it was checked, and Copy. Taken from Vintage-E's EMC doctor.
    /// </summary>
    public class GuiDialogDoctor : GuiDialog
    {
        const double Width = 740;
        const double ListHeight = 430;
        const double Pad = 12;
        const double CardX = 0, CardW = Width - 24;
        const double TitleX = 58, BadgeW = 110;
        /// <summary>The room an advice line leaves for its drawn arrow.</summary>
        const double AdviceLead = 18;

        static readonly double[] Green = { 0.42, 0.75, 0.30, 1 };
        static readonly double[] Orange = { 0.92, 0.62, 0.18, 1 };
        static readonly double[] Red = { 0.85, 0.30, 0.28, 1 };
        static readonly double[] Grey = { 0.55, 0.52, 0.48, 1 };
        static readonly double[] Blue = { 0.55, 0.8, 1, 1 };
        static readonly double[] Gold = { 0.80, 0.62, 0.30, 1 };
        static readonly double[] FixText = { 0.68, 0.92, 0.58, 1 };
        static readonly double[] Advice = { 0.92, 0.88, 0.75, 1 };
        static readonly double[] CardBg = { 0, 0, 0, 0.28 };
        static readonly double[] DetailBg = { 0, 0, 0, 0.18 };

        /// <summary>A button was pressed: (kind, target, argument) - open, refresh, fix, url, copy or folder.</summary>
        readonly Action<string, string, string> onAction;
        readonly string version;
        readonly Stack<DoctorReportPacket> history = new Stack<DoctorReportPacket>();

        /// <summary>Keeps the newest <paramref name="keep"/> reports of a Back trail (each holds a whole report).</summary>
        internal static void TrimHistory(Stack<DoctorReportPacket> stack, int keep)
        {
            if (stack.Count <= keep) return;
            DoctorReportPacket[] newest = stack.Take(keep).ToArray();
            stack.Clear();
            for (int i = newest.Length - 1; i >= 0; i--) stack.Push(newest[i]);
        }
        DoctorReportPacket current;
        readonly HashSet<int> expanded = new HashSet<int>();
        /// <summary>Check states hidden by clicking their chip at the top.</summary>
        readonly HashSet<State> hidden = new HashSet<State>();
        /// <summary>On the check-up: the one section shown (a mod's id, or the game's), picked from the list at the top. Null for all of them.</summary>
        string modFilter;
        /// <summary>
        /// On the check-up: sections the player folded or unfolded by clicking their heading (true: folded). Kept
        /// while the window is open; a section nobody clicked is folded when it has no warnings or problems.
        /// </summary>
        readonly Dictionary<string, bool> foldedByHand = new Dictionary<string, bool>(StringComparer.Ordinal);
        /// <summary>For each section of the page shown: how many checks, warnings and problems it has.</summary>
        readonly Dictionary<string, (int checks, int warn, int fail)> sectionCounts = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        bool busy;
        string busyText;
        DateTime receivedAt = DateTime.Now;
        bool composing;
        /// <summary>The next report replaces this one instead of going on top (Run checks again).</summary>
        bool replaceNext;

        public override string ToggleKeyCombinationCode => null;
        public override double DrawOrder => 0.25;
        public override double InputOrder => 0.25;

        public GuiDialogDoctor(ICoreClientAPI capi, DoctorReportPacket packet, string version, Action<string, string, string> onAction) : base(capi)
        {
            this.onAction = onAction;
            this.version = version;
            current = packet;
            Compose();
        }

        /// <summary>A new page: on top of the one shown (Back returns to it), or in its place after Run checks again.</summary>
        public void Show(DoctorReportPacket packet)
        {
            if (packet == null) return;
            if (!replaceNext && current != null && current.Key != packet.Key) { history.Push(current); TrimHistory(history, 40); }
            replaceNext = false;
            busy = false;
            busyText = null;
            current = packet;
            receivedAt = DateTime.Now;
            expanded.Clear();
            Compose();
        }

        // ------------------------------------------------------------------ the model

        enum Kind { Check, Heading, Text, Banner }
        enum State { Ok, Warn, Fail, Info }

        sealed class Entry
        {
            public Kind Kind;
            public State State;
            public string Title;      // the check in words, without its ✓/⚠/• mark
            public string Plain;
            public int Index;
            public DoctorLineStyle Style;
            public int Indent;
            /// <summary>On the check-up: the section it is in (a mod's id, or the game's). Null or empty elsewhere.</summary>
            public string Section;
            public readonly List<Detail> Details = new List<Detail>();
            public readonly List<(string kind, string target, string arg, string label)> Actions = new List<(string, string, string, string)>();
            public bool HasFix => Details.Any(d => d.Fix) || Actions.Any(a => a.kind == "fix");
            /// <summary>Graphs on this line (1.9.0): drawn under its text, before its buttons.</summary>
            public readonly List<(string values, string unit, string label)> Graphs = new List<(string, string, string)>();
        }

        sealed class Detail
        {
            public string Text;       // with [[item]] refs, action tokens taken out
            public DoctorLineStyle Style;
            public int Indent;
            public bool Fix;          // a "→ do this" line
            public readonly List<(string kind, string target, string arg, string label)> Actions = new List<(string, string, string, string)>();
        }

        List<Entry> cached;
        DoctorReportPacket cachedFor;

        List<Entry> Entries()
        {
            if (cachedFor == current && cached != null) return cached;
            var entries = new List<Entry>();
            string[] texts = current?.Texts ?? Array.Empty<string>();
            Entry open = null;
            for (int i = 0; i < texts.Length; i++)
            {
                string t = texts[i] ?? "";
                DoctorLineStyle style = current.Styles != null && i < current.Styles.Length ? (DoctorLineStyle)current.Styles[i] : DoctorLineStyle.Text;
                int indent = current.Indents != null && i < current.Indents.Length ? current.Indents[i] : 0;
                string section = current.Sections != null && i < current.Sections.Length ? current.Sections[i] : null;
                if (style == DoctorLineStyle.Check && indent == 0)
                {
                    var e = new Entry { Kind = Kind.Check, Index = i, Style = style, Indent = 0, Section = section };
                    e.State = TakeMark(ref t) ?? State.Info;
                    e.Title = TakeActions(t, e.Actions);
                    e.Plain = DoctorReport.Plain(capi.World, e.Title);
                    entries.Add(e);
                    open = e;
                    continue;
                }
                if (style == DoctorLineStyle.Heading)
                {
                    t = GameFont.Drawable(t);
                    entries.Add(new Entry { Kind = Kind.Heading, Index = i, Title = t, Plain = DoctorReport.Plain(capi.World, t), Style = style, Section = section });
                    open = null;
                    continue;
                }
                bool detail = open != null && (indent > 0 || style == DoctorLineStyle.Note);
                if (detail)
                {
                    var d = new Detail { Style = style, Indent = indent, Fix = t.StartsWith(DoctorReport.AdviceMark, StringComparison.Ordinal) };
                    d.Text = TakeActions(d.Fix ? t.Substring(1).Trim() : t, d.Actions);
                    open.Details.Add(d);
                    continue;
                }
                open = null;
                var plain = new Entry { Kind = i == 0 && (style == DoctorLineStyle.Total || style == DoctorLineStyle.Warn) ? Kind.Banner : Kind.Text, Index = i, Style = style, Indent = indent, Section = section };
                plain.State = style == DoctorLineStyle.Warn ? State.Fail : style == DoctorLineStyle.Total ? State.Ok : State.Info;
                // A line in the colour of a failure or a result needs no mark to say which it is.
                if (style == DoctorLineStyle.Warn || style == DoctorLineStyle.Total) TakeMark(ref t);
                plain.Title = TakeActions(t, plain.Actions);
                plain.Plain = DoctorReport.Plain(capi.World, plain.Title);
                entries.Add(plain);
            }
            // Graphs are drawn, not buttons. One in a card's details is written out in words there.
            foreach (Entry e in entries)
            {
                foreach (var a in e.Actions.Where(a => a.kind == "graph").ToList()) { e.Actions.Remove(a); e.Graphs.Add((a.target, a.arg, a.label)); }
                foreach (Detail d in e.Details)
                    foreach (var a in d.Actions.Where(a => a.kind == "graph").ToList())
                    {
                        d.Actions.Remove(a);
                        d.Text = (d.Text + " " + GameFont.Drawable(DoctorReport.PlainAction(a.kind, a.target, a.arg, a.label))).Trim();
                    }
            }
            // Every card that isn't simply "passed" gets a button that copies it whole, to paste into a report.
            foreach (Entry e in entries)
            {
                if (e.Kind != Kind.Check || e.State == State.Ok || e.Actions.Any(a => a.kind == "copy") || e.Details.Any(d => d.Actions.Any(a => a.kind == "copy"))) continue;
                e.Actions.Add(("copy", CardForReport(texts, e), "", Lang.Get("vintagedoctor:gui-copy-card")));
            }
            // Levels: ✓ passed, • something to know, ⚠ may affect play, ✖ the mod isn't running properly or could crash.
            cachedFor = current;
            cached = entries;
            return entries;
        }

        /// <summary>
        /// A card as text to paste into a report: which doctor and game, the mod it is under, then the card's lines as
        /// the page has them (marks and all, buttons as their words and commands).
        /// </summary>
        string CardForReport(string[] texts, Entry e)
        {
            var sb = new StringBuilder();
            string section = null;
            if (!string.IsNullOrEmpty(e.Section) && current?.SectionTitles != null && current.Sections != null)
            {
                int at = Array.IndexOf(current.Sections, e.Section);
                if (at >= 0 && at < current.SectionTitles.Length) section = current.SectionTitles[at];
            }
            sb.Append("Vintage Doctor ").Append(version).Append(", Vintage Story ").Append(GameVersion.ShortGameVersion);
            if (!string.IsNullOrEmpty(section)) sb.Append(", about ").Append(section);
            sb.Append('\n');
            for (int i = e.Index; i < texts.Length; i++)
            {
                DoctorLineStyle style = current.Styles != null && i < current.Styles.Length ? (DoctorLineStyle)current.Styles[i] : DoctorLineStyle.Text;
                int indent = current.Indents != null && i < current.Indents.Length ? current.Indents[i] : 0;
                if (i > e.Index && !(indent > 0 || style == DoctorLineStyle.Note)) break;
                sb.Append(new string(' ', indent * 2)).Append(DoctorReport.Plain(capi.World, texts[i] ?? "")).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Takes a leading ✓ ⚠ ✖ or • off a line and says which it was; null when the line has none. The window
        /// never draws the marks as text (the game's font can't be counted on for them): a card's colour, dot and
        /// badge say the same.
        /// </summary>
        static State? TakeMark(ref string t)
        {
            State? state = t.StartsWith(DoctorReport.OkMark, StringComparison.Ordinal) ? State.Ok
                : t.StartsWith(DoctorReport.ProblemMark, StringComparison.Ordinal) ? State.Fail
                : t.StartsWith(DoctorReport.WarnMark, StringComparison.Ordinal) ? State.Warn
                : t.StartsWith(DoctorReport.InfoMark, StringComparison.Ordinal) ? State.Info : (State?)null;
            if (state != null) t = t.Substring(1).TrimStart('\uFE0F').Trim();   // font-ok: the "as a picture" sign some keyboards add after a mark, taken off
            return state;
        }

        /// <summary>
        /// Pulls the {{action|arg|label}} tokens out of a line into buttons, leaving the text as the game can draw it
        /// (<see cref="GameFont.Drawable"/>). What a button does is kept as it was sent.
        /// </summary>
        static string TakeActions(string text, List<(string, string, string, string)> actions)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.IndexOf("{{", StringComparison.Ordinal) >= 0)
                text = DoctorReport.ActionToken.Replace(text, m => { actions.Add((m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value)); return ""; }).Trim();
            return GameFont.Drawable(text);
        }

        // ------------------------------------------------------------------ composing

        void Compose()
        {
            ClearComposers();
            // On the check-up every line is in a section: the game's, or a mod's. The list at the top shows one of them alone.
            List<Entry> all = Entries();
            var sections = new List<(string key, string name)>();
            foreach (Entry e in all)
            {
                if (e.Kind != Kind.Heading || string.IsNullOrEmpty(e.Section) || e.Section == DoctorReport.RestSection) continue;
                if (!sections.Any(s => s.key == e.Section)) sections.Add((e.Section, e.Plain));
            }
            if (modFilter != null && !sections.Any(s => s.key == modFilter)) modFilter = null;
            List<Entry> entries = modFilter == null ? all : all.Where(e => string.IsNullOrEmpty(e.Section) || e.Section == modFilter).ToList();
            // A card that is under two mods is one check.
            List<Entry> counted = entries.Where(e => e.Kind == Kind.Check).GroupBy(e => (e.State, e.Plain)).Select(g => g.First()).ToList();
            sectionCounts.Clear();
            foreach (var g in all.Where(e => e.Kind == Kind.Check && !string.IsNullOrEmpty(e.Section)).GroupBy(e => e.Section))
                sectionCounts[g.Key] = (g.Count(), g.Count(e => e.State == State.Warn), g.Count(e => e.State == State.Fail));
            // A folded section shows its heading only (with what is in it); the list at the top showing one section shows it whole.
            if (modFilter == null) entries = entries.Where(e => e.Kind == Kind.Heading || string.IsNullOrEmpty(e.Section) || !Folded(e.Section)).ToList();
            int ok = counted.Count(e => e.State == State.Ok);
            int warn = counted.Count(e => e.State == State.Warn);
            int fail = counted.Count(e => e.State == State.Fail);
            int info = counted.Count(e => e.State == State.Info);
            int checks = counted.Count;

            double top = GuiStyle.TitleBarHeight + 8;
            CairoFont chipFont = CairoFont.WhiteSmallText();
            var chips = new List<(string text, double[] colour, double x, double w)>();
            double cx = 0;
            foreach (var (n, word, colour) in new[] { (ok, Lang.Get("vintagedoctor:gui-passed"), Green), (warn, Lang.Get("vintagedoctor:gui-warnings"), Orange), (fail, Lang.Get("vintagedoctor:gui-problems"), Red), (info, Lang.Get("vintagedoctor:gui-info"), Blue) })
            {
                string text = n + " " + word;
                double w = TextWidth(chipFont, text) + 22;
                chips.Add((text, colour, cx, w));
                cx += w + 10;
            }
            // Run, Back and Copy are all the same width: the widest label plus padding (the game's button grows
            // past a narrower fixed width, which is what made them differ).
            CairoFont buttonFont = CairoFont.SmallButtonText(EnumButtonStyle.Normal);
            double ButtonW = Math.Max(110, new[] { Lang.Get("vintagedoctor:gui-run"), Lang.Get("vintagedoctor:gui-scanning"), Lang.Get("vintagedoctor:gui-home"), Lang.Get("vintagedoctor:gui-back"), Lang.Get("vintagedoctor:gui-copy") }
                .Max(t => TextWidth(buttonFont, t)) + 30);
            // The list of mods sits between the chips and Run checks again, right-aligned next to Run. With big counts
            // the chips grow into them, so then the two go on a row of their own under the chips.
            string[] filterValues = new[] { "" }.Concat(sections.Select(sec => sec.key)).ToArray();
            string[] filterNames = new[] { Lang.Get("vintagedoctor:gui-allmods") }.Concat(sections.Select(sec => sectionCounts.TryGetValue(sec.key, out var n) ? ListName(sec.name, n.fail, n.warn) : sec.name)).ToArray();
            CairoFont filterFont = CairoFont.WhiteSmallText();
            double filterW = sections.Count == 0 ? 0 : Math.Min(280, Math.Max(150, filterNames.Max(name => TextWidth(filterFont, name)) + 46));
            double chipsEnd = chips.Count == 0 ? 0 : chips[chips.Count - 1].x + chips[chips.Count - 1].w;
            bool secondRow = chipsEnd + 12 > Width - ButtonW - 8 - filterW;
            double buttonTop = secondRow ? top + 34 : top - 2;
            ElementBounds runBounds = ElementBounds.Fixed(Width - ButtonW, buttonTop, ButtonW, 30);
            ElementBounds filterBounds = ElementBounds.Fixed(Width - ButtonW - 8 - filterW, buttonTop + 1, Math.Max(1, filterW), 28);
            double barTop = buttonTop + 40;
            ElementBounds barBounds = ElementBounds.Fixed(0, barTop, Width, 6);

            double listTop = barTop + 18;
            ElementBounds listBounds = ElementBounds.Fixed(0, listTop, Width, ListHeight);
            ElementBounds clipBounds = listBounds.ForkBoundingParent();
            ElementBounds scrollbarBounds = ElementBounds.Fixed(Width + 4, listTop, 20, ListHeight);

            double bottom = listTop + ListHeight + 12;
            ElementBounds homeBounds = ElementBounds.Fixed(0, bottom, ButtonW, 30);
            ElementBounds backBounds = ElementBounds.Fixed(ButtonW + 8, bottom, ButtonW, 30);
            // Two lines ("Checked at …" and "N checks") fit; it was 24 high and the second line was cut off.
            ElementBounds lastBounds = ElementBounds.Fixed(2 * ButtonW + 18, bottom - 2, Width - 3 * ButtonW - 28, 40);
            ElementBounds copyBounds = ElementBounds.Fixed(Width - ButtonW, bottom, ButtonW, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(runBounds, filterBounds, barBounds, clipBounds, scrollbarBounds, homeBounds, backBounds, lastBounds, copyBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            composing = true;
            GuiComposer c = capi.Gui.CreateCompo("vintagedoctor-window", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar((string.IsNullOrEmpty(current?.Title) ? Lang.Get("vintagedoctor:title") : GameFont.Drawable(current.Title)) + " " + version, () => TryClose())
                .BeginChildElements(bgBounds);

            // Summary chips and the progress bar.
            var chipStates = new[] { State.Ok, State.Warn, State.Fail, State.Info };
            for (int i = 0; i < chips.Count; i++)
            {
                var chip = chips[i];
                State state = chipStates[i];
                bool off = hidden.Contains(state);
                ElementBounds cb = ElementBounds.Fixed(chip.x, top, chip.w, 26);
                bgBounds.WithChild(cb);
                // Selected (its checks shown): a lighter tint of its colour behind the words; unselected: the plain dark chip.
                double[] colour = chip.colour;
                double[] fill = off ? null : new[] { colour[0], colour[1], colour[2], 0.32 };
                Canvas(c, cb, (ctx, b) => DrawChip(ctx, b, colour, fill));
                c.AddStaticText(chip.text, chipFont.Clone().WithColor(off ? new[] { colour[0], colour[1], colour[2], 0.75 } : colour), ElementBounds.Fixed(chip.x + 11, top + 4, chip.w, 24).WithParent(bgBounds));
                // Click a chip to hide or show the checks of that kind.
                ElementBounds hb = ElementBounds.Fixed(chip.x, top, chip.w, 26);
                bgBounds.WithChild(hb);
                c.AddInteractiveElement(new GuiElementHotspot(capi, hb, () => ToggleFilter(state)));
            }
            if (sections.Count > 0) c.AddDropDown(filterValues, filterNames, Math.Max(0, Array.IndexOf(filterValues, modFilter ?? "")), OnModFilter, filterBounds, filterFont, "modfilter");
            c.AddSmallButton(busy ? Lang.Get("vintagedoctor:gui-scanning") : Lang.Get("vintagedoctor:gui-run"), OnRun, runBounds, EnumButtonStyle.Normal, "run");
            Canvas(c, barBounds, (ctx, b) => DrawBar(ctx, b, busy ? 0.55 : 1));

            // Link-only lines under one heading (the "What are you seeing?" questions, the next steps) become a
            // column of buttons, all the width of the widest, so they line up.
            uniformWidth.Clear();
            var group = new List<Entry>();
            CairoFont buttonLabel = CairoFont.ButtonText();
            void EndGroup()
            {
                if (group.Count > 1)
                {
                    double w = Math.Min(CardW - 8, group.Max(g => g.Actions.Max(a => TextWidth(buttonLabel, GameFont.Drawable(a.label)) + 24)));
                    foreach (Entry g in group) uniformWidth[g] = w;
                }
                group.Clear();
            }
            foreach (Entry e in entries)
            {
                if (e.Kind == Kind.Text && e.Title.Length == 0 && e.Actions.Count > 0 && e.Graphs.Count == 0) group.Add(e);
                else EndGroup();
            }
            EndGroup();

            // The cards.
            ElementBounds content = ElementBounds.Fixed(0, 0, Width, 10);
            c.BeginClip(clipBounds).BeginChildElements(content);
            double y = 0;
            foreach (Entry e in entries)
            {
                if (e.Kind == Kind.Check && hidden.Contains(e.State)) continue;
                y = AddEntry(c, content, e, y);
            }
            if (entries.Count == 0) y = AddRow(c, content, Lang.Get("vintagedoctor:gui-empty"), CairoFont.WhiteSmallText(), 0, y);
            content.fixedHeight = Math.Max(y + 4, ListHeight);
            c.EndChildElements().EndClip()
                .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                .AddSmallButton(Lang.Get("vintagedoctor:gui-home"), OnHome, homeBounds, EnumButtonStyle.Normal, "home")
                .AddSmallButton(Lang.Get("vintagedoctor:gui-back"), OnBack, backBounds, EnumButtonStyle.Normal, "back")
                .AddDynamicText(busy ? (busyText ?? "") : Lang.Get("vintagedoctor:gui-lastrun", receivedAt.ToString("HH:mm:ss"), checks), CairoFont.WhiteDetailText().WithColor(new double[] { 0.72, 0.72, 0.72, 1 }), lastBounds, "last")
                .AddSmallButton(Lang.Get("vintagedoctor:gui-copy"), OnCopy, copyBounds);
            SingleComposer = c.EndChildElements().Compose();
            composing = false;
            SingleComposer.GetButton("back").Enabled = history.Count > 0;
            SingleComposer.GetButton("run").Enabled = !busy;
            GuiElementScrollbar bar = SingleComposer.GetScrollbar("scrollbar");
            bar?.SetHeights((float)ListHeight, (float)content.fixedHeight);
            scrollContent = content;
        }

        ElementBounds scrollContent;

        /// <summary>Height (unscaled GUI units, like ElementBounds.Fixed) that a text needs when wrapped to a width.</summary>
        double TextHeight(CairoFont font, string text, double width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double scale = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
            return capi.Gui.Text.GetMultilineTextHeight(font, text, GuiElement.scaled(width)) / scale + 4;
        }

        /// <summary>Width (unscaled GUI units) of one line of text in a font.</summary>
        double TextWidth(CairoFont font, string text)
        {
            double scale = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
            return font.GetTextExtents(text ?? "").Width / scale;
        }

        /// <summary>Bounds inside the scrolling content, registered as its child so they move when it scrolls.</summary>
        static ElementBounds In(ElementBounds parent, ElementBounds bounds)
        {
            parent.WithChild(bounds);
            return bounds;
        }

        static GuiComposer Canvas(GuiComposer c, ElementBounds bounds, Action<Context, ElementBounds> draw)
        {
            c.AddInteractiveElement(new GuiElementCanvas(c.Api, bounds, draw));
            return c;
        }

        /// <summary>One entry: a card, a section heading, a banner or a plain row. Returns the y below it.</summary>
        double AddEntry(GuiComposer c, ElementBounds parent, Entry e, double y)
        {
            switch (e.Kind)
            {
                case Kind.Heading:
                    {
                        CairoFont f = CairoFont.WhiteSmallishText().WithColor(SectionColour(e.Section)).WithWeight(FontWeight.Bold);
                        y += 6;
                        // On the check-up a section's heading folds and unfolds it: a drawn triangle in front, and
                        // when folded, what is in it after the name.
                        bool foldable = modFilter == null && !string.IsNullOrEmpty(e.Section) && sectionCounts.ContainsKey(e.Section);
                        if (!foldable)
                        {
                            y = AddRow(c, parent, e.Plain, f, 2, y);
                        }
                        else
                        {
                            bool folded = Folded(e.Section);
                            double top = y;
                            var parts = new List<RichTextComponentBase> { new RichTextComponent(capi, e.Plain, f) { VerticalAlign = EnumVerticalAlign.Middle } };
                            if (folded)
                            {
                                var (checks, warn, fail) = sectionCounts[e.Section];
                                string what = warn + fail == 0 ? Lang.Get("vintagedoctor:gui-folded-fine", checks) : Lang.Get("vintagedoctor:gui-folded", checks, warn, fail);
                                parts.Add(new RichTextComponent(capi, "   " + GameFont.Drawable(what), CairoFont.WhiteDetailText().WithColor(new double[] { 0.72, 0.72, 0.72, 1 })) { VerticalAlign = EnumVerticalAlign.Middle });
                            }
                            double h = TextHeight(f, e.Plain, CardW - 24);
                            c.AddRichtext(parts.ToArray(), In(parent, ElementBounds.Fixed(22, y, CardW - 22, h + 2)));
                            double rowH = h;
                            ElementBounds tri = In(parent, ElementBounds.Fixed(2, top, 18, rowH));
                            double[] triColour = SectionColour(e.Section);
                            Canvas(c, tri, (ctx, b) => DrawTriangle(ctx, GuiElement.scaled(9), b.OuterHeight / 2, !folded, triColour));
                            string key = e.Section;
                            c.AddInteractiveElement(new GuiElementHotspot(capi, In(parent, ElementBounds.Fixed(CardX, top, CardW, rowH)), () => ToggleSection(key)));
                            y += h;
                        }
                        ElementBounds line = In(parent, ElementBounds.Fixed(CardX, y - 2, CardW, 2));
                        double[] lineColour = SectionColour(e.Section);
                        Canvas(c, line, (ctx, b) => { ctx.SetSourceRGBA(lineColour[0], lineColour[1], lineColour[2], 0.35); ctx.Rectangle(0, 0, b.OuterWidth, GuiElement.scaled(1)); ctx.Fill(); });
                        return y + 6;
                    }
                case Kind.Banner:
                    {
                        double[] colour = e.State == State.Fail ? Red : Green;
                        CairoFont f = CairoFont.WhiteSmallText().WithColor(colour).WithWeight(FontWeight.Bold);
                        double h = TextHeight(f, e.Plain, CardW - 28) + 14;
                        ElementBounds box = In(parent, ElementBounds.Fixed(CardX, y, CardW, h));
                        Canvas(c, box, (ctx, b) => DrawCard(ctx, b, colour, DetailBg));
                        AddText(c, parent, e.Title, f, CardX + 16, y + 7, CardW - 28);
                        y = AddActions(c, parent, e.Actions, CardX + 16, y + h - 4, CardW - 28, dense: true);
                        return y + 8;
                    }
                case Kind.Text:
                    {
                        CairoFont f = e.Style == DoctorLineStyle.Note ? CairoFont.WhiteDetailText().WithColor(new double[] { 0.72, 0.72, 0.72, 1 })
                            : e.Style == DoctorLineStyle.Warn ? CairoFont.WhiteSmallText().WithColor(Red)
                            : e.Style == DoctorLineStyle.Total ? CairoFont.WhiteSmallText().WithColor(Green).WithWeight(FontWeight.Bold)
                            : CairoFont.WhiteSmallText();
                        if (e.Title.Length > 0) y = AddText(c, parent, e.Title, f, CardX + 4 + e.Indent * 16, y, CardW - 8 - e.Indent * 16);
                        foreach (var g in e.Graphs) y = AddGraph(c, parent, g.values, g.unit, g.label, CardX + 4 + e.Indent * 16, y + 4, CardW - 8 - e.Indent * 16);
                        if (e.Actions.Count > 0) y = AddActions(c, parent, e.Actions, CardX + 4 + e.Indent * 16, y + 2, CardW - 8, dense: e.Title.Length == 0, uniform: uniformWidth.TryGetValue(e, out double uw) ? uw : (double?)null);
                        return y + 2;
                    }
                default:
                    return AddCard(c, parent, e, y);
            }
        }

        double AddCard(GuiComposer c, ElementBounds parent, Entry e, double y)
        {
            double[] colour = e.State == State.Ok ? Green : e.State == State.Warn ? Orange : e.State == State.Fail ? Red : Grey;
            bool canOpen = e.Details.Count > 0 || e.Actions.Count > 0;
            bool open = canOpen && expanded.Contains(e.Index);
            CairoFont titleFont = CairoFont.WhiteSmallText();
            double titleW = CardW - TitleX - BadgeW - 24;
            double titleH = TextHeight(titleFont, e.Plain, titleW);
            double headH = Math.Max(40, titleH + 14);

            // The details' height first, so the card can be drawn in one piece.
            double detailH = 0;
            var layout = new List<(Detail d, double y, double h, CairoFont f)>();
            var arrows = new List<(double x, double y)>();   // where an advice line's arrow is drawn, from the card's corner
            if (open)
            {
                double dy = 8;
                foreach (Detail d in e.Details)
                {
                    CairoFont f = d.Fix ? CairoFont.WhiteSmallText().WithColor(Advice)
                        : d.Style == DoctorLineStyle.Warn ? CairoFont.WhiteSmallText().WithColor(Red)
                        : d.Style == DoctorLineStyle.Total ? CairoFont.WhiteSmallText().WithColor(Green)
                        : CairoFont.WhiteDetailText().WithColor(new double[] { 0.82, 0.80, 0.76, 1 });
                    string text = DoctorReport.Plain(capi.World, d.Text);
                    double inset = Math.Max(0, d.Indent - 1) * 14, lead = d.Fix ? AdviceLead : 0;
                    double h = text.Length > 0 ? TextHeight(f, text, CardW - TitleX - 20 - inset - lead) : 0;
                    if (d.Fix && text.Length > 0) arrows.Add((TitleX + inset, headH + dy + LineHeight(f, d.Text) / 2));
                    layout.Add((d, dy, h, f));
                    double aw = CardW - (TitleX + Math.Max(0, d.Indent - 1) * 14) - 20;
                    dy += h + (d.Actions.Count > 0 ? ActionsHeight(d.Actions, aw, null) + 4 : 0);
                }
                if (e.Actions.Count > 0) dy += ActionsHeight(e.Actions, CardW - TitleX - 20, null) + 4;
                detailH = dy + 6;
            }
            double cardH = headH + detailH;
            ElementBounds box = In(parent, ElementBounds.Fixed(CardX, y, CardW, cardH));
            double hh = headH;
            // The card draws its own triangle left of the dot (it opens) and the arrow before each advice line: the
            // game's font can't be counted on for those characters. The title is right of the dot; the whole header
            // (but not the badge) opens and closes the card.
            Canvas(c, box, (ctx, b) => DrawCheckCard(ctx, b, colour, hh, open, canOpen, arrows));
            var comps = new List<RichTextComponentBase>();
            AddRichParts(comps, e.Title, titleFont, -1);
            ElementBounds titleBounds = In(parent, ElementBounds.Fixed(CardX + TitleX, y + (headH - titleH) / 2 + 1, titleW, titleH + 4));
            c.AddRichtext(comps.ToArray(), titleBounds);
            if (canOpen)
            {
                int index = e.Index;
                // The header opens and closes the card, except where an item icon or name sits: a click there opens
                // the item's report (the icon component takes the click but never marks it handled).
                RichTextComponentBase[] parts = comps.ToArray();
                c.AddInteractiveElement(new GuiElementHotspot(capi, In(parent, ElementBounds.Fixed(CardX, y, CardW - BadgeW - 24, headH)), () =>
                {
                    foreach (RichTextComponentBase part in parts)
                    {
                        if ((part is ItemstackTextComponent || part is LinkTextComponent) && part.BoundsPerLine != null && part.UseMouseOverCursor(titleBounds)) return;
                    }
                    Toggle(index);
                }));
            }

            // The badge.
            string badge = e.State == State.Ok ? Lang.Get("vintagedoctor:gui-badge-ok") : e.State == State.Warn ? Lang.Get("vintagedoctor:gui-badge-warn") : e.State == State.Fail ? Lang.Get("vintagedoctor:gui-badge-fail") : Lang.Get("vintagedoctor:gui-badge-info");
            CairoFont badgeFont = CairoFont.WhiteDetailText().WithColor(colour);
            double bw = TextWidth(badgeFont, badge) + 18;
            ElementBounds bb = In(parent, ElementBounds.Fixed(CardX + CardW - bw - 12, y + (headH - 22) / 2, bw, 22));
            Canvas(c, bb, (ctx, b) => DrawChip(ctx, b, colour));
            c.AddRichtext(new RichTextComponentBase[] { new RichTextComponent(capi, badge, badgeFont) }, In(parent, ElementBounds.Fixed(CardX + CardW - bw - 12 + 9, y + (headH - 22) / 2 + 3, bw, 20)));

            if (open)
            {
                double baseY = y + headH;
                foreach (var (d, dy, h, f) in layout)
                {
                    double x = TitleX + Math.Max(0, d.Indent - 1) * 14, lead = d.Fix ? AdviceLead : 0;
                    if (d.Text.Length > 0) AddText(c, parent, d.Text, f, x + lead, baseY + dy, CardW - x - 20 - lead);
                    if (d.Actions.Count > 0) AddActions(c, parent, d.Actions, x, baseY + dy + h + 2, CardW - x - 20, dense: true);
                }
                if (e.Actions.Count > 0) AddActions(c, parent, e.Actions, TitleX, baseY + detailH - ActionsHeight(e.Actions, CardW - TitleX - 20, null) - 8, CardW - TitleX - 20, dense: true);
            }
            return y + cardH + 8;
        }

        /// <summary>Text with [[item]] refs as icon + link; returns the y below it.</summary>
        double AddText(GuiComposer c, ElementBounds parent, string text, CairoFont font, double x, double y, double width)
        {
            string plain = DoctorReport.Plain(capi.World, text);
            double h = TextHeight(font, plain, width);
            // An item reference draws a 24 px icon before its name: taller than a text line, and wider than the plain
            // name the height was measured on. Count the lines with the icons' width added, then give each line the
            // icon's height, or the text would run into whatever sits below it.
            int refs = DoctorReport.RefToken.Matches(text).Count;
            if (refs > 0)
            {
                double scale = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
                double lineH = font.GetFontExtents().Height / scale;
                double measured = TextHeight(font, plain + new string('W', 4 * refs), width) - 4;
                int lines = Math.Max(1, (int)Math.Round(measured / Math.Max(1, lineH)));
                h = lines * Math.Max(lineH, 30) + 4;
            }
            var comps = new List<RichTextComponentBase>();
            AddRichParts(comps, text, font, -1);
            c.AddRichtext(comps.ToArray(), In(parent, ElementBounds.Fixed(x, y, width, h + 2)));
            return y + h;
        }

        /// <summary>The height of one line of text in a font (unscaled GUI units); an item's icon makes its line 30 high.</summary>
        double LineHeight(CairoFont font, string text)
        {
            double scale = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
            double lineH = font.GetFontExtents().Height / scale;
            return DoctorReport.RefToken.IsMatch(text ?? "") ? Math.Max(lineH, 30) : lineH;
        }

        double AddRow(GuiComposer c, ElementBounds parent, string text, CairoFont font, double x, double y)
        {
            double h = TextHeight(font, text, CardW - x);
            c.AddRichtext(new RichTextComponentBase[] { new RichTextComponent(capi, text, font) }, In(parent, ElementBounds.Fixed(x, y, CardW - x, h + 2)));
            return y + h;
        }

        /// <summary>The pieces of a line: text, and each [[key]] as an item icon and name that opens that item's report.</summary>
        void AddRichParts(List<RichTextComponentBase> comps, string text, CairoFont font, int toggleIndex)
        {
            CairoFont linkFont = font.Clone().WithColor(Blue);
            int pos = 0;
            foreach (System.Text.RegularExpressions.Match m in DoctorReport.RefToken.Matches(text))
            {
                if (m.Index > pos) comps.Add(Piece(text.Substring(pos, m.Index - pos), font, toggleIndex));
                string key = m.Groups[1].Value;
                CollectibleObject col = DoctorReport.Resolve(capi.World, key);
                if (col?.Code != null)
                {
                    ItemStack stack = null;
                    try { stack = new ItemStack(col); } catch { }
                    if (stack != null) comps.Add(new ItemstackTextComponent(capi, stack, 24, 3, EnumFloat.Inline, _ => OpenItem(key)) { VerticalAlign = EnumVerticalAlign.Middle });
                    var link = new LinkTextComponent(capi, DoctorReport.NameOf(col), linkFont, _ => OpenItem(key)) { VerticalAlign = EnumVerticalAlign.Middle };
                    link.Font = linkFont.Clone();
                    comps.Add(link);
                }
                else comps.Add(Piece(key.Substring(key.IndexOf(':') + 1), font, toggleIndex));
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) comps.Add(Piece(text.Substring(pos), font, toggleIndex));
        }

        RichTextComponentBase Piece(string text, CairoFont font, int toggleIndex)
        {
            if (toggleIndex < 0) return new RichTextComponent(capi, text, font) { VerticalAlign = EnumVerticalAlign.Middle };
            var link = new LinkTextComponent(capi, text, font, _ => Toggle(toggleIndex)) { VerticalAlign = EnumVerticalAlign.Middle };
            link.Font = font.Clone();
            return link;
        }

        /// <summary>A row of buttons for a line's actions: fixes and the reports it opens. Returns the y below them.</summary>
        /// <summary>Buttons in one group all get the same width, so a list of questions lines up.</summary>
        readonly Dictionary<Entry, double> uniformWidth = new Dictionary<Entry, double>();

        double AddActions(GuiComposer c, ElementBounds parent, List<(string kind, string target, string arg, string label)> actions, double x, double y, double width, bool dense, double? uniform = null)
        {
            if (actions.Count == 0) return y;
            CairoFont f = CairoFont.ButtonText();
            double bx = x;
            double rowY = y;
            foreach (var (kind, target, arg, label) in actions)
            {
                string text = ButtonLabel(f, label, width);
                double w = Math.Min(width, uniform ?? (TextWidth(f, text) + 24));
                if (bx > x && bx + w > x + width) { bx = x; rowY += 32; }
                string k = kind, t = target, g = arg;
                ElementBounds bounds = In(parent, ElementBounds.Fixed(bx, rowY, w, 28));
                // A button that changes something has its words in green; the others open a page.
                if (kind == "fix") c.AddButton(text, () => { Act(k, t, g); return true; }, bounds, CairoFont.SmallButtonText(EnumButtonStyle.Small).WithColor(FixText), EnumButtonStyle.Small);
                else c.AddSmallButton(text, () => { Act(k, t, g); return true; }, bounds, EnumButtonStyle.Small);
                bx += w + 8;
            }
            return rowY + 30;
        }

        /// <summary>The height AddActions will take for these buttons in this width (they wrap onto more rows).</summary>
        double ActionsHeight(List<(string kind, string target, string arg, string label)> actions, double width, double? uniform)
        {
            if (actions.Count == 0) return 0;
            CairoFont f = CairoFont.ButtonText();
            double bx = 0, rows = 1;
            foreach (var (kind, target, arg, label) in actions)
            {
                double w = Math.Min(width, uniform ?? (TextWidth(f, ButtonLabel(f, label, width)) + 24));
                if (bx > 0 && bx + w > width) { bx = 0; rows++; }
                bx += w + 8;
            }
            return rows * 32 - 2;
        }

        /// <summary>
        /// A button's label as the game can draw it, cut with … when it wouldn't fit the row, because the game's
        /// button grows to its label and would run out of the card (seen with the long "Health checks: …" line).
        /// </summary>
        string ButtonLabel(CairoFont f, string label, double width)
        {
            string text = GameFont.Drawable(label);
            double max = width - 24;
            if (TextWidth(f, text) <= max) return text;
            int cut = text.Length;
            while (cut > 4 && TextWidth(f, text.Substring(0, cut) + "…") > max) cut -= Math.Max(1, cut / 20);
            return text.Substring(0, cut).TrimEnd() + "…";
        }

        // ------------------------------------------------------------------ drawing

        static void DrawChip(Context ctx, ElementBounds b, double[] colour, double[] fill = null)
        {
            double r = GuiElement.scaled(4);
            GuiElement.RoundRectangle(ctx, 0, 0, b.OuterWidth, b.OuterHeight, r);
            if (fill != null) ctx.SetSourceRGBA(fill[0], fill[1], fill[2], fill[3]); else ctx.SetSourceRGBA(0, 0, 0, 0.3);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(colour[0], colour[1], colour[2], 0.7);
            ctx.LineWidth = GuiElement.scaled(1);
            ctx.Stroke();
        }

        static void DrawBar(Context ctx, ElementBounds b, double fraction)
        {
            double r = GuiElement.scaled(3);
            GuiElement.RoundRectangle(ctx, 0, 0, b.OuterWidth, b.OuterHeight, r);
            ctx.SetSourceRGBA(0, 0, 0, 0.45);
            ctx.Fill();
            if (fraction <= 0) return;
            GuiElement.RoundRectangle(ctx, 0, 0, b.OuterWidth * Math.Min(1, fraction), b.OuterHeight, r);
            ctx.SetSourceRGBA(Gold[0], Gold[1], Gold[2], 0.9);
            ctx.Fill();
        }

        static void DrawCard(Context ctx, ElementBounds b, double[] colour, double[] bg)
        {
            double r = GuiElement.scaled(4);
            GuiElement.RoundRectangle(ctx, 0, 0, b.OuterWidth, b.OuterHeight, r);
            ctx.SetSourceRGBA(bg[0], bg[1], bg[2], bg[3]);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0, 0, 0, 0.35);
            ctx.LineWidth = GuiElement.scaled(1);
            ctx.Stroke();
            ctx.Rectangle(0, 0, GuiElement.scaled(3), b.OuterHeight);
            ctx.SetSourceRGBA(colour[0], colour[1], colour[2], 1);
            ctx.Fill();
        }

        /// <summary>A small triangle centred on (tx, cy): pointing right when closed, down when open.</summary>
        // ------------------------------------------------------------------ graphs (1.9.0)

        public const double GraphH = 118;
        static readonly double[][] GraphColours = { new[] { 0.95, 0.62, 0.25, 1 }, new[] { 0.40, 0.70, 0.95, 1 } };

        double AddGraph(GuiComposer c, ElementBounds parent, string values, string unit, string label, double x, double y, double width)
        {
            List<List<double>> lines = DoctorReport.GraphValues(values);
            string title = GameFont.Drawable(label);
            Canvas(c, In(parent, ElementBounds.Fixed(x, y, width, GraphH)), (ctx, b) => DrawGraph(ctx, b, lines, unit, title));
            return y + GraphH + 6;
        }

        /// <summary>A graph: a dark panel, a line per series (orange then blue), the highest value at the top left and the title.</summary>
        static void DrawGraph(Context ctx, ElementBounds b, List<List<double>> lines, string unit, string title)
        {
            double w = b.OuterWidth, h = b.OuterHeight, pad = GuiElement.scaled(6), text = GuiElement.scaled(12);
            ctx.SetSourceRGBA(0, 0, 0, 0.35);
            GuiElement.RoundRectangle(ctx, 0, 0, w, h, GuiElement.scaled(4));
            ctx.Fill();
            double max = 0;
            foreach (var l in lines) foreach (double v in l) max = Math.Max(max, v);
            if (max <= 0) max = 1;
            ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Normal);
            ctx.SetFontSize(text);
            ctx.SetSourceRGBA(0.9, 0.88, 0.84, 1);
            ctx.MoveTo(pad, pad + text);
            ctx.ShowText(title + "   " + GameFont.Drawable(DoctorReport.GraphNumber(max, unit)));
            double top = pad * 2 + text, bottom = h - pad, left = pad, right = w - pad;
            // A faint line at the middle, so heights can be read.
            ctx.SetSourceRGBA(1, 1, 1, 0.08);
            ctx.LineWidth = 1;
            ctx.MoveTo(left, (top + bottom) / 2); ctx.LineTo(right, (top + bottom) / 2); ctx.Stroke();
            for (int s = 0; s < lines.Count && s < GraphColours.Length; s++)
            {
                List<double> v = lines[s];
                if (v.Count == 0) continue;
                double[] col = GraphColours[s];
                ctx.SetSourceRGBA(col[0], col[1], col[2], col[3]);
                ctx.LineWidth = GuiElement.scaled(1.6);
                for (int i = 0; i < v.Count; i++)
                {
                    double px = v.Count == 1 ? right : left + (right - left) * i / (v.Count - 1);
                    double py = bottom - (bottom - top) * v[i] / max;
                    if (i == 0) ctx.MoveTo(px, py); else ctx.LineTo(px, py);
                }
                ctx.Stroke();
            }
        }

        static void DrawTriangle(Context ctx, double tx, double cy, bool open, double[] colour)
        {
            double s = GuiElement.scaled(5);
            ctx.NewPath();
            if (open) { ctx.MoveTo(tx - s, cy - s * 0.6); ctx.LineTo(tx + s, cy - s * 0.6); ctx.LineTo(tx, cy + s * 0.8); }
            else { ctx.MoveTo(tx - s * 0.6, cy - s); ctx.LineTo(tx + s * 0.8, cy); ctx.LineTo(tx - s * 0.6, cy + s); }
            ctx.ClosePath();
            ctx.SetSourceRGBA(colour[0], colour[1], colour[2], colour[3]);
            ctx.Fill();
        }

        /// <summary>
        /// A check-up section's heading in the colour of the worst in it: red with a problem, orange with a warning,
        /// gold otherwise, the way the cards are coloured. That is also the order the sections are in.
        /// </summary>
        double[] SectionColour(string section)
        {
            if (string.IsNullOrEmpty(section) || !sectionCounts.TryGetValue(section, out var n)) return Gold;
            return Worst(n.fail, n.warn) == 2 ? Red : Worst(n.fail, n.warn) == 1 ? Orange : Gold;
        }

        /// <summary>2 with a problem, 1 with a warning, 0 otherwise.</summary>
        internal static int Worst(int problems, int warnings) => problems > 0 ? 2 : warnings > 0 ? 1 : 0;

        /// <summary>A section's name in the list of mods: marked [x] or [!] like a card, since the list can't colour one entry.</summary>
        internal static string ListName(string name, int problems, int warnings)
        {
            int w = Worst(problems, warnings);
            return w == 2 ? GameFont.Problem + " " + name : w == 1 ? GameFont.Warn + " " + name : name;
        }

        /// <summary>Whether a section of the check-up is folded: as the player left it, else folded when nothing in it needs attention.</summary>
        bool Folded(string section)
        {
            if (string.IsNullOrEmpty(section)) return false;
            if (foldedByHand.TryGetValue(section, out bool byHand)) return byHand;
            return FoldedAtFirst(section, sectionCounts.TryGetValue(section, out var n) ? n.warn + n.fail : 0);
        }

        /// <summary>
        /// A mod's section (and the one for the mods nothing was found against) starts folded when it has no warnings
        /// or problems; the game's own, with the doctor's pages in it, starts open.
        /// </summary>
        internal static bool FoldedAtFirst(string section, int needAttention) => section != DoctorReport.GameSection && needAttention == 0;

        void ToggleSection(string section)
        {
            foldedByHand[section] = !Folded(section);
            Recompose();
        }

        static void DrawCheckCard(Context ctx, ElementBounds b, double[] colour, double headH, bool open, bool canOpen, List<(double x, double y)> arrows)
        {
            DrawCard(ctx, b, colour, CardBg);
            if (open)
            {
                // A faint line under the header, and a slightly darker body.
                double hy = GuiElement.scaled(headH);
                ctx.Rectangle(GuiElement.scaled(3), hy, b.OuterWidth - GuiElement.scaled(3), b.OuterHeight - GuiElement.scaled(headH));
                ctx.SetSourceRGBA(0, 0, 0, 0.12);
                ctx.Fill();
                ctx.SetSourceRGBA(1, 1, 1, 0.08);
                ctx.Rectangle(GuiElement.scaled(12), hy, b.OuterWidth - GuiElement.scaled(24), GuiElement.scaled(1));
                ctx.Fill();
            }
            // The status dot, with a soft glow.
            double cx = GuiElement.scaled(38), cy = GuiElement.scaled(headH / 2), rad = GuiElement.scaled(6);
            ctx.Arc(cx, cy, rad * 1.8, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(colour[0], colour[1], colour[2], 0.18);
            ctx.Fill();
            ctx.Arc(cx, cy, rad, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(colour[0], colour[1], colour[2], 1);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0, 0, 0, 0.5);
            ctx.LineWidth = GuiElement.scaled(1);
            ctx.Stroke();
            // Left of the dot, a small triangle on a card that opens: pointing right while closed, down while open.
            if (canOpen) DrawTriangle(ctx, GuiElement.scaled(19), cy, open, new[] { 0.85, 0.8, 0.7, 1 });
            // An arrow before each line that says what to do.
            if (open && arrows != null)
            {
                foreach (var (ax, ay) in arrows)
                {
                    double x0 = GuiElement.scaled(ax + 1), x1 = GuiElement.scaled(ax + 12), my = GuiElement.scaled(ay), head = GuiElement.scaled(3.5);
                    ctx.NewPath();
                    ctx.MoveTo(x0, my); ctx.LineTo(x1, my);
                    ctx.MoveTo(x1 - head, my - head); ctx.LineTo(x1, my); ctx.LineTo(x1 - head, my + head);
                    ctx.SetSourceRGBA(Advice[0], Advice[1], Advice[2], 0.9);
                    ctx.LineWidth = GuiElement.scaled(1.5);
                    ctx.LineCap = LineCap.Round;
                    ctx.LineJoin = LineJoin.Round;
                    ctx.Stroke();
                }
            }
        }

        // ------------------------------------------------------------------ actions

        void Toggle(int index)
        {
            if (!expanded.Remove(index)) expanded.Add(index);
            Recompose();
        }

        void ToggleFilter(State state)
        {
            if (!hidden.Remove(state)) hidden.Add(state);
            Recompose();
        }

        /// <summary>A mod (or the game, or all of them) was picked from the list at the top of the check-up.</summary>
        void OnModFilter(string key, bool selected)
        {
            if (composing) return;
            string next = string.IsNullOrEmpty(key) ? null : key;
            if (next == modFilter) return;
            modFilter = next;
            Recompose(toTop: true);
        }

        /// <summary>Rebuilds the window on the next frame, keeping the scroll position: rebuilding inside the click that asked for it tears down the element being clicked.</summary>
        void Recompose(bool toTop = false)
        {
            float scroll = toTop ? 0 : SingleComposer?.GetScrollbar("scrollbar")?.CurrentYPosition ?? 0;
            capi.Event.EnqueueMainThreadTask(() =>
            {
                if (!IsOpened()) return;
                Compose();
                GuiElementScrollbar bar = SingleComposer?.GetScrollbar("scrollbar");
                if (bar != null)
                {
                    float max = (float)Math.Max(0, (scrollContent?.fixedHeight ?? 0) - ListHeight);
                    bar.CurrentYPosition = Math.Min(scroll, max);
                    OnScroll(bar.CurrentYPosition);
                }
            }, "vintagedoctor-window");
        }

        void Act(string kind, string target, string arg)
        {
            // Copy, links and folders answer at once; a page or a fix takes a moment.
            if (kind == "open" || kind == "fix") SetBusy(Lang.Get("vintagedoctor:gui-scanning"));
            onAction?.Invoke(kind, target ?? "", arg ?? "");
        }

        void OpenItem(string key)
        {
            SetBusy(Lang.Get("vintagedoctor:gui-scanning"));
            onAction?.Invoke("open", Checks.TopicItem, key);
        }

        /// <summary>Stops waiting and says something in the status line (a failure, "copied").</summary>
        /// <summary>The measured run's countdown, shown in the status line in place of what was there; null when none runs.</summary>
        string profiling, beforeProfiling;
        public void SetProfiling(string text)
        {
            if (text == profiling) return;
            profiling = text;
            if (busy) return;
            SingleComposer?.GetDynamicText("last")?.SetNewText(text ?? beforeProfiling ?? "");
        }

        public void SetIdle(string text)
        {
            busy = false;
            busyText = null;
            beforeProfiling = text;
            SingleComposer?.GetDynamicText("last")?.SetNewText(profiling ?? text ?? "");
            if (SingleComposer?.GetButton("run") is GuiElementTextButton run) run.Enabled = true;
        }

        public void SetBusy(string text)
        {
            busy = true;
            busyText = text;
            SingleComposer?.GetDynamicText("last")?.SetNewText(text ?? "");
            if (SingleComposer?.GetButton("run") is GuiElementTextButton run) run.Enabled = false;
        }

        void CurrentKey(out string topic, out string arg)
        {
            string key = current?.Key ?? "";
            int bar = key.IndexOf('|');
            topic = bar < 0 ? key : key.Substring(0, bar);
            arg = bar < 0 ? "" : key.Substring(bar + 1);
        }

        /// <summary>Run checks again: the same page, with the logs and folders read afresh.</summary>
        bool OnRun()
        {
            replaceNext = true;
            CurrentKey(out string topic, out string arg);
            SetBusy(Lang.Get("vintagedoctor:gui-scanning"));
            onAction?.Invoke("refresh", topic, arg);
            return true;
        }

        /// <summary>Back to the check-up (the doctor's first page), forgetting the trail of pages.</summary>
        bool OnHome()
        {
            history.Clear();
            replaceNext = true;
            SetBusy(Lang.Get("vintagedoctor:gui-scanning"));
            onAction?.Invoke("open", Pages.Overview, "");
            return true;
        }

        bool OnBack()
        {
            if (history.Count == 0) return true;
            current = history.Pop();
            expanded.Clear();
            busy = false;
            // Deferred: this runs from the button being clicked, and rebuilding now would tear that button down under itself.
            capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) Compose(); }, "vintagedoctor-back");
            return true;
        }

        bool OnCopy()
        {
            var sb = new StringBuilder();
            string[] texts = current?.Texts ?? Array.Empty<string>();
            for (int i = 0; i < texts.Length; i++)
            {
                // With one mod picked, the page is that mod's part of it.
                string section = current.Sections != null && i < current.Sections.Length ? current.Sections[i] : null;
                if (modFilter != null && !string.IsNullOrEmpty(section) && section != modFilter) continue;
                int indent = current.Indents != null && i < current.Indents.Length ? current.Indents[i] : 0;
                sb.Append(' ', indent * 2).Append(DoctorReport.Plain(capi.World, texts[i], labelsOnly: true)).Append('\n');
            }
            try { capi.Forms.SetClipboardText(sb.ToString().TrimEnd()); }
            catch (Exception ex) { capi.Logger.Warning("[vintagedoctor] The clipboard is not available: {0}", ex.Message); return true; }
            SingleComposer?.GetDynamicText("last")?.SetNewText(Lang.Get("vintagedoctor:gui-copied"));
            return true;
        }

        void OnScroll(float value)
        {
            if (scrollContent == null) return;
            scrollContent.fixedY = -value;
            scrollContent.CalcWorldBounds();
        }
    }
}
