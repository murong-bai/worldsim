using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>
    /// 确定性 A* 网格寻路（M2.2，GDD §2.5）。
    ///
    /// 节点 = 「可栖息格」：自身与头顶一格非固体，且（下方固体/平台承接 或 本格是梯子）。
    /// 边（Worker 移动能力 = 玩家子集，GDD §2.3 决议 #6，无跳跃）：
    /// - 平走：同行相邻格（cost 10）；
    /// - 跨上 1 格台阶：相邻列上方格，要求台阶实心、头顶净空（cost 20）；
    /// - 跨下 1 格台阶：相邻列下方格（cost 12）；
    /// - 梯子垂直：本格与目标格都是梯子（cost 10）。
    ///
    /// 确定性：open 集用线性扫描选优 —— 比较 (f, h, packed key) 的固定字典序，
    /// 任意实现收敛到同一条路径；maxNodes 防御异常世界的搜索爆炸。
    /// 启发 = 曼哈顿 × 10（边 cost ≥ 10，可采纳）。
    /// </summary>
    public static class Pathfinding
    {
        private const int CostWalk = 10;
        private const int CostStepUp = 20;
        private const int CostStepDown = 12;
        private const int MaxNodesDefault = 4096;

        /// <summary>p 是否为可栖息格（脚站固体/平台，或身在梯中；身高 1.8 需 2 格净空）。</summary>
        public static bool IsStandable(Simulation sim, GridPos p)
        {
            if (sim.IsSolid(p)) return false;
            if (sim.IsSolid(new GridPos(p.X, p.Y + 1))) return false;
            if (sim.TileRules[sim.World.GetTile(p)].IsLadder) return true;
            var below = sim.TileRules[sim.World.GetTile(new GridPos(p.X, p.Y - 1))];
            return below.Solid || below.BlocksItems;
        }

        /// <summary>
        /// A* 搜索 from → to。to 不可栖息或找不到路径返回 null；同点返回空表。
        /// 返回路径点列表（不含 from，含终点节点）。
        /// </summary>
        public static List<GridPos> FindPath(Simulation sim, GridPos from, GridPos to, int maxNodes = MaxNodesDefault)
        {
            return FindPathNear(sim, from, to, 0, maxNodes);
        }

        /// <summary>
        /// A* 搜索到 to 的「附近」：曼哈顿距离 ≤ nearRange 即视为到达
        /// （用于目标本身是固体格的挖掘/存箱：小人只需走到射程内，无需站上目标格）。
        /// nearRange=0 退化为精确到点（to 须可栖息）。
        /// </summary>
        public static List<GridPos> FindPathNear(Simulation sim, GridPos from, GridPos to, int nearRange, int maxNodes = MaxNodesDefault)
        {
            if (from.X == to.X && from.Y == to.Y) return new List<GridPos>();
            if (nearRange <= 0 && !IsStandable(sim, to)) return null;

            var gScore = new Dictionary<long, int>();
            var cameFrom = new Dictionary<long, long>();
            var open = new List<Node>();

            long fromKey = CraftingStore.Pack(from);
            long toKey = CraftingStore.Pack(to);
            gScore[fromKey] = 0;
            open.Add(new Node(fromKey, 0, Heuristic(from, to, nearRange)));

            int expanded = 0;
            while (open.Count > 0)
            {
                // 固定序取最优：f 升序 → h 升序 → key 升序（确定性 tie-break）
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                {
                    if (Compare(open[i], open[best]) < 0) best = i;
                }
                Node current = open[best];
                open.RemoveAt(best);

                GridPos cur = CraftingStore.Unpack(current.Key);
                if (Manhattan(cur, to) <= nearRange)
                {
                    return Reconstruct(cameFrom, current.Key);
                }

                if (++expanded > maxNodes) return null; // 防御断路

                int curG = gScore[current.Key];

                // 八方向邻居 + 梯子垂直（cost=0 标记梯子边，TryNeighbor 内校验两端皆梯）
                TryNeighbor(sim, gScore, cameFrom, open, cur, to, nearRange, new GridPos(cur.X + 1, cur.Y), CostWalk);
                TryNeighbor(sim, gScore, cameFrom, open, cur, to, nearRange, new GridPos(cur.X - 1, cur.Y), CostWalk);
                TryNeighbor(sim, gScore, cameFrom, open, cur, to, nearRange, new GridPos(cur.X, cur.Y + 1), 0);
                TryNeighbor(sim, gScore, cameFrom, open, cur, to, nearRange, new GridPos(cur.X, cur.Y - 1), 0);
                TryNeighbor(sim, gScore, cameFrom, open, cur, to, nearRange, new GridPos(cur.X + 1, cur.Y + 1), CostStepUp);
                TryNeighbor(sim, gScore, cameFrom, open, cur, to, nearRange, new GridPos(cur.X - 1, cur.Y + 1), CostStepUp);
                TryNeighbor(sim, gScore, cameFrom, open, cur, to, nearRange, new GridPos(cur.X + 1, cur.Y - 1), CostStepDown);
                TryNeighbor(sim, gScore, cameFrom, open, cur, to, nearRange, new GridPos(cur.X - 1, cur.Y - 1), CostStepDown);
            }
            return null;
        }

        private static int Manhattan(GridPos a, GridPos b)
        {
            int dx = a.X > b.X ? a.X - b.X : b.X - a.X;
            int dy = a.Y > b.Y ? a.Y - b.Y : b.Y - a.Y;
            return dx + dy;
        }

        private static int Heuristic(GridPos a, GridPos b, int nearRange)
        {
            int dist = Manhattan(a, b) - nearRange;
            return (dist > 0 ? dist : 0) * CostWalk;
        }

        /// <summary>尝试松弛一条边。baseCost=0 表示梯子垂直边（校验两端都是梯子，按 CostWalk 计价）。</summary>
        private static void TryNeighbor(Simulation sim,
            Dictionary<long, int> gScore, Dictionary<long, long> cameFrom, List<Node> open,
            GridPos cur, GridPos to, int nearRange, GridPos next, int baseCost)
        {
            long curKey = CraftingStore.Pack(cur);
            long nextKey = CraftingStore.Pack(next);
            if (nextKey == curKey) return;

            bool ladderEdge = baseCost == 0;
            if (ladderEdge)
            {
                if (!sim.TileRules[sim.World.GetTile(cur)].IsLadder) return;
                if (!sim.TileRules[sim.World.GetTile(next)].IsLadder) return;
            }
            else if (next.Y == cur.Y + 1)
            {
                // 跨上 1 格台阶：台阶块实心（1 格高墙）且当前列头顶净空
                if (!sim.IsSolid(new GridPos(next.X, cur.Y))) return;
                if (sim.IsSolid(new GridPos(cur.X, cur.Y + 2))) return;
            }

            if (!IsStandable(sim, next)) return;

            int cost = ladderEdge ? CostWalk : baseCost;
            int newG = gScore[curKey] + cost;
            int oldG;
            if (gScore.TryGetValue(nextKey, out oldG) && oldG <= newG) return;

            gScore[nextKey] = newG;
            cameFrom[nextKey] = curKey;
            open.Add(new Node(nextKey, newG, Heuristic(next, to, nearRange)));
        }

        private static int Compare(Node a, Node b)
        {
            int fa = a.G + a.H, fb = b.G + b.H;
            if (fa != fb) return fa < fb ? -1 : 1;
            if (a.H != b.H) return a.H < b.H ? -1 : 1;
            return a.Key < b.Key ? -1 : (a.Key > b.Key ? 1 : 0);
        }

        private static List<GridPos> Reconstruct(Dictionary<long, long> cameFrom, long endKey)
        {
            var path = new List<GridPos>();
            long key = endKey;
            long prev;
            while (cameFrom.TryGetValue(key, out prev))
            {
                path.Add(CraftingStore.Unpack(key));
                key = prev;
            }
            path.Reverse();
            return path;
        }

        private struct Node
        {
            public long Key;
            public int G;
            public int H;

            public Node(long key, int g, int h)
            {
                Key = key;
                G = g;
                H = h;
            }
        }
    }
}
