using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>
    /// 世界变更日志：记录本 tick 内被 SetTile / SetState 触碰过的格子。
    /// 同一份数据服务两个消费者（GDD §3.6）：
    /// 1. 表现层增量渲染（WorldTilemapRenderer.SyncTick）；
    /// 2. 联机增量广播（M3 网络层直接下发本列表 + 新状态）。
    /// 纯记录：不影响模拟状态、不参与 WorldHash、不做去重（消费端幂等）。
    /// </summary>
    public sealed class WorldChanges
    {
        private readonly List<GridPos> _positions = new List<GridPos>();

        public IReadOnlyList<GridPos> Positions { get { return _positions; } }

        public void Mark(GridPos pos)
        {
            _positions.Add(pos);
        }

        /// <summary>由 Simulation.Step 在每个逻辑帧开始时调用（清空上一 tick 记录）。</summary>
        public void BeginTick()
        {
            if (_positions.Count > 0) _positions.Clear();
        }
    }
}
