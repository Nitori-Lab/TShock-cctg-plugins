using System;
using System.Collections.Generic;
using Terraria;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// 越界检测管理器
    /// </summary>
    public class BoundaryChecker
    {
        // 游戏开始时间
        private DateTime gameStartTime = DateTime.MinValue;

        // 越界检测持续时间（18分钟）
        private const double BOUNDARY_CHECK_DURATION = 18 * 60;

        // 玩家越界状态
        private Dictionary<int, BoundaryViolationState> playerBoundaryStates = new Dictionary<int, BoundaryViolationState>();

        // 游戏是否已开始
        private bool gameStarted = false;

        /// <summary>
        /// 开始越界检测
        /// </summary>
        public void StartBoundaryCheck()
        {
            gameStarted = true;
            gameStartTime = DateTime.Now;
            playerBoundaryStates.Clear();
            TShock.Log.ConsoleInfo("[CCTG] 游戏已开始！越界检测已启动（18分钟）");
        }

        /// <summary>
        /// 停止越界检测
        /// </summary>
        public void StopBoundaryCheck()
        {
            gameStarted = false;
            playerBoundaryStates.Clear();
        }

        /// <summary>
        /// 清空所有玩家的越界状态
        /// </summary>
        public void ClearBoundaryStates()
        {
            playerBoundaryStates.Clear();
        }

        /// <summary>
        /// 越界检测和惩罚
        /// </summary>
        public void CheckBoundaryViolation(TSPlayer player)
        {
            if (!gameStarted || gameStartTime == DateTime.MinValue)
            {
                return;
            }

            // 检查是否在18分钟内
            double timeSinceStart = (DateTime.Now - gameStartTime).TotalSeconds;
            if (timeSinceStart > BOUNDARY_CHECK_DURATION)
                return;

            // 获取玩家队伍
            int playerTeam = player.TPlayer.team;
            if (playerTeam != 1 && playerTeam != 3)
                return;

            // 获取出生点X坐标
            int spawnX = Main.spawnTileX;
            int playerTileX = (int)(player.TPlayer.position.X / 16);

            // 检查是否越界
            bool isOutOfBounds = false;
            if (playerTeam == 1) // 红队：从左侧，不能越过出生点
            {
                isOutOfBounds = playerTileX >= spawnX;
            }
            else if (playerTeam == 3) // 蓝队：从右侧，不能越过出生点
            {
                isOutOfBounds = playerTileX <= spawnX;
            }

            // 初始化玩家越界状态
            if (!playerBoundaryStates.ContainsKey(player.Index))
            {
                playerBoundaryStates[player.Index] = new BoundaryViolationState();
            }

            var state = playerBoundaryStates[player.Index];

            // 处理越界状态变化
            if (isOutOfBounds)
            {
                // 刚刚越界
                if (!state.IsOutOfBounds)
                {
                    // 检查是否在5秒返回窗口内
                    if (state.LastReturnTime != DateTime.MinValue)
                    {
                        double timeSinceReturn = (DateTime.Now - state.LastReturnTime).TotalSeconds;
                        if (timeSinceReturn <= 5.0)
                        {
                            // 5秒内再次越界，计时继续
                            state.IsOutOfBounds = true;
                            state.ViolationStartTime = DateTime.Now;
                            TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 再次越界，计时继续（累计 {state.AccumulatedTime:F1}s）");
                            // 不要return，继续处理警告和伤害
                        }
                        else
                        {
                            // 超过5秒后再次越界，重置状态
                            state.IsOutOfBounds = true;
                            state.ViolationStartTime = DateTime.Now;
                            state.FirstViolationTime = DateTime.Now;
                            state.AccumulatedTime = 0;
                            state.WarningShown = false;
                            state.WarningShownTime = DateTime.MinValue;
                            state.FirstDamageApplied = false;
                            TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 越界（队伍={playerTeam}，位置={playerTileX}，出生点={spawnX}）");
                        }
                    }
                    else
                    {
                        // 首次越界
                        state.IsOutOfBounds = true;
                        state.ViolationStartTime = DateTime.Now;
                        state.FirstViolationTime = DateTime.Now;
                        state.AccumulatedTime = 0;
                        state.WarningShown = false;
                        state.WarningShownTime = DateTime.MinValue;
                        state.FirstDamageApplied = false;
                        TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 越界（队伍={playerTeam}，位置={playerTileX}，出生点={spawnX}）");
                    }
                }

                // 计算累计时间（首次越界和持续越界都执行）
                double currentViolationTime = (DateTime.Now - state.ViolationStartTime).TotalSeconds;
                double totalTime = state.AccumulatedTime + currentViolationTime;

                TShock.Log.ConsoleInfo($"[CCTG调试] 玩家 {player.Name} 越界中: 当前违规时间={currentViolationTime:F2}s, 累计={state.AccumulatedTime:F2}s, 总计={totalTime:F2}s");

                // 0.6秒内不提醒
                if (totalTime <= 0.6)
                {
                    TShock.Log.ConsoleInfo($"[CCTG调试] 玩家 {player.Name} 越界时间 {totalTime:F2}s <= 0.6s，暂不警告");
                    return;
                }

                // 0.6s后：显示警告
                if (totalTime > 0.6)
                {
                    if (!state.WarningShown)
                    {
                        player.SendErrorMessage("你越界了！");
                        state.WarningShown = true;
                        state.WarningShownTime = DateTime.Now;
                        TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 越界警告（{totalTime:F1}s）");
                    }
                    else
                    {
                        TShock.Log.ConsoleInfo($"[CCTG调试] 警告已显示，等待伤害时机");
                    }
                }

                // 警告显示后的时间
                if (state.WarningShown && state.WarningShownTime != DateTime.MinValue)
                {
                    double timeSinceWarning = (DateTime.Now - state.WarningShownTime).TotalSeconds;

                    TShock.Log.ConsoleInfo($"[CCTG调试] 警告已显示 {timeSinceWarning:F2}s, FirstDamageApplied={state.FirstDamageApplied}");

                    // 警告显示后1s：扣除首次10hp
                    if (timeSinceWarning >= 1.0 && !state.FirstDamageApplied)
                    {
                        int damage = 10;
                        player.DamagePlayer(damage);

                        state.FirstDamageApplied = true;
                        state.LastDamageTime = DateTime.Now;
                        TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 警告后1s，扣除{damage}hp");
                        return;
                    }

                    // 警告显示后2s及之后：每秒扣除递增伤害
                    if (timeSinceWarning >= 2.0)
                    {
                        double timeSinceLastDamage = (DateTime.Now - state.LastDamageTime).TotalSeconds;
                        if (timeSinceLastDamage >= 1.0)
                        {
                            // 计算伤害：10 * (1.5 ^ (从警告后开始计算的秒数 - 1))，最大200
                            int secondsSinceWarning = (int)Math.Floor(timeSinceWarning);
                            int damage = (int)(10 * Math.Pow(1.5, secondsSinceWarning - 1));

                            // 限制最大伤害为200
                            if (damage > 200)
                                damage = 200;

                            player.DamagePlayer(damage);

                            state.LastDamageTime = DateTime.Now;
                            TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 警告后{timeSinceWarning:F1}s，扣除{damage}hp");
                        }
                    }
                }
            }
            else
            {
                // 玩家返回边界内
                if (state.IsOutOfBounds)
                {
                    // 记录本次越界的累计时间
                    double thisViolationTime = (DateTime.Now - state.ViolationStartTime).TotalSeconds;
                    state.AccumulatedTime += thisViolationTime;
                    state.IsOutOfBounds = false;
                    state.LastReturnTime = DateTime.Now;

                    TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 返回边界内（本次越界 {thisViolationTime:F1}s，累计 {state.AccumulatedTime:F1}s）");
                }
                else
                {
                    // 检查是否超过5秒，需要重置
                    if (state.LastReturnTime != DateTime.MinValue)
                    {
                        double timeSinceReturn = (DateTime.Now - state.LastReturnTime).TotalSeconds;
                        if (timeSinceReturn > 5.0)
                        {
                            // 重置状态
                            state.AccumulatedTime = 0;
                            state.FirstViolationTime = DateTime.MinValue;
                            state.LastReturnTime = DateTime.MinValue;
                            state.WarningShown = false;
                            state.WarningShownTime = DateTime.MinValue;
                            state.FirstDamageApplied = false;
                            TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 越界计时已重置");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 获取调试信息
        /// </summary>
        public string GetDebugInfo(TSPlayer player)
        {
            if (!gameStarted || gameStartTime == DateTime.MinValue)
            {
                return "游戏未开始";
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

            string info = $"游戏时间: {timeSinceStart:F1}秒\n";
            info += $"越界检测持续时间: {BOUNDARY_CHECK_DURATION}秒（{BOUNDARY_CHECK_DURATION / 60}分钟）\n";
            info += $"越界检测是否有效: {timeSinceStart <= BOUNDARY_CHECK_DURATION}\n";
            info += $"玩家队伍: {playerTeam}\n";
            info += $"玩家位置: {playerTileX}\n";
            info += $"出生点: {spawnX}\n";
            info += $"越界检测: {isOut}\n";

            if (playerBoundaryStates.ContainsKey(player.Index))
            {
                var state = playerBoundaryStates[player.Index];
                info += $"\n当前越界状态: {state.IsOutOfBounds}\n";
                info += $"累计越界时间: {state.AccumulatedTime:F2}秒\n";
                info += $"警告已显示: {state.WarningShown}\n";
                info += $"首次伤害已扣除: {state.FirstDamageApplied}\n";
            }

            return info;
        }
    }
}
