using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// 传送管理器
    /// </summary>
    public class TeleportManager
    {
        // 回城物品列表
        private static readonly HashSet<int> RecallItems = new HashSet<int>
        {
            ItemID.RecallPotion,        // 回城药水
            ItemID.MagicMirror,          // 魔镜
            ItemID.IceMirror,            // 冰雪镜
            ItemID.CellPhone,            // 手机
            ItemID.Shellphone,           // 贝壳手机
            ItemID.PDA,                  // PDA
            ItemID.ShellphoneDummy,      // 贝壳手机（假）
            ItemID.ShellphoneHell,       // 地狱贝壳手机
            ItemID.ShellphoneOcean,      // 海洋贝壳手机
            ItemID.ShellphoneSpawn       // 出生点贝壳手机
        };

        // 回城传送状态
        private Dictionary<int, RecallTeleportState> playerRecallStates = new Dictionary<int, RecallTeleportState>();

        // 玩家队伍状态跟踪
        private Dictionary<int, PlayerTeamState> playerTeamStates = new Dictionary<int, PlayerTeamState>();

        public Dictionary<int, RecallTeleportState> PlayerRecallStates => playerRecallStates;
        public Dictionary<int, PlayerTeamState> PlayerTeamStates => playerTeamStates;

        /// <summary>
        /// 检查是否为回城物品
        /// </summary>
        public bool IsRecallItem(int itemType)
        {
            return RecallItems.Contains(itemType);
        }

        /// <summary>
        /// 传送到队伍小屋
        /// </summary>
        public void TeleportToTeamHouse(TSPlayer player, Point leftHouseSpawn, Point rightHouseSpawn)
        {
            // 获取玩家队伍
            int playerTeam = player.TPlayer.team;
            Point targetSpawn = Point.Zero;
            string destination = "";

            TShock.Log.ConsoleInfo($"[CCTG] 尝试传送玩家 {player.Name}，当前队伍: {playerTeam}");
            TShock.Log.ConsoleInfo($"[CCTG] 左侧小屋坐标: ({leftHouseSpawn.X}, {leftHouseSpawn.Y})");
            TShock.Log.ConsoleInfo($"[CCTG] 右侧小屋坐标: ({rightHouseSpawn.X}, {rightHouseSpawn.Y})");

            // 根据队伍决定传送目标
            if (playerTeam == 1 && leftHouseSpawn.X != -1) // 红队 → 左侧小屋
            {
                targetSpawn = leftHouseSpawn;
                destination = "红队小屋";
            }
            else if (playerTeam == 3 && rightHouseSpawn.X != -1) // 蓝队 → 右侧小屋
            {
                targetSpawn = rightHouseSpawn;
                destination = "蓝队小屋";
            }
            else // 无队伍或其他队伍 → 保持在原位（出生点）
            {
                TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 不在红队或蓝队（队伍={playerTeam}），停留在出生点");
                return;
            }

            // 执行传送到队伍小屋
            player.Teleport(targetSpawn.X * 16, targetSpawn.Y * 16);
            player.SendSuccessMessage($"已传送到{destination}！");

            TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 回城传送到{destination} ({targetSpawn.X}, {targetSpawn.Y})");
        }

        /// <summary>
        /// 清空所有传送状态
        /// </summary>
        public void ClearAllStates()
        {
            playerRecallStates.Clear();
            playerTeamStates.Clear();
        }
    }
}
