using System;
using System.Collections.Generic;
using RimWorld;
using TechTraderPrevention.Core;
using Verse;

namespace TechTraderPrevention
{
    public sealed class TechTraderSettings : ModSettings
    {
        public bool TraderEnabled = true;
        public TechRule TraderRule = TechRule.AtOrBelow;
        public int AllowedTechAbove;
        public int AllowedTechBelow = 7;
        public bool FilterCaravans = true;
        public bool FilterSettlements = true;
        public bool FilterOrbitalTraders = true;
        public bool FilterOtherTraders = true;
        public bool FilterTraderItems = true;
        public bool FilterPawnTrade;
        public bool AllowUndefinedItemTech = true;
        public bool ReplaceDisallowedCaravans = true;
        public bool AllowOriginalWhenNoReplacement;
        public bool AllowFactionlessTraders = true;
        public TechLevel OrbitalTraderTechLevel = TechLevel.Spacer;
        public bool PreserveQuestTraders = true;

        public ResearchMode ColonyTechMode = ResearchMode.TierCompletion;
        public ResearchMetric CompletionMetric = ResearchMetric.ResearchCost;
        public TechLevel ManualColonyTech = TechLevel.Industrial;
        public TechLevel MinimumColonyTech = TechLevel.Neolithic;
        public TechLevel MaximumColonyTech = TechLevel.Archotech;
        public bool UseFactionTechAsFloor;
        public float CompletionThreshold = 0.75f;
        public int MinimumCompletedProjects = 1;
        public bool RequireLowerTiers = true;
        public bool EmptyTierBlocks;
        public bool IncludeHiddenResearch;
        public bool IncludeZeroCostResearch;
        public bool IncludeSpecialResearch;
        public int ResearchRefreshTicks = 2500;
        public Dictionary<int, float> TierThresholds = new Dictionary<int, float>();

        public bool SalvageEnabled;
        public SalvageTrigger SalvageTriggerMode = SalvageTrigger.Death;
        public bool SalvageDownedWeaponsOnly = true;
        public bool SalvageWeapons = true;
        public bool SalvageApparel = true;
        public bool SalvagePlayerPawns;
        public bool SalvageHostilePawns = true;
        public bool SalvageNeutralPawns;
        public bool SalvageAlliedPawns;
        public bool SalvagePrisoners;
        public bool SalvageHumanlikeOnly = true;
        public bool SalvageOnlyAboveColonyTech = true;
        public float SalvageBreakChance = 0.5f;
        public bool SalvageRandomYield;
        public float SalvageFixedYield = 0.5f;
        public float SalvageMinYield = 0.25f;
        public float SalvageMaxYield = 0.75f;
        public bool SalvageScaleByHitPoints;
        public bool SalvageRecipeFallback = true;
        public bool SalvagePreserveRelics = true;
        public bool SalvagePreserveBiocoded = true;
        public bool SalvagePreserveQuestItems = true;

        public string IgnoredResearchDefsCsv = "";
        public string IgnoredResearchPackagesCsv = "";
        public string ResearchTechOverridesCsv = "";
        public string ItemTechOverridesCsv = "";
        public string FactionTechOverridesCsv = "";
        public string TradeExemptThingDefsCsv = "";
        public string TradeBlockedThingDefsCsv = "";
        public string TradeExemptFactionDefsCsv = "";
        public string TradeBlockedFactionDefsCsv = "";
        public string SalvageExcludedThingDefsCsv = "";
        public bool DeveloperLogging;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref TraderEnabled, "traderEnabled", true);
            Scribe_Values.Look(ref TraderRule, "traderRule", TechRule.AtOrBelow);
            Scribe_Values.Look(ref AllowedTechAbove, "allowedTechAbove", 0);
            Scribe_Values.Look(ref AllowedTechBelow, "allowedTechBelow", 7);
            Scribe_Values.Look(ref FilterCaravans, "filterCaravans", true);
            Scribe_Values.Look(ref FilterSettlements, "filterSettlements", true);
            Scribe_Values.Look(ref FilterOrbitalTraders, "filterOrbitalTraders", true);
            Scribe_Values.Look(ref FilterOtherTraders, "filterOtherTraders", true);
            Scribe_Values.Look(ref FilterTraderItems, "filterTraderItems", true);
            Scribe_Values.Look(ref FilterPawnTrade, "filterPawnTrade");
            Scribe_Values.Look(ref AllowUndefinedItemTech, "allowUndefinedItemTech", true);
            Scribe_Values.Look(ref ReplaceDisallowedCaravans, "replaceDisallowedCaravans", true);
            Scribe_Values.Look(ref AllowOriginalWhenNoReplacement, "allowOriginalWhenNoReplacement");
            Scribe_Values.Look(ref AllowFactionlessTraders, "allowFactionlessTraders", true);
            Scribe_Values.Look(ref OrbitalTraderTechLevel, "orbitalTraderTechLevel", TechLevel.Spacer);
            Scribe_Values.Look(ref PreserveQuestTraders, "preserveQuestTraders", true);
            Scribe_Values.Look(ref ColonyTechMode, "colonyTechMode", ResearchMode.TierCompletion);
            Scribe_Values.Look(ref CompletionMetric, "completionMetric", ResearchMetric.ResearchCost);
            Scribe_Values.Look(ref ManualColonyTech, "manualColonyTech", TechLevel.Industrial);
            Scribe_Values.Look(ref MinimumColonyTech, "minimumColonyTech", TechLevel.Neolithic);
            Scribe_Values.Look(ref MaximumColonyTech, "maximumColonyTech", TechLevel.Archotech);
            Scribe_Values.Look(ref UseFactionTechAsFloor, "useFactionTechAsFloor");
            Scribe_Values.Look(ref CompletionThreshold, "completionThreshold", 0.75f);
            Scribe_Values.Look(ref MinimumCompletedProjects, "minimumCompletedProjects", 1);
            Scribe_Values.Look(ref RequireLowerTiers, "requireLowerTiers", true);
            Scribe_Values.Look(ref EmptyTierBlocks, "emptyTierBlocks");
            Scribe_Values.Look(ref IncludeHiddenResearch, "includeHiddenResearch");
            Scribe_Values.Look(ref IncludeZeroCostResearch, "includeZeroCostResearch");
            Scribe_Values.Look(ref IncludeSpecialResearch, "includeSpecialResearch");
            Scribe_Values.Look(ref ResearchRefreshTicks, "researchRefreshTicks", 2500);
            Scribe_Collections.Look(ref TierThresholds, "tierThresholds", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref SalvageEnabled, "salvageEnabled");
            Scribe_Values.Look(ref SalvageTriggerMode, "salvageTriggerMode", SalvageTrigger.Death);
            Scribe_Values.Look(ref SalvageDownedWeaponsOnly, "salvageDownedWeaponsOnly", true);
            Scribe_Values.Look(ref SalvageWeapons, "salvageWeapons", true);
            Scribe_Values.Look(ref SalvageApparel, "salvageApparel", true);
            Scribe_Values.Look(ref SalvagePlayerPawns, "salvagePlayerPawns");
            Scribe_Values.Look(ref SalvageHostilePawns, "salvageHostilePawns", true);
            Scribe_Values.Look(ref SalvageNeutralPawns, "salvageNeutralPawns");
            Scribe_Values.Look(ref SalvageAlliedPawns, "salvageAlliedPawns");
            Scribe_Values.Look(ref SalvagePrisoners, "salvagePrisoners");
            Scribe_Values.Look(ref SalvageHumanlikeOnly, "salvageHumanlikeOnly", true);
            Scribe_Values.Look(ref SalvageOnlyAboveColonyTech, "salvageOnlyAboveColonyTech", true);
            Scribe_Values.Look(ref SalvageBreakChance, "salvageBreakChance", 0.5f);
            Scribe_Values.Look(ref SalvageRandomYield, "salvageRandomYield");
            Scribe_Values.Look(ref SalvageFixedYield, "salvageFixedYield", 0.5f);
            Scribe_Values.Look(ref SalvageMinYield, "salvageMinYield", 0.25f);
            Scribe_Values.Look(ref SalvageMaxYield, "salvageMaxYield", 0.75f);
            Scribe_Values.Look(ref SalvageScaleByHitPoints, "salvageScaleByHitPoints");
            Scribe_Values.Look(ref SalvageRecipeFallback, "salvageRecipeFallback", true);
            Scribe_Values.Look(ref SalvagePreserveRelics, "salvagePreserveRelics", true);
            Scribe_Values.Look(ref SalvagePreserveBiocoded, "salvagePreserveBiocoded", true);
            Scribe_Values.Look(ref SalvagePreserveQuestItems, "salvagePreserveQuestItems", true);
            Scribe_Values.Look(ref IgnoredResearchDefsCsv, "ignoredResearchDefs", "");
            Scribe_Values.Look(ref IgnoredResearchPackagesCsv, "ignoredResearchPackages", "");
            Scribe_Values.Look(ref ResearchTechOverridesCsv, "researchTechOverrides", "");
            Scribe_Values.Look(ref ItemTechOverridesCsv, "itemTechOverrides", "");
            Scribe_Values.Look(ref FactionTechOverridesCsv, "factionTechOverrides", "");
            Scribe_Values.Look(ref TradeExemptThingDefsCsv, "tradeExemptThingDefs", "");
            Scribe_Values.Look(ref TradeBlockedThingDefsCsv, "tradeBlockedThingDefs", "");
            Scribe_Values.Look(ref TradeExemptFactionDefsCsv, "tradeExemptFactionDefs", "");
            Scribe_Values.Look(ref TradeBlockedFactionDefsCsv, "tradeBlockedFactionDefs", "");
            Scribe_Values.Look(ref SalvageExcludedThingDefsCsv, "salvageExcludedThingDefs", "");
            Scribe_Values.Look(ref DeveloperLogging, "developerLogging");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Normalize();
        }

        public void Normalize()
        {
            AllowedTechAbove = TechRules.ClampTier(AllowedTechAbove);
            AllowedTechBelow = TechRules.ClampTier(AllowedTechBelow);
            if (!Enum.IsDefined(typeof(TechRule), TraderRule)) TraderRule = TechRule.AtOrBelow;
            if (!Enum.IsDefined(typeof(ResearchMode), ColonyTechMode)) ColonyTechMode = ResearchMode.TierCompletion;
            if (!Enum.IsDefined(typeof(ResearchMetric), CompletionMetric)) CompletionMetric = ResearchMetric.ResearchCost;
            if (!Enum.IsDefined(typeof(SalvageTrigger), SalvageTriggerMode)) SalvageTriggerMode = SalvageTrigger.Death;
            ManualColonyTech = (TechLevel)TechRules.ClampTier((int)ManualColonyTech);
            MinimumColonyTech = (TechLevel)TechRules.ClampTier((int)MinimumColonyTech);
            MaximumColonyTech = (TechLevel)Math.Max((int)MinimumColonyTech, TechRules.ClampTier((int)MaximumColonyTech));
            OrbitalTraderTechLevel = (TechLevel)TechRules.ClampTier((int)OrbitalTraderTechLevel);
            CompletionThreshold = (float)TechRules.Fraction(CompletionThreshold, 0.75);
            MinimumCompletedProjects = Math.Max(0, Math.Min(10000, MinimumCompletedProjects));
            ResearchRefreshTicks = Math.Max(60, Math.Min(60000, ResearchRefreshTicks));
            SalvageBreakChance = (float)TechRules.Fraction(SalvageBreakChance, 0.5);
            SalvageFixedYield = (float)TechRules.Fraction(SalvageFixedYield, 0.5);
            SalvageMinYield = (float)TechRules.Fraction(SalvageMinYield, 0.25);
            SalvageMaxYield = (float)TechRules.Fraction(SalvageMaxYield, 0.75);
            if (SalvageMinYield > SalvageMaxYield)
            { float swap = SalvageMinYield; SalvageMinYield = SalvageMaxYield; SalvageMaxYield = swap; }
            if (TierThresholds == null) TierThresholds = new Dictionary<int, float>();
            foreach (int key in new List<int>(TierThresholds.Keys))
                if (key < 0 || key > 7) TierThresholds.Remove(key);
                else TierThresholds[key] = (float)TechRules.Fraction(TierThresholds[key], CompletionThreshold);
        }
    }
}
