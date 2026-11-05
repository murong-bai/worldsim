using Sim.Core;

namespace Sim.Systems
{
    /// <summary>
    /// 玩家角色物理（30Hz、Fix 定点、纯权威端模拟，GDD §2.3 / §3.2）。
    ///
    /// 输入模型：客户端仅在按键状态变化时发送 SetPlayerInputCommand（每秒数条命令，
    /// 而非每帧一条）；本系统每 tick 读取实体上的输入状态执行物理 ——
    /// 这正是联机「命令上行 → 权威端模拟」的同一套模型，单人联机零改动。
    ///
    /// 每帧执行顺序（确定性约束，固定不可变）：
    ///   水平速度 = 输入 × 步速 → X 轴碰撞解析 → 重力 → Y 轴碰撞解析（含平台单向支撑）。
    ///
    /// 碰撞盒：半宽 0.3、高 1.8 —— 可钻 2 格通道、跳上 2 格台阶。
    /// 平台方块（非固体但 BlocksItems）：单向支撑，仅从上方落到顶面时承接，可从下方穿越。
    /// </summary>
    public sealed class PlayerMovementSystem : ISimSystem
    {
        // 常量集中定义：所有值为 Q16.16 定点，跨平台位级一致
        private static readonly Fix Dt = Fix.FromRaw(Fix.OneRaw / 30);       // 1/30 s
        private static readonly Fix WalkSpeed = Fix.FromInt(6);              // 6 tiles/s
        private static readonly Fix GravityPerTick = Fix.FromInt(-2);        // -60 t/s² / 30Hz
        private static readonly Fix JumpSpeed = Fix.FromInt(16);             // 跳高 ≈ 2.13 格
        private static readonly Fix TerminalVy = Fix.FromInt(-25);           // < 1 格/tick，保证扫掠判定安全
        private static readonly Fix HalfWidth = Fix.FromRaw(19661);          // 0.3
        private static readonly Fix Height = Fix.FromRaw(117965);            // 1.8
        private static readonly Fix Eps = Fix.FromRaw(64);                   // 2^-10，格边界容差

        public string Name { get { return "PlayerMovement"; } }

        public void Tick(Simulation sim)
        {
            var entities = sim.Entities.InDeterministicOrder();
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                if (e.Kind != (ushort)EntityKind.Player) continue;
                StepPlayer(sim, e);
            }
        }

        private void StepPlayer(Simulation sim, EntityState e)
        {
            Fix x = e.FixX;
            Fix y = e.FixY;

            // ---- 速度积分 ----
            Fix vx = e.MoveInput == 2 ? WalkSpeed : (e.MoveInput == 1 ? -WalkSpeed : Fix.Zero);
            Fix vy = e.Vy + GravityPerTick;
            if (vy < TerminalVy) vy = TerminalVy;

            bool grounded = false;
            if (e.JumpInput == 1 && e.Grounded == 1)
            {
                vy = JumpSpeed;
            }

            // ---- X 轴：先水平移动并解析墙体 ----
            Fix newX = x + vx * Dt;
            if (vx != Fix.Zero)
            {
                int dir = vx > Fix.Zero ? 1 : -1;
                Fix edge = dir > 0 ? newX + HalfWidth : newX - HalfWidth;
                int cell = (dir > 0 ? edge - Eps : edge + Eps).ToIntTruncated();
                int yLow = (y + Eps).ToIntTruncated();
                int yHigh = (y + Height - Eps).ToIntTruncated();
                for (int cy = yLow; cy <= yHigh; cy++)
                {
                    if (sim.IsSolid(new GridPos(cell, cy)))
                    {
                        newX = dir > 0
                            ? Fix.FromInt(cell) - HalfWidth        // 右缘贴墙左
                            : Fix.FromInt(cell + 1) + HalfWidth;   // 左缘贴墙右
                        break;
                    }
                }
            }
            x = newX;

            // ---- Y 轴：再竖直移动并解析地面/天花板 ----
            Fix newY = y + vy * Dt;
            int cxL = (x - HalfWidth + Eps).ToIntTruncated();
            int cxR = (x + HalfWidth - Eps).ToIntTruncated();

            if (vy < Fix.Zero)
            {
                // 下落：逐格检查脚底扫过的支撑（固体 或 从上方跨越的平台顶）
                int topCell = (y - Eps).ToIntTruncated();   // 脚底当前所在格
                int botCell = newY.ToIntTruncated();        // 目标脚底格
                for (int cy = topCell; cy >= botCell; cy--)
                {
                    bool solid = false, platform = false;
                    for (int cx = cxL; cx <= cxR; cx++)
                    {
                        var rule = sim.TileRules[sim.World.GetTile(new GridPos(cx, cy))];
                        if (rule.Solid) solid = true;
                        else if (rule.BlocksItems) platform = true;
                    }
                    if (solid || (platform && y >= Fix.FromInt(cy + 1)))
                    {
                        newY = Fix.FromInt(cy + 1);
                        vy = Fix.Zero;
                        grounded = true;
                        break;
                    }
                }
            }
            else if (vy > Fix.Zero)
            {
                // 上升：检查头顶扫过的天花板（仅固体阻挡）
                int headOld = (y + Height - Eps).ToIntTruncated();
                int headNew = (y + Height + vy * Dt).ToIntTruncated();
                for (int cy = headOld; cy <= headNew; cy++)
                {
                    bool solid = false;
                    for (int cx = cxL; cx <= cxR; cx++)
                    {
                        if (sim.IsSolid(new GridPos(cx, cy))) { solid = true; break; }
                    }
                    if (solid)
                    {
                        newY = Fix.FromInt(cy) - Height;
                        vy = Fix.Zero;
                        break;
                    }
                }
            }

            // ---- 朝向与写回 ----
            byte facing = e.MoveInput == 1 ? (byte)0 : (e.MoveInput == 2 ? (byte)1 : e.Data);

            sim.Entities.SetFixPos(e.Id, x, newY);
            sim.Entities.SetMotion(e.Id, vx, vy, (byte)(grounded ? 1 : 0));
            if (facing != e.Data)
            {
                sim.Entities.SetData(e.Id, facing);
            }
        }
    }
}
