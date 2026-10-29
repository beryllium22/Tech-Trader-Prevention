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

        public override IEnumerable<string> ConfigErrors()
        {
            if (techLevelOverride < -1 || techLevelOverride > 7)
                yield return "Tech Trader Prevention: techLevelOverride must be -1 (unchanged) or a technology tier from 0 through 7.";
            if (tradeExempt && tradeBlocked)
                yield return "Tech Trader Prevention: tradeExempt and tradeBlocked are both enabled; tradeBlocked takes precedence.";
        }
    }

    public static class DefPolicy
    {
        private static int revision = -1;
        private static TechTraderSettings previousSettings;
        private static readonly OverrideCache itemTech = new OverrideCache("Item tech");
        private static readonly OverrideCache factionTech = new OverrideCache("Faction tech");
        private static readonly OverrideCache researchTech = new OverrideCache("Research tech");
        private static readonly SetCache itemAllow = new SetCache();
        private static readonly SetCache itemDeny = new SetCache();
        private static readonly SetCache factionAllow = new SetCache();
        private static readonly SetCache factionDeny = new SetCache();
        private static readonly SetCache salvageDeny = new SetCache();
        private static readonly SetCache researchDeny = new SetCache();
        private static readonly SetCache packageDeny = new SetCache(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> errors = new List<string>();
        public static IReadOnlyList<string> ConfigurationWarnings { get { Ensure(); return errors; } }

        public static TechLevel EffectiveTech(ThingDef def)
        { Ensure(); return Effective(def, def?.techLevel ?? TechLevel.Undefined, itemTech.Values, Extension(def)); }

        public static TechLevel EffectiveTech(Faction faction)
        { Ensure(); return Effective(faction?.def, faction?.def?.techLevel ?? TechLevel.Undefined, factionTech.Values, Extension(faction?.def)); }

        public static TechLevel EffectiveTech(ResearchProjectDef def)
        { Ensure(); return Effective(def, def?.techLevel ?? TechLevel.Undefined, researchTech.Values, Extension(def)); }

        private static TechLevel Effective(Def def, TechLevel fallback, Dictionary<string, TechLevel> map, TechTraderDefExtension extension)
        {
            if (def == null) return fallback;
            if (def.defName != null && map.TryGetValue(def.defName, out TechLevel setting)) return setting;
            int overrideLevel = extension?.techLevelOverride ?? -1;
            return overrideLevel >= 0 && overrideLevel <= 7 ? (TechLevel)overrideLevel : fallback;
        }

        private static TechTraderDefExtension Extension(Def def) => def?.GetModExtension<TechTraderDefExtension>();

        public static bool TradeExempt(ThingDef def)
        { Ensure(); return def != null && (itemAllow.Contains(def.defName) || Extension(def)?.tradeExempt == true); }
        public static bool TradeBlocked(ThingDef def)
        { Ensure(); return def != null && (itemDeny.Contains(def.defName) || Extension(def)?.tradeBlocked == true); }
        public static bool FactionExempt(Faction faction)
        { Ensure(); return faction?.def != null && (factionAllow.Contains(faction.def.defName) || Extension(faction.def)?.tradeExempt == true); }
        public static bool FactionBlocked(Faction faction)
        { Ensure(); return faction?.def != null && (factionDeny.Contains(faction.def.defName) || Extension(faction.def)?.tradeBlocked == true); }
        public static bool SalvageExcluded(ThingDef def)
        { Ensure(); return def != null && (salvageDeny.Contains(def.defName) || Extension(def)?.salvageExcluded == true); }
        public static bool ResearchIgnored(ResearchProjectDef def)
        {
            Ensure();
            return def == null || researchDeny.Contains(def.defName)
                || PackageIgnored(def.modContentPack) || Extension(def)?.excludeResearch == true;
        }

        // A research refresh asks both questions for every loaded project. Resolve
        // the mod extension once, without caching mutable third-party Def fields.
        internal static bool TryGetResearchTech(ResearchProjectDef def, out TechLevel level)
        {
            Ensure();
            level = def?.techLevel ?? TechLevel.Undefined;
            if (def == null || researchDeny.Contains(def.defName) || PackageIgnored(def.modContentPack)) return false;
            TechTraderDefExtension extension = Extension(def);
            if (extension?.excludeResearch == true) return false;
            level = Effective(def, level, researchTech.Values, extension);
            return true;
        }

        private static bool PackageIgnored(ModContentPack package)
        {
            // A local/Workshop duplicate can have an internal ID different from
            // the package ID players copy from About.xml or the mod list.
            return package != null && (packageDeny.Contains(package.PackageId)
                || packageDeny.Contains(package.PackageIdPlayerFacing));
        }

        private static void Ensure()
        {
            TechTraderSettings s = TechTraderMod.Settings;
            if (revision == TechTraderMod.SettingsRevision && ReferenceEquals(previousSettings, s)) return;
            if (s == null) return;
            // Slider changes also advance SettingsRevision. Reparse only the
            // individual CSV strings that changed, not every large override list.
            itemTech.Update(s.ItemTechOverridesCsv);
            factionTech.Update(s.FactionTechOverridesCsv);
            researchTech.Update(s.ResearchTechOverridesCsv);
            itemAllow.Update(s.TradeExemptThingDefsCsv);
            itemDeny.Update(s.TradeBlockedThingDefsCsv);
            factionAllow.Update(s.TradeExemptFactionDefsCsv);
            factionDeny.Update(s.TradeBlockedFactionDefsCsv);
            salvageDeny.Update(s.SalvageExcludedThingDefsCsv);
            researchDeny.Update(s.IgnoredResearchDefsCsv);
            packageDeny.Update(s.IgnoredResearchPackagesCsv);
            errors.Clear();
            errors.AddRange(itemTech.Warnings);
            errors.AddRange(factionTech.Warnings);
            errors.AddRange(researchTech.Warnings);
            previousSettings = s;
            revision = TechTraderMod.SettingsRevision;
        }

        private static IEnumerable<string> Entries(string value)
        {
            if (string.IsNullOrEmpty(value)) yield break;
            int start = 0;
            for (int i = 0; i <= value.Length; i++)
            {
                if (i != value.Length && value[i] != ',' && value[i] != ';' && value[i] != '\n' && value[i] != '\r') continue;
                if (i > start)
                {
                    string entry = value.Substring(start, i - start).Trim();
                    if (entry.Length > 0) yield return entry;
                }
                start = i + 1;
            }
        }

        private sealed class SetCache
        {
            private string source;
            private readonly IEqualityComparer<string> comparer;
            private HashSet<string> values;
            internal SetCache(IEqualityComparer<string> comparer = null)
            {
                this.comparer = comparer ?? StringComparer.Ordinal;
                values = new HashSet<string>(this.comparer);
            }
            internal bool Contains(string name) => name != null && values.Contains(name);
            internal void Update(string value)
            {
                value = value ?? "";
                if (string.Equals(source, value, StringComparison.Ordinal)) return;
                values = new HashSet<string>(Entries(value), comparer);
                source = value;
            }
        }

        private sealed class OverrideCache
        {
            private string source;
            private readonly string category;
            internal Dictionary<string, TechLevel> Values { get; private set; } = new Dictionary<string, TechLevel>(StringComparer.Ordinal);
            internal readonly List<string> Warnings = new List<string>();
            internal OverrideCache(string category) { this.category = category; }
            internal void Update(string value)
            {
                value = value ?? "";
                if (string.Equals(source, value, StringComparison.Ordinal)) return;
                var parsed = new Dictionary<string, TechLevel>(StringComparer.Ordinal);
                Warnings.Clear();
                int invalid = 0;
                foreach (string entry in Entries(value))
                {
                    int separator = entry.IndexOf('=');
                    string name = separator < 0 ? "" : entry.Substring(0, separator).Trim();
                    if (separator < 1 || separator != entry.LastIndexOf('=') || name.Length == 0
                        || !Enum.TryParse(entry.Substring(separator + 1).Trim(), true, out TechLevel level)
                        || (int)level < 0 || (int)level > 7)
                    {
                        invalid++;
                        if (Warnings.Count < 32)
                            Warnings.Add(category + ": ignored invalid entry \""
                                + (entry.Length <= 160 ? entry : entry.Substring(0, 160) + "...") + "\".");
                        continue;
                    }
                    parsed[name] = level;
                }
                if (invalid > 32) Warnings.Add(category + ": " + (invalid - 32) + " more invalid entries omitted from the warning list.");
                Values = parsed;
                source = value;
            }
        }
    }
}
