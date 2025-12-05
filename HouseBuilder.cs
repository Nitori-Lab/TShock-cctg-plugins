using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// 房屋建造管理器
    /// </summary>
    public class HouseBuilder
    {
        // 房屋保护区域
        private List<Rectangle> protectedHouseAreas = new List<Rectangle>();

        // 左右小屋的位置（用于队伍传送）
        private Point leftHouseSpawn = new Point(-1, -1);
        private Point rightHouseSpawn = new Point(-1, -1);

        // 房屋建造状态
        private bool housesBuilt = false;

        // 随机数生成器
        private Random _random = new Random();

        // 属性访问器
        public List<Rectangle> ProtectedHouseAreas => protectedHouseAreas;
        public Point LeftHouseSpawn => leftHouseSpawn;
        public Point RightHouseSpawn => rightHouseSpawn;
        public bool HousesBuilt => housesBuilt;

        /// <summary>
        /// 建造左右两侧房屋
        /// </summary>
        public void BuildHouses()
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

        /// <summary>
        /// 清除所有房屋
        /// </summary>
        public void ClearHouses()
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

        /// <summary>
        /// 清除小屋内的MOB
        /// </summary>
        public void ClearMobsInHouses()
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

        // 建造单个房屋 - 5x11 和 10x7 组合
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

            // === 建造左房间（5x11）===
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

            // === 建造右房间（10x7）===
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
    }
}
