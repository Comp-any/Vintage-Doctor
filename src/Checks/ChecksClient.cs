using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VintageDoctor
{
    public static partial class Checks
    {
        public const string TopicHotkeys = "hotkeys";

        /// <summary>The player's own game: graphics card, frame rate, the settings that cost the most, memory.</summary>
        public static void ClientPerformance(DoctorContext x, bool full)
        {
            var capi = x.Api as ICoreClientAPI;
            if (capi == null) return;
            if (full) x.Section(x.T("sec-perf-client"));
            LogScan log = x.Ev.Log;

            // The graphics card the game is drawing with.
            string gpu = log?.GpuRenderer;
            if (!string.IsNullOrEmpty(gpu))
            {
                string low = gpu.ToLowerInvariant();
                if (low.Contains("llvmpipe") || low.Contains("software") || low.Contains("gdi generic") || low.Contains("microsoft basic") || low.Contains("swiftshader"))
                {
                    x.Problem(x.T("gfx-software", gpu));
                    x.Explain(x.T("gfx-software-why"));
                    x.Advice(x.T("gfx-software-advice"));
                }
                else if (full) x.Ok(x.T("gfx-card", gpu));
                if (full && log.GpuVersion != null) x.Detail(x.T("gfx-version", log.GpuVendor ?? "?", log.GpuVersion));
            }

            // Frame rate, and what would raise it.
            var (avg, p95, max, samples) = x.Doctor.Ticks.Recent();
            var s = new ClientSettingsView(capi);
            if (samples >= 60)
            {
                double fps = 1000.0 / Math.Max(0.1, avg), lowFps = 1000.0 / Math.Max(0.1, p95);
                if (fps < 30)
                {
                    x.Warn(x.T("gfx-fps-low", N(fps, "0"), N(lowFps, "0")));
                    x.Explain(x.T("gfx-fps-why"));
                    Suggest(x, s);
                }
                else if (lowFps < 20)
                {
                    x.Warn(x.T("gfx-fps-stutter", N(fps, "0"), N(lowFps, "0")));
                    x.Explain(x.T("gfx-stutter-why"));
                    Suggest(x, s);
                }
                else x.Ok(x.T("gfx-fps-ok", N(fps, "0"), N(lowFps, "0")));
                x.Detail(x.T("gfx-fps-detail", N(avg), N(p95), N(max, "0")));
                ProfileButtons(x);
            }
            else if (full) x.Info(x.T("gfx-fps-early"));

            if (s.Bool("glDebugMode") == true)
            {
                x.Warn(x.T("gfx-gldebug"));
                x.Explain(x.T("gfx-gldebug-why"));
                x.FixButton("doctor:setting", "bool:glDebugMode=false", x.T("btn-setting-off", "glDebugMode"));
            }
            if (s.Bool("occlusionculling") == false)
            {
                x.Info(x.T("gfx-occlusion"));
                x.FixButton("doctor:setting", "bool:occlusionculling=true", x.T("btn-setting-on", "occlusionculling"));
            }
            if (full) x.Line(x.T("gfx-settings", s.Summary()), DoctorLineStyle.Note);

            Memory(x, full);
            if (full || x.Doctor.Profile.HasResult || x.Doctor.Profile.Running) ProfileResult(x, full);
        }

        /// <summary>The settings that cost the most frames, each with a button that turns it down.</summary>
        static void Suggest(DoctorContext x, ClientSettingsView s)
        {
            int view = s.Int("viewDistance") ?? 0;
            int[] steps = { 1024, 768, 512, 384, 320, 256, 192, 128 };
            int lower = steps.FirstOrDefault(v => v < view);
            if (view > 128 && lower > 0) x.FixButton("doctor:setting", "int:viewDistance=" + lower, x.T("btn-viewdistance", view, lower));
            if ((s.Int("shadowMapQuality") ?? 0) > 0) x.FixButton("doctor:setting", "int:shadowMapQuality=0", x.T("btn-setting-off", x.T("set-shadows")));
            if ((s.Int("ssaoQuality") ?? 0) > 0) x.FixButton("doctor:setting", "int:ssaoQuality=0", x.T("btn-setting-off", x.T("set-ssao")));
            if ((s.Int("godRays") ?? 0) > 0) x.FixButton("doctor:setting", "int:godRays=0", x.T("btn-setting-off", x.T("set-godrays")));
            if ((s.Float("ssaa") ?? 1f) > 1.01f) x.FixButton("doctor:setting", "float:ssaa=1", x.T("btn-setting-off", x.T("set-ssaa")));
            if (s.Bool("bloom") == true) x.FixButton("doctor:setting", "bool:bloom=false", x.T("btn-setting-off", x.T("set-bloom")));
            x.Advice(x.T("gfx-suggest-advice"));
        }

        /// <summary>Reads the game's settings without ever throwing for one that doesn't exist in this version.</summary>
        public sealed class ClientSettingsView
        {
            readonly ICoreClientAPI capi;
            public ClientSettingsView(ICoreClientAPI capi) { this.capi = capi; }
            public int? Int(string key) { try { return capi.Settings.Int[key]; } catch { return null; } }
            public bool? Bool(string key) { try { return capi.Settings.Bool[key]; } catch { return null; } }
            public float? Float(string key) { try { return capi.Settings.Float[key]; } catch { return null; } }

            public string Summary()
            {
                var parts = new List<string>();
                foreach (string k in new[] { "viewDistance", "maxFps", "vsyncMode", "shadowMapQuality", "ssaoQuality", "godRays", "particleLevel", "maxDynamicLights", "optimizeRamMode" })
                {
                    int? v = Int(k);
                    if (v != null) parts.Add(k + "=" + v);
                }
                foreach (string k in new[] { "bloom", "fxaa", "occlusionculling", "dynamicColorGrading", "liquidFoamAndShinyEffect", "smoothShadows" })
                {
                    bool? v = Bool(k);
                    if (v != null) parts.Add(k + "=" + (v.Value ? "on" : "off"));
                }
                float? ssaa = Float("ssaa");
                if (ssaa != null) parts.Add("ssaa=" + N(ssaa.Value, "0.##"));
                return string.Join(", ", parts);
            }
        }

        /// <summary>Two controls on the same key: only one of them gets the key press.</summary>
        public static void Hotkeys(DoctorContext x, bool full)
        {
            var capi = x.Api as ICoreClientAPI;
            if (capi == null) return;
            if (full) x.Section(x.T("sec-hotkeys"));
            var clashes = new List<string>();
            try
            {
                var all = capi.Input.HotKeys.Where(kv => kv.Value?.CurrentMapping != null && kv.Value.CurrentMapping.KeyCode > 0).ToList();
                for (int i = 0; i < all.Count; i++)
                    for (int j = i + 1; j < all.Count; j++)
                    {
                        HotKey a = all[i].Value, b = all[j].Value;
                        KeyCombination k = a.CurrentMapping, o = b.CurrentMapping;
                        if (k.KeyCode != o.KeyCode || k.SecondKeyCode != o.SecondKeyCode || k.Ctrl != o.Ctrl || k.Alt != o.Alt || k.Shift != o.Shift) continue;
                        // A window key and a movement key on one letter is how the game itself works; the same kind twice is a clash.
                        if (Kind(a.KeyCombinationType) != Kind(b.KeyCombinationType)) continue;
                        clashes.Add(k + ": " + a.Name + " = " + b.Name);
                    }
            }
            catch (Exception e) { x.Info(x.T("hotkeys-error", e.Message)); return; }
            if (clashes.Count == 0) { x.Ok(x.T("hotkeys-ok")); return; }
            x.Warn(x.T("hotkeys-clash", clashes.Count));
            foreach (string c in clashes.Take(full ? 60 : 8)) x.Detail(c);
            if (!full && clashes.Count > 8) x.Detail(x.T("more-lines", clashes.Count - 8));
            x.Explain(x.T("hotkeys-why"));
            x.Advice(x.T("hotkeys-advice"));
        }

        static int Kind(HotkeyType t)
        {
            switch (t)
            {
                case HotkeyType.CharacterControls:
                case HotkeyType.MovementControls:
                case HotkeyType.MouseControls:
                case HotkeyType.MouseModifiers:
                    return 1;
                default:
                    return 0;
            }
        }

        /// <summary>The player's mods against the server's: a different version of the same mod is a classic cause of odd behaviour.</summary>
        public static void ModsVersusServer(DoctorContext x, bool full)
        {
            string[] server = x.Doctor.ServerMods;
            if (server == null || server.Length == 0 || x.SameProcess) return;
            var theirs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string s in server)
            {
                int at = s.IndexOf('@');
                if (at > 0) theirs[s.Substring(0, at)] = s.Substring(at + 1);
            }
            var different = new List<string>();
            var onlyMine = new List<string>();
            foreach (ModEntry m in x.Ev.Mods.UserMods)
            {
                if (theirs.TryGetValue(m.Id, out string v)) { if (v != m.Version) different.Add(x.T("versus-line", m.Name, m.Version, v)); }
                else onlyMine.Add(m.Name);
            }
            if (different.Count > 0)
            {
                x.Warn(x.T("versus-different", different.Count));
                foreach (string d in different.Take(full ? 60 : 8)) x.Detail(d);
                x.Explain(x.T("versus-why"));
                x.Advice(x.T("versus-advice"));
            }
            else x.Ok(x.T("versus-ok", theirs.Count));
            if (onlyMine.Count > 0)
            {
                x.Info(x.T("versus-onlymine", onlyMine.Count));
                x.Detail(string.Join(", ", onlyMine.Take(40)));
                x.Explain(x.T("versus-onlymine-why"));
            }
        }
    }
}
