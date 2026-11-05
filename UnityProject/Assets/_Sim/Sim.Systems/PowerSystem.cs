using System.Collections.Generic;
using Sim.Core;

namespace Sim.Systems
{
    /// <summary>
    /// 电网结算（M2.3，GDD §2.4：连通图 + 总量平衡）。
    ///
    /// 每 tick：对注册的电力节点做 4 邻接 flood-fill 分解连通分量（电网），
    /// 每网整数瓦结算：Supply = Σ 可用发电机输出，Demand = Σ 有活跃加工任务的用电器功耗。
    /// Supply >= Demand → 正常供电；否则该网全体用电器断电（简单粗暴的全网暂停，
    /// 按比例降载/优先级留 M3+）。
    ///
    /// 发电机语义：
    /// - 燃煤机（FuelItemId != 0）：有燃料即输出；仅当该网 Demand > 0 时燃烧（待机不烧）；
    /// - 人力机（FuelItemId == 0）：任一 Player/Worker 实体紧邻（|dx|<=1 且 |dy|<=1）即输出。
    ///
    /// 遍历确定性：注册表 OrderedKeys（key 升序）起 BFS，邻居展开序固定（上/右/下/左）；
    /// 结果写入 PowerState（派生数据不入哈希）。执行顺序须在 CraftingSystem 之前。
    /// </summary>
    public sealed class PowerSystem : ISimSystem
    {
        /// <summary>4 邻接展开序（固定，确定性）。</summary>
        private static readonly int[] DxDy = { 0, 1, 0, -1, 1, 0, -1, 0 };

        public string Name { get { return "Power"; } }

        public void Tick(Simulation sim)
        {
            var state = sim.PowerState;
            state.Begin(sim.Tick);

            var keys = sim.Power.OrderedKeys();
            if (keys.Count == 0) return;

            var visited = new HashSet<long>();
            var queue = new Queue<long>();
            var component = new List<long>();

            foreach (long startKey in keys)
            {
                if (!visited.Add(startKey)) continue;

                // BFS 收集连通分量
                component.Clear();
                queue.Enqueue(startKey);
                visited.Add(startKey);
                while (queue.Count > 0)
                {
                    long key = queue.Dequeue();
                    component.Add(key);
                    GridPos cur = CraftingStore.Unpack(key);
                    for (int d = 0; d < 4; d++)
                    {
                        var next = new GridPos(cur.X + DxDy[d * 2], cur.Y + DxDy[d * 2 + 1]);
                        long nextKey = CraftingStore.Pack(next);
                        if (nextKey == key) continue;
                        if (visited.Contains(nextKey)) continue;
                        if (!sim.Power.ContainsKey(nextKey)) continue;
                        visited.Add(nextKey);
                        queue.Enqueue(nextKey);
                    }
                }

                // ---- 本网结算（整数瓦） ----
                int supply = 0, demand = 0;
                var fueledGens = new List<long>();
                var consumers = new List<long>();

                foreach (long key in component)
                {
                    PowerNode node;
                    if (!sim.Power.TryGetByKey(key, out node)) continue;
                    var rule = sim.TileRules[node.TileId];
                    var pos = CraftingStore.Unpack(key);

                    if (rule.PowerGenW > 0)
                    {
                        bool active;
                        if (rule.FuelItemId != 0)
                        {
                            active = node.FuelTicks > 0;
                            if (active) fueledGens.Add(key);
                        }
                        else
                        {
                            active = IsManned(sim, pos);
                        }
                        if (active) supply += rule.PowerGenW;
                    }

                    if (rule.PowerDrawW > 0)
                    {
                        consumers.Add(key);
                        // 只统计有活跃加工任务的用电器（闲置设备不耗电）
                        if (sim.Crafting.TryGet(pos, out _))
                        {
                            demand += rule.PowerDrawW;
                        }
                    }
                }

                bool powered = supply >= demand;

                // 有负荷才烧燃料：该网全部有料的燃煤机同步燃烧（负载跟随优化留后续）
                if (powered && demand > 0)
                {
                    for (int i = 0; i < fueledGens.Count; i++)
                    {
                        PowerNode gen;
                        sim.Power.TryGetByKey(fueledGens[i], out gen);
                        gen.FuelTicks--;
                        sim.Power.SetNodeByKey(fueledGens[i], gen);
                    }
                }

                foreach (long key in consumers)
                {
                    state.SetConsumerPowered(key, powered);
                }

                state.AddGrid(new PowerState.GridInfo
                {
                    SupplyW = supply,
                    DemandW = demand,
                    Powered = powered,
                    NodeCount = component.Count,
                    Anchor = CraftingStore.Unpack(component[0]),
                });
            }
        }

        /// <summary>人力发电机是否有人：任一 Player/Worker 实体所在格与发电机格 |dx|<=1 且 |dy|<=1。</summary>
        private static bool IsManned(Simulation sim, GridPos pos)
        {
            var entities = sim.Entities.InDeterministicOrder();
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                if (e.Kind != (ushort)EntityKind.Player && e.Kind != (ushort)EntityKind.Worker) continue;
                int dx = e.Pos.X - pos.X;
                int dy = e.Pos.Y - pos.Y;
                if (dx >= -1 && dx <= 1 && dy >= -1 && dy <= 1) return true;
            }
            return false;
        }
    }
}
