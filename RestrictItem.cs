using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
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

        // Modify NPC shop to replace first item with item ID 1922 in Demolitionist shop
        // Using the same method as TShockMoreShopItem plugin
        public void ModifyNPCShop(int npcType, List<Item> shop)
        {
            if (npcType == NPCID.Demolitionist)
            {
                TShock.Log.ConsoleInfo("[CCTG] Modifying Demolitionist shop - replacing first item with ID 1922");

                // Use the same method as TShockMoreShopItem: Send raw data packet to client
                // This is the key - we need to send a shop update packet directly to players

                // Find all players and send them the modified shop data
                foreach (var player in TShock.Players)
                {
                    if (player != null && player.Active)
                    {
                        try
                        {
                            // Check if player is near any demolitionsit
                            bool nearDemolitionist = false;
                            for (int i = 0; i < Main.npc.Length; i++)
                            {
                                var npc = Main.npc[i];
                                if (npc != null && npc.active && npc.type == NPCID.Demolitionist && npc.townNPC)
                                {
                                    float distance = Vector2.Distance(player.TPlayer.position, npc.position);
                                    if (distance < 300) // Within shop interaction range
                                    {
                                        nearDemolitionist = true;

                                        // Send the shop modification packet - empty item
                                        SendShopItemModification(player, 0, 0, 0, 0, 0); // Slot 0, Item ID 0 (empty), Stack 0, Prefix 0, Price 0

                                        TShock.Log.ConsoleInfo($"[CCTG] Sent shop modification to player {player.Name} (near demolitionsit at {i})");
                                    }
                                }
                            }

                            if (!nearDemolitionist)
                            {
                                TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} is not near any demolitionsit");
                            }
                        }
                        catch (Exception ex)
                        {
                            TShock.Log.ConsoleError($"[CCTG] Error modifying shop for player {player.Name}: {ex.Message}");
                        }
                    }
                }
            }
        }

        // Send shop item modification packet to player (based on TShockMoreShopItem's UpdateSlot method)
        private void SendShopItemModification(TSPlayer player, int slot, int itemId, int stack, int prefix, int price)
        {
            try
            {
                // Convert item data to bytes (same format as TShockMoreShopItem)
                byte[] idBytes = BitConverter.GetBytes((short)itemId);
                byte[] stackBytes = BitConverter.GetBytes((short)(stack > 0 ? stack : 1));
                byte[] priceBytes = BitConverter.GetBytes(price > 0 ? price : 1);

                // Send the raw shop update packet
                // Packet format: [14, 0, 104, slot, itemId_low, itemId_high, stack_low, stack_high, prefix, price_bytes...]
                byte[] packet = new byte[]
                {
                    14, 0, 104, (byte)slot,
                    idBytes[0], idBytes[1],
                    stackBytes[0], stackBytes[1],
                    (byte)prefix,
                    priceBytes[0], priceBytes[1], priceBytes[2], priceBytes[3],
                    0
                };

                player.SendRawData(packet);

                TShock.Log.ConsoleInfo($"[CCTG] Sent shop modification packet: Slot {slot}, Item ID {itemId}, Stack {stack}, Price {price}");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[CCTG] Error sending shop modification packet: {ex.Message}");
            }
        }

        // Replace Starfury (id: 65) with Enchanted Sword (id: 989) in Skyland Chests
        public void ReplaceStarfuryInSkylandChests()
        {
            int replacedCount = 0;

            TShock.Log.ConsoleInfo("[CCTG] Starting Starfury replacement in Skyland Chests...");

            // Iterate through all chests in the world
            for (int chestIndex = 0; chestIndex < Main.chest.Length; chestIndex++)
            {
                var chest = Main.chest[chestIndex];

                // Skip if chest doesn't exist or is empty
                if (chest == null || chest.item == null)
                    continue;

                // Check if this is a Skyland Chest (Floating Islands chest type)
                bool isSkylandChest = false;

                // Check chest location - Skyland chests are typically at high altitude
                // Sky Islands usually appear above certain height (around 300+ tiles in large worlds)
                if (chest.y < Main.worldSurface / 2) // High altitude chests
                {
                    isSkylandChest = true;
                }
                // Also check if chest is specifically in floating island biome
                else if (chest.y < Main.worldSurface && Main.tile[chest.x, chest.y] != null)
                {
                    // Check surrounding tiles for sky island characteristics
                    bool hasFloatingIslandFeatures = false;
                    for (int checkX = chest.x - 5; checkX <= chest.x + 5; checkX++)
                    {
                        for (int checkY = chest.y - 5; checkY <= chest.y + 5; checkY++)
                        {
                            if (IsValidCoord(checkX, checkY))
                            {
                                var tile = Main.tile[checkX, checkY];
                                // Look for cloud blocks or floating island specific tiles
                                if (tile != null && tile.active() &&
                                    (tile.type == TileID.Cloud || tile.type == TileID.RainCloud))
                                {
                                    hasFloatingIslandFeatures = true;
                                    break;
                                }
                            }
                        }
                        if (hasFloatingIslandFeatures) break;
                    }
                    isSkylandChest = hasFloatingIslandFeatures;
                }

                if (isSkylandChest)
                {
                    // Check all items in this chest
                    for (int itemIndex = 0; itemIndex < chest.item.Length; itemIndex++)
                    {
                        var item = chest.item[itemIndex];

                        // Check if item exists and is Starfury (id: 65)
                        if (item != null && item.active && item.type == ItemID.Starfury)
                        {
                            // Replace Starfury with Enchanted Sword (id: 989)
                            string oldItemName = item.Name;
                            int oldStack = item.stack;

                            // Replace with Enchanted Sword
                            item.SetDefaults(ItemID.EnchantedSword);
                            item.stack = oldStack;

                            replacedCount++;
                            TShock.Log.ConsoleInfo($"[CCTG] Replaced {oldItemName} with Enchanted Sword in Skyland Chest at ({chest.x}, {chest.y})");
                        }
                    }
                }
            }

            TShock.Log.ConsoleInfo($"[CCTG] Starfury replacement completed. Total items replaced: {replacedCount}");
        }

        // Debug method to print Demolitionist shop items
        public void DebugDemolitionistShop()
        {
            TShock.Log.ConsoleInfo("[CCTG] ===== DEMOLITIONIST SHOP DEBUG =====");

            // Find all Demolitionist NPCs in the world
            bool foundDemolitionist = false;

            for (int i = 0; i < Main.npc.Length; i++)
            {
                var npc = Main.npc[i];
                if (npc != null && npc.active && npc.type == NPCID.Demolitionist && npc.townNPC)
                {
                    foundDemolitionist = true;
                    TShock.Log.ConsoleInfo($"[CCTG] Found Demolitionist NPC #{i} at position ({npc.position.X / 16}, {npc.position.Y / 16})");

                    // Test shop modification with known items
                    try
                    {
                        var shopItems = new List<Item>();

                        // Create some test items to see if modification works
                        var testItem1 = new Item();
                        testItem1.SetDefaults(ItemID.Dynamite);
                        shopItems.Add(testItem1);
                        TShock.Log.ConsoleInfo($"[CCTG] Test Item 1: {testItem1.Name} (ID: {testItem1.type})");

                        var testItem2 = new Item();
                        testItem2.SetDefaults(ItemID.Grenade);
                        shopItems.Add(testItem2);
                        TShock.Log.ConsoleInfo($"[CCTG] Test Item 2: {testItem2.Name} (ID: {testItem2.type})");

                        var testItem3 = new Item();
                        testItem3.SetDefaults(1922); // The target item ID we want to set
                        shopItems.Add(testItem3);
                        TShock.Log.ConsoleInfo($"[CCTG] Test Item 3 (ID 1922): {testItem3.Name} (ID: {testItem3.type})");

                        TShock.Log.ConsoleInfo($"[CCTG] Total test items created: {shopItems.Count}");

                        // Trigger the shop modification
                        TShock.Log.ConsoleInfo("[CCTG] Attempting to modify shop with test items...");
                        ModifyNPCShop(NPCID.Demolitionist, shopItems);

                        TShock.Log.ConsoleInfo($"[CCTG] Shop items after modification: {shopItems.Count}");
                        for (int k = 0; k < shopItems.Count; k++)
                        {
                            var item = shopItems[k];
                            TShock.Log.ConsoleInfo($"[CCTG] Modified Shop Slot {k}: {item.Name} (ID: {item.type}) - Value: {item.value}");
                        }
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleError($"[CCTG] Error in shop modification test: {ex.Message}");
                        TShock.Log.ConsoleError($"[CCTG] Stack trace: {ex.StackTrace}");
                    }
                }
            }

            if (!foundDemolitionist)
            {
                TShock.Log.ConsoleInfo("[CCTG] No Demolitionist NPCs found in the world");

                // List available NPCs for debugging
                TShock.Log.ConsoleInfo("[CCTG] Available NPCs in world:");
                for (int i = 0; i < Math.Min(20, Main.npc.Length); i++)
                {
                    var npc = Main.npc[i];
                    if (npc != null && npc.active)
                    {
                        TShock.Log.ConsoleInfo($"[CCTG] NPC #{i}: Type {npc.type}, Active: {npc.active}, TownNPC: {npc.townNPC}");
                    }
                }
            }

            TShock.Log.ConsoleInfo("[CCTG] ===== END DEMOLITIONIST SHOP DEBUG =====");
        }

        // Modify demolitionsit shop for a specific player (no console output for automatic use)
        public void ModifyDemolitionistShop(TSPlayer player)
        {
            try
            {
                // Check if player is near any demolitionsit
                for (int i = 0; i < Main.npc.Length; i++)
                {
                    var npc = Main.npc[i];
                    if (npc != null && npc.active && npc.type == NPCID.Demolitionist && npc.townNPC)
                    {
                        float distance = Vector2.Distance(player.TPlayer.position, npc.position);
                        if (distance < 300) // Within shop interaction range
                        {
                            // Send the shop modification packet - empty item
                            SendShopItemModification(player, 0, 0, 0, 0, 0); // Slot 0, Item ID 0 (empty), Stack 0, Prefix 0, Price 0
                            break; // Only modify once even if multiple demolitionsits are found
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[CCTG] Error modifying demolitionsit shop for player {player.Name}: {ex.Message}");
            }
        }

        // Check if coordinates are valid
        private bool IsValidCoord(int x, int y)
        {
            return x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY;
        }
    }
}
