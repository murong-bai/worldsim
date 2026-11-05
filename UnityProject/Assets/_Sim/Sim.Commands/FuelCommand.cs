using Sim.Core;

namespace Sim.Commands
{
    /// <summary>
    /// 往燃煤发电机加一份燃料（M2.3）：从归属角色背包消耗 1 个燃料物品，
    /// 燃料剩余 += FuelTicksPerItem（上限 3 份，防囤积）。
    /// 校验顺序固定：归属必须携带（材料命令无 creative 路径）→ 射程 → 目标是燃煤机 → 未达上限 → 材料原子消耗。
    /// </summary>
    public sealed class FuelCommand : ISimCommand
    {
        public GridPos Pos;
        public int ByEntityId;

        public FuelCommand() { }

        public FuelCommand(GridPos pos, int byEntityId)
        {
            Pos = pos;
            ByEntityId = byEntityId;
        }

        public string CommandType => "fuel";

        public void Apply(Simulation sim)
        {
            if (ByEntityId == 0) return;
            if (!sim.IsActor(ByEntityId)) return;
            if (!sim.WithinReach(ByEntityId, Pos)) return;

            ushort tile = sim.World.GetTile(Pos);
            if (tile == 0 || tile >= sim.TileRules.Length) return;
            var rule = sim.TileRules[tile];
            if (rule.PowerGenW <= 0 || rule.FuelItemId == 0) return;

            PowerNode node;
            if (!sim.Power.TryGet(Pos, out node)) return;

            int per = rule.FuelTicksPerItem < 1 ? 1 : rule.FuelTicksPerItem;
            int cap = per * 3;
            if (node.FuelTicks >= cap) return;

            if (!sim.Inventories.GetOrCreate(ByEntityId).TryConsume(rule.FuelItemId, 1)) return;

            node.FuelTicks = (node.FuelTicks + per) > cap ? cap : node.FuelTicks + per;
            sim.Power.SetNode(Pos, node);
        }
    }
}
