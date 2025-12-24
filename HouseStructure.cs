using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// House structure builder - responsible for constructing house shapes and furniture
    /// </summary>
    public class HouseStructure
    {
        private const int TotalWidth = 14; // 5 + 10 - 1 = 14
        private const int MaxHeight = 11; // highest height
        private const int SkyClearHeight = 40; // Clear 40 blocks above

        /// <summary>
        /// Build a complete house structure
        /// </summary>
        /// <param name="startX">Starting X coordinate</param>
        /// <param name="groundLevel">Ground level Y coordinate</param>
        /// <param name="direction">Direction: -1 for red team (left), 1 for blue team (right)</param>
        /// <param name="side">Side name for logging</param>
        /// <returns>Spawn point inside the house</returns>
        public Point BuildHouse(int startX, int groundLevel, int direction, string side)
        {
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

            TShock.Log.ConsoleInfo($"[CCTG] {side} House build position: X={startX}, ground level={groundLevel}");

            // Clear entire area (including space outside doors and 40 blocks above)
            ClearArea(startX, groundLevel);

            TShock.Log.ConsoleInfo($"[CCTG] {side} House area cleared (including {SkyClearHeight} blocks above)");

            // Build rooms based on team
            int leftStartX, leftTopY, rightStartX, rightTopY;

            if (direction < 0) // Red team
            {
                BuildRedTeamRooms(startX, groundLevel, leftRoomWidth, leftRoomHeight, rightRoomWidth, rightRoomHeight,
                    out leftStartX, out leftTopY, out rightStartX, out rightTopY);
            }
            else // Blue team
            {
                BuildBlueTeamRooms(startX, groundLevel, leftRoomWidth, leftRoomHeight, rightRoomWidth, rightRoomHeight,
                    out leftStartX, out leftTopY, out rightStartX, out rightTopY);
            }

            // Build middle wall with passage
            BuildMiddleWall(leftTopY, rightTopY, rightStartX, groundLevel, direction);

            // Place doors
            PlaceDoor(leftStartX, groundLevel - 3, TileID.ClosedDoor); // left room left side door
            PlaceDoor(rightStartX + rightRoomWidth - 1, groundLevel - 3, TileID.ClosedDoor); // right room right side door

            // Place platforms and torches
            PlacePlatformsAndTorches(leftStartX, groundLevel, direction);

            // Fill walls
            FillWalls(leftStartX, leftTopY, leftRoomWidth, groundLevel);
            FillWalls(rightStartX, rightTopY, rightRoomWidth, groundLevel);

            // Fill middle wall background (including passage area)
            FillMiddleWall(leftTopY, rightTopY, rightStartX, groundLevel);

            // Place furniture
            PlaceFurniture(leftStartX, rightStartX, groundLevel, leftRoomWidth, rightRoomWidth, direction);

            // Refresh area
            TSPlayer.All.SendTileRect((short)startX, (short)(groundLevel - MaxHeight - SkyClearHeight),
                (byte)(TotalWidth + 4), (byte)(MaxHeight + SkyClearHeight + 2));

            // Return house spawn point (right room center, above floor)
            int spawnX = rightStartX + rightRoomWidth / 2;
            int spawnY = groundLevel - 3; // 2 blocks above floor

            TShock.Log.ConsoleInfo($"[CCTG] {side} House built, spawn set to: ({spawnX}, {spawnY})");
            return new Point(spawnX, spawnY);
        }

        /// <summary>
        /// Get protected areas for this house
        /// </summary>
        public (Rectangle leftRoom, Rectangle rightRoom) GetProtectedAreas(int startX, int groundLevel, int direction)
        {
            int leftRoomWidth, leftRoomHeight, rightRoomWidth, rightRoomHeight;

            if (direction < 0) // Red team
            {
                leftRoomWidth = 5;
                leftRoomHeight = 11;
                rightRoomWidth = 10;
                rightRoomHeight = 7;
            }
            else // Blue team
            {
                leftRoomWidth = 10;
                leftRoomHeight = 7;
                rightRoomWidth = 5;
                rightRoomHeight = 11;
            }

            int leftStartX = startX;
            int leftTopY = groundLevel - leftRoomHeight;
            int rightStartX = leftStartX + leftRoomWidth - 1;
            int rightTopY = groundLevel - rightRoomHeight;

            // Left room interior area
            int leftInteriorX = leftStartX + 1;
            int leftInteriorY = leftTopY + 1;
            int leftInteriorWidth = leftRoomWidth - 1; // Excluding left wall and middle wall
            int leftInteriorHeight = leftRoomHeight - 2; // Excluding ceiling and floor

            // Right room interior area
            int rightInteriorX = rightStartX + 1;
            int rightInteriorY = rightTopY + 1;
            int rightInteriorWidth = rightRoomWidth - 2; // Not including middle wall and right wall
            int rightInteriorHeight = rightRoomHeight - 2; // Excluding ceiling and floor

            return (
                new Rectangle(leftInteriorX, leftInteriorY, leftInteriorWidth, leftInteriorHeight),
                new Rectangle(rightInteriorX, rightInteriorY, rightInteriorWidth, rightInteriorHeight)
            );
        }

        /// <summary>
        /// Clear the area where house will be built
        /// </summary>
        private void ClearArea(int startX, int groundLevel)
        {
            // 2 extra blocks on left of left door, 2 extra on right of right door
            int clearStartX = startX - 2;
            int clearEndX = startX + TotalWidth + 2;

            for (int x = clearStartX; x < clearEndX; x++)
            {
                // Clear from (groundLevel - MaxHeight - SkyClearHeight) to groundLevel
                for (int y = groundLevel - MaxHeight - SkyClearHeight; y <= groundLevel; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].ClearEverything();
                    }
                }
            }
        }

        /// <summary>
        /// Build red team rooms (small room on left, large room on right)
        /// </summary>
        private void BuildRedTeamRooms(int startX, int groundLevel,
            int leftRoomWidth, int leftRoomHeight, int rightRoomWidth, int rightRoomHeight,
            out int leftStartX, out int leftTopY, out int rightStartX, out int rightTopY)
        {
            // === Build left room (5x11) ===
            leftStartX = startX;
            leftTopY = groundLevel - leftRoomHeight;

            // Left room foundation layer (at ground level, 1 extra block each side)
            for (int x = leftStartX - 1; x <= leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab);
            }

            // left room floor (interior floor)
            for (int x = leftStartX; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab);
            }

            // left room ceiling - include full width to cover middle wall position
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
            rightStartX = leftStartX + leftRoomWidth - 1;
            rightTopY = groundLevel - rightRoomHeight;

            // right room foundation layer (at ground level, 1 extra block on right side)
            for (int x = rightStartX; x <= rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab);
            }

            // right room floor
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab);
            }

            // right room ceiling - exclude middle wall position to avoid overlap with left room
            for (int x = rightStartX + 1; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, rightTopY, TileID.StoneSlab);
            }

            // right room right wall
            for (int y = rightTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(rightStartX + rightRoomWidth - 1, y, TileID.StoneSlab);
            }
        }

        /// <summary>
        /// Build blue team rooms (large room on left, small room on right)
        /// </summary>
        private void BuildBlueTeamRooms(int startX, int groundLevel,
            int leftRoomWidth, int leftRoomHeight, int rightRoomWidth, int rightRoomHeight,
            out int leftStartX, out int leftTopY, out int rightStartX, out int rightTopY)
        {
            TShock.Log.ConsoleInfo($"[CCTG] Blue team: Building left room (10x7) first");

            // === Build left room (10x7) first ===
            leftStartX = startX;
            leftTopY = groundLevel - leftRoomHeight;

            // Left room foundation layer (at ground level, 1 extra block each side)
            for (int x = leftStartX - 1; x <= leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab);
            }

            // left room floor (interior floor)
            for (int x = leftStartX; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab);
            }

            // left room ceiling - exclude middle wall position to avoid overlap with right room
            for (int x = leftStartX; x < leftStartX + leftRoomWidth - 1; x++)
            {
                PlaceTile(x, leftTopY, TileID.StoneSlab);
            }

            // left room left wall
            for (int y = leftTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(leftStartX, y, TileID.StoneSlab);
            }

            TShock.Log.ConsoleInfo($"[CCTG] Blue team: Building right room (5x11) second");

            // === Build right room (5x11) second ===
            rightStartX = leftStartX + leftRoomWidth - 1;
            rightTopY = groundLevel - rightRoomHeight;

            // right room foundation layer (at ground level, 1 extra block on right side)
            for (int x = rightStartX; x <= rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab);
            }

            // right room floor
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab);
            }

            // right room ceiling - include full width to cover middle wall position
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, rightTopY, TileID.StoneSlab);
            }

            // right room right wall
            for (int y = rightTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(rightStartX + rightRoomWidth - 1, y, TileID.StoneSlab);
            }
        }

        /// <summary>
        /// Build middle wall with passage
        /// </summary>
        private void BuildMiddleWall(int leftTopY, int rightTopY, int middleX, int groundLevel, int direction)
        {
            int passageHeight = 4; // passage height (bottom 4 blocks)

            // Get the higher ceiling (smaller Y value, higher position)
            int higherCeilingY = Math.Min(leftTopY, rightTopY);

            // Get the lower ceiling (larger Y value, lower position)
            int lowerCeilingY = Math.Max(leftTopY, rightTopY);

            // Build middle wall from the higher ceiling to ground
            for (int y = higherCeilingY + 1; y < groundLevel - 1; y++)
            {
                // Check if we're in the passage area (bottom 4 blocks from lower ceiling)
                if (y >= lowerCeilingY + 1 && y >= groundLevel - passageHeight)
                {
                    // This is the passage area - leave it empty
                    continue;
                }
                else
                {
                    // Build wall
                    PlaceTile(middleX, y, TileID.StoneSlab);
                }
            }

            TShock.Log.ConsoleInfo($"[CCTG] Middle wall built from Y={higherCeilingY + 1} to Y={groundLevel - 1}, passage area: Y>={groundLevel - passageHeight}");
        }

        /// <summary>
        /// Place platforms and torches
        /// </summary>
        private void PlacePlatformsAndTorches(int leftStartX, int groundLevel, int direction)
        {
            int leftDoorTopX = leftStartX;
            int leftDoorTopY = groundLevel - 4;

            if (direction < 0) // Red team house - platforms above left door
            {
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
        }

        /// <summary>
        /// Fill wood walls
        /// </summary>
        private void FillWalls(int startX, int topY, int roomWidth, int groundLevel)
        {
            for (int x = startX + 1; x < startX + roomWidth - 1; x++)
            {
                for (int y = topY + 1; y < groundLevel - 1; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].wall = WallID.Wood;
                    }
                }
            }
        }

        /// <summary>
        /// Fill middle wall background (only in passage area where there are no tiles)
        /// </summary>
        private void FillMiddleWall(int leftTopY, int rightTopY, int middleX, int groundLevel)
        {
            // Get the higher ceiling (smaller Y value, higher position)
            int higherCeilingY = Math.Min(leftTopY, rightTopY);

            // Fill background wall from higher ceiling to ground, but only where there's no tile
            for (int y = higherCeilingY + 1; y < groundLevel - 1; y++)
            {
                if (IsValidCoord(middleX, y))
                {
                    var tile = Main.tile[middleX, y];
                    // Only place wall if there's no active tile (i.e., in the passage area)
                    if (tile != null && !tile.active())
                    {
                        tile.wall = WallID.Wood;
                    }
                }
            }

            TShock.Log.ConsoleInfo($"[CCTG] Middle wall background filled (only in passage area without tiles)");
        }

        /// <summary>
        /// Place furniture
        /// </summary>
        private void PlaceFurniture(int leftStartX, int rightStartX, int groundLevel, int leftWidth, int rightWidth, int direction)
        {
            int floorY = groundLevel - 2; // Above floor

            TShock.Log.ConsoleInfo($"[CCTG] Placing furniture for {(direction < 0 ? "red team" : "blue team")} house...");

            // Red team (left house): furniture in right room (large room)
            // Blue team (right house): furniture in left room (large room)

            if (direction < 0) // Red team house - furniture in right room
            {
                PlaceFurnitureSet(rightStartX, floorY, "Red team - Right room");
            }
            else // Blue team house - furniture in left room
            {
                PlaceFurnitureSet(leftStartX, floorY, "Blue team - Left room");
            }

            // Refresh furniture area
            NetMessage.SendTileSquare(-1, leftStartX, floorY - 2, 15);
        }

        /// <summary>
        /// Place a set of furniture (chair, anvil, platform, workbench, furnace)
        /// </summary>
        private void PlaceFurnitureSet(int startX, int floorY, string roomName)
        {
            // Wood chair
            if (WorldGen.PlaceObject(startX + 2, floorY, TileID.Chairs, false, 0))
            {
                TShock.Log.ConsoleInfo($"[CCTG] {roomName}: Wood chair placed at ({startX + 2}, {floorY})");
            }

            // Anvil
            if (WorldGen.PlaceObject(startX + 3, floorY, TileID.Anvils, false, 0))
            {
                TShock.Log.ConsoleInfo($"[CCTG] {roomName}: Anvil placed at ({startX + 3}, {floorY})");
            }

            // Wood platform (2 blocks)
            int platformY = floorY - 1;
            PlacePlatform(startX + 3, platformY, TileID.Platforms, 0);
            PlacePlatform(startX + 4, platformY, TileID.Platforms, 0);
            TShock.Log.ConsoleInfo($"[CCTG] {roomName}: Wood platform placed at ({startX + 3}, {platformY})");

            // Work bench
            if (WorldGen.PlaceObject(startX + 3, platformY - 1, TileID.WorkBenches, false, 0))
            {
                TShock.Log.ConsoleInfo($"[CCTG] {roomName}: Work bench placed at ({startX + 3}, {platformY - 1})");
            }

            // Furnace
            if (WorldGen.PlaceObject(startX + 6, floorY, TileID.Furnaces, false, 0))
            {
                TShock.Log.ConsoleInfo($"[CCTG] {roomName}: Furnace placed at ({startX + 6}, {floorY})");
            }
        }

        /// <summary>
        /// Place platforms
        /// </summary>
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

        /// <summary>
        /// Place tile
        /// </summary>
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

        /// <summary>
        /// Place door
        /// </summary>
        private void PlaceDoor(int x, int y, ushort type)
        {
            if (IsValidCoord(x, y))
            {
                WorldGen.PlaceDoor(x, y, type);
                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        /// <summary>
        /// Place torches
        /// </summary>
        private void PlaceTorch(int x, int y)
        {
            if (IsValidCoord(x, y))
            {
                WorldGen.PlaceTile(x, y, TileID.Torches, false, false, -1, 0);
                WorldGen.SquareTileFrame(x, y, true);
            }
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
