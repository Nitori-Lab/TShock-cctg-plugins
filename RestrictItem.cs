using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    public class RestrictItem
    {
        // Restricted items list
        private static readonly HashSet<int> RestrictedItems = new HashSet<int>
        {
            61,      // Ebonstone Block
            836      // Crimstone Block
        };

        // Check and remove restricted items from player inventory
        public void CheckAndRemoveRestrictedItems(TSPlayer player)
        {
            if (player == null || !player.Active)
                return;

            bool itemsRemoved = false;
            List<string> removedItemNames = new List<string>();

            // Check all inventory slots
            for (int i = 0; i < player.TPlayer.inventory.Length; i++)
            {
                var item = player.TPlayer.inventory[i];

                if (item != null && RestrictedItems.Contains(item.type))
                {
                    string itemName = item.Name;
                    int itemCount = item.stack;

                    // Clear the item
                    item.TurnToAir();

                    // Sync to client
                    player.SendData(PacketTypes.PlayerSlot, "", player.Index, i);

                    itemsRemoved = true;
                    removedItemNames.Add($"{itemName} x{itemCount}");

                    TShock.Log.ConsoleInfo($"[CCTG] Removed restricted item from {player.Name}: {itemName} x{itemCount}");
                }
            }

            // Notify player if items were removed
            if (itemsRemoved)
            {
                player.SendWarningMessage($"Restricted items removed: {string.Join(", ", removedItemNames)}");
            }
        }

        // Modify NPC shop to remove grenades from Demolitionist
        public void ModifyNPCShop(int npcType, List<Item> shop)
        {
            if (npcType == NPCID.Demolitionist)
            {
                // Remove Grenade from shop
                for (int i = shop.Count - 1; i >= 0; i--)
                {
                    if (shop[i].type == ItemID.Grenade)
                    {
                        shop.RemoveAt(i);
                        TShock.Log.ConsoleInfo("[CCTG] Removed Grenade from Demolitionist shop");
                    }
                }
            }
        }
    }
}
