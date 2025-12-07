using System;
using System.Collections.Generic;
using Terraria;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// Boundary check manager
    /// </summary>
    public class BoundaryChecker
    {
        // Game start time
        private DateTime gameStartTime = DateTime.MinValue;

        // Boundary check duration (18 minutes)
        private const double BOUNDARY_CHECK_DURATION = 18 * 60;

        // Player boundary violation states
        private Dictionary<int, BoundaryViolationState> playerBoundaryStates = new Dictionary<int, BoundaryViolationState>();

        // Game started flag
        private bool gameStarted = false;

        /// <summary>
        /// Start boundary check
        /// </summary>
        public void StartBoundaryCheck()
        {
            gameStarted = true;
            gameStartTime = DateTime.Now;
            playerBoundaryStates.Clear();
            TShock.Log.ConsoleInfo("[CCTG] Game started! Boundary check active (18 minutes)");
        }

        /// <summary>
        /// Stop boundary check
        /// </summary>
        public void StopBoundaryCheck()
        {
            gameStarted = false;
            playerBoundaryStates.Clear();
        }

        /// <summary>
        /// Clear all player boundary states
        /// </summary>
        public void ClearBoundaryStates()
        {
            playerBoundaryStates.Clear();
        }

        /// <summary>
        /// Boundary check and punishment
        /// </summary>
        public void CheckBoundaryViolation(TSPlayer player)
        {
            if (!gameStarted || gameStartTime == DateTime.MinValue)
            {
                return;
            }

            // Check if within 18 minutes
            double timeSinceStart = (DateTime.Now - gameStartTime).TotalSeconds;
            if (timeSinceStart > BOUNDARY_CHECK_DURATION)
                return;

            // Get player team
            int playerTeam = player.TPlayer.team;
            if (playerTeam != 1 && playerTeam != 3)
                return;

            // Get spawn X coordinate
            int spawnX = Main.spawnTileX;
            int playerTileX = (int)(player.TPlayer.position.X / 16);

            // Check if out of bounds
            bool isOutOfBounds = false;
            if (playerTeam == 1) // Red team: from left, cannot cross spawn
            {
                isOutOfBounds = playerTileX >= spawnX;
            }
            else if (playerTeam == 3) // Blue team: from right, cannot cross spawn
            {
                isOutOfBounds = playerTileX <= spawnX;
            }

            // Initialize player boundary state
            if (!playerBoundaryStates.ContainsKey(player.Index))
            {
                playerBoundaryStates[player.Index] = new BoundaryViolationState();
            }

            var state = playerBoundaryStates[player.Index];

            // Handle boundary state changes
            if (isOutOfBounds)
            {
                // Just went out of bounds
                if (!state.IsOutOfBounds)
                {
                    // Check if within 5-second return window
                    if (state.LastReturnTime != DateTime.MinValue)
                    {
                        double timeSinceReturn = (DateTime.Now - state.LastReturnTime).TotalSeconds;
                        if (timeSinceReturn <= 5.0)
                        {
                            // Out of bounds again within 5 seconds, timer continues
                            state.IsOutOfBounds = true;
                            state.ViolationStartTime = DateTime.Now;
                            // Don't return, continue handling warning and damage
                        }
                        else
                        {
                            // Out of bounds again after 5 seconds, reset state
                            state.IsOutOfBounds = true;
                            state.ViolationStartTime = DateTime.Now;
                            state.FirstViolationTime = DateTime.Now;
                            state.AccumulatedTime = 0;
                            state.WarningShown = false;
                            state.WarningShownTime = DateTime.MinValue;
                            state.FirstDamageApplied = false;
                        }
                    }
                    else
                    {
                        // First violation
                        state.IsOutOfBounds = true;
                        state.ViolationStartTime = DateTime.Now;
                        state.FirstViolationTime = DateTime.Now;
                        state.AccumulatedTime = 0;
                        state.WarningShown = false;
                        state.WarningShownTime = DateTime.MinValue;
                        state.FirstDamageApplied = false;
                    }
                }

                // Calculate accumulated time (for both first and ongoing violations)
                double currentViolationTime = (DateTime.Now - state.ViolationStartTime).TotalSeconds;
                double totalTime = state.AccumulatedTime + currentViolationTime;

                // No warning within 0.6 seconds
                if (totalTime <= 0.6)
                {
                    return;
                }

                // After 0.6s: show warning
                if (totalTime > 0.6)
                {
                    if (!state.WarningShown)
                    {
                        player.SendErrorMessage("You are out of bounds!");
                        state.WarningShown = true;
                        state.WarningShownTime = DateTime.Now;
                    }
                }

                // Time since warning shown
                if (state.WarningShown && state.WarningShownTime != DateTime.MinValue)
                {
                    double timeSinceWarning = (DateTime.Now - state.WarningShownTime).TotalSeconds;

                    // 1s after warning: apply first 10hp damage
                    if (timeSinceWarning >= 1.0 && !state.FirstDamageApplied)
                    {
                        int damage = 10;
                        player.DamagePlayer(damage);

                        state.FirstDamageApplied = true;
                        state.LastDamageTime = DateTime.Now;
                        return;
                    }

                    // 2s+ after warning: apply escalating damage per second
                    if (timeSinceWarning >= 2.0)
                    {
                        double timeSinceLastDamage = (DateTime.Now - state.LastDamageTime).TotalSeconds;
                        if (timeSinceLastDamage >= 1.0)
                        {
                            // Calculate damage: 10 * (1.5 ^ (seconds since warning - 1)), max 200
                            int secondsSinceWarning = (int)Math.Floor(timeSinceWarning);
                            int damage = (int)(10 * Math.Pow(1.5, secondsSinceWarning - 1));

                            // Cap max damage at 200
                            if (damage > 200)
                                damage = 200;

                            player.DamagePlayer(damage);

                            state.LastDamageTime = DateTime.Now;
                        }
                    }
                }
            }
            else
            {
                // Player returned to bounds
                if (state.IsOutOfBounds)
                {
                    // Record accumulated time for this violation
                    double thisViolationTime = (DateTime.Now - state.ViolationStartTime).TotalSeconds;
                    state.AccumulatedTime += thisViolationTime;
                    state.IsOutOfBounds = false;
                    state.LastReturnTime = DateTime.Now;
                }
                else
                {
                    // Check if over 5 seconds, need to reset
                    if (state.LastReturnTime != DateTime.MinValue)
                    {
                        double timeSinceReturn = (DateTime.Now - state.LastReturnTime).TotalSeconds;
                        if (timeSinceReturn > 5.0)
                        {
                            // Reset state
                            state.AccumulatedTime = 0;
                            state.FirstViolationTime = DateTime.MinValue;
                            state.LastReturnTime = DateTime.MinValue;
                            state.WarningShown = false;
                            state.WarningShownTime = DateTime.MinValue;
                            state.FirstDamageApplied = false;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get debug info
        /// </summary>
        public string GetDebugInfo(TSPlayer player)
        {
            if (!gameStarted || gameStartTime == DateTime.MinValue)
            {
                return "Game not started";
            }

            double timeSinceStart = (DateTime.Now - gameStartTime).TotalSeconds;
            int playerTeam = player.TPlayer.team;
            int spawnX = Main.spawnTileX;
            int playerTileX = (int)(player.TPlayer.position.X / 16);

            bool isOut = false;
            if (playerTeam == 1)
            {
                isOut = playerTileX >= spawnX;
            }
            else if (playerTeam == 3)
            {
                isOut = playerTileX <= spawnX;
            }

            string info = $"Game time: {timeSinceStart:F1} seconds\n";
            info += $"Boundary check duration: {BOUNDARY_CHECK_DURATION} seconds ({BOUNDARY_CHECK_DURATION / 60} minutes)\n";
            info += $"Boundary check active: {timeSinceStart <= BOUNDARY_CHECK_DURATION}\n";
            info += $"Player team: {playerTeam}\n";
            info += $"Player position: {playerTileX}\n";
            info += $"Spawn point: {spawnX}\n";
            info += $"Boundary check: {isOut}\n";

            if (playerBoundaryStates.ContainsKey(player.Index))
            {
                var state = playerBoundaryStates[player.Index];
                info += $"\nCurrent out of bounds: {state.IsOutOfBounds}\n";
                info += $"Accumulated violation time: {state.AccumulatedTime:F2} seconds\n";
                info += $"Warning shown: {state.WarningShown}\n";
                info += $"First damage applied: {state.FirstDamageApplied}\n";
            }

            return info;
        }
    }
}
