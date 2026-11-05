using Sim.Core;

namespace Sim.Commands
{
    /// <summary>
    /// 存入容器：把实体（玩家/Worker）背包内全部物品移入指定箱格（M2.2）。
    /// 箱满则部分存入，余量留在实体背包。权威端校验：角色身份、射程、该格是容器。
    /// 玩家用 V 键触发（客户端找最近箱子）；JobSystem 用它完成搬运任务的「入箱」环节。
    /// </summary>
    public sealed class DepositCommand : ISimCommand
    {
        public GridPos Pos;
        public int ByEntityId;

        public DepositCommand() { }

        public DepositCommand(GridPos pos, int byEntityId)
        {
            Pos = pos;
            ByEntityId = byEntityId;
        }

        public string CommandType => "deposit";

        public void Apply(Simulation sim)
        {
            if (ByEntityId == 0) return;
            if (!sim.Entities.TryGet(ByEntityId, out var e)) return;
            if (e.Kind != (ushort)EntityKind.Player && e.Kind != (ushort)EntityKind.Worker) return;
            if (!sim.WithinReach(ByEntityId, Pos)) return;

            ushort tile = sim.World.GetTile(Pos);
            if (tile == 0 || tile >= sim.TileRules.Length) return;
            if (!sim.TileRules[tile].IsContainer) return;

            var chest = sim.Containers.GetOrCreate(Pos);
            var inv = sim.Inventories.TryGet(ByEntityId, out var source) ? source : null;
            if (inv == null) return;

            // 按槽位序（0..SlotCount-1）转移：确定性的整背包倾倒
            for (int i = 0; i < Inventory.SlotCount; i++)
            {
                ushort itemId = inv.ItemIds[i];
                ushort count = inv.Counts[i];
                if (itemId == 0 || count == 0) continue;

                ushort leftover = chest.AddItem(itemId, count);
                if (leftover == 0)
                {
                    inv.ItemIds[i] = 0;
                    inv.Counts[i] = 0;
                }
                else
                {
                    inv.Counts[i] = leftover;
                }
            }
        }
    }
}
