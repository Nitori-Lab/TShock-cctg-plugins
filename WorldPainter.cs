using System;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// 世界涂色管理器
    /// </summary>
    public class WorldPainter
    {
        /// <summary>
        /// 涂色整个世界
        /// </summary>
        public void PaintWorld()
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

        /// <summary>
        /// 涂色一整列（X坐标固定，遍历所有Y）
        /// </summary>
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
    }
}
