using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>电网节点（注册表条目）。电线无状态；发电机带燃料剩余。</summary>
    public struct PowerNode
    {
        public ushort TileId;   // 注册时的方块（防止挖后残留误判）
        public int FuelTicks;   // 燃料剩余 tick（燃煤机燃烧中递减；人力机/电线恒 0）
    }

    /// <summary>
    /// 电力节点注册表（M2.3）：电网格坐标 → PowerNode。
    /// 结构与 ContainerStore 同构：long 打包坐标 key、OrderedKeys 排序遍历、全部入 WorldHash。
    /// 电线/发电机/用电器放置即注册（PlaceTileCommand），挖除注销（DigTileCommand）；
    /// PowerSystem 据此做 4 邻接 flood-fill 分解电网（GDD §2.4：连通图 + 总量平衡）。
    /// </summary>
    public sealed class PowerStore
    {
        private readonly Dictionary<long, PowerNode> _nodes = new Dictionary<long, PowerNode>();

        public int Count => _nodes.Count;

        /// <summary>注册电力节点（放置时调用；目标格必为空，新节点燃料为 0）。</summary>
        public void Register(GridPos pos, ushort tileId)
        {
            _nodes[CraftingStore.Pack(pos)] = new PowerNode { TileId = tileId, FuelTicks = 0 };
        }

        public bool TryGet(GridPos pos, out PowerNode node)
        {
            return _nodes.TryGetValue(CraftingStore.Pack(pos), out node);
        }

        /// <summary>按 key 直取（PowerSystem flood-fill 用，避免重复打包）。</summary>
        public bool TryGetByKey(long key, out PowerNode node)
        {
            return _nodes.TryGetValue(key, out node);
        }

        public bool ContainsKey(long key) => _nodes.ContainsKey(key);

        public void SetNode(GridPos pos, PowerNode node)
        {
            _nodes[CraftingStore.Pack(pos)] = node;
        }

        public void SetNodeByKey(long key, PowerNode node)
        {
            _nodes[key] = node;
        }

        /// <summary>注销节点（挖除时调用），返回被移除的节点。</summary>
        public bool Remove(GridPos pos)
        {
            return _nodes.Remove(CraftingStore.Pack(pos));
        }

        /// <summary>按 Key 升序的节点键列表（确定性迭代）。</summary>
        public List<long> OrderedKeys()
        {
            var keys = new List<long>(_nodes.Keys);
            keys.Sort();
            return keys;
        }

        public void HashInto(ref Fnv64 h)
        {
            var keys = OrderedKeys();
            h.Update((ulong)keys.Count);
            foreach (long key in keys)
            {
                h.Update((ulong)key);
                var node = _nodes[key];
                h.Update(node.TileId);
                h.Update((ulong)node.FuelTicks);
            }
        }
    }

    /// <summary>
    /// 每 tick 电网结算结果（派生数据，**不入 WorldHash**——可由世界+注册表+实体位置确定性重算）。
    /// PowerSystem 每 tick 重建；CraftingSystem 查询断电；客户端查询面板展示。
    /// </summary>
    public sealed class PowerState
    {
        public struct GridInfo
        {
            public int SupplyW;    // 该网发电功率合计
            public int DemandW;    // 该网活跃用电器耗电合计
            public bool Powered;   // SupplyW >= DemandW
            public int NodeCount;  // 节点数（含电线）
            public GridPos Anchor; // 最小 key 节点位置（展示锚点）
        }

        private readonly Dictionary<long, bool> _consumerPowered = new Dictionary<long, bool>();
        private readonly List<GridInfo> _grids = new List<GridInfo>();

        public long Tick { get; private set; }
        public IReadOnlyList<GridInfo> Grids => _grids;

        public void Begin(long tick)
        {
            Tick = tick;
            _consumerPowered.Clear();
            _grids.Clear();
        }

        public void SetConsumerPowered(long key, bool powered)
        {
            _consumerPowered[key] = powered;
        }

        public void AddGrid(GridInfo info)
        {
            _grids.Add(info);
        }

        /// <summary>
        /// 指定格的用电器本 tick 是否有电。
        /// 未结算（本 tick 无 PowerSystem，旧测试路径）或非注册用电器 → true（宽松缺省，
        /// 断电惩罚只施加给明确登记且结算为断电的用电器）。
        /// </summary>
        public bool IsPoweredAt(Simulation sim, GridPos pos)
        {
            if (Tick != sim.Tick) return true;
            bool powered;
            return _consumerPowered.TryGetValue(CraftingStore.Pack(pos), out powered) ? powered : true;
        }
    }
}
