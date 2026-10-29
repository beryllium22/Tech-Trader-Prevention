using System;
using System.Collections.Generic;
using TechTraderPrevention.Core;
using Verse;

namespace TechTraderPrevention
{
    internal static class SalvageTransaction
    {
        // A malformed mod definition must not turn one pawn death into millions of spawned stacks.
        private const int MaximumStacksPerItem = 256;

        internal static bool TryRecover(Thing original, int originalStackCount, Map map, IntVec3 position,
            List<SalvageMaterial> materials, float fraction)
        {
            if (original == null || original.Destroyed || original.stackCount != originalStackCount ||
                map == null || !position.InBounds(map))
                return false;

            var staged = new List<Thing>();
            bool consumed = false;
            try
            {
                foreach (SalvageMaterial material in materials)
                {
                    int remaining = SalvageMath.RecoveredCount(material.Count, originalStackCount, fraction);
                    int limit = Math.Max(1, material.Def.stackLimit);
                    if ((long)staged.Count + ((long)remaining + limit - 1L) / limit > MaximumStacksPerItem)
                        return false;
                    while (remaining > 0)
                    {
                        Thing stack = ThingMaker.MakeThing(material.Def);
                        stack.stackCount = Math.Min(remaining, limit);
                        remaining -= stack.stackCount;
                        staged.Add(stack);
                    }
                }

                foreach (Thing stack in staged)
                {
                    // Excluding cells containing items prevents merging with pre-existing stacks.
                    // We can then roll back precisely our own outputs if any placement fails.
                    if (!GenPlace.TryPlaceThing(stack, position, map, ThingPlaceMode.Near,
                        null, cell => ItemCellEmpty(cell, map)) || stack.Destroyed || !stack.Spawned)
                        return false;
                }

                // Source remains intact until all outputs exist. A configured 0% yield deliberately
                // reaches this step with no outputs; an unresolvable recipe never reaches this code.
                original.Destroy(DestroyMode.Vanish);
                consumed = original.Destroyed;
                return consumed;
            }
            catch (Exception ex)
            {
                // A third-party Destroy hook can throw after destruction. Keep the materials then.
                consumed = original.Destroyed;
                Log.WarningOnce("[Tech Trader Prevention] Equipment salvage could not finish for " +
                    original.def.defName + ": " + ex.Message, 0x1d430001 ^ original.def.shortHash);
                return consumed;
            }
            finally
            {
                if (!consumed)
                    RollBack(staged);
            }
        }

        private static bool ItemCellEmpty(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
                return false;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
                if (things[i].def.category == ThingCategory.Item)
                    return false;
            return true;
        }

        private static void RollBack(List<Thing> staged)
        {
            for (int i = staged.Count - 1; i >= 0; i--)
            {
                Thing stack = staged[i];
                if (stack.Destroyed)
                    continue;
                try
                {
                    stack.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[Tech Trader Prevention] A mod prevented rollback of a salvage " +
                        "output: " + ex.Message, 0x1d430002 ^ stack.def.shortHash);
                }
            }
        }
    }
}
