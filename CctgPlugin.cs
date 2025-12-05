using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace cctgPlugin
{
    [ApiVersion(2, 1)]
    public class CctgPlugin : TerrariaPlugin
    {
        public override string Name => "CctgPlugin";
        public override string Author => "stardust";
        public override string Description => "CCTG插件 - 以出生点为中心涂色世界并建造房屋";
        public override Version Version => new Version(1, 0, 0);

        // 房屋保护区域
        private List<Rectangle> protectedHouseAreas = new List<Rectangle>();
        private bool housesBuilt = false;

        // 左右小屋的位置（用于队伍传送）
        private Point leftHouseSpawn = new Point(-1, -1);  // 左侧小屋（红队）
        private Point rightHouseSpawn = new Point(-1, -1); // 右侧小屋（蓝队）

        // 随机数生成器（用于房屋位置随机偏移）
        private Random _random = new Random();

        // 玩家队伍状态跟踪
        private class PlayerTeamState
        {
            public int LastTeam = 0;
            public DateTime LastTeamChangeTime = DateTime.MinValue;
        }
        private Dictionary<int, PlayerTeamState> playerTeamStates = new Dictionary<int, PlayerTeamState>();

        // Recall 回城物品列表
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
        private class RecallTeleportState
        {
            public bool WaitingForTeleport = false;
            public bool WaitingToTeleportToTeamHouse = false;
            public DateTime TeleportDetectedTime;
            public Vector2 LastKnownPosition;
            public DateTime LastItemUseTime = DateTime.MinValue;
        }
        private Dictionary<int, RecallTeleportState> playerRecallStates = new Dictionary<int, RecallTeleportState>();

        // 记分栏更新计数器
        private int scoreboardUpdateCounter = 0;
        private const int SCOREBOARD_UPDATE_INTERVAL = 60; // 每60帧（约1秒）更新一次

        // 小屋mob清理计数器
        private int mobClearCounter = 0;
        private const int MOB_CLEAR_INTERVAL = 30; // 每30帧（约0.5秒）检测一次

        // 游戏开始时间和越界检测
        private DateTime gameStartTime = DateTime.MinValue;
        private const double BOUNDARY_CHECK_DURATION = 18 * 60; // 18分钟（秒）

        // 玩家越界状态
        private class BoundaryViolationState
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
        private Dictionary<int, BoundaryViolationState> playerBoundaryStates = new Dictionary<int, BoundaryViolationState>();

        // 游戏状态
        private bool gameStarted = false;

        public CctgPlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            // 注册事件和命令
            ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInitialize);

            // 注册 Tile 编辑事件，保护房屋
            GetDataHandlers.TileEdit += OnTileEdit;

            // 注册网络数据事件，监听队伍更改
            ServerApi.Hooks.NetGetData.Register(this, OnGetData);

            // 注册游戏更新事件，处理延迟传送
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

            // 注册玩家加入和重生事件
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnPlayerJoin);

            // 注册命令 - 公开权限
            Commands.ChatCommands.Add(new Command(PaintWorldCommand, "paintworld"));
            Commands.ChatCommands.Add(new Command(BuildHousesCommand, "buildhouses"));
            Commands.ChatCommands.Add(new Command(StartCommand, "start"));
            Commands.ChatCommands.Add(new Command(EndCommand, "end"));
            Commands.ChatCommands.Add(new Command(DebugBoundaryCommand, "debugbound")); // 调试越界检测

            TShock.Log.ConsoleInfo("CctgPlugin 已加载！");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 取消注册事件
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInitialize);
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnPlayerJoin);
                GetDataHandlers.TileEdit -= OnTileEdit;
            }
            base.Dispose(disposing);
        }

        private void OnPostInitialize(EventArgs args)
        {
            // 游戏完全初始化后执行
            TShock.Log.ConsoleInfo("[CCTG] 插件已加载.");
        }

        // 命令：手动触发涂色
        private void PaintWorldCommand(CommandArgs args)
        {
            args.Player.SendInfoMessage("开始涂色世界...");
            PaintWorld();
            args.Player.SendSuccessMessage("涂色完成！");
        }

        // 涂色整个世界
        private void PaintWorld()
        {
            int spawnX = Main.spawnTileX; // 出生点X坐标
            TShock.Log.ConsoleInfo($"[CCTG] 开始涂色世界，出生点 X 坐标: {spawnX}");

            int paintedTiles = 0;

            // 涂红色：横坐标 0, -1, -2, -3
            int[] redColumns = { 0, -1, -2, -3 };
            foreach (int offset in redColumns)
            {
                int worldX = spawnX + offset;
                if (worldX >= 0 && worldX < Main.maxTilesX)
                {
                    paintedTiles += PaintColumn(worldX, PaintID.RedPaint, "红色");
                }
            }

            // 涂蓝色：横坐标 1, 2, 3, 4
            int[] blueColumns = { 1, 2, 3, 4 };
            foreach (int offset in blueColumns)
            {
                int worldX = spawnX + offset;
                if (worldX >= 0 && worldX < Main.maxTilesX)
                {
                    paintedTiles += PaintColumn(worldX, PaintID.BluePaint, "蓝色");
                }
            }

            TShock.Log.ConsoleInfo($"[CCTG] 涂色完成！共涂色 {paintedTiles} 个方块");

            // 刷新所有玩家的视野
            TSPlayer.All.SendSuccessMessage($"[CCTG] 世界涂色完成！共涂色 {paintedTiles} 个方块");
        }

        // 涂色一整列（X坐标固定，遍历所有Y）
        private int PaintColumn(int x, byte paintColor, string colorName)
        {
            int count = 0;

            for (int y = 0; y < Main.maxTilesY; y++)
            {
                var tile = Main.tile[x, y];

                // 只涂有方块的地方
                if (tile != null && tile.active())
                {
                    tile.color(paintColor);
                    count++;

                    // 每涂100个方块就发送一次更新
                    if (count % 100 == 0)
                    {
                        WorldGen.SquareTileFrame(x, y, true);
                    }
                }
            }

            // 发送整列的更新到所有客户端
            // 分段发送，避免一次发送太大的数据包
            const int sectionHeight = 100;
            for (int startY = 0; startY < Main.maxTilesY; startY += sectionHeight)
            {
                int height = Math.Min(sectionHeight, Main.maxTilesY - startY);
                TSPlayer.All.SendTileRect((short)x, (short)startY, 1, (byte)height);
            }

            TShock.Log.ConsoleInfo($"[CCTG] X={x} 列涂{colorName}，共 {count} 个方块");
            return count;
        }

        // 命令：手动建造房屋
        private void BuildHousesCommand(CommandArgs args)
        {
            args.Player.SendInfoMessage("开始建造房屋...");
            BuildHouses();
            args.Player.SendSuccessMessage("房屋建造完成！");
        }

        // 命令：开始游戏
        private void StartCommand(CommandArgs args)
        {
            if (gameStarted)
            {
                args.Player.SendErrorMessage("游戏已经开始了，不能重复 start！");
                return;
            }

            args.Player.SendInfoMessage("开始初始化游戏...");

            // 1. 建造房屋
            BuildHouses();
            TSPlayer.All.SendSuccessMessage("[游戏开始] 房屋建造完成！");

            // 2. 涂色世界
            PaintWorld();
            TSPlayer.All.SendSuccessMessage("[游戏开始] 世界涂色完成！");

            // 3. 设置时间为 10:30
            SetTime(10, 30);
            TSPlayer.All.SendSuccessMessage("[游戏开始] 时间已设置为 10:30");

            // 4. 重置玩家背包和状态（没有 ignoresse 权限的玩家）
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                {
                    // 检查玩家是否有 ignoresse 权限
                    if (!player.HasPermission("ignoresse"))
                    {
                        // 重置玩家为SSC配置的新玩家状态
                        player.PlayerData.CopyCharacter(player);
                        TShock.CharacterDB.InsertPlayerData(player);
                        player.IgnoreSSCPackets = false;

                        // 设置为SSC起始数据
                        player.TPlayer.statLife = TShock.ServerSideCharacterConfig.Settings.StartingHealth;
                        player.TPlayer.statLifeMax = TShock.ServerSideCharacterConfig.Settings.StartingHealth;
                        player.TPlayer.statMana = TShock.ServerSideCharacterConfig.Settings.StartingMana;
                        player.TPlayer.statManaMax = TShock.ServerSideCharacterConfig.Settings.StartingMana;

                        // 清空背包
                        for (int i = 0; i < NetItem.InventorySlots; i++)
                        {
                            player.TPlayer.inventory[i].SetDefaults(0);
                        }

                        // 清空装备（盔甲和饰品）
                        for (int i = 0; i < NetItem.ArmorSlots; i++)
                        {
                            player.TPlayer.armor[i].SetDefaults(0);
                        }

                        // 清空染料
                        for (int i = 0; i < NetItem.DyeSlots; i++)
                        {
                            player.TPlayer.dye[i].SetDefaults(0);
                        }

                        // 清空其他装备（宠物、坐骑等）
                        for (int i = 0; i < NetItem.MiscEquipSlots; i++)
                        {
                            player.TPlayer.miscEquips[i].SetDefaults(0);
                        }

                        // 清空其他染料
                        for (int i = 0; i < NetItem.MiscDyeSlots; i++)
                        {
                            player.TPlayer.miscDyes[i].SetDefaults(0);
                        }

                        // 给予起始物品
                        var startingItems = TShock.ServerSideCharacterConfig.Settings.StartingInventory;
                        for (int i = 0; i < startingItems.Count && i < NetItem.InventorySlots; i++)
                        {
                            player.TPlayer.inventory[i] = startingItems[i].ToItem();
                        }

                        // 同步到客户端
                        player.SendData(PacketTypes.PlayerHp, "", player.Index);
                        player.SendData(PacketTypes.PlayerMana, "", player.Index);
                        player.SendData(PacketTypes.PlayerInfo, "", player.Index);

                        // 同步背包
                        for (int i = 0; i < NetItem.InventorySlots; i++)
                        {
                            player.SendData(PacketTypes.PlayerSlot, "", player.Index, i);
                        }

                        // 同步装备槽（盔甲和饰品）
                        for (int i = 0; i < NetItem.ArmorSlots; i++)
                        {
                            player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + i);
                        }

                        // 同步染料槽
                        for (int i = 0; i < NetItem.DyeSlots; i++)
                        {
                            player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + i);
                        }

                        // 同步其他装备槽
                        for (int i = 0; i < NetItem.MiscEquipSlots; i++)
                        {
                            player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots + i);
                        }

                        // 同步其他染料槽
                        for (int i = 0; i < NetItem.MiscDyeSlots; i++)
                        {
                            player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots + NetItem.MiscEquipSlots + i);
                        }

                        player.SendSuccessMessage("[游戏开始] 你的背包、装备和状态已重置为初始状态");
                        TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 背包、装备和状态已重置");
                    }
                    else
                    {
                        player.SendInfoMessage("[游戏开始] 你有 ignoresse 权限，背包和状态保持不变");
                        TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 有 ignoresse 权限，跳过重置");
                    }
                }
            }

            // 5. 为所有玩家随机分配红队或蓝队
            Random random = new Random();
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                {
                    // 随机选择红队(1)或蓝队(3)
                    int team = random.Next(2) == 0 ? 1 : 3;
                    player.SetTeam(team);

                    string teamName = team == 1 ? "红队" : "蓝队";
                    player.SendSuccessMessage($"你已被分配到 {teamName}！");

                    // 延迟传送到对应房屋
                    if (!playerTeamStates.ContainsKey(player.Index))
                    {
                        playerTeamStates[player.Index] = new PlayerTeamState();
                    }

                    var state = playerTeamStates[player.Index];
                    state.LastTeam = team;
                    state.LastTeamChangeTime = DateTime.Now;

                    TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 被分配到{teamName}，准备传送");
                }
            }

            gameStarted = true;
            gameStartTime = DateTime.Now;  // 记录游戏开始时间

            // 清空所有玩家的越界状态
            playerBoundaryStates.Clear();

            TSPlayer.All.SendSuccessMessage("════════════════════════════");
            TSPlayer.All.SendSuccessMessage("    游戏开始！祝你好运！    ");
            TSPlayer.All.SendSuccessMessage("  前18分钟请勿越过出生点！  ");
            TSPlayer.All.SendSuccessMessage("════════════════════════════");

            TShock.Log.ConsoleInfo("[CCTG] 游戏已开始！越界检测已启动（18分钟）");
        }

        // 调试命令：检查越界检测状态
        private void DebugBoundaryCommand(CommandArgs args)
        {
            var player = args.Player;

            player.SendInfoMessage("=== 越界检测调试信息 ===");
            player.SendInfoMessage($"游戏已开始: {gameStarted}");
            player.SendInfoMessage($"游戏开始时间: {gameStartTime}");

            if (gameStartTime != DateTime.MinValue)
            {
                double timeSinceStart = (DateTime.Now - gameStartTime).TotalSeconds;
                player.SendInfoMessage($"游戏已运行: {timeSinceStart:F1}秒");
                player.SendInfoMessage($"越界检测持续时间: {BOUNDARY_CHECK_DURATION}秒（{BOUNDARY_CHECK_DURATION/60}分钟）");
                player.SendInfoMessage($"越界检测是否有效: {timeSinceStart <= BOUNDARY_CHECK_DURATION}");
            }

            player.SendInfoMessage($"你的队伍: {player.TPlayer.team} ({(player.TPlayer.team == 1 ? "红队" : player.TPlayer.team == 3 ? "蓝队" : "无队伍")})");
            player.SendInfoMessage($"出生点X: {Main.spawnTileX}");
            player.SendInfoMessage($"你的位置X: {(int)(player.TPlayer.position.X / 16)}");

            if (player.TPlayer.team == 1)
            {
                bool isOut = (int)(player.TPlayer.position.X / 16) >= Main.spawnTileX;
                player.SendInfoMessage($"红队越界检测: {isOut} (位置 >= 出生点)");
            }
            else if (player.TPlayer.team == 3)
            {
                bool isOut = (int)(player.TPlayer.position.X / 16) <= Main.spawnTileX;
                player.SendInfoMessage($"蓝队越界检测: {isOut} (位置 <= 出生点)");
            }

            TShock.Log.ConsoleInfo($"[CCTG] {player.Name} 使用了越界检测调试命令");
        }

        // 命令：结束游戏
        private void EndCommand(CommandArgs args)
        {
            args.Player.SendInfoMessage("正在结束游戏...");

            // 1. 设置时间为 10:30
            SetTime(10, 30);
            TSPlayer.All.SendSuccessMessage("[游戏结束] 时间已设置为 10:30");

            // 2. 清除所有 NPC（怪物和 Boss）
            int killedCount = 0;
            for (int i = 0; i < Main.npc.Length; i++)
            {
                if (Main.npc[i].active && !Main.npc[i].townNPC)
                {
                    Main.npc[i].active = false;
                    Main.npc[i].type = 0;
                    TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                    killedCount++;
                }
            }
            TSPlayer.All.SendSuccessMessage($"[游戏结束] 已清除 {killedCount} 个敌对生物");

            // 3. 清除房屋
            if (housesBuilt)
            {
                ClearHouses();
                TSPlayer.All.SendSuccessMessage("[游戏结束] 房屋已清除");
            }

            // 4. 解除游戏开始状态
            gameStarted = false;
            gameStartTime = DateTime.MinValue;

            // 清空所有玩家的越界状态
            playerBoundaryStates.Clear();

            TSPlayer.All.SendSuccessMessage("════════════════════════════");
            TSPlayer.All.SendSuccessMessage("       游戏已结束！         ");
            TSPlayer.All.SendSuccessMessage("════════════════════════════");

            TShock.Log.ConsoleInfo("[CCTG] 游戏已结束！");
        }

        // 设置游戏时间
        private void SetTime(int hour, int minute)
        {
            // 计算时间（游戏时间从 4:30 AM 开始）
            double targetMinutes = hour * 60 + minute;
            double startMinutes = 4 * 60 + 30; // 4:30 AM

            if (targetMinutes < startMinutes)
            {
                // 如果目标时间在 4:30 之前，加一天
                targetMinutes += 24 * 60;
            }

            double minutesFromStart = targetMinutes - startMinutes;

            // 白天持续 15 小时 (4:30 AM 到 7:30 PM)
            // 夜晚持续 9 小时 (7:30 PM 到 4:30 AM)
            double dayMinutes = 15 * 60; // 900 分钟

            if (minutesFromStart <= dayMinutes)
            {
                // 白天时间
                Main.dayTime = true;
                Main.time = (minutesFromStart / dayMinutes) * 54000;
            }
            else
            {
                // 夜晚时间
                double nightMinutesFromStart = minutesFromStart - dayMinutes;
                double nightMinutes = 9 * 60; // 540 分钟
                Main.dayTime = false;
                Main.time = (nightMinutesFromStart / nightMinutes) * 32400;
            }

            TSPlayer.All.SendData(PacketTypes.TimeSet);
        }

        // 建造左右两侧的房屋
        private void BuildHouses()
        {
            int spawnX = Main.spawnTileX;
            int spawnY = Main.spawnTileY;

            TShock.Log.ConsoleInfo($"[CCTG] 开始建造房屋，出生点坐标: ({spawnX}, {spawnY})");

            // 左侧房屋（初始200格，优先向出生点搜索至100格，失败则向外搜索>200格）红队
            int leftHouseX = spawnX - (200 + _random.Next(-20, 21));
            var leftLocation = BuildSingleHouse(leftHouseX, spawnY, "左侧", -1); // -1 表示向左
            if (leftLocation.X != -1)
            {
                leftHouseSpawn = leftLocation; // 记录左侧小屋位置
                TShock.Log.ConsoleInfo($"[CCTG] 左侧小屋（红队）出生点: ({leftHouseSpawn.X}, {leftHouseSpawn.Y})");
            }

            // 右侧房屋（初始200格，优先向出生点搜索至100格，失败则向外搜索>200格）蓝队
            int rightHouseX = spawnX + (200 + _random.Next(-20, 21));
            var rightLocation = BuildSingleHouse(rightHouseX, spawnY, "右侧", 1); // 1 表示向右
            if (rightLocation.X != -1)
            {
                rightHouseSpawn = rightLocation; // 记录右侧小屋位置
                TShock.Log.ConsoleInfo($"[CCTG] 右侧小屋（蓝队）出生点: ({rightHouseSpawn.X}, {rightHouseSpawn.Y})");
            }

            housesBuilt = true;
            TShock.Log.ConsoleInfo($"[CCTG] 房屋建造完成！");
            TSPlayer.All.SendSuccessMessage("[CCTG] 出生点左右两侧房屋已建造完成！");
        }

        // 清除房屋
        private void ClearHouses()
        {
            if (protectedHouseAreas.Count == 0)
            {
                TShock.Log.ConsoleInfo("[CCTG] 没有房屋需要清除");
                return;
            }

            TShock.Log.ConsoleInfo($"[CCTG] 开始清除房屋，共 {protectedHouseAreas.Count} 个区域");

            foreach (var houseArea in protectedHouseAreas)
            {
                // 扩展清除范围：包括墙壁、地基、天花板和上方40格空间
                int clearStartX = houseArea.X - 2;
                int clearEndX = houseArea.X + houseArea.Width + 2;
                int clearStartY = houseArea.Y - 41; // 上方40格 + 天花板1格
                int clearEndY = houseArea.Y + houseArea.Height + 2; // 下方包括地基

                for (int x = clearStartX; x < clearEndX; x++)
                {
                    for (int y = clearStartY; y < clearEndY; y++)
                    {
                        if (IsValidCoord(x, y))
                        {
                            Main.tile[x, y].ClearEverything();
                        }
                    }
                }

                // 刷新区域
                TSPlayer.All.SendTileRect((short)clearStartX, (short)clearStartY,
                    (byte)(clearEndX - clearStartX), (byte)(clearEndY - clearStartY));
            }

            // 清空房屋保护区域列表
            protectedHouseAreas.Clear();

            // 重置房屋位置
            leftHouseSpawn = new Point(-1, -1);
            rightHouseSpawn = new Point(-1, -1);

            // 重置房屋建造状态
            housesBuilt = false;

            TShock.Log.ConsoleInfo("[CCTG] 房屋清除完成");
        }

        // 建造单个房屋 - 5x10 和 10x6 组合
        // direction: -1 向左搜索, 1 向右搜索
        // 返回：小屋的出生点位置（房屋中心地板上方）
        private Point BuildSingleHouse(int centerX, int groundY, string side, int direction)
        {
            // 左房间：5宽 x 11高
            const int leftRoomWidth = 5;
            const int leftRoomHeight = 11;

            // 右房间：10宽 x 7高
            const int rightRoomWidth = 10;
            const int rightRoomHeight = 7;

            // 总宽度
            const int totalWidth = leftRoomWidth + rightRoomWidth - 1;
            const int maxHeight = leftRoomHeight; // 用最高的房间来查找位置

            // 横向搜索合适的位置
            // direction: -1 表示向左（红队），1 表示向右（蓝队）
            int worldSpawnX = Main.spawnTileX;
            int startX = centerX;
            int groundLevel = -1;

            // 先尝试初始位置（200格左右）
            groundLevel = FindSuitableHeightForHouse(startX, groundY, totalWidth, maxHeight);

            // 第一阶段：优先向出生点方向搜索（200格→100格）
            if (groundLevel == -1)
            {
                TShock.Log.ConsoleInfo($"[CCTG] 初始位置 X={startX} 不合适，开始向出生点方向搜索（至100格）");

                // 计算当前距离出生点的距离
                int currentDistance = Math.Abs(centerX - worldSpawnX);

                // 向出生点方向搜索，直到距离出生点100格为止
                for (int offset = 1; offset <= currentDistance - 100; offset++)
                {
                    // 向出生点方向移动（direction为负时向右移动，为正时向左移动）
                    int testX = centerX - (direction * offset);

                    groundLevel = FindSuitableHeightForHouse(testX, groundY, totalWidth, maxHeight);
                    if (groundLevel != -1)
                    {
                        startX = testX;
                        int distanceToSpawn = Math.Abs(testX - worldSpawnX);
                        TShock.Log.ConsoleInfo($"[CCTG] 在距离出生点{distanceToSpawn}格处找到合适位置: X={startX}");
                        break;
                    }
                }
            }

            // 第二阶段：如果到100格仍未找到，向远离出生点方向搜索（>200格）
            if (groundLevel == -1)
            {
                TShock.Log.ConsoleWarn($"[CCTG] {side}房屋在100-200格范围内未找到合适位置，向>200格方向搜索");

                // 从初始位置向远离出生点方向搜索，最多搜索100格
                for (int offset = 1; offset <= 100; offset++)
                {
                    // 向远离出生点方向移动（direction为负时向左移动，为正时向右移动）
                    int testX = centerX + (direction * offset);

                    groundLevel = FindSuitableHeightForHouse(testX, groundY, totalWidth, maxHeight);
                    if (groundLevel != -1)
                    {
                        startX = testX;
                        int distanceToSpawn = Math.Abs(testX - worldSpawnX);
                        TShock.Log.ConsoleInfo($"[CCTG] 在距离出生点{distanceToSpawn}格处找到合适位置: X={startX}");
                        break;
                    }
                }
            }

            // 如果仍然找不到合适位置，使用强制建造模式
            if (groundLevel == -1)
            {
                TShock.Log.ConsoleWarn($"[CCTG] {side}房屋在所有搜索范围内未找到理想位置，使用强制建造模式（降低要求：50%地面接触）");

                // 从出生点±200格位置开始向两侧搜索
                int forceSpawnX = Main.spawnTileX;
                int forceBuildStartX = direction < 0 ? forceSpawnX - 200 : forceSpawnX + 200;

                bool foundValidLocation = false;
                const int forceBuildSearchRange = 200; // 向两侧各搜索200格

                // 向两侧搜索
                for (int offset = 0; offset <= forceBuildSearchRange && !foundValidLocation; offset++)
                {
                    // 尝试两个方向
                    int[] testXPositions = offset == 0
                        ? new int[] { forceBuildStartX }
                        : new int[] { forceBuildStartX + offset, forceBuildStartX - offset };

                    foreach (int testX in testXPositions)
                    {
                        // 向下搜索合适的高度（在出生点Y附近100格范围内）
                        for (int y = groundY - 30; y < groundY + 70; y++)
                        {
                            if (!IsValidCoord(testX, y))
                                continue;

                            // 检查这一层的地面接触率
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

                            // 要求50%以上地面接触
                            if (totalChecked > 0 && solidCount >= totalWidth * 0.5)
                            {
                                // 检查上方是否有方块阻挡
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
                                    startX = testX;
                                    groundLevel = y;
                                    foundValidLocation = true;
                                    int contactPercent = (int)((double)solidCount / totalWidth * 100);
                                    TShock.Log.ConsoleInfo($"[CCTG] {side}房屋强制建造在 X={startX}, Y={groundLevel}（地面接触{contactPercent}%，上方无阻挡）");
                                    break;
                                }
                            }
                        }
                        if (foundValidLocation) break;
                    }
                }

                if (!foundValidLocation)
                {
                    // 如果还找不到，使用最基础的备用方案
                    startX = forceBuildStartX;
                    groundLevel = groundY;
                    TShock.Log.ConsoleError($"[CCTG] {side}房屋无法找到满足条件的位置，将在 X={startX}, Y={groundLevel} 强制建造并清空空间");
                }
            }

            TShock.Log.ConsoleInfo($"[CCTG] {side}房屋建造位置: X={startX}, 地面高度={groundLevel}");

            // 清除整个区域（包括门外侧的空间和上方40格天空空间）
            // 左门左侧额外2格，右门右侧额外2格
            int clearStartX = startX - 2;
            int clearEndX = startX + totalWidth + 2;
            const int skyClearHeight = 40; // 清理上方40格空间

            for (int x = clearStartX; x < clearEndX; x++)
            {
                // 清理从房顶向上40格的空间，到地基层
                for (int y = groundLevel - maxHeight - skyClearHeight; y <= groundLevel; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].ClearEverything();
                    }
                }
            }

            TShock.Log.ConsoleInfo($"[CCTG] {side}房屋区域清理完成（包括上方{skyClearHeight}格空间）");

            // === 建造左房间（5x10）===
            int leftStartX = startX;
            int leftTopY = groundLevel - leftRoomHeight;

            // 左房间地基层（在地表 groundLevel，两侧各多1格）
            for (int x = leftStartX - 1; x <= leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab); // 地基，建在地表上
            }

            // 左房间地板（室内地板）
            for (int x = leftStartX; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab); // 地板
            }

            // 左房间天花板
            for (int x = leftStartX; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, leftTopY, TileID.StoneSlab);
            }

            // 左房间左墙
            for (int y = leftTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(leftStartX, y, TileID.StoneSlab);
            }

            // === 建造右房间（10x6）===
            int rightStartX = leftStartX + leftRoomWidth - 1;
            int rightTopY = groundLevel - rightRoomHeight;

            // 右房间地基层（在地表 groundLevel，右侧多1格）
            // 左侧已经由左房间的地基覆盖，从 rightStartX 开始
            for (int x = rightStartX; x <= rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab); // 地基，建在地表上
            }

            // 右房间地板（室内地板）
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab); // 地板
            }

            // 右房间天花板
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, rightTopY, TileID.StoneSlab);
            }

            // 右房间右墙
            for (int y = rightTopY + 1; y < groundLevel - 1; y++)
            {   
                PlaceTile(rightStartX + rightRoomWidth - 1, y, TileID.StoneSlab);
            }

            // === 建造中间墙（左右房间之间，通道高度位置打通）===
            int middleX = rightStartX; // 中间墙的X坐标
            int passageHeight = 4; // 通道高度（底部4格）

            // 中间墙从左房间的顶部开始建造（因为左房间更高）
            for (int y = leftTopY + 1; y < groundLevel - 1; y++)
            {
                // 只在右房间高度范围内判断通道
                if (y >= rightTopY)
                {
                    // 通道高度位置（底部4格）不放置方块，作为通道
                    if (y < groundLevel - passageHeight)
                    {
                        PlaceTile(middleX, y, TileID.StoneSlab);
                    }
                }
                else
                {
                    // 超出右房间高度的部分，全部建造墙壁
                    PlaceTile(middleX, y, TileID.StoneSlab);
                }
            }

            // 放置外侧门（左墙和右墙）
            PlaceDoor(leftStartX, groundLevel - 3, TileID.ClosedDoor); // 左房间左侧门
            PlaceDoor(rightStartX + rightRoomWidth - 1, groundLevel - 3, TileID.ClosedDoor); // 右房间右侧门

            // 左门上方放置木平台和火把
            // 左门上侧第一个方块为基准点 (leftStartX, groundLevel - 4)
            int leftDoorTopX = leftStartX;
            int leftDoorTopY = groundLevel - 4;

            // 在 (1, 3) 和 (3, 3) 处放置木平台，向上移动6格（y - 6）
            PlacePlatform(leftDoorTopX + 1, leftDoorTopY + 3 - 6, TileID.Platforms, 0);
            PlacePlatform(leftDoorTopX + 3, leftDoorTopY + 3 - 6, TileID.Platforms, 0);
            TShock.Log.ConsoleInfo($"[CCTG] 左门上方木平台放置完成");

            // 在平台上放置火把
            PlaceTorch(leftDoorTopX + 1, leftDoorTopY + 3 - 6 - 1);
            PlaceTorch(leftDoorTopX + 3, leftDoorTopY + 3 - 6 - 1);
            TShock.Log.ConsoleInfo($"[CCTG] 左门上方火把放置完成");

            // 填充木墙 - 左房间
            for (int x = leftStartX + 1; x < leftStartX + leftRoomWidth - 1; x++)
            {
                for (int y = leftTopY + 1; y < groundLevel - 1; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].wall = WallID.Wood;
                    }
                }
            }

            // 填充木墙 - 右房间
            for (int x = rightStartX; x < rightStartX + rightRoomWidth - 1; x++)
            {
                for (int y = rightTopY + 1; y < groundLevel - 1; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].wall = WallID.Wood;
                    }
                }
            }

            // 放置家具
            PlaceFurniture(leftStartX, rightStartX, groundLevel, leftRoomWidth, rightRoomWidth);

            // 刷新区域
            TSPlayer.All.SendTileRect((short)startX, (short)leftTopY, (byte)(totalWidth + 2), (byte)(maxHeight + 2));

            // 保存保护区域（只保护房屋内部空间，不包括墙壁）
            // 左房间内部区域
            int leftInteriorX = leftStartX + 1;
            int leftInteriorY = leftTopY + 1;
            int leftInteriorWidth = leftRoomWidth - 1; // 不包括左墙和中间墙
            int leftInteriorHeight = leftRoomHeight - 2; // 不包括天花板和地板
            protectedHouseAreas.Add(new Rectangle(leftInteriorX, leftInteriorY, leftInteriorWidth, leftInteriorHeight));

            // 右房间内部区域
            int rightInteriorX = rightStartX + 1;
            int rightInteriorY = rightTopY + 1;
            int rightInteriorWidth = rightRoomWidth - 2; // 不包括中间墙和右墙
            int rightInteriorHeight = rightRoomHeight - 2; // 不包括天花板和地板
            protectedHouseAreas.Add(new Rectangle(rightInteriorX, rightInteriorY, rightInteriorWidth, rightInteriorHeight));

            TShock.Log.ConsoleInfo($"[CCTG] {side}组合房屋建造完成！");
            TShock.Log.ConsoleInfo($"[CCTG] 左房间保护区域: ({leftInteriorX}, {leftInteriorY}, {leftInteriorWidth}x{leftInteriorHeight})");
            TShock.Log.ConsoleInfo($"[CCTG] 右房间保护区域: ({rightInteriorX}, {rightInteriorY}, {rightInteriorWidth}x{rightInteriorHeight})");

            // 返回小屋出生点（右房间中心，地板上方）
            int spawnX = rightStartX + rightRoomWidth / 2;
            int spawnY = groundLevel - 3; // 地板上方2格

            TShock.Log.ConsoleInfo($"[CCTG] {side}房屋建造完成，出生点设置为: ({spawnX}, {spawnY})");
            return new Point(spawnX, spawnY);
        }

        // 放置家具
        private void PlaceFurniture(int leftStartX, int rightStartX, int groundLevel, int leftWidth, int rightWidth)
        {
            int floorY = groundLevel - 2; // 地板上方

            TShock.Log.ConsoleInfo($"[CCTG] 开始放置家具...");

            // 右房间 - 木椅（位置不变，占1格）
            // 椅子在 rightStartX+2
            if (WorldGen.PlaceObject(rightStartX + 2, floorY, TileID.Chairs, false, 0))
            {
                TShock.Log.ConsoleInfo($"[CCTG] 右房间：木椅放置成功（朝右） at ({rightStartX + 2}, {floorY})");
            }

            // 右房间 - 铁砧（紧贴椅子右侧，占2格）
            // 铁砧在 rightStartX+3，占据 rightStartX+3 和 rightStartX+4
            if (WorldGen.PlaceObject(rightStartX + 3, floorY, TileID.Anvils, false, 0))
            {
                TShock.Log.ConsoleInfo($"[CCTG] 右房间：铁砧放置成功 at ({rightStartX + 3}, {floorY})");
            }

            // 右房间 - 木平台（在铁砧上方 y+1，占2格）
            // 平台在 floorY - 1（铁砧上方1格）
            int platformY = floorY - 1;
            PlacePlatform(rightStartX + 3, platformY, TileID.Platforms, 0); // 木平台
            PlacePlatform(rightStartX + 4, platformY, TileID.Platforms, 0); // 木平台
            TShock.Log.ConsoleInfo($"[CCTG] 右房间：木平台放置成功 at ({rightStartX + 3}, {platformY})");

            // 右房间 - 木工作台（放在平台上，占2格）
            // 工作台在平台上方，位置为 platformY - 1
            if (WorldGen.PlaceObject(rightStartX + 3, platformY - 1, TileID.WorkBenches, false, 0))
            {
                TShock.Log.ConsoleInfo($"[CCTG] 右房间：工作台放置成功（在平台上） at ({rightStartX + 3}, {platformY - 1})");
            }

            // 右房间 - 熔炉（铁砧右侧，占3格）
            // 熔炉在 rightStartX+6
            if (WorldGen.PlaceObject(rightStartX + 6, floorY, TileID.Furnaces, false, 0))
            {
                TShock.Log.ConsoleInfo($"[CCTG] 右房间：熔炉放置成功 at ({rightStartX + 6}, {floorY})");
            }

            // 刷新家具区域
            NetMessage.SendTileSquare(-1, leftStartX, floorY - 2, 15);
        }

        // 放置平台
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

        // 查找合适的建造高度（垂直搜索）
        // 使用更严格的地面检测逻辑
        private int FindSuitableHeightForHouse(int startX, int startY, int width, int height)
        {
            const int searchRange = 30; // 在出生点上下30格范围内搜索
            const int liquidCheckHeight = 5; // 检查上方5格是否有大量液体

            TShock.Log.ConsoleInfo($"[CCTG] 在地表附近（出生点Y={startY}上下{searchRange}格）搜索地面");

            // 先向下搜索
            for (int offsetY = 0; offsetY <= searchRange; offsetY++)
            {
                int testGroundLevel = startY + offsetY;

                // 检查这个位置是否是合适的地面
                if (IsValidGroundSurface(startX, testGroundLevel, width, liquidCheckHeight, height))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] 在出生点下方 {offsetY} 格找到合适的地面高度: {testGroundLevel}");
                    return testGroundLevel;
                }
            }

            // 再向上搜索
            for (int offsetY = 1; offsetY <= searchRange; offsetY++)
            {
                int testGroundLevel = startY - offsetY;

                // 检查这个位置是否是合适的地面
                if (IsValidGroundSurface(startX, testGroundLevel, width, liquidCheckHeight, height))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] 在出生点上方 {offsetY} 格找到合适的地面高度: {testGroundLevel}");
                    return testGroundLevel;
                }
            }

            // 如果没找到合适位置，返回-1表示失败
            TShock.Log.ConsoleInfo($"[CCTG] 未在地表附近找到合适位置");
            return -1;
        }

        // 检查是否是有效的地面表面
        // height: 房子的高度（用于检查向上40格空间）
        private bool IsValidGroundSurface(int startX, int groundY, int width, int liquidCheckHeight, int houseHeight)
        {
            int validGroundTiles = 0;
            int totalChecked = 0;

            // 检查宽度范围内的每个位置
            for (int x = startX; x < startX + width; x++)
            {
                if (!IsValidCoord(x, groundY))
                    continue;

                totalChecked++;
                var groundTile = Main.tile[x, groundY];

                // 1. 检查地面格子是否是实心方块（底部必须嵌入地面）
                if (groundTile == null || !groundTile.active() || !Main.tileSolid[groundTile.type])
                    continue;

                // 2. 检查上面一格是否不是实心方块（表示这是"顶面"）
                if (!IsValidCoord(x, groundY - 1))
                    continue;

                var aboveTile = Main.tile[x, groundY - 1];
                if (aboveTile != null && aboveTile.active() && Main.tileSolid[aboveTile.type])
                    continue;

                // 3. 检查上方是否有大量液体
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

                // 如果上方液体太多（超过检查高度的一半），跳过
                if (liquidCount > liquidCheckHeight / 2)
                    continue;

                // 这个位置通过所有检查
                validGroundTiles++;
            }

            // 要求100%的地面接触（房子底部必须全部接触地面）
            if (totalChecked == 0 || validGroundTiles != width)
                return false;

            // 4. 检查向上40格空间是否有方块（房子上方必须有足够空间）
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
                        TShock.Log.ConsoleInfo($"[CCTG] 高度 {groundY} 检测失败: 位置 ({x}, {y}) 上方有方块阻挡");
                        return false;
                    }
                }
            }

            TShock.Log.ConsoleInfo($"[CCTG] 高度 {groundY} 检测通过: {validGroundTiles}/{width} 个有效地面格子 (100%覆盖), 上方40格空间清空");
            return true;
        }

        // 统计指定区域内的方块数量
        private int CountBlocksInArea(int startX, int startY, int width, int height)
        {
            int count = 0;

            for (int x = startX; x < startX + width; x++)
            {
                for (int y = startY; y < startY + height; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        var tile = Main.tile[x, y];
                        if (tile != null && tile.active() && Main.tileSolid[tile.type])
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        // 放置方块
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

        // 放置门
        private void PlaceDoor(int x, int y, ushort type)
        {
            if (IsValidCoord(x, y))
            {
                WorldGen.PlaceDoor(x, y, type);
                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        // 放置火把
        private void PlaceTorch(int x, int y)
        {
            if (IsValidCoord(x, y))
            {
                WorldGen.PlaceTile(x, y, TileID.Torches, false, false, -1, 0);
                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        // 检查坐标是否有效
        private bool IsValidCoord(int x, int y)
        {
            return x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY;
        }

        // 查找地面高度
        private int FindGroundLevel(int x, int startY)
        {
            // 从给定高度向下搜索，找到第一个实心方块
            for (int y = startY; y < Main.maxTilesY - 10; y++)
            {
                var tile = Main.tile[x, y];
                if (tile != null && tile.active() && Main.tileSolid[tile.type])
                {
                    return y;
                }
            }
            return -1;
        }

        // 找到或创建平坦的地面
        private int FindOrCreateGround(int startX, int startY, int width)
        {
            // 尝试找到平均地面高度
            int totalHeight = 0;
            int validCount = 0;

            for (int x = startX; x < startX + width; x++)
            {
                int groundY = FindGroundLevel(x, startY);
                if (groundY != -1)
                {
                    totalHeight += groundY;
                    validCount++;
                }
            }

            // 确定地面高度
            int targetGroundLevel;
            if (validCount > 0)
            {
                targetGroundLevel = totalHeight / validCount; // 平均高度
            }
            else
            {
                targetGroundLevel = startY + 10; // 如果找不到，使用默认高度
            }

            TShock.Log.ConsoleInfo($"[CCTG] 创建平坦地面，高度: {targetGroundLevel}");

            // 清除地面上方所有方块（包括不平的地面）
            for (int x = startX; x < startX + width; x++)
            {
                for (int y = 0; y < targetGroundLevel; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].ClearEverything();
                    }
                }
            }

            // 在目标高度填充平坦的地面
            for (int x = startX; x < startX + width; x++)
            {
                // 确保地面下方有支撑
                for (int y = targetGroundLevel; y < targetGroundLevel + 3; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        PlaceTile(x, y, TileID.Dirt);
                    }
                }
            }

            TShock.Log.ConsoleInfo($"[CCTG] 平坦地面创建完成");
            return targetGroundLevel;
        }

        // Tile 编辑事件处理器 - 保护房屋
        private void OnTileEdit(object sender, GetDataHandlers.TileEditEventArgs e)
        {
            if (!housesBuilt || e.Handled)
                return;

            // 检查编辑位置是否在任何保护区域内
            foreach (var area in protectedHouseAreas)
            {
                if (area.Contains(e.X, e.Y))
                {
                    // 如果玩家没有管理员权限，则阻止破坏
                    if (!e.Player.HasPermission("cctg.edit"))
                    {
                        e.Handled = true;
                        e.Player.SendErrorMessage("CCTG 房屋受到保护，无法破坏！");

                        // 恢复原始 Tile 状态
                        e.Player.SendTileRect((short)e.X, (short)e.Y, 1, 1);
                    }
                    break;
                }
            }
        }

        // 玩家加入游戏事件
        private void OnPlayerJoin(GreetPlayerEventArgs e)
        {
            var player = TShock.Players[e.Who];
            if (player != null)
            {
                TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 加入游戏");
            }
        }


        // 网络数据接收处理器 - 监听队伍更改和回城物品使用
        private void OnGetData(GetDataEventArgs e)
        {
            if (e.Handled)
                return;

            var player = TShock.Players[e.Msg.whoAmI];
            if (player == null || !player.Active)
                return;

            // 监听 PlayerUpdate 数据包 - 检测回城物品使用
            if (e.MsgID == PacketTypes.PlayerUpdate && housesBuilt)
            {
                using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
                {
                    try
                    {
                        var playerId = reader.ReadByte();
                        var control = reader.ReadByte();
                        var pulley = reader.ReadByte();
                        var miscFlags = reader.ReadByte();
                        var sleepingInfo = reader.ReadByte();
                        var selectedItem = reader.ReadByte();

                        // 检查玩家当前选中的物品
                        if (selectedItem < player.TPlayer.inventory.Length)
                        {
                            var item = player.TPlayer.inventory[selectedItem];

                            // 检查是否是回城物品
                            if (item != null && RecallItems.Contains(item.type))
                            {
                                // 检查玩家是否正在使用物品
                                bool isUsingItem = (control & 0x20) != 0;

                                if (isUsingItem)
                                {
                                    // 初始化回城状态
                                    if (!playerRecallStates.ContainsKey(player.Index))
                                    {
                                        playerRecallStates[player.Index] = new RecallTeleportState
                                        {
                                            LastKnownPosition = player.TPlayer.position
                                        };
                                    }

                                    var recallState = playerRecallStates[player.Index];

                                    // 如果正在等待传送，忽略重复使用
                                    if (recallState.WaitingForTeleport || recallState.WaitingToTeleportToTeamHouse)
                                    {
                                        return;
                                    }

                                    // 记录使用道具时的位置和状态
                                    recallState.WaitingForTeleport = true;
                                    recallState.LastKnownPosition = player.TPlayer.position;
                                    recallState.LastItemUseTime = DateTime.Now;

                                    TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 使用回城物品，等待传送发生...");
                                    return;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.Error($"[CCTG] 解析 PlayerUpdate 出错: {ex.Message}");
                    }
                }
            }

            // 监听 PlayerTeam 数据包
            if (e.MsgID == PacketTypes.PlayerTeam && housesBuilt)
            {
                // 初始化玩家状态
                if (!playerTeamStates.ContainsKey(player.Index))
                {
                    playerTeamStates[player.Index] = new PlayerTeamState();
                }

                var state = playerTeamStates[player.Index];

                // 直接从数据包中读取队伍值
                int newTeam = -1;
                using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
                {
                    try
                    {
                        var playerId = reader.ReadByte();
                        newTeam = reader.ReadByte(); // 读取队伍值
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.Error($"[CCTG] 解析 PlayerTeam 数据包出错: {ex.Message}");
                        return;
                    }
                }

                // 检测队伍更改
                if (newTeam != -1 && newTeam != state.LastTeam)
                {
                    TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 队伍更改: {state.LastTeam} -> {newTeam}");

                    // 红队（1）或蓝队（3）
                    if (newTeam == 1 || newTeam == 3)
                    {
                        state.LastTeamChangeTime = DateTime.Now;
                        state.LastTeam = newTeam;

                        string teamName = newTeam == 1 ? "红队" : "蓝队";
                        player.SendInfoMessage($"已加入{teamName}，0.5秒后将传送到对应小屋...");
                        TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 加入{teamName}，准备传送");
                    }
                    else
                    {
                        state.LastTeam = newTeam;
                    }
                }
            }

            // 监听 PlayerSpawn 数据包（玩家重生）
            if (e.MsgID == PacketTypes.PlayerSpawn && housesBuilt)
            {
                // 获取玩家队伍
                int playerTeam = player.TPlayer.team;

                TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 重生，队伍: {playerTeam}");

                // 延迟0.5秒后传送到队伍小屋
                if (playerTeam == 1 || playerTeam == 3)
                {
                    // 初始化玩家队伍状态
                    if (!playerTeamStates.ContainsKey(player.Index))
                    {
                        playerTeamStates[player.Index] = new PlayerTeamState();
                    }

                    var state = playerTeamStates[player.Index];
                    state.LastTeam = playerTeam;
                    state.LastTeamChangeTime = DateTime.Now;

                    string teamName = playerTeam == 1 ? "红队" : "蓝队";
                    player.SendInfoMessage($"重生中，0.5秒后将传送到{teamName}小屋...");
                    TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 重生，将传送到{teamName}小屋");
                }
            }
        }

        // 游戏更新事件处理器 - 处理延迟传送、回城引导和记分栏
        private void OnGameUpdate(EventArgs args)
        {
            // 更新记分栏（每秒一次）
            scoreboardUpdateCounter++;
            if (scoreboardUpdateCounter >= SCOREBOARD_UPDATE_INTERVAL)
            {
                scoreboardUpdateCounter = 0;
                UpdateScoreboard();
            }

            if (!housesBuilt)
                return;

            // 清理小屋内的mob（每0.5秒一次，仅在游戏开始后）
            if (gameStarted)
            {
                mobClearCounter++;
                if (mobClearCounter >= MOB_CLEAR_INTERVAL)
                {
                    mobClearCounter = 0;
                    ClearMobsInHouses();
                }
            }

            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active)
                    continue;

                // === 处理队伍切换后的传送 ===
                if (playerTeamStates.ContainsKey(player.Index))
                {
                    var state = playerTeamStates[player.Index];

                    // 检查是否需要传送（队伍更改后 0.5 秒）
                    if (state.LastTeamChangeTime != DateTime.MinValue)
                    {
                        var timeSinceChange = (DateTime.Now - state.LastTeamChangeTime).TotalSeconds;

                        if (timeSinceChange >= 0.5)
                        {
                            // 执行传送
                            Point targetSpawn = Point.Zero;
                            string teamName = "";

                            if (state.LastTeam == 1) // 红队 → 左侧小屋
                            {
                                targetSpawn = leftHouseSpawn;
                                teamName = "红队";
                            }
                            else if (state.LastTeam == 3) // 蓝队 → 右侧小屋
                            {
                                targetSpawn = rightHouseSpawn;
                                teamName = "蓝队";
                            }

                            if (targetSpawn.X != -1 && targetSpawn.Y != -1)
                            {
                                player.Teleport(targetSpawn.X * 16, targetSpawn.Y * 16);
                                player.SendSuccessMessage($"已传送到{teamName}小屋！");
                                TShock.Log.ConsoleInfo($"[CCTG] 玩家 {player.Name} 传送到{teamName}小屋 ({targetSpawn.X}, {targetSpawn.Y})");
                            }

                            // 清除传送标记
                            state.LastTeamChangeTime = DateTime.MinValue;
                        }
                    }
                }

                // === 处理回城传送 ===
                if (playerRecallStates.ContainsKey(player.Index))
                {
                    var recallState = playerRecallStates[player.Index];

                // 第一阶段：等待原版传送发生
                if (recallState.WaitingForTeleport)
                {
                    var timeSinceItemUse = (DateTime.Now - recallState.LastItemUseTime).TotalSeconds;

                    if (timeSinceItemUse < 3.0)
                    {
                        float distance = Vector2.Distance(player.TPlayer.position, recallState.LastKnownPosition);

                        // 如果位置变化超过200像素，说明传送发生了（降低阈值以支持近距离回城）
                        if (distance > 200f)
                        {
                            TShock.Log.ConsoleInfo($"[CCTG] 检测到回城传送 (距离: {distance}), 0.5秒后传送到队伍小屋");

                            recallState.WaitingForTeleport = false;
                            recallState.WaitingToTeleportToTeamHouse = true;
                            recallState.TeleportDetectedTime = DateTime.Now;
                            continue;
                        }

                        recallState.LastKnownPosition = player.TPlayer.position;
                    }
                    else
                    {
                        TShock.Log.ConsoleInfo($"[CCTG] 等待传送超时，取消");
                        recallState.WaitingForTeleport = false;
                    }

                    continue;
                }

                // 第二阶段：等待 0.5 秒后传送到队伍小屋
                if (recallState.WaitingToTeleportToTeamHouse)
                {
                    var timeSinceTeleport = (DateTime.Now - recallState.TeleportDetectedTime).TotalSeconds;

                    if (timeSinceTeleport >= 0.5)
                    {
                        TeleportToTeamHouse(player, recallState);
                        recallState.WaitingToTeleportToTeamHouse = false;
                    }

                    continue;
                }
                } // 结束回城传送处理

                // === 处理越界检测 ===
                CheckBoundaryViolation(player);
            }
        }

        // 越界检测和惩罚
        private void CheckBoundaryViolation(TSPlayer player)
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

        // 传送到队伍小屋
        private void TeleportToTeamHouse(TSPlayer player, RecallTeleportState state)
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

        // 清理小屋内的mob
        private void ClearMobsInHouses()
        {
            if (protectedHouseAreas.Count == 0)
                return;

            int clearedCount = 0;

            // 遍历所有活跃的NPC
            for (int i = 0; i < Main.npc.Length; i++)
            {
                var npc = Main.npc[i];

                // 跳过无效、非活跃的NPC
                if (npc == null || !npc.active)
                    continue;

                // 跳过友好NPC（城镇NPC、宠物等）
                if (npc.friendly || npc.townNPC)
                    continue;

                // 获取NPC的图块坐标
                int npcTileX = (int)(npc.position.X / 16);
                int npcTileY = (int)(npc.position.Y / 16);

                // 检查NPC是否在任何一个房屋保护区域内
                foreach (var houseArea in protectedHouseAreas)
                {
                    if (houseArea.Contains(npcTileX, npcTileY))
                    {
                        // 清除NPC（不是杀死，而是直接移除）
                        npc.active = false;
                        npc.type = 0;

                        // 同步到所有客户端
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);

                        clearedCount++;
                        break; // 找到一个匹配的区域就够了
                    }
                }
            }

            // 如果清除了mob，记录日志
            if (clearedCount > 0)
            {
                TShock.Log.ConsoleInfo($"[CCTG] 清除了小屋内的 {clearedCount} 个mob");
            }
        }

        // 更新记分栏 - 显示距离下一个白天的时间
        private void UpdateScoreboard()
        {
            // 计算距离下一个白天（4:30 AM）的时间
            string timeString = CalculateTimeUntilDawn();

            // 发送给所有在线玩家
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                {
                    // 使用带颜色的格式化文本显示在屏幕上
                    player.SendData(PacketTypes.Status, timeString, 0, 0f, 0f, 0, 0);
                }
            }
        }

        // 计算距离黎明的时间
        private string CalculateTimeUntilDawn()
        {
            double ticksUntilDawn;

            if (Main.dayTime)
            {
                // 当前是白天，计算到晚上的时间 + 整个夜晚的时间
                double dayTicksRemaining = 54000 - Main.time; // 白天总共54000 ticks
                double nightTicks = 32400; // 夜晚总共32400 ticks
                ticksUntilDawn = dayTicksRemaining + nightTicks;
            }
            else
            {
                // 当前是夜晚，计算到黎明的剩余时间
                ticksUntilDawn = 32400 - Main.time; // 夜晚总共32400 ticks
            }

            // 转换 ticks 为实际时间（60 ticks = 1 秒）
            double secondsUntilDawn = ticksUntilDawn / 60.0;

            // 格式化时间显示
            int hours = (int)(secondsUntilDawn / 3600);
            int minutes = (int)((secondsUntilDawn % 3600) / 60);
            int seconds = (int)(secondsUntilDawn % 60);

            string timePhase = Main.dayTime ? "白天" : "夜晚";
            string currentGameTime = GetGameTimeString();

            // 返回格式化的记分栏文本
            return $"━━━━━━━━━━━━━━━━\n" +
                   $"  当前时间: {currentGameTime}\n" +
                   $"  时段: {timePhase}\n" +
                   $"  ▼ 距离黎明 ▼\n" +
                   $"  {hours:D2}:{minutes:D2}:{seconds:D2}\n" +
                   $"━━━━━━━━━━━━━━━━";
        }

        // 获取游戏内时间字符串（格式：HH:MM）
        private string GetGameTimeString()
        {
            double time = Main.time;

            if (Main.dayTime)
            {
                // 白天时间从 4:30 AM (0) 到 7:30 PM (54000)
                // 4:30 AM = 4.5 hours = 270 minutes
                double totalMinutes = 270 + (time / 54000.0) * (15 * 60); // 白天持续15小时
                int hours = ((int)(totalMinutes / 60)) % 24;
                int minutes = (int)(totalMinutes % 60);
                return $"{hours:D2}:{minutes:D2}";
            }
            else
            {
                // 夜晚时间从 7:30 PM (0) 到 4:30 AM (32400)
                // 7:30 PM = 19.5 hours = 1170 minutes
                double totalMinutes = 1170 + (time / 32400.0) * (9 * 60); // 夜晚持续9小时
                int hours = ((int)(totalMinutes / 60)) % 24;
                int minutes = (int)(totalMinutes % 60);
                return $"{hours:D2}:{minutes:D2}";
            }
        }
    }
}
