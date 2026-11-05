namespace Sim.Core
{
    /// <summary>
    /// 32×32 区块：模拟调度、存档、联机同步的最小单位（GDD §2.2 / §3.8）。
    /// 存储每格 tileId（ushort，0=air）与 tileState（byte，含义由 TileRule 定义，如作物生长进度）。
    /// local 坐标：lx/ly ∈ [0,32)，ly=0 为区块底部。
    /// </summary>
    public sealed class Chunk
    {
        public const int Size = 32;
        public const int Area = Size * Size;
        public const int Shift = 5;             // log2(Size)
        public const int Mask = Size - 1;       // Size-1

        public readonly ChunkKey Key;

        private readonly ushort[] _tiles;
        private readonly byte[] _states;

        public Chunk(ChunkKey key)
        {
            Key = key;
            _tiles = new ushort[Area];
            _states = new byte[Area];
        }

        public static int LocalIndex(int lx, int ly) => lx + ly * Size;

        public ushort GetTile(int lx, int ly) => _tiles[LocalIndex(lx, ly)];
        public byte GetState(int lx, int ly) => _states[LocalIndex(lx, ly)];

        /// <summary>放置/替换方块，状态归零。</summary>
        public void SetTile(int lx, int ly, ushort tileId)
        {
            int i = LocalIndex(lx, ly);
            _tiles[i] = tileId;
            _states[i] = 0;
        }

        public void SetState(int lx, int ly, byte state)
        {
            _states[LocalIndex(lx, ly)] = state;
        }

        public void HashInto(ref Fnv64 h)
        {
            for (int i = 0; i < Area; i++)
            {
                h.Update(_tiles[i]);
                h.Update(_states[i]);
            }
        }
    }
}
