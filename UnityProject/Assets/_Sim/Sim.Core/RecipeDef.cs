namespace Sim.Core
{
    /// <summary>
    /// 配方定义（数据驱动，由 Sim.Data 从 JSON 装配后注入 Simulation）。
    /// 与 TileRule 同级：模拟核心只认数字物品 ID，不解析文本 —— 保证核心可独立测试。
    /// </summary>
    public sealed class RecipeDef
    {
        public string Id;                 // 配方字符串 ID（命令编解码用，不参与模拟逻辑）
        public ushort[] InputItemIds;     // 输入物品 ID（与 InputCounts 一一对应）
        public int[] InputCounts;         // 输入数量
        public ushort OutputItemId;       // 输出物品 ID
        public int OutputCount;           // 输出数量（默认 1）
        public int CraftTicks;            // 加工时长（逻辑帧）
    }
}
