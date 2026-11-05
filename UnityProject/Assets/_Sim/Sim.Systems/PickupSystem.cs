using Sim.Core;

namespace Sim.Systems
{
    /// <summary>
    /// 掉落物拾取：玩家碰撞范围（半径 1.5 格，从玩家中身算）内的掉落物
    /// 自动吸收进背包（Terraria 式自动拾取），堆满则部分拾取留余量。
    ///
    /// 这是模拟系统而非命令 —— 拾取是位置接触的必然结果，无需玩家操作；
    /// 背包状态全部入 WorldHash（确定性/联机各端一致）。
    /// 执行顺序：PlayerMovement / ItemGravity 之后（用最新位置判定）。
    /// TODO(M2)：刚丢弃物品的短暂不可拾取窗口（防止丢出即捡回）。
    /// </summary>
    public sealed class PickupSystem : ISimSystem
    {
        /// <summary>拾取半径平方：1.5² = 2.25。</summary>
        private static readonly Fix RadiusSq = Fix.FromRaw(147456);

        private static readonly Fix PlayerCenterY = Fix.FromRaw(58982);  // 0.9（底边+中身）
        private static readonly Fix ItemCenterY = Fix.FromRaw(32768);    // 0.5（底边+中心）

        public string Name { get { return "Pickup"; } }

        public void Tick(Simulation sim)
        {
            var entities = sim.Entities.InDeterministicOrder();

            for (int i = 0; i < entities.Count; i++)
            {
                var player = entities[i];
                if (player.Kind != (ushort)EntityKind.Player) continue;

                Fix px = player.FixX;
                Fix py = player.FixY + PlayerCenterY;
                var inv = sim.Inventories.GetOrCreate(player.Id);

                for (int j = 0; j < entities.Count; j++)
                {
                    var item = entities[j];
                    if (item.Kind != (ushort)EntityKind.DroppedItem) continue;

                    Fix dx = item.FixX - px;
                    Fix dy = (item.FixY + ItemCenterY) - py;
                    if (dx * dx + dy * dy > RadiusSq) continue;

                    ushort count = item.Aux2 == 0 ? (ushort)1 : item.Aux2;
                    ushort leftover = inv.AddItem(item.Aux, count);
                    if (leftover == 0)
                    {
                        sim.Entities.Despawn(item.Id);
                    }
                    else if (leftover != count)
                    {
                        sim.Entities.SetAux2(item.Id, leftover);
                    }
                }
            }
        }
    }
}
