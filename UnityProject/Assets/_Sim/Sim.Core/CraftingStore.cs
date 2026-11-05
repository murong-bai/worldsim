using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>一个进行中的加工任务（挂在某个工作台格子上）。</summary>
    public struct CraftJob
    {
        public int RecipeIndex;     // Simulation.Recipes 索引
        public int RemainingTicks;  // 剩余逻辑帧
    }

    /// <summary>
    /// 加工任务板：格子坐标 → 加工任务。
    /// 一个工作台同时只跑一个任务（M1 简化；M2 可扩展多任务队列）。
    ///
    /// 确定性约束（GDD §3.3）：
    /// - Key 为 long 打包坐标（避免自定义结构哈希的跨平台差异）；
    /// - 遍历必须走 OrderedKeys()（按 Key 升序），禁止直接遍历内部字典；
    /// - 全部状态纳入 WorldHash —— 任务分叉会被确定性测试捕获。
    /// </summary>
    public sealed class CraftingStore
    {
        private readonly Dictionary<long, CraftJob> _jobs = new Dictionary<long, CraftJob>();

        public static long Pack(GridPos pos)
        {
            return ((long)pos.X << 32) | (uint)pos.Y;
        }

        public static GridPos Unpack(long key)
        {
            return new GridPos((int)(key >> 32), (int)(key & 0xFFFFFFFFL));
        }

        public int Count => _jobs.Count;

        public bool HasJob(GridPos pos)
        {
            return _jobs.ContainsKey(Pack(pos));
        }

        public bool TryGet(GridPos pos, out CraftJob job)
        {
            return _jobs.TryGetValue(Pack(pos), out job);
        }

        public bool TryGet(long key, out CraftJob job)
        {
            return _jobs.TryGetValue(key, out job);
        }

        /// <summary>登记任务（格子已有任务时拒绝，调用方需先检查）。</summary>
        public bool TryStart(GridPos pos, int recipeIndex, int craftTicks)
        {
            long key = Pack(pos);
            if (_jobs.ContainsKey(key)) return false;
            _jobs[key] = new CraftJob { RecipeIndex = recipeIndex, RemainingTicks = craftTicks };
            return true;
        }

        /// <summary>取消任务（挖掉工作台等场景）。材料是否返还有待 M2 决策。</summary>
        public bool TryCancel(GridPos pos)
        {
            return _jobs.Remove(Pack(pos));
        }

        public void SetRemaining(long key, int remainingTicks)
        {
            if (_jobs.TryGetValue(key, out var job))
            {
                job.RemainingTicks = remainingTicks;
                _jobs[key] = job;
            }
        }

        public void Remove(long key)
        {
            _jobs.Remove(key);
        }

        /// <summary>按 Key 升序的任务键列表（确定性迭代）。</summary>
        public List<long> OrderedKeys()
        {
            var keys = new List<long>(_jobs.Keys);
            keys.Sort();
            return keys;
        }

        public void HashInto(ref Fnv64 h)
        {
            var keys = OrderedKeys();
            h.Update((ulong)keys.Count);
            foreach (long key in keys)
            {
                h.Update((ulong)key);
                h.Update(_jobs[key].RecipeIndex);
                h.Update(_jobs[key].RemainingTicks);
            }
        }
    }
}
