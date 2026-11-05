using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>
    /// 结构支撑规则（GDD §2.2：平台每 N 格需支撑柱或结构连接，防「浮空城」）。
    ///
    /// 模型（M1）：
    /// - 受支撑 = 从该格出发沿平台邻接图存在路径到达「锚点」，且路径**水平步数**
    ///   ≤ MaxHorizontalSpan；
    /// - 锚点 = 正下方是固体方块的 platform（含拟放置格自身）；
    /// - 邻接：左右 platform（水平步 +1）、下方 platform（垂直传递不计水平步 ——
    ///   垂直堆叠塔不受跨度限制，本作空间核心玩法）；
    /// - 仅 NeedsSupport 方块（platform 类）受限；solid 柱/方块自由放置且可作锚
    ///   （M2 再考虑 solid 方块的连接规则）。
    ///
    /// 使用点：
    /// - PlaceTileCommand：放置校验（材料消耗之前，支撑不足整体拒绝、不扣料）；
    /// - DigTileCommand：挖掘后级联 —— 受影响 platform 逐个重判，失撑塌落为
    ///   掉落物（与放置消耗等值回收），链式传播直到稳定。
    ///   塌落走 World.SetTile → 自动进入 WorldChanges（渲染/联机增量同步免费获得）。
    ///
    /// 确定性（GDD §3.3）：
    /// - IsSupported 是路径存在性判定，结果与 BFS 遍历顺序无关；
    /// - 级联队列按固定顺序入队（上 → 左 → 右），塌落掉落物生成序确定；
    /// - BFS 带步数防御上限，异常世界（超长悬链）不会无限搜索。
    /// </summary>
    public static class StructuralSupport
    {
        /// <summary>BFS 防御上限：访问节点数超过此值按「无支撑」处理。</summary>
        private const int MaxBfsNodes = 4096;

        /// <summary>pos 处的 platform（或拟放置 platform）是否受支撑。纯只读，不改世界。</summary>
        public static bool IsSupported(Simulation sim, GridPos pos)
        {
            var visited = new HashSet<long>();
            var queue = new List<BfsNode> { new BfsNode(pos, 0) };
            visited.Add(CraftingStore.Pack(pos));

            int head = 0;
            while (head < queue.Count)
            {
                var node = queue[head];
                head++;

                // 锚点：正下方是固体（地面/立柱/石块）
                if (sim.IsSolid(new GridPos(node.P.X, node.P.Y - 1))) return true;

                if (queue.Count > MaxBfsNodes) return false; // 防御断路

                // 垂直向下：下方是 platform → 继续传递（不计水平步，支持高塔）
                EnqueuePlatform(sim, queue, visited, node.P.X, node.P.Y - 1, node.H);
                // 水平：左右 platform，水平步 +1，受跨度约束
                if (node.H < sim.Global.MaxHorizontalSpan)
                {
                    EnqueuePlatform(sim, queue, visited, node.P.X - 1, node.P.Y, node.H + 1);
                    EnqueuePlatform(sim, queue, visited, node.P.X + 1, node.P.Y, node.H + 1);
                }
            }
            return false;
        }

        /// <summary>
        /// 方块被移除（挖掘等）后的级联检查：受影响的 platform 逐个重判，
        /// 失撑塌落为掉落物；塌落会破坏更多链路 → 邻居继续入队，直到稳定。
        /// </summary>
        public static void OnTileRemoved(Simulation sim, GridPos removed)
        {
            // 首批受影响格：被移除格的上方（垂直承接）与左右（水平链），固定顺序
            var queue = new List<GridPos>();
            EnqueueAffected(sim, queue, removed.X, removed.Y + 1);
            EnqueueAffected(sim, queue, removed.X - 1, removed.Y);
            EnqueueAffected(sim, queue, removed.X + 1, removed.Y);

            int head = 0;
            while (head < queue.Count)
            {
                var p = queue[head];
                head++;

                ushort tile = sim.World.GetTile(p);
                if (tile == 0 || tile >= sim.TileRules.Length) continue; // 已塌/非受限
                var rule = sim.TileRules[tile];
                if (!rule.NeedsSupport) continue;
                if (IsSupported(sim, p)) continue;

                // 失撑塌落：移除 + 等值回收掉落物（DropItemId x1）
                sim.World.SetTile(p, 0);
                if (rule.DropItemId != 0)
                {
                    sim.Entities.Spawn((ushort)EntityKind.DroppedItem, p, rule.DropItemId, 1, 0);
                }

                // 邻居可能连锁失撑（固定顺序：上 → 左 → 右）
                EnqueueAffected(sim, queue, p.X, p.Y + 1);
                EnqueueAffected(sim, queue, p.X - 1, p.Y);
                EnqueueAffected(sim, queue, p.X + 1, p.Y);
            }
        }

        private static void EnqueuePlatform(Simulation sim, List<BfsNode> queue,
            HashSet<long> visited, int x, int y, int h)
        {
            var p = new GridPos(x, y);
            long key = CraftingStore.Pack(p);
            if (visited.Contains(key)) return;

            ushort tile = sim.World.GetTile(p);
            if (tile == 0 || tile >= sim.TileRules.Length) return;
            if (!sim.TileRules[tile].NeedsSupport) return; // 只沿受限平台传播

            visited.Add(key);
            queue.Add(new BfsNode(p, h));
        }

        private static void EnqueueAffected(Simulation sim, List<GridPos> queue, int x, int y)
        {
            var p = new GridPos(x, y);
            ushort tile = sim.World.GetTile(p);
            if (tile == 0 || tile >= sim.TileRules.Length) return;
            if (!sim.TileRules[tile].NeedsSupport) return;
            if (queue.Contains(p)) return; // 队列规模小，线性去重足够
            queue.Add(p);
        }

        private struct BfsNode
        {
            public GridPos P;
            public int H; // 已用水平步数

            public BfsNode(GridPos p, int h)
            {
                P = p;
                H = h;
            }
        }
    }
}
