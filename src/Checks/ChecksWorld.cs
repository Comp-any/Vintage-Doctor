using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageDoctor
{
    public static partial class Checks
    {
        public const string TopicWorld = "world", TopicItem = "item", TopicBlock = "block", TopicCraft = "craft";

        /// <summary>The world and where it is kept: free disk space, things left behind by mods that are gone, the pace of time.</summary>
        public static void WorldChecks(DoctorContext x, bool full)
        {
            var sapi = x.Api as ICoreServerAPI;
            if (sapi == null) return;
            if (full) x.Section(x.T("sec-world"));

            // Free space where the world is saved: a save that can't be written is how worlds get damaged.
            try
            {
                var drive = new DriveInfo(Path.GetFullPath(GamePaths.DataPath));
                long free = drive.AvailableFreeSpace;
                long save = 0;
                try { string file = sapi.WorldManager.CurrentWorldFilepath; if (!string.IsNullOrEmpty(file) && File.Exists(file)) save = new FileInfo(file).Length; } catch { }
                if (free < 1024L * 1024 * 1024) { x.Problem(x.T("world-disk-critical", Size(free))); x.Explain(x.T("world-disk-why")); x.Advice(x.T("world-disk-advice")); }
                else if (free < 5L * 1024 * 1024 * 1024) { x.Warn(x.T("world-disk-low", Size(free))); x.Explain(x.T("world-disk-why")); }
                else x.Ok(x.T("world-disk-ok", Size(free)));
                if (save > 0) x.Detail(x.T("world-save-size", Size(save)));
            }
            catch (Exception e) { if (full) x.Line(x.T("world-disk-unknown", e.Message), DoctorLineStyle.Note); }

            // Blocks and items the save still holds from mods that are no longer here.
            try
            {
                var missing = new Dictionary<string, int>(StringComparer.Ordinal);
                var examples = new Dictionary<string, string>(StringComparer.Ordinal);
                int blocks = 0, items = 0;
                foreach (Block b in sapi.World.Blocks)
                {
                    if (b == null || !b.IsMissing || b.Code == null) continue;
                    blocks++;
                    missing[b.Code.Domain] = missing.TryGetValue(b.Code.Domain, out int n) ? n + 1 : 1;
                    if (!examples.ContainsKey(b.Code.Domain)) examples[b.Code.Domain] = b.Code.ToString();
                }
                foreach (Item i in sapi.World.Items)
                {
                    if (i == null || !i.IsMissing || i.Code == null) continue;
                    items++;
                    missing[i.Code.Domain] = missing.TryGetValue(i.Code.Domain, out int n) ? n + 1 : 1;
                    if (!examples.ContainsKey(i.Code.Domain)) examples[i.Code.Domain] = i.Code.ToString();
                }
                if (blocks + items > 0)
                {
                    x.Warn(x.T("world-missing", blocks, items, string.Join(", ", missing.OrderByDescending(kv => kv.Value).Take(full ? 30 : 6).Select(kv => kv.Key + " (" + kv.Value + ")"))));
                    foreach (var kv in missing.OrderByDescending(kv => kv.Value).Take(full ? 30 : 6))
                    {
                        bool installed = x.Ev.Mods.Has(kv.Key);
                        x.Detail(installed ? x.T("world-missing-changed", x.ModName(kv.Key), kv.Value, examples[kv.Key]) : x.T("world-missing-gone", kv.Key, kv.Value, examples[kv.Key]));
                    }
                    x.Explain(x.T("world-missing-why"));
                    x.Advice(x.T("world-missing-advice"));
                }
                else if (full) x.Ok(x.T("world-missing-none"));
            }
            catch (Exception e) { if (full) x.Line("missing blocks: " + e.Message, DoctorLineStyle.Note); }

            // Time running at another pace changes how often everything timed comes round.
            try
            {
                float speed = sapi.World.Calendar?.SpeedOfTime ?? 60;
                if (Math.Abs(speed - 60) > 0.5f)
                {
                    x.Info(x.T("world-time", N(speed / 60.0, "0.##")));
                    x.Explain(x.T("world-time-why"));
                }
                else if (full) x.Ok(x.T("world-time-ok"));
            }
            catch { }
        }

        // ---------------------------------------------------------------- one block or item

        static CollectibleObject FindCollectible(DoctorContext x, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            code = code.Trim();
            CollectibleObject byRef = DoctorReport.Resolve(x.Api.World, code);
            if (byRef != null) return byRef;
            if (code.IndexOf(':') < 0) code = "game:" + code;
            try
            {
                var loc = new AssetLocation(code);
                return (CollectibleObject)x.Api.World.GetItem(loc) ?? x.Api.World.GetBlock(loc);
            }
            catch { return null; }
        }

        public static void ItemPage(DoctorContext x, string code)
        {
            CollectibleObject c = string.IsNullOrWhiteSpace(code) || code == "hand" ? x.HeldItem?.Collectible : FindCollectible(x, code);
            x.Section(x.T("sec-item"));
            if (c?.Code == null)
            {
                x.Info(string.IsNullOrWhiteSpace(code) || code == "hand" ? x.T("item-holdit") : x.T("item-notfound", code));
                x.Explain(x.T("item-help"));
                return;
            }
            CollectibleCard(x, c);
            if (!x.Compact)
            {
                x.Section(x.T("sec-next"));
                x.Line(DoctorReport.Open(TopicCraft, c.Code.ToString(), x.T("btn-craft")) + " " + DoctorReport.Open("overview", "", x.T("btn-home")));
            }
        }

        static void CollectibleCard(DoctorContext x, CollectibleObject c)
        {
            string domain = c.Code.Domain;
            bool isBlock = c is Block;
            string type = c.GetType().FullName ?? "?";
            string typeMod = x.Ev.Mods.ModOfType(type);
            if (c.IsMissing)
            {
                x.Problem(x.T("item-missing", c.Code.ToString()));
                x.Explain(x.Ev.Mods.Has(domain) ? x.T("item-missing-why-changed", x.ModName(domain)) : x.T("item-missing-why-gone", domain));
                x.Advice(x.T("world-missing-advice"));
                if (!x.Ev.Mods.Has(domain) && !ModSnapshot.IsGameMod(domain)) x.UrlButton(ModDbSearch(domain), x.T("btn-find", domain));
                return;
            }
            x.Info(DoctorReport.Ref(c) + " " + x.T("item-title", ModSnapshot.IsGameMod(domain) ? x.T("the-game") : x.ModName(domain)));
            x.Detail(x.T("item-kind", isBlock ? x.T("item-block") : x.T("item-item"), c.Code.ToString(), string.IsNullOrEmpty(c.Class) ? "-" : c.Class, type));
            if (typeMod != null && typeMod != domain) x.Detail(x.T("item-typemod", x.ModName(typeMod)));
            try
            {
                var behaviors = (c.CollectibleBehaviors ?? Array.Empty<CollectibleBehavior>()).Select(b => b.GetType().Name).ToList();
                if (isBlock) behaviors.AddRange(((Block)c).BlockBehaviors?.Select(b => b.GetType().Name) ?? Enumerable.Empty<string>());
                if (behaviors.Count > 0) x.Detail(x.T("item-behaviors", string.Join(", ", behaviors.Distinct().Take(20))));
                if (isBlock && !string.IsNullOrEmpty(((Block)c).EntityClass)) x.Detail(x.T("item-entityclass", ((Block)c).EntityClass));
            }
            catch { }
            if (!ModSnapshot.IsGameMod(domain) && x.Ev.Mods.Has(domain)) ModButtons(x, domain, false);

            // Anything the log says about this very thing.
            if (x.Ev.Log != null)
            {
                string code = c.Code.ToString();
                var hits = x.Ev.Log.Issues.Where(i => i.Message.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0).Take(6).ToList();
                if (hits.Count > 0)
                {
                    x.Warn(x.T("item-inlog", hits.Count));
                    foreach (LogIssue i in hits) x.Detail("[" + i.Level + "] " + LogScan.Shorten(Anonymise.Line(i.Message), 300));
                }
                else x.Ok(x.T("item-notinlog"));
            }
        }

        public static void BlockPage(DoctorContext x)
        {
            x.Section(x.T("sec-block"));
            BlockPos pos = x.LookedAt;
            if (pos == null) { x.Info(x.T("block-lookat")); return; }
            Block block;
            try { block = x.Api.World.BlockAccessor.GetBlock(pos); }
            catch (Exception e) { x.Info(x.T("block-error", e.Message)); return; }
            if (block?.Code == null || block.Id == 0) { x.Info(x.T("block-lookat")); return; }
            CollectibleCard(x, block);
            try
            {
                BlockEntity be = x.Api.World.BlockAccessor.GetBlockEntity(pos);
                if (be != null)
                {
                    string type = be.GetType().FullName ?? "?";
                    string mod = x.Ev.Mods.ModOfType(type);
                    x.Info(x.T("block-entity", type, mod != null ? x.ModName(mod) : x.T("the-game")));
                    var behaviors = be.Behaviors?.Select(b => b.GetType().Name).ToList();
                    if (behaviors != null && behaviors.Count > 0) x.Detail(x.T("item-behaviors", string.Join(", ", behaviors)));
                }
                else if (!string.IsNullOrEmpty(block.EntityClass))
                {
                    x.Warn(x.T("block-noentity", block.EntityClass));
                    x.Explain(x.T("block-noentity-why"));
                }
            }
            catch (Exception e) { x.Line("block entity: " + e.Message, DoctorLineStyle.Note); }
        }

        // ---------------------------------------------------------------- why can't I craft this?

        public static void CraftPage(DoctorContext x, string code)
        {
            CollectibleObject c = string.IsNullOrWhiteSpace(code) || code == "hand" ? x.HeldItem?.Collectible : FindCollectible(x, code);
            x.Section(x.T("sec-craft"));
            if (c?.Code == null)
            {
                x.Info(string.IsNullOrWhiteSpace(code) || code == "hand" ? x.T("craft-holdit") : x.T("item-notfound", code));
                x.Explain(x.T("craft-help"));
                return;
            }
            List<GridRecipe> recipes;
            try
            {
                recipes = x.Api.World.GridRecipes.Where(r => r?.Output != null && (r.Output.ResolvedItemStack?.Collectible == c || (r.Output.ResolvedItemStack == null && r.Output.Code != null && r.Output.Code.Equals(c.Code)))).ToList();
            }
            catch (Exception e) { x.Info(x.T("craft-error", e.Message)); return; }
            x.Line(DoctorReport.Ref(c) + " " + x.T("craft-count", recipes.Count), DoctorLineStyle.Text);
            if (recipes.Count == 0)
            {
                x.Info(x.T("craft-none"));
                x.Explain(x.T("craft-none-why"));
                if (x.Ev.Log != null && x.Ev.Log.Unresolved.TryGetValue(c.Code.Domain, out int unresolved))
                {
                    x.Warn(x.T("craft-unresolved", unresolved, x.ModName(c.Code.Domain)));
                    x.Explain(x.T("start-unresolved-why"));
                }
                return;
            }
            int n = 0;
            foreach (GridRecipe r in recipes.Take(10))
            {
                n++;
                string needs = Ingredients(r);
                string trait = r.RequiresTrait;
                bool? hasTrait = string.IsNullOrEmpty(trait) || x.Player == null ? (bool?)null : HasTrait(x, trait);
                if (!r.Enabled) { x.Info(x.T("craft-disabled", n, needs)); x.Explain(x.T("craft-disabled-why")); }
                else if (!string.IsNullOrEmpty(trait) && hasTrait == false) { x.Warn(x.T("craft-trait-missing", n, trait, needs)); x.Explain(x.T("craft-trait-why")); }
                else if (!string.IsNullOrEmpty(trait)) { x.Info(x.T("craft-trait", n, trait, needs)); x.Explain(x.T("craft-trait-why")); }
                else x.Ok(x.T("craft-recipe", n, needs));
                if (r.Name != null) x.Detail(x.T("craft-from", r.Name.ToString()));
            }
            if (recipes.Count > 10) x.Line(x.T("more-lines", recipes.Count - 10));
            RecipeShadows(x, c, true);
            x.Line(x.T("craft-tips"));
        }

        static string Ingredients(GridRecipe r)
        {
            try
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                var order = new List<string>();
                foreach (CraftingRecipeIngredient ing in r.ResolvedIngredients ?? Array.Empty<CraftingRecipeIngredient>())
                {
                    if (ing == null) continue;
                    CollectibleObject c = ing.ResolvedItemStack?.Collectible;
                    string code = ing.Code?.ToString();
                    // An ingredient matched by tags has no code of its own: its own description says what it takes.
                    if (c == null && (code == null || code == "*:*")) code = LogScan.Shorten(ing.ToString() ?? "?", 70);
                    string key = (c != null ? DoctorReport.Ref(c) : code ?? "?") + (ing.IsTool ? " (tool)" : "");
                    if (!counts.ContainsKey(key)) { counts[key] = 0; order.Add(key); }
                    counts[key] += Math.Max(1, ing.Quantity);
                }
                return order.Count == 0 ? "?" : string.Join(", ", order.Select(k => (counts[k] > 1 ? counts[k] + "× " : "") + k));
            }
            catch { return "?"; }
        }

        /// <summary>Whether the asker's character has a trait, asked of the game's character system without tying the doctor to it; null if that can't be told.</summary>
        static bool? HasTrait(DoctorContext x, string trait)
        {
            try
            {
                ModSystem sys = x.Api.ModLoader.GetModSystem("Vintagestory.GameContent.CharacterSystem");
                var method = sys?.GetType().GetMethod("HasTrait", new[] { typeof(IPlayer), typeof(string) });
                if (method == null) return null;
                return method.Invoke(sys, new object[] { x.Player, trait }) as bool?;
            }
            catch { return null; }
        }
    }
}
