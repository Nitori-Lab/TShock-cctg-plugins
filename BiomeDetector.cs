using System;
using Terraria;
using TShockAPI;

namespace cctgPlugin
{
    public class BiomeDetector
    {
        // Dungeon side: -1 = left, 1 = right, 0 = unknown
        private int dungeonSide = 0;

        public BiomeDetector()
        {
            DetectDungeonSide();
        }

        // Detect which side the dungeon is on
        private void DetectDungeonSide()
        {
            // Get dungeon X position from world data
            int dungeonX = Main.dungeonX;
            int worldCenterX = Main.maxTilesX / 2;

            if (dungeonX < worldCenterX)
            {
                dungeonSide = -1; // Left side
                TShock.Log.ConsoleInfo($"[CCTG] Dungeon detected on LEFT side (X: {dungeonX})");
            }
            else
            {
                dungeonSide = 1; // Right side
                TShock.Log.ConsoleInfo($"[CCTG] Dungeon detected on RIGHT side (X: {dungeonX})");
            }
        }

        // Get biome information message
        public string GetBiomeInfoMessage()
        {
            string leftBiome = "";
            string rightBiome = "";

            if (dungeonSide == -1)
            {
                // Dungeon on left = Snow on left, Jungle on right
                leftBiome = "Snow Biome";
                rightBiome = "Jungle Biome";
            }
            else if (dungeonSide == 1)
            {
                // Dungeon on right = Snow on right, Jungle on left
                leftBiome = "Jungle Biome";
                rightBiome = "Snow Biome";
            }
            else
            {
                leftBiome = "Unknown";
                rightBiome = "Unknown";
            }

            return $"Left: {leftBiome}, Right: {rightBiome}";
        }

        // Check if dungeon is on left side
        public bool IsDungeonOnLeft()
        {
            return dungeonSide == -1;
        }

        // Check if dungeon is on right side
        public bool IsDungeonOnRight()
        {
            return dungeonSide == 1;
        }

        // Get detailed biome information
        public string GetDetailedBiomeInfo()
        {
            int worldCenter = Main.maxTilesX / 2;
            int dungeonX = Main.dungeonX;

            string dungeonPosition = dungeonSide == -1 ? "LEFT" : "RIGHT";
            string snowSide = dungeonSide == -1 ? "LEFT" : "RIGHT";
            string jungleSide = dungeonSide == -1 ? "RIGHT" : "LEFT";

            return $"=== Biome Information ===\n" +
                   $"Dungeon: {dungeonPosition} (X: {dungeonX})\n" +
                   $"Snow Biome: {snowSide}\n" +
                   $"Jungle Biome: {jungleSide}\n" +
                   $"World Center: {worldCenter}";
        }
    }
}
