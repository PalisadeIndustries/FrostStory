using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace FrostStory.Expedition;

/// <summary>The starting kit as listed in <c>config/expeditionkit.json</c>.</summary>
public class ExpeditionKitList
{
    public KitEntry[] Items = Array.Empty<KitEntry>();
}

public class KitEntry
{
    /// <summary>Item or block code, with its domain.</summary>
    public string Code = "";
    public int Quantity = 1;

    /// <summary>Put it on when it is clothing or armour and the slot is free.</summary>
    public bool Wear;
}

/// <summary>Resolves the kit's codes and hands it to a player.</summary>
public static class ExpeditionKit
{
    /// <summary>Mod data on the player: true once they have had the kit in this world.</summary>
    public const string GivenKey = "froststory:expeditionKit";

    /// <summary>The kit's entries that exist, each with its collectible. Missing codes are logged once per call.</summary>
    public static List<(KitEntry Entry, CollectibleObject Thing)> Resolve(IWorldAccessor world, ExpeditionKitList kit, ILogger log)
    {
        var found = new List<(KitEntry, CollectibleObject)>();
        foreach (var entry in kit.Items)
        {
            if (string.IsNullOrWhiteSpace(entry.Code) || entry.Quantity <= 0) continue;
            var location = new AssetLocation(entry.Code);
            CollectibleObject? thing = world.GetItem(location);
            if (thing == null || thing.IsMissing) thing = world.GetBlock(location);
            if (thing == null || thing.Id == 0 || thing.IsMissing)
            {
                log.Warning("Expedition kit: there is no item or block '{0}'; it is left out of the kit.", entry.Code);
                continue;
            }

            found.Add((entry, thing));
        }

        return found;
    }

    /// <summary>
    /// Gives the kit to a player: wearables go on where their slot is free, the rest into the inventory and
    /// whatever does not fit is dropped at their feet. A wearable the player already has (from the character
    /// class) is not given twice. Returns how many stacks were handed over.
    /// </summary>
    public static int Give(IServerPlayer player, ExpeditionKitList kit, ILogger log)
    {
        var world = player.Entity.World;
        int given = 0;
        foreach (var (entry, thing) in Resolve(world, kit, log))
        {
            if (entry.Wear && Holds(player, thing.Code)) continue;

            int left = entry.Quantity;
            while (left > 0)
            {
                int n = Math.Min(left, Math.Max(1, thing.MaxStackSize));
                left -= n;
                var stack = new ItemStack(thing, n);
                // Unset, the game rolls new clothing at 0-40% condition (and so 0-80% warmth).
                if (entry.Wear) stack.Attributes.SetFloat("condition", 1f);
                given++;
                if (entry.Wear && TryWear(player, stack)) continue;
                player.InventoryManager.TryGiveItemstack(stack, true);
                if (stack.StackSize > 0) world.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
            }
        }

        player.Entity.WatchedAttributes.MarkAllDirty();
        return given;
    }

    /// <summary>Puts clothing or armour into its free slot of the character inventory.</summary>
    private static bool TryWear(IServerPlayer player, ItemStack stack)
    {
        string? category = stack.ItemAttributes?["clothescategory"]?.AsString();
        if (category == null || !Enum.TryParse(category, true, out EnumCharacterDressType dress) || dress == EnumCharacterDressType.Unknown) return false;

        var gear = player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
        int index = (int)dress;
        if (gear == null || index < 0 || index >= gear.Count) return false;
        var slot = gear[index];
        if (slot == null || !slot.Empty || !slot.CanHold(new DummySlot(stack))) return false;

        slot.Itemstack = stack;
        slot.MarkDirty();
        return true;
    }

    /// <summary>Whether the player already wears or carries something with this code.</summary>
    private static bool Holds(IServerPlayer player, AssetLocation code)
    {
        foreach (var inv in player.InventoryManager.Inventories.Values)
        {
            if (inv == null || inv.ClassName == GlobalConstants.creativeInvClassName) continue;
            foreach (var slot in inv)
            {
                if (slot?.Itemstack?.Collectible?.Code?.Equals(code) == true) return true;
            }
        }

        return false;
    }
}
