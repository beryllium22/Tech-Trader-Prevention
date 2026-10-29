using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using TechTraderPrevention.Core;
using Verse;

namespace TechTraderPrevention
{
    /// <summary>Snapshots worn/equipped gear before vanilla death/downing drops it.</summary>
    public static class SalvagePatches
    {
        private static readonly HashSet<Pawn> ActiveTransitions = new HashSet<Pawn>();

        public static void Apply(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill),
                new[] { typeof(DamageInfo?), typeof(Hediff) });
            CompatibilityDiagnostics.Patch(harmony, target, "Equipment death",
                prefix: new HarmonyMethod(typeof(SalvagePatches), nameof(CaptureBeforeDeath)) { priority = Priority.Last },
                postfix: new HarmonyMethod(typeof(SalvagePatches), nameof(RecoverAfterDeath)) { priority = Priority.Last },
                finalizer: new HarmonyMethod(typeof(SalvagePatches), nameof(FinalizeDeath)));

            MethodInfo downed = AccessTools.Method(typeof(Pawn_HealthTracker), "MakeDowned",
                new[] { typeof(DamageInfo?), typeof(Hediff) });
            if (AccessTools.Field(typeof(Pawn_HealthTracker), "pawn") == null)
                downed = null;
            CompatibilityDiagnostics.Patch(harmony, downed, "Equipment downing",
                prefix: new HarmonyMethod(typeof(SalvagePatches), nameof(CaptureBeforeDowned)) { priority = Priority.Last },
                postfix: new HarmonyMethod(typeof(SalvagePatches), nameof(RecoverAfterDowned)) { priority = Priority.Last },
                finalizer: new HarmonyMethod(typeof(SalvagePatches), nameof(FinalizeDowned)));
        }

        public static void ClearCaches()
        {
            MaterialResolver.ClearCaches();
        }

        private sealed class EquippedItem
        {
            internal Thing Thing;
            internal int StackCount;
            internal bool Apparel;
        }

        private sealed class DeathSnapshot
        {
            internal Map Map;
            internal IntVec3 Position;
            internal bool Downing;
            internal readonly List<EquippedItem> Items = new List<EquippedItem>();
        }

        private static void CaptureBeforeDeath(Pawn __instance, out DeathSnapshot __state)
        {
            Capture(__instance, false, out __state);
        }

        private static void CaptureBeforeDowned(Pawn ___pawn, out DeathSnapshot __state)
        {
            Capture(___pawn, true, out __state);
        }

        private static void Capture(Pawn pawn, bool downing, out DeathSnapshot __state)
        {
            __state = null;
            TechTraderSettings settings = TechTraderMod.Settings;
            if (pawn == null || pawn.Dead || (downing && pawn.Downed) || settings == null || !settings.SalvageEnabled ||
                settings.SalvageBreakChance <= 0f ||
                (downing && settings.SalvageTriggerMode == SalvageTrigger.Death) ||
                (!downing && settings.SalvageTriggerMode == SalvageTrigger.Downed) ||
                !AffectsPawn(pawn, settings) || pawn.MapHeld == null || !ActiveTransitions.Add(pawn))
                return;
            try
            {
                var state = new DeathSnapshot { Map = pawn.MapHeld, Position = pawn.PositionHeld, Downing = downing };
                var seen = new HashSet<Thing>();
                if (settings.SalvageWeapons && pawn.equipment != null)
                    foreach (Thing thing in pawn.equipment.AllEquipmentListForReading)
                        AddItem(thing, false, state, seen, settings);
                if (settings.SalvageApparel && pawn.apparel != null)
                    foreach (Apparel apparel in pawn.apparel.WornApparel)
                        AddItem(apparel, true, state, seen, settings);
                __state = state;
            }
            catch (Exception ex)
            {
                ActiveTransitions.Remove(pawn);
                Log.WarningOnce("[Tech Trader Prevention] Skipped equipment salvage snapshot: " + ex.Message, 0x1d430003);
            }
        }

        private static void RecoverAfterDeath(Pawn __instance, DeathSnapshot __state)
        {
            Recover(__instance, __state);
        }

        private static void RecoverAfterDowned(Pawn ___pawn, DeathSnapshot __state)
        {
            Recover(___pawn, __state);
        }

        private static void Recover(Pawn pawn, DeathSnapshot state)
        {
            if (state == null)
                return;
            try
            {
                TechTraderSettings settings = TechTraderMod.Settings;
                if (settings == null || !settings.SalvageEnabled ||
                    (state.Downing && !pawn.Downed && !pawn.Dead) || (!state.Downing && !pawn.Dead))
                    return;
                // If death occurred inside MakeDowned, the outer snapshot owns the whole event.
                // This prevents two rolls for the same item within nested kill/downing hooks.
                bool downedOnly = state.Downing && !(pawn.Dead && settings.SalvageTriggerMode == SalvageTrigger.DeathOrDowned);
                foreach (EquippedItem entry in state.Items)
                {
                    try
                    {
                        Thing item = entry.Thing;
                        if (downedOnly && settings.SalvageDownedWeaponsOnly && entry.Apparel)
                            continue;
                        if (item.Destroyed || item.stackCount != entry.StackCount ||
                            !StillBelongsToPawn(item, pawn, state.Map))
                            continue;
                        // Most equipment does not break at a low configured chance. Avoid material
                        // and recipe work for those items in large battles and content-heavy packs.
                        if (!Rand.Chance(settings.SalvageBreakChance))
                            continue;
                        List<SalvageMaterial> costs;
                        if (!MaterialResolver.TryResolve(item, settings.SalvageRecipeFallback, out costs))
                            continue;
                        float fraction = SalvageMath.RecoveryFraction(settings.SalvageRandomYield,
                            settings.SalvageFixedYield, settings.SalvageMinYield, settings.SalvageMaxYield,
                            settings.SalvageRandomYield ? Rand.Value : 0f);
                        if (settings.SalvageScaleByHitPoints && item.def.useHitPoints)
                            fraction *= item.MaxHitPoints > 0 ? Math.Min(1f, Math.Max(0f, (float)item.HitPoints / item.MaxHitPoints)) : 0f;
                        IntVec3 position = item.Spawned ? item.Position : state.Position;
                        SalvageTransaction.TryRecover(item, entry.StackCount, state.Map, position, costs, fraction);
                    }
                    catch (Exception ex)
                    {
                        Log.WarningOnce("[Tech Trader Prevention] Equipment salvage encountered an error: " +
                            ex.Message, 0x1d430004 ^ entry.Thing.def.shortHash);
                    }
                }
            }
            finally
            {
                ActiveTransitions.Remove(pawn);
            }
        }

        private static Exception FinalizeDeath(Pawn __instance, DeathSnapshot __state, Exception __exception)
        {
            if (__state != null)
                ActiveTransitions.Remove(__instance);
            // Never suppress another mod's exception.
            return __exception;
        }

        private static Exception FinalizeDowned(Pawn ___pawn, DeathSnapshot __state, Exception __exception)
        {
            if (__state != null)
                ActiveTransitions.Remove(___pawn);
            return __exception;
        }

        private static void AddItem(Thing item, bool apparel, DeathSnapshot state, HashSet<Thing> seen, TechTraderSettings settings)
        {
            if (item == null || item.Destroyed || !item.def.destroyable || item.stackCount < 1 || !seen.Add(item) ||
                DefPolicy.SalvageExcluded(item.def))
                return;
            if (settings.SalvageOnlyAboveColonyTech &&
                (int)DefPolicy.EffectiveTech(item.def) <= (int)ColonyTech.CurrentLevel + settings.AllowedTechAbove)
                return;
            if ((settings.SalvagePreserveRelics && item.IsRelic()) ||
                (settings.SalvagePreserveBiocoded && CompBiocodable.IsBiocoded(item)))
                return;
            if (settings.SalvagePreserveQuestItems && item.questTags != null && item.questTags.Count > 0)
                return;
            state.Items.Add(new EquippedItem { Thing = item, StackCount = item.stackCount, Apparel = apparel });
        }

        private static bool AffectsPawn(Pawn pawn, TechTraderSettings settings)
        {
            if (settings.SalvageHumanlikeOnly && !pawn.RaceProps.Humanlike)
                return false;
            // Prisoners have a separate policy; colony slaves are protected with player pawns.
            if (pawn.IsPrisonerOfColony)
                return settings.SalvagePrisoners;
            if (pawn.Faction == Faction.OfPlayer || pawn.IsSlaveOfColony)
                return settings.SalvagePlayerPawns;
            if (Faction.OfPlayer == null)
                return settings.SalvageNeutralPawns;
            // Honor actual hostility too (including factionless hostile pawns supplied by mods).
            if (pawn.HostileTo(Faction.OfPlayer))
                return settings.SalvageHostilePawns;
            if (pawn.Faction == null)
                return settings.SalvageNeutralPawns;
            FactionRelationKind relation = pawn.Faction.RelationKindWith(Faction.OfPlayer);
            if (relation == FactionRelationKind.Hostile)
                return settings.SalvageHostilePawns;
            if (relation == FactionRelationKind.Ally)
                return settings.SalvageAlliedPawns;
            return settings.SalvageNeutralPawns;
        }

        private static bool StillBelongsToPawn(Thing item, Pawn pawn, Map map)
        {
            if (item.Spawned)
                return item.Map == map;
            IThingHolder holder = item.ParentHolder;
            for (int depth = 0; holder != null && depth < 32; depth++)
            {
                if (ReferenceEquals(holder, pawn))
                    return true;
                if (holder is Pawn)
                    return false;
                holder = holder.ParentHolder;
            }
            return false;
        }
    }
}
