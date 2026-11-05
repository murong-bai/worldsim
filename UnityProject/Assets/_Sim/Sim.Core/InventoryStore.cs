using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>
    /// 单个背包：固定槽位（itemId, count）数组，堆叠上限 MaxStack。
    /// 全部方法为确定性整数运算；槽位扫描顺序恒为 0..SlotCount-1
    /// （先堆叠已有槽、再开新槽），保证双端背包状态位级一致。
    /// </summary>
    public sealed class Inventory
    {
        public const int SlotCount = 10;
        public const ushort MaxStack = 50;

        public readonly ushort[] ItemIds = new ushort[SlotCount];
        public readonly ushort[] Counts = new ushort[SlotCount];

        /// <summary>添加物品（先堆叠已有槽，再开新槽）。返回放不下的余量（0 = 全部收纳）。</summary>
        public ushort AddItem(ushort itemId, ushort count)
        {
            for (int i = 0; i < SlotCount && count > 0; i++)
            {
                if (ItemIds[i] == itemId && Counts[i] > 0)
                {
                    ushort space = (ushort)(MaxStack - Counts[i]);
                    ushort take = count < space ? count : space;
                    if (take > 0)
                    {
                        Counts[i] += take;
                        count -= take;
                    }
                }
            }
            for (int i = 0; i < SlotCount && count > 0; i++)
            {
                if (Counts[i] == 0)
                {
                    ushort take = count < MaxStack ? count : MaxStack;
                    ItemIds[i] = itemId;
                    Counts[i] = take;
                    count -= take;
                }
            }
            return count;
        }

        /// <summary>原子消耗：数量不足时不做任何改动并返回 false（供放置命令权威校验）。</summary>
        public bool TryConsume(ushort itemId, ushort count)
        {
            if (CountOf(itemId) < count) return false;
            int remaining = count;
            for (int i = 0; i < SlotCount && remaining > 0; i++)
            {
                if (ItemIds[i] == itemId && Counts[i] > 0)
                {
                    int take = Counts[i] < remaining ? Counts[i] : remaining;
                    Counts[i] -= (ushort)take;
                    remaining -= take;
                    if (Counts[i] == 0) ItemIds[i] = 0;
                }
            }
            return true;
        }

        public int CountOf(ushort itemId)
        {
            int total = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (ItemIds[i] == itemId) total += Counts[i];
            }
            return total;
        }
    }

    /// <summary>
    /// 背包仓库：按实体 Id 挂载背包。拾取（系统写入）与放置消耗（命令权威校验）
    /// 都走本仓库 —— 属于模拟状态，全部纳入 WorldHash（确定性/回放覆盖）。
    /// 遍历与哈希必须按键排序（GDD §3.3）。
    /// </summary>
    public sealed class InventoryStore
    {
        private readonly Dictionary<int, Inventory> _inventories = new Dictionary<int, Inventory>();

        public Inventory GetOrCreate(int entityId)
        {
            Inventory inv;
            if (!_inventories.TryGetValue(entityId, out inv))
            {
                inv = new Inventory();
                _inventories[entityId] = inv;
            }
            return inv;
        }

        public bool TryGet(int entityId, out Inventory inventory)
        {
            return _inventories.TryGetValue(entityId, out inventory);
        }

        public void HashInto(ref Fnv64 h)
        {
            var ids = new List<int>(_inventories.Keys);
            ids.Sort();
            h.Update((ulong)ids.Count);
            foreach (int id in ids)
            {
                h.Update(id);
                var inv = _inventories[id];
                for (int i = 0; i < Inventory.SlotCount; i++)
                {
                    h.Update(inv.ItemIds[i]);
                    h.Update(inv.Counts[i]);
                }
            }
        }
    }
}
