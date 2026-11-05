using Sim.Core;

namespace Sim.Systems
{
    /// <summary>
    /// 作物生长：每 IntervalTicks 对全部作物格推进状态直到 GrowthTicks（成熟）。
    /// 遍历顺序：区块按 Key 升序 → 行优先（GDD §3.3 确定性要求）。
    /// 优化空间：作物登记表（活跃格列表），避免全图扫描（M2）。
    /// </summary>
    public sealed class CropGrowthSystem : ISimSystem
    {
        public const int IntervalTicks = 5;

        public string Name { get { return "CropGrowth"; } }

        public void Tick(Simulation sim)
        {
            if (sim.Tick % IntervalTicks != 0) return;

            var chunks = sim.World.ChunksInDeterministicOrder();
            for (int c = 0; c < chunks.Count; c++)
            {
                var chunk = chunks[c];
                for (int ly = 0; ly < Chunk.Size; ly++)
                {
                    for (int lx = 0; lx < Chunk.Size; lx++)
                    {
                        ushort tile = chunk.GetTile(lx, ly);
                        if (tile == 0) continue;

                        var rule = sim.TileRules[tile];
                        if (rule.GrowthTicks <= 0) continue;

                        byte state = chunk.GetState(lx, ly);
                        if (state < 255 && state < rule.GrowthTicks)
                        {
                            // 经 World.SetState 写入：自动进入 WorldChanges（渲染增量同步）
                            var worldPos = new GridPos(
                                (chunk.Key.Cx << Chunk.Shift) + lx,
                                (chunk.Key.Cy << Chunk.Shift) + ly);
                            sim.World.SetState(worldPos, (byte)(state + 1));
                        }
                    }
                }
            }
        }
    }
}
