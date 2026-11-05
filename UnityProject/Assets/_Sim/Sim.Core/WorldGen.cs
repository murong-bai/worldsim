namespace Sim.Core
{
    /// <summary>
    /// M1 测试地图生成：用「种子派生的独立随机流」生成 2 chunk 宽的小地图
    /// （随机游走地表 + 泥土层 + 石层 + 稀疏树木）。
    /// 正式版主基地为固定手工地图（JSON，见 GDD §2.2 决策 3），本类仅服务垂直切片与测试。
    /// </summary>
    public static class WorldGen
    {
        public const int Left = -32;
        public const int Right = 31;

        public static void GenerateTerrain(Simulation sim, KnownTiles tiles)
        {
            // 独立派生流：地形生成不消耗运行期随机流 Rng
            var rng = new SimRandom(sim.Seed ^ 0x5EEDCA75UL);

            int ground = 0;
            for (int x = Left; x <= Right; x++)
            {
                ground += rng.NextInt(-1, 2);          // -1/0/+1 随机游走
                if (ground < -4) ground = -4;
                if (ground > 4) ground = 4;

                for (int y = ground; y >= ground - 2; y--)
                {
                    sim.World.SetTile(new GridPos(x, y), tiles.Dirt);
                }
                for (int y = ground - 3; y >= ground - 20; y--)
                {
                    sim.World.SetTile(new GridPos(x, y), tiles.Stone);
                }
                if (rng.NextInt(0, 100) < 15)
                {
                    sim.World.SetTile(new GridPos(x, ground + 1), tiles.Tree);
                }
            }
        }

        /// <summary>
        /// 在 x 列找到地表并生成默认玩家，返回实体 Id。
        /// M1 单人：出生在世界初始化阶段（确定性 setup，重放一致）；
        /// 联机版将改为「玩家加入时由命令生成」，此方法退役为测试/工具用途。
        /// </summary>
        public static int SpawnDefaultPlayer(Simulation sim, int x)
        {
            int y = 16;
            while (y > -20 && !sim.IsSolid(new GridPos(x, y - 1))) y--;
            return sim.Entities.Spawn((ushort)EntityKind.Player, new GridPos(x, y), 0, 0, 1);
        }
    }
}
