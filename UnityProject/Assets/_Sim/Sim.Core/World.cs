using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>
    /// 格子世界：按区块组织的无限平面（M1 骨架阶段不设边界）。
    /// 重要：任何需要确定性的遍历必须使用 ChunksInDeterministicOrder()。
    /// </summary>
    public sealed class World
    {
        private readonly Dictionary<ChunkKey, Chunk> _chunks = new Dictionary<ChunkKey, Chunk>();

        /// <summary>本 tick 变更记录（渲染增量同步 / 联机增量广播的数据源）。</summary>
        public readonly WorldChanges Changes = new WorldChanges();

        public int ChunkCount => _chunks.Count;

        public Chunk GetChunk(ChunkKey key)
        {
            _chunks.TryGetValue(key, out var chunk);
            return chunk;
        }

        public Chunk EnsureChunk(ChunkKey key)
        {
            if (!_chunks.TryGetValue(key, out var chunk))
            {
                chunk = new Chunk(key);
                _chunks.Add(key, chunk);
            }
            return chunk;
        }

        public ushort GetTile(GridPos pos)
        {
            var chunk = GetChunk(new ChunkKey(pos.X >> Chunk.Shift, pos.Y >> Chunk.Shift));
            return chunk == null ? (ushort)0 : chunk.GetTile(pos.X & Chunk.Mask, pos.Y & Chunk.Mask);
        }

        public byte GetState(GridPos pos)
        {
            var chunk = GetChunk(new ChunkKey(pos.X >> Chunk.Shift, pos.Y >> Chunk.Shift));
            return chunk == null ? (byte)0 : chunk.GetState(pos.X & Chunk.Mask, pos.Y & Chunk.Mask);
        }

        public void SetTile(GridPos pos, ushort tileId)
        {
            var chunk = EnsureChunk(new ChunkKey(pos.X >> Chunk.Shift, pos.Y >> Chunk.Shift));
            chunk.SetTile(pos.X & Chunk.Mask, pos.Y & Chunk.Mask, tileId);
            Changes.Mark(pos);
        }

        public void SetState(GridPos pos, byte state)
        {
            var chunk = EnsureChunk(new ChunkKey(pos.X >> Chunk.Shift, pos.Y >> Chunk.Shift));
            chunk.SetState(pos.X & Chunk.Mask, pos.Y & Chunk.Mask, state);
            Changes.Mark(pos);
        }

        /// <summary>
        /// 按 ChunkKey 升序遍历全部区块（确定性要求）。
        /// 优化空间：后续可缓存已排序键数组 + 脏标记，避免每次排序。
        /// </summary>
        public List<Chunk> ChunksInDeterministicOrder()
        {
            var list = new List<Chunk>(_chunks.Values);
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            return list;
        }

        public void HashInto(ref Fnv64 h)
        {
            var chunks = ChunksInDeterministicOrder();
            h.Update((ulong)chunks.Count);
            foreach (var chunk in chunks)
            {
                h.Update(chunk.Key.Cx);
                h.Update(chunk.Key.Cy);
                chunk.HashInto(ref h);
            }
        }
    }
}
