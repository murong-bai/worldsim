using System;

namespace Sim.Core
{
    /// <summary>
    /// Q16.16 定点数。模拟核心中所有非整数运算一律使用本类型，
    /// 禁止使用 float/double 参与模拟（跨平台浮点行为不一致，联机会导致状态分叉）。
    /// </summary>
    public readonly struct Fix : IEquatable<Fix>
    {
        public const int FractionBits = 16;
        public const long OneRaw = 1L << FractionBits;

        private readonly long _raw;

        private Fix(long raw)
        {
            _raw = raw;
        }

        public long Raw => _raw;

        public static Fix FromRaw(long raw) => new Fix(raw);
        public static Fix FromInt(int value) => new Fix((long)value << FractionBits);
        public static Fix Zero => default(Fix);

        /// <summary>截断取整。</summary>
        public int ToIntTruncated() => (int)(_raw >> FractionBits);

        public static Fix operator +(Fix a, Fix b) => new Fix(a._raw + b._raw);
        public static Fix operator -(Fix a, Fix b) => new Fix(a._raw - b._raw);
        public static Fix operator -(Fix a) => new Fix(-a._raw);

        public static Fix operator *(Fix a, Fix b) => new Fix((a._raw * b._raw) >> FractionBits);

        /// <summary>注意：极端大数值下存在溢出风险，M1 骨架阶段不做检测（后续按需补充）。</summary>
        public static Fix operator /(Fix a, Fix b)
        {
            if (b._raw == 0)
                throw new DivideByZeroException("Fix: divide by zero");
            return new Fix((a._raw << FractionBits) / b._raw);
        }

        public static bool operator ==(Fix a, Fix b) => a._raw == b._raw;
        public static bool operator !=(Fix a, Fix b) => a._raw != b._raw;
        public static bool operator <(Fix a, Fix b) => a._raw < b._raw;
        public static bool operator >(Fix a, Fix b) => a._raw > b._raw;
        public static bool operator <=(Fix a, Fix b) => a._raw <= b._raw;
        public static bool operator >=(Fix a, Fix b) => a._raw >= b._raw;

        /// <summary>绝对值（M2.2 JobSystem 卡住检测用）。</summary>
        public static Fix Abs(Fix a) => new Fix(a._raw < 0 ? -a._raw : a._raw);

        /// <summary>转 float（仅供表现层：渲染/调试显示。禁止参与模拟运算）。</summary>
        public float ToFloat() => (float)_raw / OneRaw;

        public bool Equals(Fix other) => _raw == other._raw;
        public override bool Equals(object obj) => obj is Fix other && Equals(other);
        public override int GetHashCode() => _raw.GetHashCode();
        public override string ToString() => ((double)_raw / OneRaw).ToString("0.####");
    }
}
