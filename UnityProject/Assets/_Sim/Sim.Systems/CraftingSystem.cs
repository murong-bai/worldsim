using Sim.Core;

namespace Sim.Systems
{
    /// <summary>
    /// 加工推进：每 tick 将全部任务倒计时 -1；归零时在工作台上方弹出产物掉落物
    /// （与 PickupSystem 天然闭环：玩家走近自动拾取）。
    ///
    /// 这是模拟系统而非命令 —— 加工是时间的必然结果（与作物生长同构），
    /// 任务状态全部入 WorldHash（确定性/联机各端一致）。
    /// 遍历走 CraftingStore.OrderedKeys()（按打包坐标升序，GDD §3.3）。
    /// </summary>
    public sealed class CraftingSystem : ISimSystem
    {
        public string Name { get { return "Crafting"; } }

        public void Tick(Simulation sim)
        {
            if (sim.Crafting.Count == 0) return;

            var keys = sim.Crafting.OrderedKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                CraftJob job;
                if (!sim.Crafting.TryGet(keys[i], out job)) continue; // 不会被并发修改（单线程），保险而已

                if (job.RecipeIndex < 0 || job.RecipeIndex >= sim.Recipes.Length)
                {
                    sim.Crafting.Remove(keys[i]); // 内容变更防御：无效任务直接清除
                    continue;
                }

                if (job.RemainingTicks > 1)
                {
                    sim.Crafting.SetRemaining(keys[i], job.RemainingTicks - 1);
                    continue;
                }

                // 完成：弹出产出（数量取配方 OutputCount，至少 1）
                var recipe = sim.Recipes[job.RecipeIndex];
                var pos = CraftingStore.Unpack(keys[i]);
                int output = recipe.OutputCount < 1 ? 1 : recipe.OutputCount;
                if (output > 999) output = 999;
                sim.Entities.Spawn((ushort)EntityKind.DroppedItem,
                    pos + new GridPos(0, 1), recipe.OutputItemId, (ushort)output, 0);
                sim.Crafting.Remove(keys[i]);
            }
        }
    }
}
