using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>已执行命令记录（回放/存档的基本单元：tick + 命令引用）。</summary>
    public struct CommandRecord
    {
        public long Tick;
        public ISimCommand Command;
    }

    /// <summary>
    /// 命令队列：按「目标执行 tick + 入队顺序」消费。
    /// AppliedLog 即回放日志 —— 存档 = 初始种子 + 该日志（GDD §3.5）。
    /// </summary>
    public sealed class CommandQueue
    {
        private struct Pending
        {
            public long ExecuteTick;
            public ISimCommand Command;
        }

        private readonly List<Pending> _pending = new List<Pending>();
        private readonly List<CommandRecord> _applied = new List<CommandRecord>();

        public int PendingCount => _pending.Count;
        public IReadOnlyList<CommandRecord> Applied => _applied;

        /// <summary>入队：在指定逻辑帧执行（回放/网络命令使用）。</summary>
        public void Enqueue(ISimCommand command, long executeTick)
        {
            _pending.Add(new Pending { ExecuteTick = executeTick, Command = command });
        }

        internal void ExecuteDue(long tick, Simulation sim)
        {
            // 入队顺序即执行顺序；ExecuteTick 已过的命令立即补执行。
            for (int i = 0; i < _pending.Count; )
            {
                if (_pending[i].ExecuteTick <= tick)
                {
                    var command = _pending[i].Command;
                    _pending.RemoveAt(i);
                    command.Apply(sim);
                    _applied.Add(new CommandRecord { Tick = tick, Command = command });
                }
                else
                {
                    i++;
                }
            }
        }
    }
}
