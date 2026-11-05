using System.Collections.Generic;

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

        /// <summary>
        /// 矿脉配置（world.json "ores" 数组，按数组序生成）：石层内指定深度带按概率替换为矿石。
        /// M2.1 管线扩展：新增矿种此后纯 JSON（TileId 由 DefLoader 按字符串名解析填充）。
        /// 深度分带 = 向下探索动机的雏形（浅层煤、深层铜）。
        /// </summary>
        public readonly List<OreVein> Ores = new List<OreVein>();

        /// <summary>一条矿脉的生成参数。</summary>
        public sealed class OreVein
        {
            /// <summary>矿石方块 ID（DefLoader 解析字符串名）。</summary>
            public ushort TileId;

            /// <summary>替换概率（千分比，按石层格逐格判定）。</summary>
            public int ChancePerMille;

            /// <summary>深度带浅端（含，绝对 Y；数值较大 = 更浅）。</summary>
            public int YTop;

            /// <summary>深度带深端（含，绝对 Y；数值较小 = 更深）。</summary>
            public int YBottom;
        }

        /// <summary>缺省值 = M1 时代的硬编码常量（world.json 缺失或未传时兜底）。</summary>
        public static GlobalDef Default
        {
            get { return new GlobalDef(); }
        }
    }
}
