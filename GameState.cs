using System;
using Microsoft.Xna.Framework;

namespace cctgPlugin
{
    /// <summary>
    /// 玩家队伍状态跟踪
    /// </summary>
    public class PlayerTeamState
    {
        public int LastTeam = 0;
        public DateTime LastTeamChangeTime = DateTime.MinValue;
    }

    /// <summary>
    /// 回城传送状态
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
    /// 玩家越界状态
    /// </summary>
    public class BoundaryViolationState
    {
        public bool IsOutOfBounds = false;              // 当前是否越界
        public DateTime FirstViolationTime = DateTime.MinValue;  // 首次越界时间
        public DateTime ViolationStartTime = DateTime.MinValue;  // 本次越界开始时间
        public DateTime LastReturnTime = DateTime.MinValue;      // 最后一次返回时间
        public double AccumulatedTime = 0;              // 累计越界时间（秒）
        public bool WarningShown = false;               // 是否已显示警告
        public DateTime WarningShownTime = DateTime.MinValue;    // 警告显示时间
        public bool FirstDamageApplied = false;         // 是否已扣除首次10hp
        public DateTime LastDamageTime = DateTime.MinValue;  // 上次扣血时间
    }
}
