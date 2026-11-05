using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>任务类型（M2.2 雏形：挖掘 + 搬运）。</summary>
    public enum JobKind : byte
    {
        None = 0,
        DigTile = 1,   // 挖掉 TargetPos 的方块（玩家标记）
        HaulItem = 2,  // 拾取 TargetEntityId 掉落物 → 送往 TargetPos 箱子（自动生成）
    }

    /// <summary>
    /// 一个任务及其执行进度（状态即模拟状态，全部入哈希）。
    /// Phase 语义：0=前往目标 → 1=执行中（dig 已发命令/等待） → 2=送箱（haul）
    /// → 3=完成待清理。路径本身不入哈希（派生数据，可由世界状态重算）。
    /// </summary>
    public struct Job
    {
        public JobKind Kind;
        public GridPos TargetPos;      // DigTile: 挖掘格；HaulItem: 目的地箱子格
        public int TargetEntityId;     // HaulItem: 目标掉落物实体 Id（DigTile: 0）
        public int WorkerId;           // 0 = 未认领
        public byte Phase;             // 见上
        public int PathIndex;          // 当前路径游标（路径缓存在 JobSystem）
        public int StuckTicks;         // 无进展计时（超限重算/放弃）
    }

    /// <summary>
    /// 任务板（M2.2）：List 顺序 = 确定性遍历序（生成顺序必须确定：
    /// 掉落物按实体 Id 升序扫描、箱子按 key 排序挑选）。
    /// 领取：Worker 按 Id 升序扫任务。全部状态入 WorldHash。
    /// </summary>
    public sealed class JobBoard
    {
        private readonly List<Job> _jobs = new List<Job>();

        public int Count => _jobs.Count;

        public Job Get(int index)
        {
            return _jobs[index];
        }

        public void Set(int index, Job job)
        {
            _jobs[index] = job;
        }

        public void Add(Job job)
        {
            _jobs.Add(job);
        }

        public void RemoveAt(int index)
        {
            _jobs.RemoveAt(index);
        }

        public List<Job> Jobs => _jobs;

        public void HashInto(ref Fnv64 h)
        {
            h.Update((ulong)_jobs.Count);
            for (int i = 0; i < _jobs.Count; i++)
            {
                var j = _jobs[i];
                h.Update((byte)j.Kind);
                h.Update(j.TargetPos.X);
                h.Update(j.TargetPos.Y);
                h.Update(j.TargetEntityId);
                h.Update(j.WorkerId);
                h.Update(j.Phase);
                h.Update(j.PathIndex);
                h.Update(j.StuckTicks);
            }
        }
    }
}
