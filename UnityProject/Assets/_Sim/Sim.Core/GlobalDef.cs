namespace Sim.Core
{
    /// <summary>
    /// 全局模拟参数（数据驱动：Content/Defs/world.json → DefLoader 加载）。
    ///
    /// 与 TileRule/RecipeDef 的区别：这些参数不挂在某个方块/配方上，而是全局生效——
    /// 结构支撑跨度、世界生成宽度等（M2.0 技术清债：从硬编码常量迁入 JSON）。
    ///
    /// 注意：数值参与模拟结果 → 影响 WorldHash。联机时各端必须加载同一份 world.json
    /// （与 tiles.json 同理，属于「配置即模拟输入」的一部分）。
    /// </summary>
    public sealed class GlobalDef
    {
        /// <summary>平台离最近锚点的最大水平跨度（格）。StructuralSupport 消费。</summary>
        public int MaxHorizontalSpan = 6;

        /// <summary>世界左边界（含）。须与 32 格 chunk 对齐。</summary>
        public int WorldLeft = -32;

        /// <summary>世界右边界（含）。须与 32 格 chunk 对齐。</summary>
        public int WorldRight = 31;

        /// <summary>世界宽度（格）。</summary>
        public int Width
        {
            get { return WorldRight - WorldLeft + 1; }
        }

        /// <summary>缺省值 = M1 时代的硬编码常量（world.json 缺失或未传时兜底）。</summary>
        public static GlobalDef Default
        {
            get { return new GlobalDef(); }
        }
    }
}
