using Sim.Core;

namespace Sim.Systems
{
    /// <summary>
    /// 角色物理（30Hz、Fix 定点、纯权威端模拟，GDD §2.3 / §3.2）。
    ///
    /// 输入模型：客户端仅在按键状态变化时发送 SetPlayerInputCommand（每秒数条命令，
    /// 而非每帧一条）；本系统每 tick 读取实体上的输入状态执行物理 ——
    /// 这正是联机「命令上行 → 权威端模拟」的同一套模型，单人联机零改动。
    ///
    /// 处理对象：Player 与 Worker（M2.2 小人复用同一物理；Worker 的输入由 JobSystem
    /// 写入，永不跳跃 —— 移动能力是玩家的子集，GDD §2.3 决议 #6）。
    ///
    /// 每帧执行顺序（确定性约束，固定不可变）：
    ///   梯子判定 → 水平速度 = 输入 × 步速 → X 轴碰撞解析（Worker 可跨 1 格台阶）
    ///   → 重力/攀爬 → Y 轴碰撞解析（含平台单向支撑）。
    ///
    /// 碰撞盒：半宽 0.3、高 1.8 —— 可钻 2 格通道、跳上 2 格台阶。
    /// 平台方块（非固体但 BlocksItems）：单向支撑，仅从上方落到顶面时承接，可从下方穿越。
    /// 梯子（IsLadder，非固体）：身体任一格在梯上 → 攀爬输入生效（±ClimbSpeed）、
    /// 无输入时悬挂（vy=0，可安全停留）；梯底着地时 Grounded=1 保留跳跃。
    /// </summary>
    public sealed class PlayerMovementSystem : ISimSystem
    {
        // 常量集中定义：所有值为 Q16.16 定点，跨平台位级一致
        private static readonly Fix Dt = Fix.FromRaw(Fix.OneRaw / 30);       // 1/30 s
        private static readonly Fix WalkSpeed = Fix.FromInt(6);              // 6 tiles/s
        private static readonly Fix ClimbSpeed = Fix.FromInt(4);             // 4 tiles/s（梯上）
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
                if (e.Kind != (ushort)EntityKind.Player && e.Kind != (ushort)EntityKind.Worker) continue;
                StepCharacter(sim, e);
            }
        }

        /// <summary>身体覆盖的格中是否有梯子（列 = 碰撞盒中心所在列，行 = 脚底到头顶）。</summary>
        private static bool OnLadder(Simulation sim, EntityState e)
        {
            int col = e.FixX.ToIntTruncated();
            int yLow = (e.FixY + Eps).ToIntTruncated();
            int yHigh = (e.FixY + Height - Eps).ToIntTruncated();
            for (int cy = yLow; cy <= yHigh; cy++)
            {
                var rule = sim.TileRules[sim.World.GetTile(new GridPos(col, cy))];
                if (rule.IsLadder) return true;
            }
            return false;
        }

        private void StepCharacter(Simulation sim, EntityState e)
        {
            Fix x = e.FixX;
            Fix y = e.FixY;
            bool isWorker = e.Kind == (ushort)EntityKind.Worker;
            bool onLadder = OnLadder(sim, e);

            // ---- 速度积分 ----
            Fix vx = e.MoveInput == 2 ? WalkSpeed : (e.MoveInput == 1 ? -WalkSpeed : Fix.Zero);
            Fix vy;
            if (onLadder)
            {
                // 梯上：攀爬输入直接设定垂直速度；无输入悬挂（vy=0）。
                // 跳跃仅在梯底着地时生效（Grounded 会在下方 Y 轴解析中置位）。
                vy = e.ClimbInput == 1 ? ClimbSpeed : (e.ClimbInput == 2 ? -ClimbSpeed : Fix.Zero);
                if (vy > Fix.Zero && e.JumpInput == 1 && e.Grounded == 1) vy = JumpSpeed;
            }
            else
            {
                vy = e.Vy + GravityPerTick;
                if (vy < TerminalVy) vy = TerminalVy;
                if (e.JumpInput == 1 && e.Grounded == 1) vy = JumpSpeed;
            }

            bool grounded = false;

            // ---- X 轴：先水平移动并解析墙体（Worker 可自动跨上 1 格高台阶） ----
            Fix newX = x + vx * Dt;
            if (vx != Fix.Zero)
            {
                int dir = vx > Fix.Zero ? 1 : -1;
                Fix edge = dir > 0 ? newX + HalfWidth : newX - HalfWidth;
                int cell = (dir > 0 ? edge - Eps : edge + Eps).ToIntTruncated();
                int yLow = (y + Eps).ToIntTruncated();
                int yHigh = (y + Height - Eps).ToIntTruncated();
                int blockedAt = -1;
                for (int cy = yLow; cy <= yHigh; cy++)
                {
                    if (sim.IsSolid(new GridPos(cell, cy)))
                    {
                        blockedAt = cy;
                        break;
                    }
                }

                if (blockedAt >= 0)
                {
                    // Worker 跨台阶（GDD §2.3 决议 #6）：墙只有脚底 1 格高、且抬升后
                    // 头顶有净空 → 直接迈上去（仅着地时，防空中瞬移）。
                    bool stepped = false;
                    if (isWorker && e.Grounded == 1 && blockedAt == yLow
                        && !sim.IsSolid(new GridPos(cell, yLow + 1))
                        && !sim.IsSolid(new GridPos(cell, yLow + 2)))
                    {
                        y = Fix.FromInt(yLow + 1);
                        stepped = true;
                    }
                    if (!stepped)
                    {
                        newX = dir > 0
                            ? Fix.FromInt(cell) - HalfWidth        // 右缘贴墙左
                            : Fix.FromInt(cell + 1) + HalfWidth;   // 左缘贴墙右
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
            else if (onLadder && e.ClimbInput == 0)
            {
                // 梯上悬挂：检查脚下是否有支撑（梯底 = 站立，保留跳跃能力）
                int below = (y - Eps).ToIntTruncated();
                for (int cx = cxL; cx <= cxR; cx++)
                {
                    var rule = sim.TileRules[sim.World.GetTile(new GridPos(cx, below))];
                    if (rule.Solid || rule.BlocksItems) { grounded = true; break; }
                }
                if (grounded) newY = Fix.FromInt(below + 1);
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
