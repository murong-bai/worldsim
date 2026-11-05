using System;

namespace Sim.Core
{
    /// <summary>区块坐标（可排序，保证确定性遍历顺序）。</summary>
    public readonly struct ChunkKey : IEquatable<ChunkKey>, IComparable<ChunkKey>
    {
        public readonly int Cx;
        public readonly int Cy;

        public ChunkKey(int cx, int cy)
        {
            Cx = cx;
            Cy = cy;
        }

        public int CompareTo(ChunkKey other)
        {
            int c = Cx.CompareTo(other.Cx);
            return c != 0 ? c : Cy.CompareTo(other.Cy);
        }

        public bool Equals(ChunkKey other) => Cx == other.Cx && Cy == other.Cy;
        public override bool Equals(object obj) => obj is ChunkKey other && Equals(other);
        public override int GetHashCode() => (Cx * 397) ^ Cy;
        public override string ToString() => $"[{Cx},{Cy}]";
    }
}
