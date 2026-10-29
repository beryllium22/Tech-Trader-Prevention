using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TechTraderPrevention
{
    // Optional XML extension for other mods; no dependency on any expansion or content pack.
    public sealed class TechTraderDefExtension : DefModExtension
    {
        public int techLevelOverride = -1;
        public bool tradeExempt;
        public bool tradeBlocked;
        public bool salvageExcluded;
        public bool excludeResearch;
    }

    public static class DefPolicy
    {
        private static int revision = -1;
        private static Dictionary<string, TechLevel> itemTech, factionTech, researchTech;
        private static HashSet<string> itemAllow, itemDeny, factionAllow, factionDeny, salvageDeny, researchDeny, packageDeny;
        private static readonly List<string> errors = new List<string>();
        public static IReadOnlyList<string> ConfigurationWarnings { get { Ensure(); return errors; } }

        public static TechLevel EffectiveTech(ThingDef def)
        { Ensure(); return Effective(def, def?.techLevel ?? TechLevel.Undefined, itemTech); }

        public static TechLevel EffectiveTech(Faction faction)
        { Ensure(); return Effective(faction?.def, faction?.def?.techLevel ?? TechLevel.Undefined, factionTech); }

        public static TechLevel EffectiveTech(ResearchProjectDef def)
        { Ensure(); return Effective(def, def?.techLevel ?? TechLevel.Undefined, researchTech); }

        private static TechLevel Effective(Def def, TechLevel fallback, Dictionary<string, TechLevel> map)
        {
            if (def == null) return fallback;
            if (map.TryGetValue(def.defName, out TechLevel setting)) return setting;
            int extension = def.GetModExtension<TechTraderDefExtension>()?.techLevelOverride ?? -1;
            return extension >= 0 && extension <= 7 ? (TechLevel)extension : fallback;
        }

        public static bool TradeExempt(ThingDef def)
        { Ensure(); return def != null && (itemAllow.Contains(def.defName) || def.GetModExtension<TechTraderDefExtension>()?.tradeExempt == true); }
        public static bool TradeBlocked(ThingDef def)
        { Ensure(); return def != null && (itemDeny.Contains(def.defName) || def.GetModExtension<TechTraderDefExtension>()?.tradeBlocked == true); }
        public static bool FactionExempt(Faction faction)
        { Ensure(); return faction?.def != null && (factionAllow.Contains(faction.def.defName) || faction.def.GetModExtension<TechTraderDefExtension>()?.tradeExempt == true); }
        public static bool FactionBlocked(Faction faction)
        { Ensure(); return faction?.def != null && (factionDeny.Contains(faction.def.defName) || faction.def.GetModExtension<TechTraderDefExtension>()?.tradeBlocked == true); }
        public static bool SalvageExcluded(ThingDef def)
        { Ensure(); return def != null && (salvageDeny.Contains(def.defName) || def.GetModExtension<TechTraderDefExtension>()?.salvageExcluded == true); }
        public static bool ResearchIgnored(ResearchProjectDef def)
        {
            Ensure();
            return def == null || researchDeny.Contains(def.defName)
                || (def.modContentPack != null && packageDeny.Contains(def.modContentPack.PackageId))
                || def.GetModExtension<TechTraderDefExtension>()?.excludeResearch == true;
        }

        private static void Ensure()
        {
            if (revision == TechTraderMod.SettingsRevision) return;
            TechTraderSettings s = TechTraderMod.Settings;
            errors.Clear();
            itemTech = Overrides(s.ItemTechOverridesCsv, "Item tech");
            factionTech = Overrides(s.FactionTechOverridesCsv, "Faction tech");
            researchTech = Overrides(s.ResearchTechOverridesCsv, "Research tech");
            itemAllow = Set(s.TradeExemptThingDefsCsv);
            itemDeny = Set(s.TradeBlockedThingDefsCsv);
            factionAllow = Set(s.TradeExemptFactionDefsCsv);
            factionDeny = Set(s.TradeBlockedFactionDefsCsv);
            salvageDeny = Set(s.SalvageExcludedThingDefsCsv);
            researchDeny = Set(s.IgnoredResearchDefsCsv);
            packageDeny = Set(s.IgnoredResearchPackagesCsv, StringComparer.OrdinalIgnoreCase);
            revision = TechTraderMod.SettingsRevision;
        }

        private static IEnumerable<string> Entries(string value)
        {
            foreach (string raw in (value ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            { string entry = raw.Trim(); if (entry.Length > 0) yield return entry; }
        }

        private static HashSet<string> Set(string value, IEqualityComparer<string> comparer = null)
        { return new HashSet<string>(Entries(value), comparer ?? StringComparer.Ordinal); }

        private static Dictionary<string, TechLevel> Overrides(string value, string category)
        {
            var result = new Dictionary<string, TechLevel>(StringComparer.Ordinal);
            foreach (string entry in Entries(value))
            {
                string[] parts = entry.Split('=');
                if (parts.Length != 2 || parts[0].Trim().Length == 0
                    || !Enum.TryParse(parts[1].Trim(), true, out TechLevel level)
                    || (int)level < 0 || (int)level > 7)
                { errors.Add(category + ": ignored invalid entry \"" + entry + "\"."); continue; }
                result[parts[0].Trim()] = level;
            }
            return result;
        }
    }
}
