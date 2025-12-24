using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// House builder manager - coordinates location finding and house construction
    /// </summary>
    public class HouseBuilder
    {
        // Protected house areas
        private List<Rectangle> protectedHouseAreas = new List<Rectangle>();

        // Left/right house positions for team teleportation
        private Point leftHouseSpawn = new Point(-1, -1);
        private Point rightHouseSpawn = new Point(-1, -1);

        // House building status
        private bool housesBuilt = false;

        // Helper modules
        private HouseLocationFinder locationFinder = new HouseLocationFinder();
        private HouseStructure houseStructure = new HouseStructure();

        // Property accessors
        public List<Rectangle> ProtectedHouseAreas => protectedHouseAreas;
        public Point LeftHouseSpawn => leftHouseSpawn;
        public Point RightHouseSpawn => rightHouseSpawn;
        public bool HousesBuilt => housesBuilt;

        /// <summary>
        /// Build houses on both sides of spawn
        /// </summary>
        public void BuildHouses()
        {
            int spawnX = Main.spawnTileX;
            int spawnY = Main.spawnTileY;

            TShock.Log.ConsoleInfo($"[CCTG] Starting house construction at spawn: ({spawnX}, {spawnY})");

            // Left house (Red team)
            int leftHouseX = spawnX - (200 + locationFinder.GetRandomOffset());
            var leftLocation = BuildSingleHouse(leftHouseX, spawnY, "left", -1); // -1 means left
            if (leftLocation.X != -1)
            {
                leftHouseSpawn = leftLocation;
                TShock.Log.ConsoleInfo($"[CCTG] Left house (Red team) spawn: ({leftHouseSpawn.X}, {leftHouseSpawn.Y})");
            }

            // Right house (Blue team)
            int rightHouseX = spawnX + (200 + locationFinder.GetRandomOffset());
            var rightLocation = BuildSingleHouse(rightHouseX, spawnY, "right", 1); // 1 means right
            if (rightLocation.X != -1)
            {
                rightHouseSpawn = rightLocation;
                TShock.Log.ConsoleInfo($"[CCTG] Right house (Blue team) spawn: ({rightHouseSpawn.X}, {rightHouseSpawn.Y})");
            }

            housesBuilt = true;
            TShock.Log.ConsoleInfo($"[CCTG] House construction complete!");
            TSPlayer.All.SendSuccessMessage("[CCTG] Houses on both sides of spawn built!");
        }

        /// <summary>
        /// Clear all houses
        /// </summary>
        public void ClearHouses()
        {
            if (protectedHouseAreas.Count == 0)
            {
                TShock.Log.ConsoleInfo("[CCTG] No houses to clear");
                return;
            }

            TShock.Log.ConsoleInfo($"[CCTG] Starting to clear houses, total {protectedHouseAreas.Count} areas");

            foreach (var houseArea in protectedHouseAreas)
            {
                // Extended clear range: including walls, foundation, ceiling and 40 blocks above
                int clearStartX = houseArea.X - 2;
                int clearEndX = houseArea.X + houseArea.Width + 2;
                int clearStartY = houseArea.Y - 41; // 40 blocks above + 1 ceiling block
                int clearEndY = houseArea.Y + houseArea.Height + 2; // Below including foundation

                for (int x = clearStartX; x < clearEndX; x++)
                {
                    for (int y = clearStartY; y < clearEndY; y++)
                    {
                        if (IsValidCoord(x, y))
                        {
                            Main.tile[x, y].ClearEverything();
                        }
                    }
                }

                // Refresh area
                TSPlayer.All.SendTileRect((short)clearStartX, (short)clearStartY,
                    (byte)(clearEndX - clearStartX), (byte)(clearEndY - clearStartY));
            }

            // Clear protected house areas list
            protectedHouseAreas.Clear();

            // Reset house positions
            leftHouseSpawn = new Point(-1, -1);
            rightHouseSpawn = new Point(-1, -1);

            // Reset house building status
            housesBuilt = false;

            TShock.Log.ConsoleInfo("[CCTG] Houses cleared");
        }

        /// <summary>
        /// Clear mobs from houses
        /// </summary>
        public void ClearMobsInHouses()
        {
            if (protectedHouseAreas.Count == 0)
                return;

            int clearedCount = 0;

            // Apply to all NPCs
            for (int i = 0; i < Main.npc.Length; i++)
            {
                var npc = Main.npc[i];

                // Skip inactive NPCs
                if (npc == null || !npc.active)
                    continue;

                // Skip friendly and town NPCs
                if (npc.friendly || npc.townNPC)
                    continue;

                // Get NPC tile position
                int npcTileX = (int)(npc.position.X / 16);
                int npcTileY = (int)(npc.position.Y / 16);

                // Check if NPC is within any protected house area
                foreach (var houseArea in protectedHouseAreas)
                {
                    if (houseArea.Contains(npcTileX, npcTileY))
                    {
                        // Clear Npc
                        npc.active = false;
                        npc.type = 0;

                        // Update NPC state to clients
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);

                        clearedCount++;
                        break; // No need to check other areas
                    }
                }
            }
        }

        /// <summary>
        /// Build a single house at specified position
        /// Direction: -1 for left, 1 for right
        /// Returns spawn point inside the house
        /// </summary>
        private Point BuildSingleHouse(int centerX, int groundY, string side, int direction)
        {
            const int totalWidth = 14; // 5 + 10 - 1 = 14
            const int maxHeight = 11; // highest height

            // Find suitable location
            int groundLevel = locationFinder.FindLocation(centerX, groundY, totalWidth, maxHeight, direction, side);

            // Calculate final startX based on found location
            int startX = centerX;
            if (groundLevel == -1)
            {
                TShock.Log.ConsoleError($"[CCTG] {side} Failed to find suitable location, using default");
                groundLevel = groundY;
            }

            // Build the house structure
            Point spawnPoint = houseStructure.BuildHouse(startX, groundLevel, direction, side);

            // Get and save protected areas
            var (leftRoom, rightRoom) = houseStructure.GetProtectedAreas(startX, groundLevel, direction);
            protectedHouseAreas.Add(leftRoom);
            protectedHouseAreas.Add(rightRoom);

            TShock.Log.ConsoleInfo($"[CCTG] {side} House protected areas recorded:");
            TShock.Log.ConsoleInfo($"[CCTG] Left room protected area: ({leftRoom.X}, {leftRoom.Y}, {leftRoom.Width}x{leftRoom.Height})");
            TShock.Log.ConsoleInfo($"[CCTG] Right room protected area: ({rightRoom.X}, {rightRoom.Y}, {rightRoom.Width}x{rightRoom.Height})");

            return spawnPoint;
        }

        /// <summary>
        /// Check if coordinates are within world bounds
        /// </summary>
        private bool IsValidCoord(int x, int y)
        {
            return x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY;
        }
    }
}
