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

        private sealed class CreatedStack
        {
            internal Thing Thing;
            internal int Count;
            internal long PlacedCount;
        }

        private sealed class Placement
        {
            internal Thing Thing;
            internal ThingDef Def;
            internal int CountBefore;
            internal long AddedCount;
        }

        internal static bool TryRecover(Thing original, int originalStackCount, Map map, IntVec3 position,
            List<SalvageMaterial> materials, float fraction)
        {
            if (original == null || original.Destroyed || originalStackCount <= 0 ||
                original.stackCount != originalStackCount || map == null || !position.InBounds(map))
                return false;

            bool sourceSpawned = original.Spawned;
            Map sourceMap = original.MapHeld;
            IntVec3 sourcePosition = original.PositionHeld;
            ThingOwner sourceOwner = original.holdingOwner;
            if (sourceMap != map)
                return false;
            bool SourceUnchanged() => !original.Destroyed && original.stackCount == originalStackCount &&
                original.Spawned == sourceSpawned && ReferenceEquals(original.holdingOwner, sourceOwner) &&
                original.MapHeld == sourceMap && original.PositionHeld == sourcePosition;

            var staged = new List<CreatedStack>();
            var placements = new List<Placement>();
            var byDestination = new Dictionary<Thing, Placement>();
            bool consumptionStarted = false;
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
                        int count = Math.Min(remaining, limit);
                        stack.stackCount = count;
                        remaining -= count;
                        staged.Add(new CreatedStack { Thing = stack, Count = count });
                    }
                }

                foreach (CreatedStack stack in staged)
                {
                    if (!SourceUnchanged())
                        return false;
                    bool invalidReceipt = false;
                    void Placed(Thing destination, int count)
                    {
                        if (destination == null || ReferenceEquals(destination, original) ||
                            destination.def != stack.Thing.def || count <= 0 ||
                            count > stack.Count - stack.PlacedCount || destination.stackCount < count)
                        {
                            // A misreported quantity cannot establish the old destination count.
                            // Do not debit an unknown existing stack while trying to roll it back.
                            invalidReceipt = true;
                            return;
                        }
                        Placement receipt;
                        if (!byDestination.TryGetValue(destination, out receipt))
                        {
                            receipt = new Placement
                            {
                                Thing = destination, Def = destination.def,
                                CountBefore = (int)Math.Max(0L, (long)destination.stackCount - count)
                            };
                            byDestination.Add(destination, receipt);
                            placements.Add(receipt);
                        }
                        receipt.AddedCount += count;
                        stack.PlacedCount += count;
                    }

                    // Keep vanilla outputs separate. Receipts also account for stack splitting or
                    // merging by compatible placement patches, including a partial failed placement.
                    bool placed = GenPlace.TryPlaceThing(stack.Thing, position, map, ThingPlaceMode.Near,
                        Placed, cell => ItemCellEmpty(cell, map));
                    if (invalidReceipt || placed && stack.PlacedCount != stack.Count)
                    {
                        Log.WarningOnce("[Tech Trader Prevention] A placement patch did not report the expected salvage " +
                            "material quantities; the transaction was cancelled.", 0x1d430005);
                        return false;
                    }
                    if (!placed)
                        return false;
                }

                // A placement hook may move the original to another inventory. Do not destroy it
                // after its owner/position/quantity changes, even if the outputs were placed.
                if (!SourceUnchanged() || !OutputsIntact(placements, map))
                    return false;

                // A configured 0% yield deliberately reaches this point without outputs. Unknown
                // recipes never reach this method. Only our own destruction counts as commitment.
                consumptionStarted = true;
                original.Destroy(DestroyMode.Vanish);
                consumed = original.Destroyed;
                return consumed;
            }
            catch (Exception ex)
            {
                // A third-party Destroy hook can throw after our destruction succeeds. Conversely,
                // unrelated destruction during material placement must not commit partial outputs.
                consumed = consumptionStarted && original.Destroyed;
                Log.WarningOnce("[Tech Trader Prevention] Equipment salvage could not finish for " +
                    original.def.defName + ": " + ex.Message, 0x1d430001 ^ original.def.shortHash);
                return consumed;
            }
            finally
            {
                if (!consumed)
                    RollBack(staged, placements);
            }
        }

        private static bool OutputsIntact(List<Placement> placements, Map map)
        {
            foreach (Placement receipt in placements)
                if (receipt.Thing.Destroyed || receipt.Thing.def != receipt.Def ||
                    (!receipt.Thing.Spawned && receipt.Thing.holdingOwner == null) || receipt.Thing.MapHeld != map ||
                    (long)receipt.Thing.stackCount < (long)receipt.CountBefore + receipt.AddedCount)
                    return false;
            return true;
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

        private static void RollBack(List<CreatedStack> staged, List<Placement> placements)
        {
            // Remove only the units we inserted, never the pre-existing portion of a destination
            // stack. Split outputs need not retain the identity of the original created Thing.
            // This relies on standard placement callbacks: a mod that suppresses them or converts
            // a Thing into virtual storage has effects this transaction cannot identify or undo.
            for (int i = placements.Count - 1; i >= 0; i--)
            {
                Placement receipt = placements[i];
                RemoveUnits(receipt.Thing, receipt.AddedCount, receipt.CountBefore);
            }
            foreach (CreatedStack stack in staged)
            {
                long unplaced = Math.Max(0L, stack.Count - stack.PlacedCount);
                if (unplaced > 0)
                    RemoveUnits(stack.Thing, unplaced, 0);
            }
        }

        private static void RemoveUnits(Thing thing, long count, int protectedCount)
        {
            if (thing == null || thing.Destroyed || count <= 0)
                return;
            int removable = SalvageMath.RollbackCount(thing.stackCount, protectedCount, count);
            if (removable == 0)
                return;
            try
            {
                Thing removed = thing.SplitOff(removable);
                removed.Destroy(DestroyMode.Vanish);
                if (!removed.Destroyed)
                    Log.ErrorOnce("[Tech Trader Prevention] A mod refused to remove a rolled-back salvage output.",
                        0x1d430002 ^ thing.def.shortHash);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Tech Trader Prevention] A mod prevented rollback of a salvage output: " +
                    ex.Message, 0x1d430002 ^ thing.def.shortHash);
            }
        }
    }
}
