using Sim.Core;

namespace Sim.Commands
{
    /// <summary>挖掘方块：置为空气，按规则掉落物品（作物仅在成熟时产出）。</summary>
    public sealed class DigTileCommand : ISimCommand
    {
        public GridPos Pos;

        /// <summary>归属玩家实体（0 = 不校验，供测试/调试；联机与正式流程必须携带）。
        /// 权威端校验归属与射程（GDD §3.6 防作弊）。</summary>
        public int ByEntityId;

        public DigTileCommand() { }

        public DigTileCommand(GridPos pos)
        {
            Pos = pos;
        }

        public string CommandType => "dig_tile";

        public void Apply(Simulation sim)
        {
            if (ByEntityId != 0 && !sim.WithinReach(ByEntityId, Pos)) return;

            ushort tile = sim.World.GetTile(Pos);
            if (tile == 0 || tile >= sim.TileRules.Length) return;

            var rule = sim.TileRules[tile];
            byte state = sim.World.GetState(Pos);
            sim.World.SetTile(Pos, 0);

            // 挖掉工作台：进行中的加工任务作废（材料是否返还待 M2 决策）
            sim.Crafting.TryCancel(Pos);

            // 挖掉容器（箱子）：内容物全部弹出掉落物（M2.2）
            var contents = sim.Containers.Remove(Pos);

            // 挖掉电力节点（电线/发电机/用电器）：注销注册（M2.3，拆网断电的物理来源）
            sim.Power.Remove(Pos);
            if (contents != null)
            {
                for (int i = 0; i < Inventory.SlotCount; i++)
                {
                    if (contents.ItemIds[i] != 0 && contents.Counts[i] > 0)
                    {
                        sim.Entities.Spawn((ushort)EntityKind.DroppedItem, Pos,
                            contents.ItemIds[i], contents.Counts[i], 0);
                    }
                }
            }

            if (rule.GrowthTicks > 0)
            {
                // 作物：成熟收获 = 产物×2 + 种子×1（种子 = placeItem，可持续种植）；
                // 幼苗被铲除无产出（可后续改为返还种子）
                if (state >= rule.GrowthTicks)
                {
                    if (rule.HarvestItemId != 0)
                    {
                        sim.Entities.Spawn((ushort)EntityKind.DroppedItem, Pos, rule.HarvestItemId, 2, 0);
                    }
                    if (rule.RequiredItemId != 0)
                    {
                        sim.Entities.Spawn((ushort)EntityKind.DroppedItem, Pos, rule.RequiredItemId, 1, 0);
                    }
                }
                return;
            }

            if (rule.DropItemId != 0)
            {
                sim.Entities.Spawn((ushort)EntityKind.DroppedItem, Pos, rule.DropItemId, 1, 0);
            }

            // 结构级联：被移除方块的邻近平台可能失撑塌落（GDD §2.2）。
            // 塌落走 World.SetTile → 自动进入 WorldChanges（渲染/联机增量同步）。
            StructuralSupport.OnTileRemoved(sim, Pos);
        }
    }
}
