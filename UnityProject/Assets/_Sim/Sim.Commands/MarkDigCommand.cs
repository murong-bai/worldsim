using Sim.Core;

namespace Sim.Commands
{
    /// <summary>
    /// 标记挖掘任务（M2.2）：把一个方块格登记到任务板，等待小人领取执行。
    /// 权威端校验：目标格非空。已存在同格任务时静默忽略（幂等）。
    /// 玩家用 X 键标记；联机时任何客户端都可提交（命令广播，权威端裁决）。
    /// </summary>
    public sealed class MarkDigCommand : ISimCommand
    {
        public GridPos Pos;

        public MarkDigCommand() { }

        public MarkDigCommand(GridPos pos)
        {
            Pos = pos;
        }

        public string CommandType => "mark_dig";

        public void Apply(Simulation sim)
        {
            ushort tile = sim.World.GetTile(Pos);
            if (tile == 0 || tile >= sim.TileRules.Length) return;

            // 幂等：同格已有挖掘任务则忽略
            for (int i = 0; i < sim.Jobs.Count; i++)
            {
                var j = sim.Jobs.Get(i);
                if (j.Kind == JobKind.DigTile && j.TargetPos.X == Pos.X && j.TargetPos.Y == Pos.Y) return;
            }

            sim.Jobs.Add(new Job
            {
                Kind = JobKind.DigTile,
                TargetPos = Pos,
                TargetEntityId = 0,
                WorkerId = 0,
                Phase = 0,
                PathIndex = 0,
                StuckTicks = 0,
            });
        }
    }
}
