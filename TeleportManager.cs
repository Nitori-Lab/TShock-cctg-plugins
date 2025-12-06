using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// Teleportation Manager
    /// </summary>
    public class TeleportManager
    {
        // List of recall item IDs
        private static readonly HashSet<int> RecallItems = new HashSet<int>
        {
            ItemID.RecallPotion,
            ItemID.MagicMirror,
            ItemID.IceMirror,
            ItemID.CellPhone,
            ItemID.Shellphone,
            ItemID.PDA,
            ItemID.ShellphoneDummy,
            ItemID.ShellphoneHell,
            ItemID.ShellphoneOcean,
            ItemID.ShellphoneSpawn
        };

        // Status tracking for players' recall teleportation
        private Dictionary<int, RecallTeleportState> playerRecallStates = new Dictionary<int, RecallTeleportState>();

        // Tracking for players' team states
        private Dictionary<int, PlayerTeamState> playerTeamStates = new Dictionary<int, PlayerTeamState>();

        public Dictionary<int, RecallTeleportState> PlayerRecallStates => playerRecallStates;
        public Dictionary<int, PlayerTeamState> PlayerTeamStates => playerTeamStates;

        /// <summary>
        /// Whether the item is a recall item
        /// </summary>
        public bool IsRecallItem(int itemType)
        {
            return RecallItems.Contains(itemType);
        }

        /// <summary>
        /// Teleport the player to their team house based on their team
        /// </summary>
        public void TeleportToTeamHouse(TSPlayer player, Point leftHouseSpawn, Point rightHouseSpawn)
        {
            // Get player's team
            int playerTeam = player.TPlayer.team;
            Point targetSpawn = Point.Zero;
            string destination = "";

            TShock.Log.ConsoleInfo($"[CCTG] Attempt to teleport player {player.Name}, current team: {playerTeam}");
            TShock.Log.ConsoleInfo($"[CCTG] croodinate of left house: ({leftHouseSpawn.X}, {leftHouseSpawn.Y})");
            TShock.Log.ConsoleInfo($"[CCTG] croodinate of right house: ({rightHouseSpawn.X}, {rightHouseSpawn.Y})");

            // Teleport based on team
            if (playerTeam == 1 && leftHouseSpawn.X != -1) // Red Team → Left House
            {
                targetSpawn = leftHouseSpawn;
                destination = "Red Team House";
            }
            else if (playerTeam == 3 && rightHouseSpawn.X != -1) // Blue Team → Right House
            {
                targetSpawn = rightHouseSpawn;
                destination = "Blue Team House";
            }
            else // Not in Red or Blue Team
            {
                TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} is not in Red or Blue team, teleportation aborted.");
                return;
            }

            // Teleport the player
            player.Teleport(targetSpawn.X * 16, targetSpawn.Y * 16);
            player.SendSuccessMessage($"Teleport to {destination}.");

            TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} teleports to {destination} ({targetSpawn.X}, {targetSpawn.Y})");
        }

        /// <summary>
        /// Clear all stored player states
        /// </summary>
        public void ClearAllStates()
        {
            playerRecallStates.Clear();
            playerTeamStates.Clear();
        }
    }
}
