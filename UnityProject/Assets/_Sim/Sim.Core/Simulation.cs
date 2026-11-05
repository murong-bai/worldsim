using System.Collections.Generic;

namespace Sim.Core
{
    /// <summary>
    /// 模拟根：持有世界、实体、随机流、命令队列与系统列表。
    /// 一个 Step() = 一个逻辑帧（1/30 秒）。真实时间不参与模拟（GDD §3.3）。
    /// </summary>
    public sealed class Simulation
    {
        public const int LogicFrameHz = 30;

        public long Tick;
        public readonly ulong Seed;
        public readonly World World;
        public readonly EntityStore Entities;
        public readonly InventoryStore Inventories;
        public readonly CraftingStore Crafting;
        public readonly SimRandom Rng;
        public readonly TileRule[] TileRules;
        public readonly RecipeDef[] Recipes;
        public readonly CommandQueue Commands;

        private readonly List<ISimSystem> _systems = new List<ISimSystem>();
        private readonly Dictionary<string, int> _recipeIndexById = new Dictionary<string, int>();

        public Simulation(ulong seed, TileRule[] tileRules, RecipeDef[] recipes = null)
        {
            Seed = seed;
            TileRules = tileRules;
            Recipes = recipes ?? new RecipeDef[0];
            for (int i = 0; i < Recipes.Length; i++)
            {
                _recipeIndexById[Recipes[i].Id] = i;
            }
            World = new World();
            Entities = new EntityStore();
            Inventories = new InventoryStore();
            Crafting = new CraftingStore();
            Rng = new SimRandom(seed);
            Commands = new CommandQueue();
        }

        /// <summary>注册顺序 = 每 tick 的执行顺序（确定性约束）。</summary>
        public void RegisterSystem(ISimSystem system)
        {
            _systems.Add(system);
        }

        /// <summary>常规入队：下一个逻辑帧执行。</summary>
        public void EnqueueCommand(ISimCommand command)
        {
            Commands.Enqueue(command, Tick + 1);
        }

        /// <summary>推进一个逻辑帧：清空变更日志 → 消费到期命令 → 依次执行系统。</summary>
        public void Step()
        {
            World.Changes.BeginTick();
            Tick++;
            Commands.ExecuteDue(Tick, this);
            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].Tick(this);
            }
        }

        public bool IsSolid(GridPos pos)
        {
            return TileRules[World.GetTile(pos)].Solid;
        }

        public bool BlocksItems(GridPos pos)
        {
            return TileRules[World.GetTile(pos)].BlocksItems;
        }

        /// <summary>玩家操作射程（格）的平方：5.5² = 30.25。</summary>
        public static readonly Fix MaxReachSq = Fix.FromRaw(1985744L); // 30.25 in Q16.16

        private static readonly Fix CellCenter = Fix.FromRaw(32768); // 0.5

        /// <summary>
        /// 权威端射程校验：指定玩家实体到目标格中心的距离是否在射程内。
        /// 用于挖掘/放置命令的防作弊校验（GDD §3.6：客户端只发操作，结果由权威端裁决）。
        /// </summary>
        public bool WithinReach(int entityId, GridPos pos)
        {
            if (!Entities.TryGet(entityId, out var e)) return false;
            if (e.Kind != (ushort)EntityKind.Player) return false;
            Fix dx = e.FixX - (Fix.FromInt(pos.X) + CellCenter);
            Fix dy = (e.FixY + Fix.FromRaw(58982L) /*0.9: 玩家中身高度*/) - (Fix.FromInt(pos.Y) + CellCenter);
            Fix distSq = dx * dx + dy * dy;
            return distSq <= MaxReachSq;
        }

        /// <summary>按字符串 ID 查配方索引（craft 命令权威校验用）。</summary>
        public bool TryGetRecipeIndex(string recipeId, out int index)
        {
            return _recipeIndexById.TryGetValue(recipeId, out index);
        }

        /// <summary>
        /// 全状态哈希（WorldHash）：tick、种子、随机流状态、区块、实体、背包、加工任务。
        /// 用途：CI 确定性测试 + 联机各端校对（GDD §3.3 / §3.6）。
        /// </summary>
        public ulong ComputeHash()
        {
            var h = Fnv64.Create();
            h.Update((ulong)Tick);
            h.Update(Seed);
            h.Update(Rng.State);
            World.HashInto(ref h);
            Entities.HashInto(ref h);
            Inventories.HashInto(ref h);
            Crafting.HashInto(ref h);
            return h.Value;
        }
    }
}
