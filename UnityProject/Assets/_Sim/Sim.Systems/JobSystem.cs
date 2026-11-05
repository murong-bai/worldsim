using System.Collections.Generic;
using Sim.Core;

namespace Sim.Systems
{
    /// <summary>
    /// 小人任务 AI（M2.2，GDD §2.5）：任务板驱动 + 复用 PlayerMovementSystem。
    ///
    /// 每 tick：
    ///   1. 自动生成搬运任务（每 30 tick）：无主掉落物 + 存在箱子 → HaulItem（最近箱，确定性 tie-break）；
    ///   2. 任务有效性清理（目标消失/箱子被挖 → 移除或转阶段）；
    ///   3. 空闲 Worker（Id 升序）领取未认领任务（任务按登记序）；
    ///   4. 已认领任务推进状态机：A* 寻路（缓存于本系统，派生数据不入哈希）
    ///      → 每帧写 MoveInput/ClimbInput（Worker 永不写 JumpInput）→ 物理由
    ///      PlayerMovementSystem 执行 —— 与玩家同一套权威物理，联机零额外成本；
    ///   5. 到位后发命令：DigTileCommand / DepositCommand（进命令日志，回放天然一致）。
    ///
    /// 卡住处理：90 tick 无进展重算路径，累计 4 次放弃（任务退回未认领）。
    /// 确定性：遍历全部走排序序（掉落物按实体 Id、箱子按 key、Worker 按 Id）。
    /// </summary>
    public sealed class JobSystem : ISimSystem
    {
        public const int HaulScanInterval = 30;  // 搬运任务扫描间隔（tick）
        public const int StuckTickLimit = 90;    // 无进展重算阈值
        public const int RepathLimit = 4;        // 重算累计上限（超过放弃）
        private const int NearRange = 4;         // 目标附近即可（射程 5.5 内）

        /// <summary>路径缓存（workerId → 路径）。派生数据不入 WorldHash，可由状态重算。</summary>
        private sealed class PathCache
        {
            public List<GridPos> Path;
            public GridPos Goal;        // 路径目标（变化时重算）
            public int GoalEntityId;    // haul 目标实体（物品会动）
            public int Repaths;
            public Fix LastX;
            public Fix LastY;
        }

        private readonly Dictionary<int, PathCache> _paths = new Dictionary<int, PathCache>();

        public string Name { get { return "Job"; } }

        public void Tick(Simulation sim)
        {
            GenerateHaulJobs(sim);
            CleanupJobs(sim);
            AssignJobs(sim);
            AdvanceJobs(sim);
            ClearIdleWorkerInputs(sim);
        }

        // ---- 1) 自动生成搬运任务：无主掉落物 → 最近箱子 ----
        private void GenerateHaulJobs(Simulation sim)
        {
            if (sim.Tick % HaulScanInterval != 0) return;
            if (sim.Containers.Count == 0) return;

            var chestKeys = sim.Containers.OrderedKeys();
            var entities = sim.Entities.InDeterministicOrder();

            for (int i = 0; i < entities.Count; i++)
            {
                var item = entities[i];
                if (item.Kind != (ushort)EntityKind.DroppedItem) continue;

                // 已有对应任务则跳过
                bool hasJob = false;
                for (int j = 0; j < sim.Jobs.Count; j++)
                {
                    if (sim.Jobs.Get(j).TargetEntityId == item.Id) { hasJob = true; break; }
                }
                if (hasJob) continue;

                // 目的地：距物品最近的箱子（同距取 key 小者 —— 确定性 tie-break）
                long bestKey = 0;
                int bestDist = int.MaxValue;
                for (int k = 0; k < chestKeys.Count; k++)
                {
                    var chest = CraftingStore.Unpack(chestKeys[k]);
                    int dx = chest.X - item.Pos.X;
                    int dy = chest.Y - item.Pos.Y;
                    int dist = dx * dx + dy * dy;
                    if (dist < bestDist || (dist == bestDist && chestKeys[k] < bestKey))
                    {
                        bestDist = dist;
                        bestKey = chestKeys[k];
                    }
                }

                sim.Jobs.Add(new Job
                {
                    Kind = JobKind.HaulItem,
                    TargetPos = CraftingStore.Unpack(bestKey),
                    TargetEntityId = item.Id,
                    WorkerId = 0,
                    Phase = 0,
                    PathIndex = 0,
                    StuckTicks = 0,
                });
            }
        }

        // ---- 2) 有效性清理：目标消失 → 移除；haul 物品已被捡 → 若背着货则转送箱 ----
        private void CleanupJobs(Simulation sim)
        {
            for (int i = sim.Jobs.Count - 1; i >= 0; i--)
            {
                var job = sim.Jobs.Get(i);

                if (job.Kind == JobKind.DigTile)
                {
                    ushort tile = sim.World.GetTile(job.TargetPos);
                    if (tile == 0 || tile >= sim.TileRules.Length)
                    {
                        sim.Jobs.RemoveAt(i);
                        ReleasePath(job.WorkerId);
                    }
                    continue;
                }

                if (job.Kind == JobKind.HaulItem)
                {
                    bool itemAlive = sim.Entities.TryGet(job.TargetEntityId, out var item)
                        && item.Kind == (ushort)EntityKind.DroppedItem;
                    bool chestAlive = sim.Containers.TryGet(job.TargetPos, out _);
                    bool workerHasLoad = job.WorkerId != 0
                        && sim.Inventories.TryGet(job.WorkerId, out var wInv)
                        && TotalCount(wInv) > 0;

                    if (!chestAlive)
                    {
                        // 箱子没了：任务作废（小人手里的货等下一个箱出现再搬）
                        sim.Jobs.RemoveAt(i);
                        ReleasePath(job.WorkerId);
                    }
                    else if (!itemAlive && !workerHasLoad)
                    {
                        sim.Jobs.RemoveAt(i);
                        ReleasePath(job.WorkerId);
                    }
                    else if (!itemAlive && workerHasLoad && job.Phase == 0)
                    {
                        job.Phase = 2; // 货已在手，直接送箱
                        sim.Jobs.Set(i, job);
                    }
                }
            }
        }

        // ---- 3) 领取：空闲 Worker（Id 升序）领第一个未认领任务 ----
        private void AssignJobs(Simulation sim)
        {
            var entities = sim.Entities.InDeterministicOrder();
            for (int i = 0; i < entities.Count; i++)
            {
                var worker = entities[i];
                if (worker.Kind != (ushort)EntityKind.Worker) continue;

                // 已有任务？
                bool busy = false;
                for (int j = 0; j < sim.Jobs.Count; j++)
                {
                    if (sim.Jobs.Get(j).WorkerId == worker.Id) { busy = true; break; }
                }
                if (busy) continue;

                for (int j = 0; j < sim.Jobs.Count; j++)
                {
                    var job = sim.Jobs.Get(j);
                    if (job.WorkerId != 0) continue;
                    job.WorkerId = worker.Id;
                    job.Phase = 0;
                    job.PathIndex = 0;
                    job.StuckTicks = 0;
                    sim.Jobs.Set(j, job);
                    break; // 一人一任务
                }
            }
        }

        // ---- 4) 状态机推进 ----
        private void AdvanceJobs(Simulation sim)
        {
            for (int i = 0; i < sim.Jobs.Count; i++)
            {
                var job = sim.Jobs.Get(i);
                if (job.WorkerId == 0) continue;
                if (!sim.Entities.TryGet(job.WorkerId, out var worker)) continue;

                GridPos goal;
                int nearRange = NearRange;

                if (job.Kind == JobKind.DigTile)
                {
                    // 阶段 0：走到挖掘目标射程内 → 发挖掘命令 → 阶段 1
                    if (job.Phase == 0)
                    {
                        if (sim.WithinReach(job.WorkerId, job.TargetPos))
                        {
                            StopMove(sim, job.WorkerId);
                            sim.EnqueueCommand(new Sim.Commands.DigTileCommand(job.TargetPos) { ByEntityId = job.WorkerId });
                            job.Phase = 1;
                            job.StuckTicks = 0;
                            sim.Jobs.Set(i, job);
                            continue;
                        }
                        goal = job.TargetPos;
                    }
                    else
                    {
                        // 阶段 1：等待方块消失（挖掘命令下一 tick 生效；60 tick 未清则重发）
                        StopMove(sim, job.WorkerId);
                        if (sim.Tick % 60 == 0)
                        {
                            sim.EnqueueCommand(new Sim.Commands.DigTileCommand(job.TargetPos) { ByEntityId = job.WorkerId });
                        }
                        continue;
                    }
                }
                else // HaulItem
                {
                    if (job.Phase == 0)
                    {
                        // 目标物品实体不存在（被自己/他人捡走）→ Cleanup 已转阶段/移除；这里防御性跳过
                        if (!sim.Entities.TryGet(job.TargetEntityId, out var item)
                            || item.Kind != (ushort)EntityKind.DroppedItem)
                        {
                            StopMove(sim, job.WorkerId);
                            continue;
                        }
                        // 拾取系统（半径 1.5）会自动吸收；一旦背包有货 → 转送箱
                        if (sim.Inventories.TryGet(job.WorkerId, out var wInv) && TotalCount(wInv) > 0)
                        {
                            job.Phase = 2;
                            job.PathIndex = 0;
                            job.StuckTicks = 0;
                            sim.Jobs.Set(i, job);
                            ReleasePath(job.WorkerId);
                            continue;
                        }
                        goal = new GridPos(item.FixX.ToIntTruncated(), item.FixY.ToIntTruncated());
                        nearRange = 1; // 贴邻（拾取半径 1.5：邻格中身距物品 ≈ 1.08，稳定入半径）
                    }
                    else
                    {
                        // 阶段 2：送箱
                        if (sim.WithinReach(job.WorkerId, job.TargetPos))
                        {
                            StopMove(sim, job.WorkerId);
                            sim.EnqueueCommand(new Sim.Commands.DepositCommand(job.TargetPos, job.WorkerId));
                            continue; // 存入后背包清空 → Cleanup 移除任务
                        }
                        goal = job.TargetPos;
                    }
                }

                // ---- 寻路跟随 ----
                if (!MoveAlongPath(sim, worker, ref job, goal, nearRange))
                {
                    // 放弃：任务退回未认领（重置进度）
                    job.WorkerId = 0;
                    job.Phase = 0;
                    job.PathIndex = 0;
                    job.StuckTicks = 0;
                    sim.Jobs.Set(i, job);
                    ReleasePath(job.WorkerId);
                    StopMove(sim, job.WorkerId);
                }
                else
                {
                    sim.Jobs.Set(i, job);
                }
            }
        }

        /// <summary>
        /// 沿（缓存的）路径驱动物理输入。job 按 ref 传入（PathIndex/StuckTicks 的推进需写回任务板）。
        /// 返回 false = 放弃（重算超限）。路径目标变化 / 无缓存 → 重算（A*，nearRange 附近即达）。
        /// </summary>
        private bool MoveAlongPath(Simulation sim, EntityState worker, ref Job job, GridPos goal, int nearRange)
        {
            var cache = GetPath(job.WorkerId);

            bool goalChanged = cache.Path == null
                || cache.Goal.X != goal.X || cache.Goal.Y != goal.Y
                || (job.Kind == JobKind.HaulItem && cache.GoalEntityId != job.TargetEntityId);
            if (goalChanged)
            {
                if (cache.Repaths >= RepathLimit) return false;

                var cell = new GridPos(worker.FixX.ToIntTruncated(), worker.FixY.ToIntTruncated());
                var path = Pathfinding.FindPathNear(sim, cell, goal, nearRange);
                if (path == null) return false; // 不可达 → 放弃

                cache.Path = path;
                cache.Goal = goal;
                cache.GoalEntityId = job.TargetEntityId;
                cache.LastX = worker.FixX;
                cache.LastY = worker.FixY;
                job.PathIndex = 0;
                job.StuckTicks = 0;
                if (job.Kind == JobKind.HaulItem && job.Phase == 0)
                {
                    // 物品位置会随落地移动：允许等价目标频繁重算不消耗放弃额度
                    // （Repaths 只在真不可达/卡住时累计）
                }
                else
                {
                    cache.Repaths++;
                }
            }

            // 卡住检测：位置长期无变化 → 重算
            Fix moved = Fix.Abs(worker.FixX - cache.LastX) + Fix.Abs(worker.FixY - cache.LastY);
            if (moved < Fix.FromRaw(3277)) // 0.05 格
            {
                job.StuckTicks++;
            }
            else
            {
                job.StuckTicks = 0;
            }
            cache.LastX = worker.FixX;
            cache.LastY = worker.FixY;

            if (job.StuckTicks > StuckTickLimit)
            {
                job.StuckTicks = 0;
                cache.Repaths++;
                if (cache.Repaths > RepathLimit) return false;
                cache.Path = null; // 下一 tick 重算
                return true;
            }

            // 推进游标：抵达的路径点跳过
            var wc = new GridPos(worker.FixX.ToIntTruncated(), worker.FixY.ToIntTruncated());
            while (job.PathIndex < cache.Path.Count && cache.Path[job.PathIndex].X == wc.X
                && cache.Path[job.PathIndex].Y == wc.Y)
            {
                job.PathIndex++;
            }
            if (job.PathIndex >= cache.Path.Count)
            {
                StopMove(sim, job.WorkerId); // 已到终点区域，等阶段判定
                return true;
            }

            var wp = cache.Path[job.PathIndex];
            byte move = 0;
            Fix dx = Fix.FromInt(wp.X) + Fix.FromRaw(32768) - worker.FixX; // 目标格中心 - 当前位置
            if (dx > Fix.FromRaw(32768)) move = 2;       // > 0.5 格向右
            else if (dx < -Fix.FromRaw(32768)) move = 1; // < -0.5 格向左

            byte climb = 0;
            if (wp.Y > wc.Y) climb = 1;
            else if (wp.Y < wc.Y) climb = 2;

            sim.Entities.SetPlayerInput(job.WorkerId, move, 0, climb);
            return true;
        }

        /// <summary>空闲 Worker 清零输入（否则会永远走最后一步的方向）。</summary>
        private void ClearIdleWorkerInputs(Simulation sim)
        {
            var entities = sim.Entities.InDeterministicOrder();
            for (int i = 0; i < entities.Count; i++)
            {
                var worker = entities[i];
                if (worker.Kind != (ushort)EntityKind.Worker) continue;
                bool busy = false;
                for (int j = 0; j < sim.Jobs.Count; j++)
                {
                    if (sim.Jobs.Get(j).WorkerId == worker.Id) { busy = true; break; }
                }
                if (!busy)
                {
                    if (worker.MoveInput != 0 || worker.ClimbInput != 0)
                    {
                        sim.Entities.SetPlayerInput(worker.Id, 0, 0, 0);
                    }
                    ReleasePath(worker.Id);
                }
            }
        }

        private static void StopMove(Simulation sim, int workerId)
        {
            sim.Entities.SetPlayerInput(workerId, 0, 0, 0);
        }

        private static int TotalCount(Inventory inv)
        {
            int total = 0;
            for (int i = 0; i < Inventory.SlotCount; i++) total += inv.Counts[i];
            return total;
        }

        private PathCache GetPath(int workerId)
        {
            PathCache cache;
            if (!_paths.TryGetValue(workerId, out cache))
            {
                cache = new PathCache();
                _paths[workerId] = cache;
            }
            return cache;
        }

        private void ReleasePath(int workerId)
        {
            if (workerId != 0) _paths.Remove(workerId);
        }
    }
}
