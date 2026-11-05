using UnityEngine;
using UnityEngine.Tilemaps;
using Sim.Core;

namespace Sim.Client
{
    /// <summary>
    /// 世界 → Tilemap 渲染：
    /// - RenderFull：启动时全量首绘（遍历区块，确定性顺序与模拟一致）；
    /// - SyncTick：每逻辑帧消费 World.Changes 增量刷新（同一份数据未来供联机广播，GDD §3.6）；
    /// - 坐标系：模拟格 (x,y) = Tilemap cell (x,y)，均为 Y 向上，1:1 直映射；
    /// - 作物：按生长进度 SetColor 从绿渐变到金黄（表现层浮点，不影响模拟）。
    /// </summary>
    public sealed class WorldTilemapRenderer : MonoBehaviour
    {
        private Simulation _sim;
        private Tilemap _map;
        private TilePalette _palette;

        private readonly Color _cropYoung = new Color(0.35f, 0.75f, 0.25f); // 幼苗绿
        private readonly Color _cropRipe = new Color(0.95f, 0.8f, 0.2f);   // 成熟金

        public void Init(Simulation sim, Tilemap map, TilePalette palette)
        {
            _sim = sim;
            _map = map;
            _palette = palette;
        }

        /// <summary>全量首绘（WorldGen 之后、第一个 Step 之前调用一次）。</summary>
        public void RenderFull()
        {
            _map.ClearAllTiles();
            var chunks = _sim.World.ChunksInDeterministicOrder();
            for (int c = 0; c < chunks.Count; c++)
            {
                var chunk = chunks[c];
                int baseX = chunk.Key.Cx << Chunk.Shift;
                int baseY = chunk.Key.Cy << Chunk.Shift;
                for (int ly = 0; ly < Chunk.Size; ly++)
                {
                    for (int lx = 0; lx < Chunk.Size; lx++)
                    {
                        ushort tile = chunk.GetTile(lx, ly);
                        if (tile == 0) continue;
                        SetTileVisual(new GridPos(baseX + lx, baseY + ly), tile, chunk.GetState(lx, ly));
                    }
                }
            }
        }

        /// <summary>每逻辑帧调用一次（SimStep 之后）：消费本 tick 的世界变更。</summary>
        public void SyncTick()
        {
            var changes = _sim.World.Changes.Positions;
            for (int i = 0; i < changes.Count; i++)
            {
                var pos = changes[i];
                SetTileVisual(pos, _sim.World.GetTile(pos), _sim.World.GetState(pos));
            }
        }

        private void SetTileVisual(GridPos pos, ushort tile, byte state)
        {
            var cell = new Vector3Int(pos.X, pos.Y, 0);
            if (tile == 0)
            {
                _map.SetTile(cell, null);
                return;
            }

            Tile tileAsset;
            if (!_palette.Tiles.TryGetValue(tile, out tileAsset)) return;
            _map.SetTile(cell, tileAsset);

            var rule = _sim.TileRules[tile];
            if (rule.GrowthTicks > 0)
            {
                float progress = state / (float)rule.GrowthTicks; // 表现层浮点，仅用于着色
                if (progress > 1f) progress = 1f;
                _map.SetColor(cell, Color.Lerp(_cropYoung, _cropRipe, progress));
            }
        }
    }
}
