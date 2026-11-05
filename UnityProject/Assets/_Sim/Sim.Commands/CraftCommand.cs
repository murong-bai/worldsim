using System;
using Sim.Core;

namespace Sim.Commands
{
    /// <summary>
    /// 在工作台上开始加工：权威端校验（GDD §3.6 防作弊，客户端只发操作）：
    /// 归属玩家身份 + 射程 → 该格确为工作台 → 配方存在 → 任务槽空闲 → 材料原子消耗。
    /// 全部通过后登记 CraftJob，由 CraftingSystem 推进并在完成时弹出产物掉落物。
    /// </summary>
    public sealed class CraftCommand : ISimCommand
    {
        public GridPos Pos;
        public string RecipeId;

        /// <summary>归属玩家实体（合成必须携带：材料消耗依赖背包）。</summary>
        public int ByEntityId;

        public CraftCommand() { }

        public CraftCommand(GridPos pos, string recipeId)
        {
            Pos = pos;
            RecipeId = recipeId;
        }

        public string CommandType => "craft";

        public void Apply(Simulation sim)
        {
            // 合成必须归属真实玩家（与挖/放不同：材料消耗是核心语义，无 creative 路径）
            if (ByEntityId == 0) return;
            if (!sim.Entities.TryGet(ByEntityId, out var player)) return;
            if (player.Kind != (ushort)EntityKind.Player) return;
            if (!sim.WithinReach(ByEntityId, Pos)) return;

            // 该格必须是工作台
            ushort tile = sim.World.GetTile(Pos);
            if (tile == 0 || tile >= sim.TileRules.Length) return;
            if (!sim.TileRules[tile].IsWorkbench) return;

            // 配方存在 + 任务槽空闲
            if (!sim.TryGetRecipeIndex(RecipeId, out int recipeIndex)) return;
            if (sim.Crafting.HasJob(Pos)) return;

            var recipe = sim.Recipes[recipeIndex];

            // 材料原子消耗：先验总额再扣（多输入配方不会扣一半）
            var inv = sim.Inventories.GetOrCreate(ByEntityId);
            for (int i = 0; i < recipe.InputItemIds.Length; i++)
            {
                if (inv.CountOf(recipe.InputItemIds[i]) < recipe.InputCounts[i]) return;
            }
            for (int i = 0; i < recipe.InputItemIds.Length; i++)
            {
                inv.TryConsume(recipe.InputItemIds[i], (ushort)recipe.InputCounts[i]);
            }

            sim.Crafting.TryStart(Pos, recipeIndex, recipe.CraftTicks);
        }
    }
}
