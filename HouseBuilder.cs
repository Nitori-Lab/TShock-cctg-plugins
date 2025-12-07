using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// House builder manager
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

        // Random number generator
        private Random _random = new Random();

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

            // Left house (initial 200 blocks, search towards spawn to 100, then outward if failed) Red team
            int leftHouseX = spawnX - (200 + _random.Next(-20, 21));
            var leftLocation = BuildSingleHouse(leftHouseX, spawnY, "left", -1, false); // false = not mirrored
            if (leftLocation.X != -1)
            {
                leftHouseSpawn = leftLocation; // Record left house position
                TShock.Log.ConsoleInfo($"[CCTG] Left house (Red team) spawn: ({leftHouseSpawn.X}, {leftHouseSpawn.Y})");
            }

            // Right house (initial 200 blocks, search towards spawn to 100, then outward if failed) Blue team
            int rightHouseX = spawnX + (200 + _random.Next(-20, 21));
            var rightLocation = BuildSingleHouse(rightHouseX, spawnY, "right", 1, true); // true = mirrored
            if (rightLocation.X != -1)
            {
                rightHouseSpawn = rightLocation; // Record right house position
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

        // Build a single house at specified position
        // Direction: -1 for left, 1 for right
        // mirror: true to build mirrored version (blue team), false for normal (red team)
        // Returns spawn point inside the house
        private Point BuildSingleHouse(int centerX, int groundY, string side, int direction, bool mirror)
        {
            // left house: 5 width x 11 height
            const int leftRoomWidth = 5;
            const int leftRoomHeight = 11;

            // right house: 10 width x 7 height
            const int rightRoomWidth = 10;
            const int rightRoomHeight = 7;

            // total dimensions
            const int totalWidth = leftRoomWidth + rightRoomWidth - 1;
            const int maxHeight = leftRoomHeight; // highest height

            // Find suitable ground level
            // direction: -1 for left, 1 for right
            int worldSpawnX = Main.spawnTileX;
            int startX = centerX;
            int groundLevel = -1;

            // First try initial position (around 200 blocks)
            groundLevel = FindSuitableHeightForHouse(startX, groundY, totalWidth, maxHeight);

            // Phase 1: Search towards spawn (200→100 blocks)
            if (groundLevel == -1)
            {
                // Calculate current distance from spawn
                int currentDistance = Math.Abs(centerX - worldSpawnX);

                // Search towards spawn until 100 blocks away
                for (int offset = 1; offset <= currentDistance - 100; offset++)
                {
                    // Move towards spawn (negative=right, positive=left)
                    int testX = centerX - (direction * offset);

                    groundLevel = FindSuitableHeightForHouse(testX, groundY, totalWidth, maxHeight);
                    if (groundLevel != -1)
                    {
                        startX = testX;
                        break;
                    }
                }
            }

            // Phase 2: If not found at 100 blocks, search away from spawn (>200 blocks)
            if (groundLevel == -1)
            {
                TShock.Log.ConsoleWarn($"[CCTG] {side}No suitable position found towards spawn, searching outward from spawn (>200 blocks)");

                // Search outward from spawn (beyond 200 blocks)
                for (int offset = 1; offset <= 100; offset++)
                {
                    // Move away from spawn (negative=left, positive=right)
                    int testX = centerX + (direction * offset);

                    groundLevel = FindSuitableHeightForHouse(testX, groundY, totalWidth, maxHeight);
                    if (groundLevel != -1)
                    {
                        startX = testX;
                        break;
                    }
                }
            }

            // If still no suitable position, use forced build mode
            if (groundLevel == -1)
            {
                TShock.Log.ConsoleWarn($"[CCTG] {side}No suitable position found in search range, using forced build mode (lowered requirement: 50% ground contact)");

                // Search both directions from spawn±200 blocks
                int forceSpawnX = Main.spawnTileX;
                int forceBuildStartX = direction < 0 ? forceSpawnX - 200 : forceSpawnX + 200;

                bool foundValidLocation = false;
                const int forceBuildSearchRange = 200; // Search 200 blocks in each direction

                // Search horizontally
                for (int offset = 0; offset <= forceBuildSearchRange && !foundValidLocation; offset++)
                {
                    // Try both sides
                    int[] testXPositions = offset == 0
                        ? new int[] { forceBuildStartX }
                        : new int[] { forceBuildStartX + offset, forceBuildStartX - offset };

                    foreach (int testX in testXPositions)
                    {
                        // Search downward for suitable height (within 100 blocks of spawn Y)
                        for (int y = groundY - 30; y < groundY + 70; y++)
                        {
                            if (!IsValidCoord(testX, y))
                                continue;

                            // Check ground contact rate at this level
                            int solidCount = 0;
                            int totalChecked = 0;
                            for (int x = testX; x < testX + totalWidth; x++)
                            {
                                if (IsValidCoord(x, y))
                                {
                                    totalChecked++;
                                    var tile = Main.tile[x, y];
                                    if (tile != null && tile.active() && Main.tileSolid[tile.type])
                                    {
                                        solidCount++;
                                    }
                                }
                            }

                            // Require 50%+ ground contact
                            if (totalChecked > 0 && solidCount >= totalWidth * 0.5)
                            {
                                // Check if blocks above are blocking
                                bool skyIsClear = true;
                                for (int checkX = testX; checkX < testX + totalWidth; checkX++)
                                {
                                    for (int checkY = y - maxHeight - 40; checkY < y; checkY++)
                                    {
                                        if (IsValidCoord(checkX, checkY))
                                        {
                                            var checkTile = Main.tile[checkX, checkY];
                                            if (checkTile != null && checkTile.active() && Main.tileSolid[checkTile.type])
                                            {
                                                skyIsClear = false;
                                                break;
                                            }
                                        }
                                    }
                                    if (!skyIsClear) break;
                                }

                                if (skyIsClear)
                                {
                                    startX = testX;
                                    groundLevel = y;
                                    foundValidLocation = true;
                                    break;
                                }
                            }
                        }
                        if (foundValidLocation) break;
                    }
                }

                if (!foundValidLocation)
                {
                    // If still not found, use basic fallback
                    startX = forceBuildStartX;
                    groundLevel = groundY;
                    TShock.Log.ConsoleError($"[CCTG] {side}Cannot find suitable position, will force build at X={startX}, Y={groundLevel} and clear space");
                }
            }


            // Clear entire area (including space outside doors and 40 blocks above)
            // 2 extra blocks on left of left door, 2 extra on right of right door
            int clearStartX = startX - 2;
            int clearEndX = startX + totalWidth + 2;
            const int skyClearHeight = 40; // Clear 40 blocks above

            for (int x = clearStartX; x < clearEndX; x++)
            {
                // Clear from (groundLevel - maxHeight - skyClearHeight) to groundLevel
                for (int y = groundLevel - maxHeight - skyClearHeight; y <= groundLevel; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].ClearEverything();
                    }
                }
            }

            // Determine room positions based on mirror flag
            int firstRoomWidth, firstRoomHeight, secondRoomWidth, secondRoomHeight;
            if (mirror)
            {
                // Blue team (mirrored): big room first (10x7), then small room (5x11)
                firstRoomWidth = rightRoomWidth;
                firstRoomHeight = rightRoomHeight;
                secondRoomWidth = leftRoomWidth;
                secondRoomHeight = leftRoomHeight;
            }
            else
            {
                // Red team (normal): small room first (5x11), then big room (10x7)
                firstRoomWidth = leftRoomWidth;
                firstRoomHeight = leftRoomHeight;
                secondRoomWidth = rightRoomWidth;
                secondRoomHeight = rightRoomHeight;
            }

            // === Build first room ===
            int firstStartX = startX;
            int firstTopY = groundLevel - firstRoomHeight;

            // First room foundation layer (at ground level, 1 extra block on left side)
            for (int x = firstStartX - 1; x <= firstStartX + firstRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab); // Base foundation, built on ground level
            }

            // First room floor (interior floor)
            for (int x = firstStartX; x < firstStartX + firstRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab); // floor
            }

            // First room ceiling
            for (int x = firstStartX; x < firstStartX + firstRoomWidth; x++)
            {
                PlaceTile(x, firstTopY, TileID.StoneSlab);
            }

            // First room left wall
            for (int y = firstTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(firstStartX, y, TileID.StoneSlab);
            }

            // === Build second room ===
            int secondStartX = firstStartX + firstRoomWidth - 1;
            int secondTopY = groundLevel - secondRoomHeight;

            // Second room foundation layer (at ground level, 1 extra block on right side)
            // left side already built as part of first room
            for (int x = secondStartX; x <= secondStartX + secondRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab); // base foundation
            }

            // Second room floor
            for (int x = secondStartX; x < secondStartX + secondRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab); // floor
            }

            // Second room ceiling
            for (int x = secondStartX; x < secondStartX + secondRoomWidth; x++)
            {
                PlaceTile(x, secondTopY, TileID.StoneSlab);
            }

            // Second room right wall
            for (int y = secondTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(secondStartX + secondRoomWidth - 1, y, TileID.StoneSlab);
            }

            // === Build middle wall with passage ===
            int middleX = secondStartX; // middle wall X position
            int passageHeight = 4; // passage height (bottom 4 blocks)
            int tallerRoomTopY = Math.Min(firstTopY, secondTopY); // Use the taller room's top

            // middle wall
            for (int y = tallerRoomTopY + 1; y < groundLevel - 1; y++)
            {
                // Determine if within passage height
                if (y >= secondTopY)
                {
                    // Passage area, leave empty
                    if (y < groundLevel - passageHeight)
                    {
                        PlaceTile(middleX, y, TileID.StoneSlab);
                    }
                }
                else
                {
                    // Above passage, build wall
                    PlaceTile(middleX, y, TileID.StoneSlab);
                }
            }

            // Place doors
            PlaceDoor(firstStartX, groundLevel - 3, TileID.ClosedDoor); // first room left side door
            PlaceDoor(secondStartX + secondRoomWidth - 1, groundLevel - 3, TileID.ClosedDoor); // second room right side door

            // Determine which room is the tall room (11 height) for platforms/torches
            int tallRoomStartX = mirror ? secondStartX : firstStartX;
            int tallRoomDoorTopY = groundLevel - 4;

            // Place platforms in tall room
            PlacePlatform(tallRoomStartX + 1, tallRoomDoorTopY + 3 - 6, TileID.Platforms, 0);
            PlacePlatform(tallRoomStartX + 3, tallRoomDoorTopY + 3 - 6, TileID.Platforms, 0);

            // Place torches in tall room
            PlaceTorch(tallRoomStartX + 1, tallRoomDoorTopY + 3 - 6 - 1);
            PlaceTorch(tallRoomStartX + 3, tallRoomDoorTopY + 3 - 6 - 1);

            // Fill wood walls - first room
            for (int x = firstStartX + 1; x < firstStartX + firstRoomWidth - 1; x++)
            {
                for (int y = firstTopY + 1; y < groundLevel - 1; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].wall = WallID.Wood;
                    }
                }
            }

            // Fill wood walls - second room
            for (int x = secondStartX; x < secondStartX + secondRoomWidth - 1; x++)
            {
                for (int y = secondTopY + 1; y < groundLevel - 1; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].wall = WallID.Wood;
                    }
                }
            }

            // Place furniture - pass room info based on mirror flag
            PlaceFurniture(firstStartX, secondStartX, groundLevel, firstRoomWidth, secondRoomWidth, mirror);

            // Refresh area
            TSPlayer.All.SendTileRect((short)startX, (short)tallerRoomTopY, (byte)(totalWidth + 2), (byte)(maxHeight + 2));

            // Save protected area (only interior space, not walls)
            // First room interior area
            int firstInteriorX = firstStartX + 1;
            int firstInteriorY = firstTopY + 1;
            int firstInteriorWidth = firstRoomWidth - 1; // Excluding left wall and middle wall
            int firstInteriorHeight = firstRoomHeight - 2; // Excluding ceiling and floor
            protectedHouseAreas.Add(new Rectangle(firstInteriorX, firstInteriorY, firstInteriorWidth, firstInteriorHeight));

            // Second room interior area
            int secondInteriorX = secondStartX + 1;
            int secondInteriorY = secondTopY + 1;
            int secondInteriorWidth = secondRoomWidth - 2; // Not including middle wall and right wall
            int secondInteriorHeight = secondRoomHeight - 2; // Excluding ceiling and floor
            protectedHouseAreas.Add(new Rectangle(secondInteriorX, secondInteriorY, secondInteriorWidth, secondInteriorHeight));

            // Return house spawn point (in the wider/shorter room center, above floor)
            // For normal house: spawn in second room (big room)
            // For mirrored house: spawn in first room (big room)
            int spawnX, spawnY;
            if (mirror)
            {
                // Mirrored: spawn in first room (big room)
                spawnX = firstStartX + firstRoomWidth / 2;
            }
            else
            {
                // Normal: spawn in second room (big room)
                spawnX = secondStartX + secondRoomWidth / 2;
            }
            spawnY = groundLevel - 3; // 2 blocks above floor

            TShock.Log.ConsoleInfo($"[CCTG] {side}House built, spawn set to: ({spawnX}, {spawnY})");
            return new Point(spawnX, spawnY);
        }

        // Place furniture
        private void PlaceFurniture(int firstStartX, int secondStartX, int groundLevel, int firstWidth, int secondWidth, bool mirror)
        {
            int floorY = groundLevel - 2; // Above floor

            // Determine which room gets furniture (always the big 10x7 room)
            int furnitureRoomStartX = mirror ? firstStartX : secondStartX;

            // Furniture room - Wood chair (fixed position, 1 block)
            WorldGen.PlaceObject(furnitureRoomStartX + 2, floorY, TileID.Chairs, false, 0);

            // Furniture room - Anvil (adjacent to chair, 2 blocks)
            WorldGen.PlaceObject(furnitureRoomStartX + 3, floorY, TileID.Anvils, false, 0);

            // Furniture room - Wood platform (above anvil y+1, 2 blocks)
            int platformY = floorY - 1;
            PlacePlatform(furnitureRoomStartX + 3, platformY, TileID.Platforms, 0);
            PlacePlatform(furnitureRoomStartX + 4, platformY, TileID.Platforms, 0);

            // Furniture room - Work bench (on platform, 2 blocks)
            WorldGen.PlaceObject(furnitureRoomStartX + 3, platformY - 1, TileID.WorkBenches, false, 0);

            // Furniture room - Furnace (right of anvil, 3 blocks)
            WorldGen.PlaceObject(furnitureRoomStartX + 6, floorY, TileID.Furnaces, false, 0);

            // Refresh furniture area
            NetMessage.SendTileSquare(-1, firstStartX, floorY - 2, 15);
        }

        // Place platforms
        private void PlacePlatform(int x, int y, ushort type, int style)
        {
            if (IsValidCoord(x, y))
            {
                var tile = Main.tile[x, y];
                tile.type = type;
                tile.active(true);
                tile.slope(0);
                tile.halfBrick(false);

                if (style > 0)
                {
                    tile.frameX = (short)(style * 18);
                }

                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        // Find suitable height for house placement
        // Uses stricter criteria
        private int FindSuitableHeightForHouse(int startX, int startY, int width, int height)
        {
            const int searchRange = 30; // Search 30 blocks from spawn Y
            const int liquidCheckHeight = 5; // Check liquid

            // Search downward first
            for (int offsetY = 0; offsetY <= searchRange; offsetY++)
            {
                int testGroundLevel = startY + offsetY;

                // Check if this position is suitable ground
                if (IsValidGroundSurface(startX, testGroundLevel, width, liquidCheckHeight, height))
                {
                    return testGroundLevel;
                }
            }

            // Then search upward
            for (int offsetY = 1; offsetY <= searchRange; offsetY++)
            {
                int testGroundLevel = startY - offsetY;

                // Check if this position is suitable ground
                if (IsValidGroundSurface(startX, testGroundLevel, width, liquidCheckHeight, height))
                {
                    return testGroundLevel;
                }
            }

            // If no suitable position found, return -1 for failure
            return -1;
        }

        // Check if valid ground surface
        // height: house height (for checking 40 blocks above)
        private bool IsValidGroundSurface(int startX, int groundY, int width, int liquidCheckHeight, int houseHeight)
        {
            int validGroundTiles = 0;
            int totalChecked = 0;

            // Check each position within width range
            for (int x = startX; x < startX + width; x++)
            {
                if (!IsValidCoord(x, groundY))
                    continue;

                totalChecked++;
                var groundTile = Main.tile[x, groundY];

                // 1. Check if ground tile is solid
                if (groundTile == null || !groundTile.active() || !Main.tileSolid[groundTile.type])
                    continue;

                // 2. Check if above tile is empty
                if (!IsValidCoord(x, groundY - 1))
                    continue;

                var aboveTile = Main.tile[x, groundY - 1];
                if (aboveTile != null && aboveTile.active() && Main.tileSolid[aboveTile.type])
                    continue;

                // 3. Check for liquid above ground within liquidCheckHeight
                int liquidCount = 0;
                for (int checkY = groundY - liquidCheckHeight; checkY < groundY; checkY++)
                {
                    if (IsValidCoord(x, checkY))
                    {
                        var checkTile = Main.tile[x, checkY];
                        if (checkTile != null && checkTile.liquid > 0)
                        {
                            liquidCount++;
                        }
                    }
                }

                // If too much liquid above (more than half check height), skip
                if (liquidCount > liquidCheckHeight / 2)
                    continue;

                // This position passes all checks
                validGroundTiles++;
            }

            // Require 100% ground contact (house bottom must fully touch ground)
            if (totalChecked == 0 || validGroundTiles != width)
                return false;

            // 4. Check if blocks exist 40 blocks above (house must have enough space above)
            const int skyCheckHeight = 40;
            for (int x = startX; x < startX + width; x++)
            {
                for (int y = groundY - houseHeight - 1; y >= groundY - houseHeight - skyCheckHeight; y--)
                {
                    if (!IsValidCoord(x, y))
                        continue;

                    var skyTile = Main.tile[x, y];
                    if (skyTile != null && skyTile.active() && Main.tileSolid[skyTile.type])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // Place tile
        private void PlaceTile(int x, int y, ushort type, int style = 0)
        {
            if (IsValidCoord(x, y))
            {
                var tile = Main.tile[x, y];
                tile.type = type;
                tile.active(true);
                tile.slope(0);
                tile.halfBrick(false);

                if (style > 0)
                {
                    tile.frameX = (short)(style * 18);
                }

                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        // Place door
        private void PlaceDoor(int x, int y, ushort type)
        {
            if (IsValidCoord(x, y))
            {
                WorldGen.PlaceDoor(x, y, type);
                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        // Place torches
        private void PlaceTorch(int x, int y)
        {
            if (IsValidCoord(x, y))
            {
                WorldGen.PlaceTile(x, y, TileID.Torches, false, false, -1, 0);
                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        // Check if coordinates are within world bounds
        private bool IsValidCoord(int x, int y)
        {
            return x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY;
        }
    }
}
