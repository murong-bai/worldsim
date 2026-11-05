using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>
    /// 容器仓库（M2.2）：箱子格子坐标 → Inventory。
    /// 结构与 CraftingStore 同构：long 打包坐标 key（复用 Pack/Unpack）、
    /// OrderedKeys 排序遍历、全部内容入 WorldHash。
    /// deposit 命令（玩家/Worker 存入）与 JobSystem（小人自动入箱）共用本仓库；
    /// 挖掉箱子时内容物全部掉出（DigTileCommand 调 Remove 并弹出物品）。
    /// </summary>
    public sealed class ContainerStore
    {
        private readonly Dictionary<long, Inventory> _containers = new Dictionary<long, Inventory>();

        public int Count => _containers.Count;

        public Inventory GetOrCreate(GridPos pos)
        {
            long key = CraftingStore.Pack(pos);
            Inventory inv;
            if (!_containers.TryGetValue(key, out inv))
            {
                inv = new Inventory();
                _containers[key] = inv;
            }
            return inv;
        }

        public bool TryGet(GridPos pos, out Inventory inventory)
        {
            return _containers.TryGetValue(CraftingStore.Pack(pos), out inventory);
        }

        /// <summary>移除容器并返回其内容（挖箱子时弹出掉落物用）。无容器返回 null。</summary>
        public Inventory Remove(GridPos pos)
        {
            long key = CraftingStore.Pack(pos);
            Inventory inv;
            if (!_containers.TryGetValue(key, out inv)) return null;
            _containers.Remove(key);
            return inv;
        }

        /// <summary>按 Key 升序的容器键列表（确定性迭代）。</summary>
        public List<long> OrderedKeys()
        {
            var keys = new List<long>(_containers.Keys);
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
                var inv = _containers[key];
                for (int i = 0; i < Inventory.SlotCount; i++)
                {
                    h.Update(inv.ItemIds[i]);
                    h.Update(inv.Counts[i]);
                }
            }
        }
    }
}
