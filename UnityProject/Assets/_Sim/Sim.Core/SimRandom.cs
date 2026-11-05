namespace Sim.Core
{
    /// <summary>
    /// 确定性伪随机数（splitmix64）。
    /// 规则：模拟核心内禁止使用 System.Random / UnityEngine.Random；
    /// 随机流只从本类型派生，且遍历顺序必须确定（见 Sim 确定性规范 GDD §3.3）。
    /// </summary>
    public sealed class SimRandom
    {
        private ulong _state;

        public SimRandom(ulong seed)
        {
            _state = seed;
        }

        /// <summary>当前内部状态（用于存档与 WorldHash 校验）。</summary>
        public ulong State => _state;

        private static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            return Mix(_state);
        }

        /// <summary>[minInclusive, maxExclusive) 区间随机整数。存在轻微模偏差，整数运算跨平台一致。</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            ulong range = (ulong)(long)(maxExclusive - minInclusive);
            ulong v = NextUInt64() % range;
            return minInclusive + (int)v;
        }

        /// <summary>[0, 1) 区间随机定点数。</summary>
        public Fix NextFix01() => Fix.FromRaw((long)(NextUInt64() >> 48));
    }
}
