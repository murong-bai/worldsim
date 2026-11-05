using Sim.Core;

namespace Sim.Commands
{
    /// <summary>
    /// 移动实体。M1 骨架为「瞬移」语义（仅做碰撞校验），用于打通命令管线；
    /// M2 将替换为寻路（A*）+ 逐格移动模型，命令格式保持不变。
    /// </summary>
    public sealed class MoveEntityCommand : ISimCommand
    {
        public int EntityId;
        public GridPos Target;

        public MoveEntityCommand() { }

        public MoveEntityCommand(int entityId, GridPos target)
        {
            EntityId = entityId;
            Target = target;
        }

        public string CommandType => "move_entity";

        public void Apply(Simulation sim)
        {
            if (!sim.Entities.TryGet(EntityId, out var entity)) return;
            if (sim.IsSolid(Target)) return;
            sim.Entities.SetPos(EntityId, Target);
        }
    }
}
