namespace Sim.Core
{
    /// <summary>
    /// 方块规则（数据驱动，由 Sim.Data 从 JSON 装配后注入 Simulation）。
    /// 模拟核心只认数字 ID 与本结构，不解析任何文本配置 —— 保证核心纯 C# 可独立测试。
    /// </summary>
    public readonly struct TileRule
    {
        public readonly bool Solid;            // 阻挡移动
        public readonly bool Placeable;        // 玩家可放置
        public readonly bool BlocksItems;      // 掉落物会停留（含平台类）
        public readonly ushort DropItemId;     // 被挖掘时掉落（0=无）
        public readonly ushort HarvestItemId;  // 作物成熟收获（0=非作物）
        public readonly int GrowthTicks;       // 成熟所需 tick 数（0=非作物）
        public readonly ushort RequiredItemId; // 放置所需物品（0=无消耗；作物的种子）
        public readonly int RequiredItemCount; // 放置所需数量（默认 1）
        public readonly bool IsWorkbench;      // 加工设施：craft 命令的目标
        public readonly bool NeedsSupport;     // 受结构支撑规则约束（platform 类）
        public readonly bool IsLadder;         // 可攀爬（垂直移动媒介，GDD §2.3 决议 #6）
        public readonly bool IsContainer;      // 容器（箱子）：deposit 命令的目标

        public TileRule(bool solid, bool placeable, bool blocksItems, ushort dropItemId,
            ushort harvestItemId, int growthTicks, ushort requiredItemId,
            int requiredItemCount, bool isWorkbench, bool needsSupport,
            bool isLadder, bool isContainer)
        {
            Solid = solid;
            Placeable = placeable;
            BlocksItems = blocksItems;
            DropItemId = dropItemId;
            HarvestItemId = harvestItemId;
            GrowthTicks = growthTicks;
            RequiredItemId = requiredItemId;
            RequiredItemCount = requiredItemCount;
            IsWorkbench = isWorkbench;
            NeedsSupport = needsSupport;
            IsLadder = isLadder;
            IsContainer = isContainer;
        }
    }

    /// <summary>按名字解析的常用方块 ID 集合，供地图生成等使用（避免核心硬编码数字）。</summary>
    public struct KnownTiles
    {
        public ushort Air;
        public ushort Dirt;
        public ushort Stone;
        public ushort Tree;
    }
}
