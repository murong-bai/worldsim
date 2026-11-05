using Sim.Core;

namespace Sim.Commands
{
    /// <summary>
    /// 设置玩家输入状态（移动方向 + 跳跃键）。这是角色操控的核心命令模型：
    /// 客户端仅在按键状态变化时发送（每秒数条），物理由权威端 PlayerMovementSystem
    /// 每 tick 基于实体上的输入状态推进 —— 单人与联机走同一套管线（GDD §3.2/§3.6）。
    /// 键盘/触屏/手柄只是输入采集方式不同，命令格式不变。
    /// </summary>
    public sealed class SetPlayerInputCommand : ISimCommand
    {
        public int EntityId;
        public byte MoveInput;   // 0=无 1=左 2=右
        public byte JumpInput;   // 0=无 1=跳

        public SetPlayerInputCommand() { }

        public SetPlayerInputCommand(int entityId, byte moveInput, byte jumpInput)
        {
            EntityId = entityId;
            MoveInput = moveInput;
            JumpInput = jumpInput;
        }

        public string CommandType => "set_player_input";

        public void Apply(Simulation sim)
        {
            // 权威端校验：目标必须是玩家实体（联机时防止伪造他类实体的输入）
            if (!sim.Entities.TryGet(EntityId, out var e)) return;
            if (e.Kind != (ushort)EntityKind.Player) return;
            sim.Entities.SetPlayerInput(EntityId, MoveInput, JumpInput);
        }
    }
}
