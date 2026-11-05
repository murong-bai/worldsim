using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sim.Core;

namespace Sim.Commands
{
    /// <summary>存档数据：种子 + 存档时 tick + 已执行命令日志。</summary>
    public sealed class SaveData
    {
        public ulong Seed;
        public long Tick;
        public List<CommandRecord> Records = new List<CommandRecord>();
    }

    /// <summary>
    /// 存档序列化（GDD §3.5：存档 = 种子 + 命令回放）。
    ///
    /// 文件格式（纯文本，每行一条，UTF-8）：
    ///   worldsim|1|{种子}|{存档时tick}     ← 头行：魔数|版本
    ///   {tick}|{type}|{args...}            ← 命令行（与 CommandCodec / 联机传输同格式）
    ///
    /// 文件读写由调用方负责（客户端 persistentDataPath / 测试临时目录）——本类只做
    /// 字符串 ↔ SaveData 转换与重放，保持纯 C# 可在 Unity 外测试。
    ///
    /// 读档流程 = 用同种子重建世界（确定性 setup）→ ReplayInto 重放至存档 tick。
    /// 注意：仅记录「已执行」命令——读档瞬间尚未执行的挂起输入会丢失，
    /// 客户端读档后必须重发当前输入状态（PlayerInputRouter.ForceResend）。
    /// </summary>
    public static class SaveLoad
    {
        public const string Magic = "worldsim";
        public const int FormatVersion = 1;

        /// <summary>模拟 → 存档文本（头行 + AppliedLog 每条一行）。</summary>
        public static string Serialize(Simulation sim)
        {
            var sb = new StringBuilder();
            sb.Append(Magic)
              .Append('|').Append(FormatVersion.ToString(CultureInfo.InvariantCulture))
              .Append('|').Append(sim.Seed.ToString(CultureInfo.InvariantCulture))
              .Append('|').Append(sim.Tick.ToString(CultureInfo.InvariantCulture));

            var applied = sim.Commands.Applied;
            for (int i = 0; i < applied.Count; i++)
            {
                sb.Append('\n').Append(CommandCodec.EncodeRecord(applied[i]));
            }
            return sb.ToString();
        }

        /// <summary>存档文本 → SaveData。头损坏/版本不支持时抛 FormatException。</summary>
        public static SaveData Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new FormatException("存档为空");

            string[] lines = text.Split('\n');
            string header = lines[0].TrimEnd('\r');
            string[] parts = header.Split('|');
            if (parts.Length != 4 || parts[0] != Magic)
            {
                throw new FormatException("存档头无效: " + header);
            }

            int version = int.Parse(parts[1], CultureInfo.InvariantCulture);
            if (version != FormatVersion)
            {
                throw new FormatException("不支持的存档版本: " + version);
            }

            var data = new SaveData
            {
                Seed = ulong.Parse(parts[2], CultureInfo.InvariantCulture),
                Tick = long.Parse(parts[3], CultureInfo.InvariantCulture),
            };
            if (data.Tick < 0) throw new FormatException("存档 tick 无效: " + data.Tick);

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length == 0) continue;
                data.Records.Add(CommandCodec.DecodeRecord(line));
            }
            return data;
        }

        /// <summary>
        /// 把存档重放进一个「同种子、已完成世界 setup」的新模拟，推进到存档时的 tick。
        /// 读档耗时与局长成正比（逐 tick 重放）；长档可后续优化为「周期快照 + 增量回放」。
        /// </summary>
        public static void ReplayInto(Simulation sim, SaveData data)
        {
            for (int i = 0; i < data.Records.Count; i++)
            {
                sim.Commands.Enqueue(data.Records[i].Command, data.Records[i].Tick);
            }
            while (sim.Tick < data.Tick)
            {
                sim.Step();
            }
        }
    }
}
