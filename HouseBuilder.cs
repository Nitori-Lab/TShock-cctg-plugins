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
            var leftLocation = BuildSingleHouse(leftHouseX, spawnY, "left", -1); // -1 means left
            if (leftLocation.X != -1)
            {
                leftHouseSpawn = leftLocation; // Record left house position
                TShock.Log.ConsoleInfo($"[CCTG] Left house (Red team) spawn: ({leftHouseSpawn.X}, {leftHouseSpawn.Y})");
            }

            // Right house (initial 200 blocks, search towards spawn to 100, then outward if failed) Blue team
            int rightHouseX = spawnX + (200 + _random.Next(-20, 21));
            var rightLocation = BuildSingleHouse(rightHouseX, spawnY, "right", 1); // 1 means right
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

        // Build a single house at specified position
        // Direction: -1 for left, 1 for right
        // Returns spawn point inside the house
        private Point BuildSingleHouse(int centerX, int groundY, string side, int direction)
        {
            // Red team: original layout - small room (5x11) on left, large room (10x7) on right
            // Blue team: mirror layout - large room (10x7) on left, small room (5x11) on right

            int leftRoomWidth, leftRoomHeight, rightRoomWidth, rightRoomHeight;

            if (direction < 0) // Red team house - original layout
            {
                leftRoomWidth = 5;
                leftRoomHeight = 11;
                rightRoomWidth = 10;
                rightRoomHeight = 7;
            }
            else // Blue team house - mirror layout
            {
                leftRoomWidth = 10;  // Large room on left
                leftRoomHeight = 7;  // Shorter height
                rightRoomWidth = 5;  // Small room on right
                rightRoomHeight = 11; // Taller height
            }

            // total dimensions
            const int totalWidth = 14; // 5 + 10 - 1 = 14
            const int maxHeight = 11; // highest height

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
                TShock.Log.ConsoleInfo($"[CCTG] Initial position X={startX} unsuitable, searching towards spawn (to 100 blocks)");

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
                        int distanceToSpawn = Math.Abs(testX - worldSpawnX);
                        TShock.Log.ConsoleInfo($"[CCTG] At distance from spawn{distanceToSpawn}blocks found suitable position: X={startX}");
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
                        int distanceToSpawn = Math.Abs(testX - worldSpawnX);
                        TShock.Log.ConsoleInfo($"[CCTG] At distance from spawn{distanceToSpawn}blocks found suitable position: X={startX}");
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
                                    int contactPercent = (int)((double)solidCount / totalWidth * 100);
                                    TShock.Log.ConsoleInfo($"[CCTG] {side}House force-built at X={startX}, Y={groundLevel}(ground contact{contactPercent}%, clear above)");
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

            TShock.Log.ConsoleInfo($"[CCTG] {side}House build position: X={startX}, ground level={groundLevel}");

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

            TShock.Log.ConsoleInfo($"[CCTG] {side}House area cleared (including{skyClearHeight}blocks above)");

            // === Build left room (5x11) ===
            int leftStartX = startX;
            int leftTopY = groundLevel - leftRoomHeight;

            // Left room foundation layer (at ground level, 1 extra block each side)
            for (int x = leftStartX - 1; x <= leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab); // Base foundation, built on ground level
            }

            // left room floor (interior floor)
            for (int x = leftStartX; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab); // floor
            }

            // left room ceiling
            for (int x = leftStartX; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, leftTopY, TileID.StoneSlab);
            }

            // left room left wall
            for (int y = leftTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(leftStartX, y, TileID.StoneSlab);
            }

            // === Build right room (10x7) ===
            int rightStartX = leftStartX + leftRoomWidth - 1;
            int rightTopY = groundLevel - rightRoomHeight;

            // right room foundation layer (at ground level, 1 extra block on right side)
            // left side already built as part of left room
            for (int x = rightStartX; x <= rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab); // base foundation
            }

            // right room floor
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab); // floor
            }

            // right room ceiling
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, rightTopY, TileID.StoneSlab);
            }

            // right room right wall
            for (int y = rightTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(rightStartX + rightRoomWidth - 1, y, TileID.StoneSlab);
            }

            // === Build middle wall with passage ===
            int middleX = rightStartX; // middle wall X position
            int passageHeight = 4; // passage height (bottom 4 blocks)

            // middle wall
            for (int y = leftTopY + 1; y < groundLevel - 1; y++)
            {
                // Determine if within passage height
                // Use the higher ceiling position for passage start
                int higherCeilingY = Math.Max(leftTopY, rightTopY);

                if (y >= higherCeilingY)
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
            PlaceDoor(leftStartX, groundLevel - 3, TileID.ClosedDoor); // left room left side door
            PlaceDoor(rightStartX + rightRoomWidth - 1, groundLevel - 3, TileID.ClosedDoor); // right room right side door

            // Red team: platforms/torches above left door
            // Blue team: platforms/torches above left room door, moved 9 blocks to the right

            if (direction < 0) // Red team house - platforms above left door
            {
                int leftDoorTopX = leftStartX;
                int leftDoorTopY = groundLevel - 4;

                // Place platforms above left door
                PlacePlatform(leftDoorTopX + 1, leftDoorTopY + 3 - 6, TileID.Platforms, 0);
                PlacePlatform(leftDoorTopX + 3, leftDoorTopY + 3 - 6, TileID.Platforms, 0);
                TShock.Log.ConsoleInfo($"[CCTG] Platforms above red team left door placed");

                // Place torches above left door
                PlaceTorch(leftDoorTopX + 1, leftDoorTopY + 3 - 6 - 1);
                PlaceTorch(leftDoorTopX + 3, leftDoorTopY + 3 - 6 - 1);
                TShock.Log.ConsoleInfo($"[CCTG] Torches above red team left door placed");
            }
            else // Blue team house - platforms above left room door, moved 9 blocks right
            {
                // For blue team, left room is large room (10x7), place platforms above its left door
                int leftDoorTopX = leftStartX;
                int leftDoorTopY = groundLevel - 4;

                // Move 9 blocks to the right
                int shiftedX = leftDoorTopX + 9;

                // Place platforms at shifted position (9 blocks right of original)
                PlacePlatform(shiftedX + 1, leftDoorTopY + 3 - 6, TileID.Platforms, 0);
                PlacePlatform(shiftedX + 3, leftDoorTopY + 3 - 6, TileID.Platforms, 0);
                TShock.Log.ConsoleInfo($"[CCTG] Platforms for blue team placed 9 blocks right at ({shiftedX + 1}, {leftDoorTopY + 3 - 6})");

                // Place torches at shifted position (9 blocks right of original)
                PlaceTorch(shiftedX + 1, leftDoorTopY + 3 - 6 - 1);
                PlaceTorch(shiftedX + 3, leftDoorTopY + 3 - 6 - 1);
                TShock.Log.ConsoleInfo($"[CCTG] Torches for blue team placed 9 blocks right at ({shiftedX + 1}, {leftDoorTopY + 3 - 6 - 1})");
            }

            // Fill wood walls - left room
            for (int x = leftStartX + 1; x < leftStartX + leftRoomWidth - 1; x++)
            {
                for (int y = leftTopY + 1; y < groundLevel - 1; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].wall = WallID.Wood;
                    }
                }
            }

            // Fill wood walls - right room
            for (int x = rightStartX; x < rightStartX + rightRoomWidth - 1; x++)
            {
                for (int y = rightTopY + 1; y < groundLevel - 1; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].wall = WallID.Wood;
                    }
                }
            }

            // Place furniture
            PlaceFurniture(leftStartX, rightStartX, groundLevel, leftRoomWidth, rightRoomWidth, direction);

            // For blue team (mirrored layout), fix the gap at top-right corner with hardcoded approach
            // This must be done after all other construction to ensure blocks are not overwritten
            if (direction > 0) // Blue team house
            {
                // Calculate the top-right corner position of the blue team house
                int rightRoomTopX = rightStartX + rightRoomWidth - 1;
                int rightRoomTopY = groundLevel - rightRoomHeight;

                TShock.Log.ConsoleInfo($"[CCTG] Blue team: Starting hardcoded fix for top-right corner gap at ({rightRoomTopX}, {rightRoomTopY})");

                // Hardcoded fix: from top-right corner, place 4 blocks to the left
                // and extend 3 blocks down from the 4th block
                for (int i = 1; i <= 4; i++)
                {
                    int blockX = rightRoomTopX - i; // i blocks to the left of top-right corner

                    // Place horizontal blocks (left 4 blocks) - use direct tile manipulation
                    if (IsValidCoord(blockX, rightRoomTopY))
                    {
                        var tile = Main.tile[blockX, rightRoomTopY];
                        tile.type = TileID.StoneSlab;
                        tile.active(true);
                        tile.slope(0);
                        tile.halfBrick(false);
                        WorldGen.SquareTileFrame(blockX, rightRoomTopY, true);
                        TShock.Log.ConsoleInfo($"[CCTG] Placed horizontal block at ({blockX}, {rightRoomTopY})");
                    }

                    // On the 4th block (i=4), extend 3 blocks down
                    if (i == 4)
                    {
                        for (int j = 1; j <= 3; j++)
                        {
                            int vertY = rightRoomTopY + j;
                            if (IsValidCoord(blockX, vertY))
                            {
                                var tile = Main.tile[blockX, vertY];
                                tile.type = TileID.StoneSlab;
                                tile.active(true);
                                tile.slope(0);
                                tile.halfBrick(false);
                                WorldGen.SquareTileFrame(blockX, vertY, true);
                                TShock.Log.ConsoleInfo($"[CCTG] Placed vertical block at ({blockX}, {vertY})");
                            }
                        }
                    }
                }

                TShock.Log.ConsoleInfo($"[CCTG] Blue team: Completed hardcoded fix for top-right corner gap");
            }

            // Refresh area (do this last to ensure all blocks are sent to clients)
            TSPlayer.All.SendTileRect((short)startX, (short)leftTopY, (byte)(totalWidth + 2), (byte)(maxHeight + 2));

            // Save protected area (only interior space, not walls)
            // Left room interior area
            int leftInteriorX = leftStartX + 1;
            int leftInteriorY = leftTopY + 1;
            int leftInteriorWidth = leftRoomWidth - 1; // Excluding left wall and middle wall
            int leftInteriorHeight = leftRoomHeight - 2; // Excluding ceiling and floor
            protectedHouseAreas.Add(new Rectangle(leftInteriorX, leftInteriorY, leftInteriorWidth, leftInteriorHeight));

            // right room interior area
            int rightInteriorX = rightStartX + 1;
            int rightInteriorY = rightTopY + 1;
            int rightInteriorWidth = rightRoomWidth - 2; // Not including middle wall and right wall
            int rightInteriorHeight = rightRoomHeight - 2; // Excluding ceiling and floor
            protectedHouseAreas.Add(new Rectangle(rightInteriorX, rightInteriorY, rightInteriorWidth, rightInteriorHeight));

            TShock.Log.ConsoleInfo($"[CCTG] {side}House protected areas recorded:");
            TShock.Log.ConsoleInfo($"[CCTG] leftHouse protected area: ({leftInteriorX}, {leftInteriorY}, {leftInteriorWidth}x{leftInteriorHeight})");
            TShock.Log.ConsoleInfo($"[CCTG] rightHouse protected area: ({rightInteriorX}, {rightInteriorY}, {rightInteriorWidth}x{rightInteriorHeight})");

            // Return house spawn point (right room center, above floor)
            int spawnX = rightStartX + rightRoomWidth / 2;
            int spawnY = groundLevel - 3; // 2 blocks above floor

            TShock.Log.ConsoleInfo($"[CCTG] {side}House built, spawn set to: ({spawnX}, {spawnY})");
            return new Point(spawnX, spawnY);
        }

        // Place furniture
        private void PlaceFurniture(int leftStartX, int rightStartX, int groundLevel, int leftWidth, int rightWidth, int direction)
        {
            int floorY = groundLevel - 2; // Above floor

            TShock.Log.ConsoleInfo($"[CCTG] Placing furniture for { (direction < 0 ? "red team" : "blue team") } house...");

            // Red team (left house): furniture in right room (large room) - original positions
            // Blue team (right house): furniture in left room (large room) - same layout as red team

            if (direction < 0) // Red team house - furniture in right room
            {
                // Right room - Wood chair (original position)
                if (WorldGen.PlaceObject(rightStartX + 2, floorY, TileID.Chairs, false, 0))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Red team - Right room: Wood chair placed (facing right) at ({rightStartX + 2}, {floorY})");
                }

                // Right room - Anvil (original position)
                if (WorldGen.PlaceObject(rightStartX + 3, floorY, TileID.Anvils, false, 0))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Red team - Right room: Anvil placed at ({rightStartX + 3}, {floorY})");
                }

                // Right room - Wood platform (original position, 2 blocks)
                int platformY = floorY - 1;
                PlacePlatform(rightStartX + 3, platformY, TileID.Platforms, 0);
                PlacePlatform(rightStartX + 4, platformY, TileID.Platforms, 0);
                TShock.Log.ConsoleInfo($"[CCTG] Red team - Right room: Wood platform placed at ({rightStartX + 3}, {platformY})");

                // Right room - Work bench (original position)
                if (WorldGen.PlaceObject(rightStartX + 3, platformY - 1, TileID.WorkBenches, false, 0))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Red team - Right room: Work bench placed at ({rightStartX + 3}, {platformY - 1})");
                }

                // Right room - Furnace (original position)
                if (WorldGen.PlaceObject(rightStartX + 6, floorY, TileID.Furnaces, false, 0))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Red team - Right room: Furnace placed at ({rightStartX + 6}, {floorY})");
                }
            }
            else // Blue team house - furniture in left room (same layout as red team)
            {
                // Left room - Wood chair (same position as red team's right room)
                if (WorldGen.PlaceObject(leftStartX + 2, floorY, TileID.Chairs, false, 0))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Blue team - Left room: Wood chair placed (facing right) at ({leftStartX + 2}, {floorY})");
                }

                // Left room - Anvil (same position as red team's right room)
                if (WorldGen.PlaceObject(leftStartX + 3, floorY, TileID.Anvils, false, 0))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Blue team - Left room: Anvil placed at ({leftStartX + 3}, {floorY})");
                }

                // Left room - Wood platform (same position as red team's right room, 2 blocks)
                int platformY = floorY - 1;
                PlacePlatform(leftStartX + 3, platformY, TileID.Platforms, 0);
                PlacePlatform(leftStartX + 4, platformY, TileID.Platforms, 0);
                TShock.Log.ConsoleInfo($"[CCTG] Blue team - Left room: Wood platform placed at ({leftStartX + 3}, {platformY})");

                // Left room - Work bench (same position as red team's right room)
                if (WorldGen.PlaceObject(leftStartX + 3, platformY - 1, TileID.WorkBenches, false, 0))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Blue team - Left room: Work bench placed at ({leftStartX + 3}, {platformY - 1})");
                }

                // Left room - Furnace (same position as red team's right room)
                if (WorldGen.PlaceObject(leftStartX + 6, floorY, TileID.Furnaces, false, 0))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Blue team - Left room: Furnace placed at ({leftStartX + 6}, {floorY})");
                }
            }

            // Refresh furniture area
            NetMessage.SendTileSquare(-1, leftStartX, floorY - 2, 15);
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
            TShock.Log.ConsoleInfo($"[CCTG] Finding suitable height for house at X={startX}, starting Y={startY}");
            // Search downward first
            for (int offsetY = 0; offsetY <= searchRange; offsetY++)
            {
                int testGroundLevel = startY + offsetY;

                // Check if this position is suitable ground
                if (IsValidGroundSurface(startX, testGroundLevel, width, liquidCheckHeight, height))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Below spawn at {offsetY} blocks found suitable ground level: {testGroundLevel}");
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
                    TShock.Log.ConsoleInfo($"[CCTG] Above spawn at {offsetY} blocks found suitable ground level: {testGroundLevel}");
                    return testGroundLevel;
                }
            }

            // If no suitable position found, return -1 for failure
            TShock.Log.ConsoleInfo($"[CCTG] No suitable position found near surface");
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
                        TShock.Log.ConsoleInfo($"[CCTG] Height {groundY} invalid: Obstruction found at ({x}, {y}) above house area");
                        return false;
                    }
                }
            }

            TShock.Log.ConsoleInfo($"[CCTG] Height {groundY} valid: Solid ground with clear space above");
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
