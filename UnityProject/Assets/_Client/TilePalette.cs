using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Sim.Data;

namespace Sim.Client
{
    /// <summary>
    /// 占位图块集：按 Defs 数字 ID 在运行时生成 Tile（程序化精灵，见 PlaceholderSprites）。
    /// 正式美术到位后替换为「资产表映射（tileId → TileBase 资产）」，对外接口不变。
    /// </summary>
    public sealed class TilePalette
    {
        private readonly Dictionary<ushort, Tile> _tiles = new Dictionary<ushort, Tile>();

        public IReadOnlyDictionary<ushort, Tile> Tiles
        {
            get { return _tiles; }
        }

        public TilePalette(Defs defs)
        {
            for (ushort id = 1; id < defs.TileRules.Length; id++)
            {
                string name = defs.TileNames[id];
                string hex = id < defs.TileColors.Length ? defs.TileColors[id] : null;
                var color = PlaceholderSprites.ParseColor(hex);

                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = PlaceholderSprites.TileSprite(name, color);
                tile.flags = TileFlags.None; // 允许逐格 SetColor（作物成熟度着色）
                _tiles[id] = tile;
            }
        }
    }
}
