using System;
using Microsoft.Xna.Framework;

namespace cctgPlugin
{
    /// <summary>
    /// Tracks the player's team state
    /// </summary>
    public class PlayerTeamState
    {
        public int LastTeam = 0;
        public DateTime LastTeamChangeTime = DateTime.MinValue;
    }

    /// <summary>
    /// Tracks the player's recall teleport state
    /// </summary>
    public class RecallTeleportState
    {
        public bool WaitingForTeleport = false;
        public bool WaitingToTeleportToTeamHouse = false;
        public DateTime TeleportDetectedTime;
        public Vector2 LastKnownPosition;
        public DateTime LastItemUseTime = DateTime.MinValue;
    }

    /// <summary>
    /// Tracks the player's boundary violation state
    /// </summary>
    public class BoundaryViolationState
    {
        public bool IsOutOfBounds = false;              // Whether the player is currently out of bounds
        public DateTime FirstViolationTime = DateTime.MinValue;  // Time of first boundary violation
        public DateTime ViolationStartTime = DateTime.MinValue;  // Time when the player went out of bounds
        public DateTime LastReturnTime = DateTime.MinValue;      // Last time the player returned within bounds
        public double AccumulatedTime = 0;              // Accumulated out-of-bounds time
        public bool WarningShown = false;               // Whether a warning has been shown
        public DateTime WarningShownTime = DateTime.MinValue;    // Time when the warning was shown
        public bool FirstDamageApplied = false;         // Whether the first damage has been applied
        public DateTime LastDamageTime = DateTime.MinValue;  // Last time damage was applied
    }
}
