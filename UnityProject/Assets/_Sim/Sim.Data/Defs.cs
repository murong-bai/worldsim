using System.Collections.Generic;
using Sim.Core;

namespace Sim.Data
{
    /// <summary>
    /// 全部内容定义。数字 ID 分配规则（确定性，跨平台一致）：
    /// 方块 air=0，其余按字符串 ID 的 Ordinal 升序从 1 起；物品同理（0=无效）。
    /// RecipeDef 定义在 Sim.Core（模拟核心消费数字 ID 版本）。
    /// </summary>
    public sealed class Defs
    {
        public TileRule[] TileRules;                 // 索引 = tileId
        public string[] TileNames;                   // 索引 = tileId
        public Dictionary<string, ushort> TileIds;   // 字符串 ID → 数字 ID
        public string[] ItemNames;                   // 索引 = itemId
        public Dictionary<string, ushort> ItemIds;   // 字符串 ID → 数字 ID
        public List<RecipeDef> Recipes;

        /// <summary>占位配色（hex "#RRGGBB"，索引与 TileNames 对齐）。仅表现层使用，模拟核心不读取。</summary>
        public string[] TileColors;

        /// <summary>占位配色（索引与 ItemNames 对齐）。仅表现层使用。</summary>
        public string[] ItemColors;
    }
}
