namespace Sim.Core
{
    /// <summary>
    /// FNV-1a 64 位哈希累积器。用于 WorldHash：对模拟全状态计算确定性哈希，
    /// 单机用于 CI 一致性测试，联机用于各端校对（见 GDD §3.3 / §3.6）。
    /// </summary>
    public struct Fnv64
    {
        public const ulong OffsetBasis = 14695981039346656037UL;
        public const ulong Prime = 1099511628211UL;

        private ulong _hash;

        public ulong Value => _hash;

        public static Fnv64 Create() => new Fnv64 { _hash = OffsetBasis };

        public void Update(byte b)
        {
            _hash ^= b;
            _hash *= Prime;
        }

        public void Update(ushort v)
        {
            Update((byte)(v & 0xFF));
            Update((byte)((v >> 8) & 0xFF));
        }

        public void Update(uint v)
        {
            Update((byte)(v & 0xFF));
            Update((byte)((v >> 8) & 0xFF));
            Update((byte)((v >> 16) & 0xFF));
            Update((byte)((v >> 24) & 0xFF));
        }

        public void Update(ulong v)
        {
            for (int i = 0; i < 8; i++)
            {
                Update((byte)(v & 0xFF));
                v >>= 8;
            }
        }

        public void Update(long v) => Update((ulong)v);
        public void Update(int v) => Update((uint)v);
    }
}
