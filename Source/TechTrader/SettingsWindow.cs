using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using TechTraderPrevention.Core;
using UnityEngine;
using Verse;

namespace TechTraderPrevention
{
    internal sealed class SettingsWindow
    {
        private int tab;
        private Vector2 scroll;
        private float height = 1500f;
        private readonly string[] tabs = { "Trading", "Colony research", "Equipment salvage", "Overrides", "Status & help" };
        private static TechTraderSettings S => TechTraderMod.Settings;

        public void Draw(Rect rect)
        {
            float width = (rect.width - 4 * 5f) / 5;
            for (int i = 0; i < tabs.Length; i++)
                if (Widgets.ButtonText(new Rect(rect.x + i * (width + 5), rect.y, width, 32), tabs[i]))
                { tab = i; scroll = Vector2.zero; height = 1700; }
            Rect outer = new Rect(rect.x, rect.y + 42, rect.width, rect.height - 88);
            Rect inner = new Rect(0, 0, outer.width - 20, Math.Max(outer.height, height));
            Widgets.BeginScrollView(outer, ref scroll, inner);
            var list = new Listing_Standard();
            list.Begin(inner);
            switch (tab)
            {
                case 0: Trading(list); break;
                case 1: Research(list); break;
                case 2: Salvage(list); break;
                case 3: Overrides(list); break;
                default: Status(list); break;
            }
            height = list.CurHeight + 24;
            list.End();
            Widgets.EndScrollView();
            Rect footer = new Rect(rect.x, rect.yMax - 36, rect.width, 32);
            if (Widgets.ButtonText(new Rect(footer.x, footer.y, 150, 32), "Save settings"))
                TechTraderMod.Instance.WriteSettings();
            if (Widgets.ButtonText(new Rect(footer.xMax - 160, footer.y, 160, 32), "Restore all defaults"))
            {
                var defaults = new TechTraderSettings();
                foreach (FieldInfo field in typeof(TechTraderSettings).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    field.SetValue(S, field.GetValue(defaults));
                TechTraderMod.Changed();
            }
        }

        private static void Trading(Listing_Standard l)
        {
            Heading(l, "Trader restrictions");
            Help(l, "Default: the colony's technology level or lower, with no higher-tier allowance. Research determines the colony level. Existing stock is hidden from purchases, never deleted; selling your own items remains possible.");
            Check(l, "Enable trader restrictions", ref S.TraderEnabled);
            Choice(l, "Technology rule", S.TraderRule, value => S.TraderRule = value);
            Help(l, "AtOrBelow allows lower tiers. Exact admits only the colony tier. Range uses both allowances below. Allowances do not affect Exact.");
            IntSlider(l, "Allowed tiers above colony", ref S.AllowedTechAbove, 0, 7);
            IntSlider(l, "Allowed tiers below colony (Range)", ref S.AllowedTechBelow, 0, 7);
            Check(l, "Apply to arriving caravans and caravan purchases", ref S.FilterCaravans);
            Check(l, "Apply to settlement purchases", ref S.FilterSettlements);
            Check(l, "Apply to orbital trading", ref S.FilterOrbitalTraders);
            Check(l, "Apply to other/modded standard traders", ref S.FilterOtherTraders);
            Check(l, "Filter individual items by their technology level", ref S.FilterTraderItems);
            Check(l, "Allow goods with Undefined technology", ref S.AllowUndefinedItemTech);
            Check(l, "Also filter traded pawns (animals / slaves)", ref S.FilterPawnTrade);
            Help(l, "Silver and Royal Favor are preserved. Pawn filtering is off by default for compatibility; optional content can use exemptions on the Overrides tab.");
            Heading(l, "Caravan and orbital behavior");
            Check(l, "Replace disallowed caravan factions when a valid alternative exists", ref S.ReplaceDisallowedCaravans);
            Check(l, "Allow the original faction when no replacement exists", ref S.AllowOriginalWhenNoReplacement);
            Help(l, "With fallback off, an event without an eligible faction is cancelled. Replacement reuses RimWorld's faction suitability rules rather than choosing an arbitrary faction.");
            Check(l, "Preserve quest-linked trader factions", ref S.PreserveQuestTraders);
            Help(l, "Preserves scripted quest identities; their purchasable stock still follows faction and item purchase rules. Disable only if you want strict rules to take priority over quest visits.");
            Check(l, "Exempt factionless traders from faction rules", ref S.AllowFactionlessTraders);
            Choice(l, "Assumed tech of factionless orbital traders", S.OrbitalTraderTechLevel, value => S.OrbitalTraderTechLevel = value);
            Help(l, "The assumed orbital tier is used when factionless exemption is off. Stock filtering still applies with exemption on. Custom factionless traders without a recognized ship type are blocked when exemption is off.");
        }

        private static void Research(Listing_Standard l)
        {
            Heading(l, "How colony technology is determined");
            Choice(l, "Method", S.ColonyTechMode, value => S.ColonyTechMode = value);
            Help(l, "TierCompletion: unlock tiers after researching enough of them. HighestCompleted: use your highest finished project. Faction: use the faction's existing tech level, including changes made by another mod. Manual: use your selected tier. This mod never changes your faction definition or research costs.");
            Choice(l, "Manual colony tier", S.ManualColonyTech, value => S.ManualColonyTech = value);
            Choice(l, "Minimum colony tier", S.MinimumColonyTech, value => S.MinimumColonyTech = value);
            Choice(l, "Maximum colony tier", S.MaximumColonyTech, value => S.MaximumColonyTech = value);
            Check(l, "Use the faction tech level as a minimum floor", ref S.UseFactionTechAsFloor);
            Heading(l, "Tier completion rules");
            Choice(l, "Measure completion by", S.CompletionMetric, value => S.CompletionMetric = value);
            Percent(l, "Default completion needed per tier", ref S.CompletionThreshold);
            IntSlider(l, "Minimum finished projects in a qualifying tier", ref S.MinimumCompletedProjects, 0, 100);
            Check(l, "Require earlier tiers above the minimum to qualify first", ref S.RequireLowerTiers);
            Check(l, "A tier with no eligible projects blocks progression", ref S.EmptyTierBlocks);
            Help(l, "By default, tiers without eligible research are skipped and do not unlock a level by themselves. The configured minimum tier is available without research. Costs use base research points, so storyteller/research-speed penalties do not distort weighting.");
            for (int tier = 2; tier <= 7; tier++)
            {
                bool custom = S.TierThresholds.ContainsKey(tier);
                bool before = custom;
                l.CheckboxLabeled("Custom threshold: " + (TechLevel)tier, ref custom);
                if (custom != before)
                {
                    if (custom) S.TierThresholds[tier] = S.CompletionThreshold;
                    else S.TierThresholds.Remove(tier);
                    TechTraderMod.Changed();
                }
                if (custom)
                {
                    float value = S.TierThresholds[tier];
                    float next = DrawPercent(l, "  Required research", value);
                    if (next != value) { S.TierThresholds[tier] = next; TechTraderMod.Changed(); }
                }
            }
            Heading(l, "Mod and DLC research");
            Check(l, "Include unfinished hidden research", ref S.IncludeHiddenResearch);
            Check(l, "Include zero-cost research", ref S.IncludeZeroCostResearch);
            Check(l, "Include separate knowledge-based research (Anomaly)", ref S.IncludeSpecialResearch);
            Help(l, "Only loaded definitions are considered; no DLC is required. Finished hidden discoveries still count. Hidden-research detection is available on 1.5/1.6; 1.4 has no equivalent IsHidden API. Other mods' normal research is included automatically; exclude individual projects or entire packages on the Overrides tab.");
            IntSlider(l, "Refresh interval (game ticks; 60 = one normal-speed second)", ref S.ResearchRefreshTicks, 60, 60000);
            Help(l, "Completion/reset hooks refresh immediately; periodic checks catch mods that change research through other paths. Research is evaluated on demand and cached, not rescanned every tick.");
        }

        private static void Salvage(Listing_Standard l)
        {
            Heading(l, "Optional equipment breakage");
            Help(l, "Equipped weapons and worn apparel can break into their actual crafting materials. This is off by default. The trigger captures equipped gear before it drops; loose items, carried inventory and world-caravan events are not scanned.");
            Check(l, "Enable equipment salvage", ref S.SalvageEnabled);
            Choice(l, "Trigger", S.SalvageTriggerMode, value => S.SalvageTriggerMode = value);
            Check(l, "Downing affects weapons only", ref S.SalvageDownedWeaponsOnly);
            Help(l, "Death only cannot catch a weapon already dropped during downing. Select Downed or DeathOrDowned to cover that event. Retained apparel can face a later death roll if downed apparel is enabled.");
            Check(l, "Include equipped weapons", ref S.SalvageWeapons);
            Check(l, "Include worn apparel", ref S.SalvageApparel);
            Check(l, "Only equipment above the allowed tech ceiling", ref S.SalvageOnlyAboveColonyTech);
            Help(l, "Ceiling = colony tech + the allowed tiers above it. Turn this off to apply the mechanic to equipment at every tier, independently of trader restrictions.");
            Percent(l, "Break chance per eligible item", ref S.SalvageBreakChance);
            Heading(l, "Which pawns are affected");
            Check(l, "Hostile pawns", ref S.SalvageHostilePawns);
            Check(l, "Player pawns (including colony slaves)", ref S.SalvagePlayerPawns);
            Check(l, "Prisoners", ref S.SalvagePrisoners);
            Check(l, "Neutral pawns", ref S.SalvageNeutralPawns);
            Check(l, "Allied pawns", ref S.SalvageAlliedPawns);
            Check(l, "Humanlike pawns only", ref S.SalvageHumanlikeOnly);
            Heading(l, "How much material survives");
            Check(l, "Choose a random survival percentage for each broken item", ref S.SalvageRandomYield);
            Percent(l, "Fixed survival percentage", ref S.SalvageFixedYield);
            Percent(l, "Random survival minimum", ref S.SalvageMinYield);
            Percent(l, "Random survival maximum", ref S.SalvageMaxYield);
            Check(l, "Scale survival by remaining item hit points", ref S.SalvageScaleByHitPoints);
            Help(l, "A single random percentage applies to all materials from one item; each item gets its own roll. Whole material quantities round down. Example: 80 steel + 3 components at 50% yields 40 steel + 1 component. A 0% result destroys the item and yields nothing.");
            Heading(l, "Compatibility and exclusions");
            Check(l, "Use an unambiguous crafting recipe when normal material costs are missing", ref S.SalvageRecipeFallback);
            Check(l, "Preserve relics", ref S.SalvagePreserveRelics);
            Check(l, "Preserve biocoded equipment", ref S.SalvagePreserveBiocoded);
            Check(l, "Preserve quest-tagged equipment", ref S.SalvagePreserveQuestItems);
            Help(l, "Stuffable gear returns its actual stuff. Materials are reconstructed from item/recipe definitions, not a historical record of its crafting bill. Ambiguous recipes, unknown costs and failed placement preserve the original item. Material stacks are staged before equipment is removed. Add modded equipment exclusions on the Overrides tab.");
        }

        private static void Overrides(Listing_Standard l)
        {
            Heading(l, "Definition-level customization");
            Help(l, "Use exact internal defNames, not translated labels. Separate entries with commas, semicolons or newlines. Tech overrides use DefName=Industrial (or a tier number 0–7). Tech overrides in settings take priority over mod extensions. Exemption/block flags combine; a block wins. Essential currency remains protected.");
            Entry(l, "Item technology overrides", ref S.ItemTechOverridesCsv);
            Entry(l, "Faction definition technology overrides", ref S.FactionTechOverridesCsv);
            Entry(l, "Research project technology overrides", ref S.ResearchTechOverridesCsv);
            Entry(l, "Trade-exempt item definitions", ref S.TradeExemptThingDefsCsv);
            Entry(l, "Always-blocked trade item definitions", ref S.TradeBlockedThingDefsCsv);
            Entry(l, "Trade-exempt faction definitions", ref S.TradeExemptFactionDefsCsv);
            Entry(l, "Always-blocked faction definitions", ref S.TradeBlockedFactionDefsCsv);
            Entry(l, "Equipment excluded from salvage", ref S.SalvageExcludedThingDefsCsv);
            Entry(l, "Research projects excluded from colony tech", ref S.IgnoredResearchDefsCsv);
            Entry(l, "Research package IDs excluded from colony tech", ref S.IgnoredResearchPackagesCsv);
            foreach (string warning in DefPolicy.ConfigurationWarnings) Help(l, warning);
            Help(l, "Missing definitions are harmless when a DLC or mod is not installed. The README includes an XML extension for mod authors who want to ship their own compatibility rules.");
        }

        private static void Status(Listing_Standard l)
        {
            Heading(l, "Current colony");
            if (Current.Game == null) Help(l, "Load a colony to see its research-derived technology and tier breakdown.");
            else
            {
                l.Label("Effective colony technology: " + ColonyTech.CurrentLevel);
                l.Label("Method: " + S.ColonyTechMode + " | Faction: " + (Faction.OfPlayer?.def?.techLevel.ToString() ?? "unknown"));
                ResearchResult result = ColonyTech.CurrentResult;
                if (result != null)
                    foreach (TierProgress t in result.Tiers)
                        if (t.TotalProjects > 0)
                            l.Label(((TechLevel)t.Tier) + ": " + t.CompletedProjects + "/" + t.TotalProjects + " projects; "
                                + t.Completion.ToString("P0") + " weighted completion; needs " + t.Threshold.ToString("P0")
                                + (t.Qualifies ? " — qualifies" : " — incomplete"));
                if (l.ButtonText("Recalculate now")) ColonyTech.Invalidate();
            }
            Heading(l, "Compatibility behavior");
            Help(l, "RimWorld 1.4, 1.5 and 1.6 build targets. Harmony is required; DLC is optional. The mod reads currently loaded definitions and does not edit faction tech or research costs globally. Use Faction mode with a mod that already manages faction tech.");
            Help(l, "Standard trade interfaces are filtered without destroying trader stock. Mods that replace the trade system entirely may need an adapter. Quest-linked trader identities, currency and pawn trading are preserved by default. Reopen an already-open trade window after changing settings.");
            Help(l, "Salvage is disabled by default. Defaults target hostile humanlike pawns' equipment above the allowed tech ceiling, with a 50% per-item break chance and 50% material survival. Relics, biocoded items and uncertain crafting costs are preserved.");
            Check(l, "Enable developer diagnostics", ref S.DeveloperLogging);
            Help(l, "Settings apply to every colony on this installation. The effective research tier is recalculated per game and is not written into faction definitions. Keep only this rewritten mod enabled; remove the two legacy variants.");
        }

        private static void Heading(Listing_Standard l, string text) { l.Gap(12); l.Label(text); l.GapLine(); }
        private static void Help(Listing_Standard l, string text) { l.Label(text); l.Gap(6); }
        private static void Check(Listing_Standard l, string text, ref bool value)
        { bool old = value; l.CheckboxLabeled(text, ref value); if (old != value) TechTraderMod.Changed(); }
        private static float DrawPercent(Listing_Standard l, string text, float value)
        { l.Label(text + ": " + value.ToString("P0")); return (float)Math.Round(l.Slider(value, 0, 1) * 100) / 100; }
        private static void Percent(Listing_Standard l, string text, ref float value)
        { float old = value; value = DrawPercent(l, text, value); if (old != value) TechTraderMod.Changed(); }
        private static void IntSlider(Listing_Standard l, string text, ref int value, int min, int max)
        { int old = value; l.Label(text + ": " + value); value = (int)Math.Round(l.Slider(value, min, max)); if (old != value) TechTraderMod.Changed(); }
        private static void Entry(Listing_Standard l, string label, ref string value)
        { l.Label(label); string next = l.TextEntry(value ?? "", 2); if (next != value) { value = next; TechTraderMod.Changed(); } l.Gap(6); }
        private static void Choice<T>(Listing_Standard l, string label, T value, Action<T> set) where T : struct
        {
            if (!l.ButtonText(label + ": " + value)) return;
            var options = new List<FloatMenuOption>();
            foreach (T candidate in Enum.GetValues(typeof(T)))
            { T selection = candidate; options.Add(new FloatMenuOption(selection.ToString(), () => { set(selection); TechTraderMod.Changed(); })); }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
