using Harmony;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using System;
using System.Reflection;

namespace LetThemEatPaste {

    [StaticConstructorOnStartup]
    static class Patches {

        static Patches() {
            HarmonyInstance harmony = HarmonyInstance.Create("com.unislash.LetThemEatPaste.patches");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public static Thing getPasteSource(Pawn getter, Pawn eater) {
            Thing chosenFood = null;

            // If the eater is null, nothing to do
            if (eater == null) {
                return null;
            }

            // If the eater isn't a prisoner, use the original behavior
            if (!eater.IsPrisoner) {
                return null;
            }

            // If the eater is also the getter, then the prisoner is getting food for themselves, so we want to use the original behavior
            if (eater == getter) {
                return null;
            }
            
//            Log.Message("We think this is part of a warden feeding task");

            // Find any nutrient paste meals already made
            List<Thing> possibleThings = getter.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
            Comparison<Thing> comparison = delegate(Thing t1, Thing t2) {
                float num = (float) (t1.Position - getter.Position).LengthHorizontalSquared;
                float value = (float) (t2.Position - getter.Position).LengthHorizontalSquared;
                return num.CompareTo(value);
            };
            possibleThings.Sort(comparison);

            TraverseParms traverseParams = TraverseParms.For(getter, Danger.Deadly, TraverseMode.ByPawn, false);

            // Go find the closest nutrient paste meal
            foreach (var thing in possibleThings) {
                if (thing.def.defName == "MealNutrientPaste" && !thing.IsForbidden(getter)
                    && getter.Map.reachability.CanReach(getter.Position, thing, PathEndMode.ClosestTouch, traverseParams)
                    && getter.CanReserve(thing, 1, -1, null, false)) {
                    chosenFood = thing;
                    break;
                }
            }

            if (chosenFood == null) {
                // Looks like there we didn't find a reachable nutrient paste meal. Let's look for a dispenser next
                foreach (var thing in possibleThings) {
                    Building_NutrientPasteDispenser building_NutrientPasteDispenser = thing as Building_NutrientPasteDispenser;

                    if (building_NutrientPasteDispenser != null && building_NutrientPasteDispenser.CanDispenseNow
                        && getter.Map.reachability.CanReach(getter.Position, thing, PathEndMode.ClosestTouch, traverseParams)) {
                        chosenFood = thing;
                        break;
                    }
                }
            }

            // Return the chosen food
            return chosenFood;
        }
        
        [HarmonyPatch(typeof(FoodUtility))]
        [HarmonyPatch("TryFindBestFoodSourceFor")]
        static class TryFindBestFoodSourceFor_Patch {

            /**
             * Force wardens to not use meals in their inventory when feeding prisoners in an effort to prevent them from using food they would use
             * themselves.
             */
            static bool Prefix(Pawn getter, Pawn eater, ref bool canUseInventory) {
                // If the eater is null, nothing to do
                if (eater == null || getter == null) {
//                    Log.Message("We got some nulls");
                    return true;
                }

                // If the eater isn't a prisoner, use the original behavior
                if (!eater.IsPrisoner) {
//                    Log.Message("It's not a prisoner");
                    return true;
                }
                
                // If the eater is also the getter, then the prisoner is getting food for themselves, so we want to use the original behavior
                if (eater == getter) {
//                    Log.Message("Eater = getter");
                    return true;
                }
                
//                Log.Message("Considering restricting pawn from using food in inventory");
                
                // Ok, so now that we've identified that we're a colonist feeding a prisoner, we don't want to use any amazing foods in our inventory,
                // unless...
                // 1) The best food in the inventory could actually be a nutrient paste meal. If so, feed that to the prisoner scum!
                // 2) We might not be able to find any paste meals or dispensers. If so, then I guess we can feed the prisoner what we have in
                //    our inventory... (but next time we're totally going to feed them paste)
                Thing foodInInventory = FoodUtility.BestFoodInInventory(getter, null, FoodPreferability.MealAwful, FoodPreferability.MealLavish, 0f, false);
                Thing canFindPaste = getPasteSource(getter, eater);
//                Log.Message("foodInInventory:"+foodInInventory);
//                Log.Message("canFindPaste:"+canFindPaste);
                if (foodInInventory != null && foodInInventory.def.defName != "MealNutrientPaste" && canFindPaste != null) {
                    canUseInventory = false;
//                    Log.Message("Restricting warden from using food in inventory");
                }
                
                return true;
            }
        }
        
//        [HarmonyPatch(typeof(JobGiver_Work))]
//        [HarmonyPatch("TryIssueJobPackage")]
//        static class TryIssueJobPackage_Patch {
//
//            /**
//             * Force wardens to not use meals in their inventory when feeding prisoners in an effort to prevent them from using food they would use
//             * themselves.
//             */
//            static void Prefix(Pawn pawn) {
//                Log.Message("Job for: "+pawn);
//
//            }
//        }
            
        
        [HarmonyPatch(typeof(FoodUtility))]
        [HarmonyPatch("BestFoodSourceOnMap")]
        private static class BestFoodSourceOnMap_Patch {

            /**
             * This prefix will return the nearest nutrient paste meal or dispenser iff this is part of a job for a warden or doctor to feed a prisoner.
             */
            public static bool Prefix(ref Thing __result, Pawn getter, Pawn eater) {

                Thing pasteSource = getPasteSource(getter, eater);

                if (pasteSource != null) {
                    __result = pasteSource;
//                    Log.Message("We have hijacked the call and returned a nutrient paste instead");
                    return false;
                }

                // If we didn't find a food, then use the original method
                return true;
            }
        }
    }
}