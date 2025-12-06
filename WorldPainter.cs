using System;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// WorldPainter
    /// </summary>
    public class WorldPainter
    {
        /// <summary>
        /// Paint the world around the spawn point
        /// </summary>
        public void PaintWorld()
        {
            int spawnX = Main.spawnTileX; // Get spawn X coordinate
            TShock.Log.ConsoleInfo($"[CCTG] Paint world around spawn point at X={spawnX}");

            int paintedTiles = 0;

            // Red paint: columns at X = 0, -1, -2, -3 relative to spawn
            int[] redColumns = { 0, -1, -2, -3 };
            foreach (int offset in redColumns)
            {
                int worldX = spawnX + offset;
                if (worldX >= 0 && worldX < Main.maxTilesX)
                {
                    paintedTiles += PaintColumn(worldX, PaintID.RedPaint, "Red");
                }
            }

            // Blue paint: columns at X = 1, 2, 3, 4 relative to spawn
            int[] blueColumns = { 1, 2, 3, 4 };
            foreach (int offset in blueColumns)
            {
                int worldX = spawnX + offset;
                if (worldX >= 0 && worldX < Main.maxTilesX)
                {
                    paintedTiles += PaintColumn(worldX, PaintID.BluePaint, "Blue");
                }
            }

            TShock.Log.ConsoleInfo($"[CCTG] World painting completed. Total painted tiles: {paintedTiles}");

            // Notify all players about completion
            TSPlayer.All.SendSuccessMessage($"[CCTG] World painting completed. Total painted tiles: {paintedTiles}");
        }

        /// <summary>
        /// Paint a single column of tiles
        /// </summary>
        private int PaintColumn(int x, byte paintColor, string colorName)
        {
            int count = 0;

            for (int y = 0; y < Main.maxTilesY; y++)
            {
                var tile = Main.tile[x, y];

                // Only paint active tiles
                if (tile != null && tile.active())
                {
                    tile.color(paintColor);
                    count++;

                    // refresh tile frame every 100 tiles to optimize performance
                    if (count % 100 == 0)
                    {
                        WorldGen.SquareTileFrame(x, y, true);
                    }
                }
            }

            // Send tile updates to all players
            // Split into sections to avoid large packet sizes
            const int sectionHeight = 100;
            for (int startY = 0; startY < Main.maxTilesY; startY += sectionHeight)
            {
                int height = Math.Min(sectionHeight, Main.maxTilesY - startY);
                TSPlayer.All.SendTileRect((short)x, (short)startY, 1, (byte)height);
            }

            TShock.Log.ConsoleInfo($"[CCTG] X={x} painted {count} tiles with {colorName} paint.");
            return count;
        }
    }
}
