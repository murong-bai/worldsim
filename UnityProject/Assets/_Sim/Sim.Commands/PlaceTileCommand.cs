using Sim.Core;

namespace Sim.Commands
{
    /// <summary>放置方块。权威端验证：ID 合法 / 可放置 / 目标格为空 / 归属玩家射程内。</summary>
    public sealed class PlaceTileCommand : ISimCommand
    {
        public GridPos Pos;
        public ushort TileId;

        /// <summary>归属玩家实体（0 = 不校验，供测试/调试；联机与正式流程必须携带）。
        /// 权威端校验归属与射程（GDD §3.6 防作弊）。</summary>
        public int ByEntityId;

        public PlaceTileCommand() { }

        public PlaceTileCommand(GridPos pos, ushort tileId)
        {
            Pos = pos;
            TileId = tileId;
        }

        public string CommandType => "place_tile";

        public void Apply(Simulation sim)
        {
            // 校验顺序固定：ID 合法 → 可放置 → 目标格空 → 结构支撑 → 归属射程 → 材料消耗（不足整体拒绝）
            if (TileId == 0 || TileId >= sim.TileRules.Length) return;
            var rule = sim.TileRules[TileId];
            if (!rule.Placeable) return;
            if (sim.World.GetTile(Pos) != 0) return;

            // 结构支撑是物理规则，对 creative 路径（无归属）同样生效（GDD §2.2 防浮空城）
            if (rule.NeedsSupport && !StructuralSupport.IsSupported(sim, Pos)) return;

            if (ByEntityId != 0)
            {
                if (!sim.WithinReach(ByEntityId, Pos)) return;
                if (!sim.Entities.TryGet(ByEntityId, out var e)) return;
                if (e.Kind != (ushort)EntityKind.Player) return;
                if (rule.RequiredItemId != 0)
                {
                    // 权威端材料校验：扣除放置所需物品（原子操作，不足则拒绝）
                    int need = rule.RequiredItemCount < 1 ? 1 : rule.RequiredItemCount;
                    if (!sim.Inventories.GetOrCreate(ByEntityId).TryConsume(rule.RequiredItemId, (ushort)need)) return;
                }
            }

            sim.World.SetTile(Pos, TileId);
            // 支撑规则已在上方校验；放置只会增加承接/锚点，不会令既有结构失撑
        }
    }
}
