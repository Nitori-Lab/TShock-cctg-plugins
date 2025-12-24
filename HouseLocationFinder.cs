using System;
using Terraria;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// House location finder - responsible for finding suitable positions for house placement
    /// </summary>
    public class HouseLocationFinder
    {
        private Random _random = new Random();

        /// <summary>
        /// Find suitable location for house construction
        /// Returns ground level Y coordinate, or -1 if not found
        /// </summary>
        public int FindLocation(int centerX, int groundY, int totalWidth, int maxHeight, int direction, string side)
        {
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
                        TShock.Log.ConsoleInfo($"[CCTG] At distance from spawn {distanceToSpawn} blocks found suitable position: X={startX}");
                        return groundLevel;
                    }
                }
            }
            else
            {
                return groundLevel;
            }

            // Phase 2: If not found at 100 blocks, search away from spawn (>200 blocks)
            if (groundLevel == -1)
            {
                TShock.Log.ConsoleWarn($"[CCTG] {side} No suitable position found towards spawn, searching outward from spawn (>200 blocks)");

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
                        TShock.Log.ConsoleInfo($"[CCTG] At distance from spawn {distanceToSpawn} blocks found suitable position: X={startX}");
                        return groundLevel;
                    }
                }
            }

            // Phase 3: Forced build mode
            groundLevel = ForcedBuildMode(centerX, groundY, totalWidth, maxHeight, direction, side);

            return groundLevel;
        }

        /// <summary>
        /// Forced build mode - lower requirements to 50% ground contact
        /// </summary>
        private int ForcedBuildMode(int centerX, int groundY, int totalWidth, int maxHeight, int direction, string side)
        {
            TShock.Log.ConsoleWarn($"[CCTG] {side} No suitable position found in search range, using forced build mode (lowered requirement: 50% ground contact)");

            int forceSpawnX = Main.spawnTileX;
            int forceBuildStartX = direction < 0 ? forceSpawnX - 200 : forceSpawnX + 200;

            const int forceBuildSearchRange = 200;

            // Search horizontally
            for (int offset = 0; offset <= forceBuildSearchRange; offset++)
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
                                int contactPercent = (int)((double)solidCount / totalWidth * 100);
                                TShock.Log.ConsoleInfo($"[CCTG] {side} House force-built at X={testX}, Y={y} (ground contact {contactPercent}%, clear above)");
                                return y;
                            }
                        }
                    }
                }
            }

            // Final fallback
            TShock.Log.ConsoleError($"[CCTG] {side} Cannot find suitable position, will force build at X={forceBuildStartX}, Y={groundY} and clear space");
            return groundY;
        }

        /// <summary>
        /// Find suitable height for house placement
        /// Uses stricter criteria
        /// </summary>
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
            return -1;
        }

        /// <summary>
        /// Check if valid ground surface
        /// height: house height (for checking 40 blocks above)
        /// </summary>
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

        /// <summary>
        /// Check if coordinates are within world bounds
        /// </summary>
        private bool IsValidCoord(int x, int y)
        {
            return x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY;
        }

        public int GetRandomOffset()
        {
            return _random.Next(-20, 21);
        }
    }
}
