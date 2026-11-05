using Sim.Core;

namespace Sim.Systems
{
    /// <summary>
    /// 掉落物重力：每 IntervalTicks 下落一格，被「阻挡掉落物」的格子截停（固体/平台）。
    /// M1 简化模型：实体间不碰撞；后续接入物流系统（容器/传送带拾取）。
    /// </summary>
    public sealed class ItemGravitySystem : ISimSystem
    {
        public const int IntervalTicks = 3;

        public string Name { get { return "ItemGravity"; } }

        public void Tick(Simulation sim)
        {
            if (sim.Tick % IntervalTicks != 0) return;

            var entities = sim.Entities.InDeterministicOrder();
            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (entity.Kind != (ushort)EntityKind.DroppedItem) continue;

                var below = new GridPos(entity.Pos.X, entity.Pos.Y - 1);
                if (!sim.BlocksItems(below))
                {
                    sim.Entities.SetPos(entity.Id, below);
                }
            }
        }
    }
}
