using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TechTraderPrevention
{
    internal sealed class SalvageMaterial
    {
        internal ThingDef Def;
        internal double Count;

        internal SalvageMaterial(ThingDef def, double count)
        {
            Def = def;
            Count = count;
        }
    }

    /// <summary>Reads material definitions; it never edits recipe or item definitions.</summary>
    internal static class MaterialResolver
    {
        private static readonly Dictionary<ThingDef, List<RecipeDef>> RecipesByProduct =
            new Dictionary<ThingDef, List<RecipeDef>>();
        private static int indexedRecipeCount = -1;
        private static List<RecipeDef> indexedRecipes;

        internal static bool TryResolve(Thing item, bool recipeFallback, out List<SalvageMaterial> materials)
        {
            materials = null;
            if (item == null || item.def == null || (item.def.MadeFromStuff && item.Stuff == null))
                return false;

            // The game's calculator respects stuff volume and patches supplied by other mods.
            List<ThingDefCountClass> adjusted = item.def.CostListAdjusted(item.Stuff);
            if (adjusted != null && adjusted.Count > 0)
            {
                var costs = new Dictionary<ThingDef, double>();
                foreach (ThingDefCountClass cost in adjusted)
                {
                    if (cost == null || cost.count <= 0)
                        continue;
                    if (!SafeMaterial(cost.thingDef, item.def))
                        return false;
                    Add(costs, cost.thingDef, cost.count);
                }
                materials = ToList(costs);
                return materials.Count > 0;
            }

            return recipeFallback && TryRecipeCosts(item, out materials);
        }

        internal static void ClearCaches()
        {
            RecipesByProduct.Clear();
            indexedRecipeCount = -1;
            indexedRecipes = null;
        }

        private static bool TryRecipeCosts(Thing item, out List<SalvageMaterial> materials)
        {
            materials = null;
            EnsureRecipeIndex();
            List<RecipeDef> recipes;
            if (!RecipesByProduct.TryGetValue(item.def, out recipes))
                return false;

            Dictionary<ThingDef, double> agreed = null;
            foreach (RecipeDef recipe in recipes)
            {
                // Multiple-output and ingredient-choice recipes cannot reveal what was consumed.
                if (recipe.products == null || recipe.products.Count != 1 ||
                    recipe.products[0].thingDef != item.def || recipe.products[0].count <= 0 ||
                    recipe.ingredients == null || recipe.ingredients.Count == 0 ||
                    recipe.ignoreIngredientCountTakeEntireStacks || recipe.specialProducts != null && recipe.specialProducts.Count > 0)
                    return false;
                // A custom worker may override ingredient consumption using bill/workbench state.
                // Declared native costs still work; recipe-only equipment is kept when its worker
                // cannot be reconstructed from fixed ingredient definitions alone.
                if (recipe.workerClass != typeof(RecipeWorker))
                    return false;

                var costs = new Dictionary<ThingDef, double>();
                int stuffGroups = 0;
                foreach (IngredientCount ingredient in recipe.ingredients)
                {
                    if (ingredient == null || ingredient.filter == null)
                        return false;
                    ThingDef def = null;
                    if (ingredient.IsFixedIngredient)
                        def = ingredient.FixedIngredient;
                    else if (!recipe.allowMixingIngredients && recipe.productHasIngredientStuff &&
                        item.Stuff != null && ingredient.filter.Allows(item.Stuff) && ++stuffGroups == 1)
                        def = item.Stuff;
                    if (!SafeMaterial(def, item.def) || recipe.fixedIngredientFilter != null && !recipe.fixedIngredientFilter.Allows(def))
                        return false;
                    int count = ingredient.CountRequiredOfFor(def, recipe);
                    if (count <= 0)
                        return false;
                    Add(costs, def, (double)count / recipe.products[0].count);
                }

                // Choosing a convenient expensive recipe would manufacture materials. Ambiguous
                // recipes preserve the equipment instead of guessing which recipe originally made it.
                if (agreed != null && !EqualCosts(agreed, costs))
                    return false;
                agreed = costs;
            }

            if (agreed == null || agreed.Count == 0)
                return false;
            materials = ToList(agreed);
            return true;
        }

        private static void EnsureRecipeIndex()
        {
            List<RecipeDef> all = DefDatabase<RecipeDef>.AllDefsListForReading;
            if (ReferenceEquals(indexedRecipes, all) && indexedRecipeCount == all.Count)
                return;
            RecipesByProduct.Clear();
            foreach (RecipeDef recipe in all)
            {
                if (recipe == null || recipe.products == null)
                    continue;
                foreach (ThingDefCountClass product in recipe.products)
                {
                    if (product == null || product.thingDef == null)
                        continue;
                    List<RecipeDef> list;
                    if (!RecipesByProduct.TryGetValue(product.thingDef, out list))
                        RecipesByProduct.Add(product.thingDef, list = new List<RecipeDef>());
                    if (!list.Contains(recipe))
                        list.Add(recipe);
                }
            }
            indexedRecipeCount = all.Count;
            indexedRecipes = all;
        }

        private static bool SafeMaterial(ThingDef def, ThingDef original)
        {
            return def != null && def != original && def.category == ThingCategory.Item &&
                !def.MadeFromStuff && def.destroyable && def.stackLimit > 0;
        }

        private static void Add(Dictionary<ThingDef, double> costs, ThingDef def, double count)
        {
            double existing;
            costs.TryGetValue(def, out existing);
            costs[def] = existing + count;
        }

        private static List<SalvageMaterial> ToList(Dictionary<ThingDef, double> costs)
        {
            var list = new List<SalvageMaterial>();
            foreach (KeyValuePair<ThingDef, double> cost in costs)
            {
                if (double.IsNaN(cost.Value) || double.IsInfinity(cost.Value) || cost.Value <= 0)
                    continue;
                list.Add(new SalvageMaterial(cost.Key, cost.Value));
            }
            // Stable ordering also keeps random consumption independent of dictionary iteration.
            list.Sort((a, b) => string.CompareOrdinal(a.Def.defName, b.Def.defName));
            return list;
        }

        private static bool EqualCosts(Dictionary<ThingDef, double> first, Dictionary<ThingDef, double> second)
        {
            if (first.Count != second.Count)
                return false;
            foreach (KeyValuePair<ThingDef, double> pair in first)
            {
                double value;
                if (!second.TryGetValue(pair.Key, out value) || Math.Abs(value - pair.Value) > 0.00001d)
                    return false;
            }
            return true;
        }
    }
}
