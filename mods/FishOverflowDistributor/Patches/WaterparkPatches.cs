using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using SharedCode.Utils;
using UnityEngine;

namespace FishOverflowDistributor.Patches
{
#if SUBNAUTICA
    [HarmonyPatch(typeof(WaterPark))]
    [HarmonyPatch("TryBreed")]
    internal class WaterparkPatches
    {
        // ReSharper disable once InconsistentNaming
        [HarmonyPrefix]
        private static bool Prefix(WaterPark __instance, WaterParkCreature creature)
        {
            Console.WriteLine("WaterPark.TryBreed(WaterParkCreature) called.");
#elif BELOWZERO
    [HarmonyPatch(typeof(WaterPark))]
    [HarmonyPatch(nameof(WaterPark.GetBreedingPartner))]
    internal class WaterparkPatches
    {
        // ReSharper disable once InconsistentNaming
        [HarmonyPrefix]
        public static bool Prefix(WaterPark __instance, WaterParkCreature creature, ref WaterParkCreature __result)
        {
            __result = null;
            Console.WriteLine("WaterPark.GetBreedingPartner(WaterParkCreature) called.");
#endif

            //We only distribute when the WaterPark doesn't have any space left for bred creatures.
            //This is the only place in this method where we allow the original unpatched method to execute, 
            //because the original method is never executed when there is no space left in the WaterPark.

            Console.WriteLine(
                $"__instance.HasFreeSpace() returns '{__instance.HasFreeSpace()}'.");

            if (__instance.HasFreeSpace())
                return true;
            

            if (__instance.rootWaterPark.items == null)
            {
                FishOverflowDistributor.Logger.LogError(
                    "FieldInfo or value for field 'items' in class 'WaterParkItem' with type 'List<WaterParkItem>' was null -> Should not happen, investigate!.");

                return false;
            }


            Console.WriteLine("Checking if creature is contained in items");

            //Don't know why this check is needed. Maybe TryBreed gets called on fish which arent contained in this WaterPark instance.
            if (!__instance.rootWaterPark.items.Contains(creature))
                return false;

            TechType creatureTechType = creature.pickupable.GetTechType();
            Console.WriteLine($"Creature Tech Type = {creatureTechType.ToString()}.");

            Console.WriteLine(
                "Checking whether creatureEggs.containsKey(creatureTechType)");
#if SUBNAUTICA
            //we don't want to distribute creature eggs
            if (WaterParkCreature.creatureEggs.ContainsKey(creatureTechType))
                return false;
#endif
            Console.WriteLine(
                $"Waterpark '{__instance.gameObject.name}' contains creature '{creature.gameObject.name}' and has enough space for another one.");

            var secondCreature = __instance.rootWaterPark.items.Find(item =>
                                                item != creature &&
                                                item is WaterParkCreature &&

                                                // ReSharper disable once TryCastAlwaysSucceeds
                                                (item as WaterParkCreature).GetCanBreed() &&
                                                item.pickupable != null &&
                                                item.pickupable.GetTechType() == creatureTechType) as
                WaterParkCreature;

            if (secondCreature == null)
                return false;

            Console.WriteLine(
                $"Waterpark contains two creatures '{creature.gameObject.name}' of TechType '{creatureTechType.ToString()}' which can breed with each other.");

            BaseBioReactor suitableReactor;
            try
            {
                //Get a reactor which has space for the item in the same base
                suitableReactor = SubnauticaSceneTraversalUtils
                                                 .GetComponentsInSameBase<BaseBioReactor>(__instance.gameObject)
                                                 .First(
                                                     reactor =>
                                                     {
                                                         ItemsContainer itemsContainer = reactor.container;

                                                         if (itemsContainer != null)
                                                             return itemsContainer.HasRoomFor(
                                                                 creature.pickupable);

                                                         Console.WriteLine(
                                                             $"PropertyInfo or value for property 'container' in class 'BaseBioReactor' with type 'ItemsContainer' was null -> Should not happen, investigate!.");

                                                         return false;
                                                     });
            }
            catch (Exception)
            {
                return false;
            }
            if (suitableReactor == null)
            {
                Console.WriteLine("Could not find suitable reactor");
                return false;
            }

            //Reset breed time of the second creature so it can't be used to immediately breed again.
            secondCreature.ResetBreedTime();

            Console.WriteLine(
                $"Found suitable reactor '{suitableReactor.gameObject.name}'.");

            //Now we create a pickupable from the WaterParkCreature which we can add to the reactor's inventory.
            //Because the creature can't be taken out from the reactor inventory, we don't need to add WaterparkCreature component
            //to it. This would be needed so the game knows when you drop it outside, that it came from a waterpark.


            GameObject newCreature = CraftData.InstantiateFromPrefab(creatureTechType, false);
            newCreature.SetActive(false);
            newCreature.transform.position = creature.transform.position + Vector3.down;
            var pickupable = newCreature.EnsureComponent<Pickupable>();

            /*WaterParkCreatureParameters creatureParameters =
                WaterParkCreature.GetParameters(creatureTechType);

            newCreature.transform.localScale = creatureParameters.initialSize * Vector3.one;
            var newCreatureComponent = newCreature.AddComponent<WaterParkCreature>();
            newCreatureComponent.age = 0f;

            Traverse
                .Create(newCreatureComponent)
                .Field("parameters")
                .SetValue(creatureParameters);

            Pickupable pickupable = creatureParameters.isPickupableOutside
                ? newCreature.EnsureComponent<Pickupable>()
                : newCreature.GetComponent<Pickupable>();

            newCreature.setActive();*/
#if SUBNAUTICA
            pickupable = pickupable.Pickup(false);
#elif BELOWZERO
            pickupable.Pickup(false);
#endif
            // pickupable.GetComponent<WaterParkItem>()?.SetWaterPark(null);

            var itemToAdd = new InventoryItem(pickupable);

            ItemsContainer reactorItemsContainer = suitableReactor.container;

            if (reactorItemsContainer == null)
            {
                FishOverflowDistributor.Logger.LogError(
                    $"PropertyInfo or value for property 'container' in class 'BaseBioReactor' with type 'ItemsContainer' was null -> Should not happen, investigate!.");

                return false;
            }

            reactorItemsContainer.AddItem(pickupable);

            return false;
        }
    }
}
