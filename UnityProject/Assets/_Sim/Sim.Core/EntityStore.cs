using System.Collections.Generic;

namespace Sim.Core
{
    public enum EntityKind : ushort
    {
        None = 0,
        Player = 1,
        Worker = 2,        // 中期雇佣小人（M2）
        DroppedItem = 3,
    }

    /// <summary>
    /// 实体状态（值类型，经 EntityStore 读写保持快照一致性）。
    ///
    /// 位置语义（重要）：
    /// - FixX / FixY 为唯一权威位置源：X = 碰撞盒中心，Y = 碰撞盒底边（脚底）；
    /// - Pos 为派生格坐标 = (floor(FixX), floor(FixY))，供格子查询与日志使用；
    /// - Spawn 时 FixX = pos.X + 0.5（格中心），FixY = pos.Y（格底）。
    ///
    /// 角色物理字段（Vx/Vy/MoveInput/JumpInput/Grounded）仅 Player 使用；
    /// 掉落物沿用整格模型（ItemGravitySystem 走 SetPos，同步 FixX/FixY）。
    /// 全部字段参与 WorldHash —— 物理状态分叉会被确定性测试捕获。
    /// </summary>
    public struct EntityState
    {
        public int Id;
        public ushort Kind;     // EntityKind
        public GridPos Pos;     // 派生：脚底所在格
        public ushort Aux;      // DroppedItem: itemId
        public ushort Aux2;     // 预留（如堆叠数量）
        public byte Data;       // Player: 朝向（0=左 1=右）

        // ---- 角色物理（Player 专用；其余实体保持默认值）----
        public Fix FixX;        // 碰撞盒中心 X
        public Fix FixY;        // 碰撞盒底边 Y
        public Fix Vx;
        public Fix Vy;
        public byte MoveInput;  // 0=无 1=左 2=右
        public byte JumpInput;  // 0=无 1=跳
        public byte ClimbInput; // 0=无 1=上 2=下（梯子上生效；Worker 寻路亦写此字段）
        public byte Grounded;   // 0/1
    }

    /// <summary>
    /// 实体存储。迭代必须走 InDeterministicOrder()（按 Id 升序），
    /// 禁止直接遍历内部 Dictionary（GDD §3.3）。
    /// </summary>
    public sealed class EntityStore
    {
        private static readonly Fix CellCenter = Fix.FromRaw(32768); // 0.5

        private int _nextId;
        private readonly Dictionary<int, EntityState> _entities = new Dictionary<int, EntityState>();

        public int Count => _entities.Count;

        public int Spawn(ushort kind, GridPos pos, ushort aux, ushort aux2, byte data)
        {
            _nextId++;
            var e = new EntityState
            {
                Id = _nextId,
                Kind = kind,
                Pos = pos,
                Aux = aux,
                Aux2 = aux2,
                Data = data,
                FixX = Fix.FromInt(pos.X) + CellCenter,
                FixY = Fix.FromInt(pos.Y),
            };
            _entities.Add(e.Id, e);
            return e.Id;
        }

        public bool TryGet(int id, out EntityState entity)
        {
            return _entities.TryGetValue(id, out entity);
        }

        /// <summary>整格移动（掉落物等）；同步 FixX/FixY 保持单一位置源。</summary>
        public void SetPos(int id, GridPos pos)
        {
            if (!_entities.TryGetValue(id, out var e)) return;
            e.Pos = pos;
            e.FixX = Fix.FromInt(pos.X) + CellCenter;
            e.FixY = Fix.FromInt(pos.Y);
            _entities[id] = e;
        }

        /// <summary>精确位置写入（玩家物理）；Pos 由 Fix 位置派生。</summary>
        public void SetFixPos(int id, Fix x, Fix y)
        {
            if (!_entities.TryGetValue(id, out var e)) return;
            e.FixX = x;
            e.FixY = y;
            e.Pos = new GridPos(x.ToIntTruncated(), y.ToIntTruncated());
            _entities[id] = e;
        }

        /// <summary>写入速度与着地状态。</summary>
        public void SetMotion(int id, Fix vx, Fix vy, byte grounded)
        {
            if (!_entities.TryGetValue(id, out var e)) return;
            e.Vx = vx;
            e.Vy = vy;
            e.Grounded = grounded;
            _entities[id] = e;
        }

        /// <summary>写入角色输入状态（移动方向 + 跳跃 + 攀爬）。Player 由命令驱动，Worker 由 JobSystem 驱动。</summary>
        public void SetPlayerInput(int id, byte moveInput, byte jumpInput, byte climbInput)
        {
            if (!_entities.TryGetValue(id, out var e)) return;
            e.MoveInput = moveInput;
            e.JumpInput = jumpInput;
            e.ClimbInput = climbInput;
            _entities[id] = e;
        }

        public void SetData(int id, byte data)
        {
            if (!_entities.TryGetValue(id, out var e)) return;
            e.Data = data;
            _entities[id] = e;
        }

        public void SetAux(int id, ushort aux)
        {
            if (!_entities.TryGetValue(id, out var e)) return;
            e.Aux = aux;
            _entities[id] = e;
        }

        /// <summary>更新掉落物堆叠数量（部分拾取后的余量）。</summary>
        public void SetAux2(int id, ushort aux2)
        {
            if (!_entities.TryGetValue(id, out var e)) return;
            e.Aux2 = aux2;
            _entities[id] = e;
        }

        public void Despawn(int id)
        {
            _entities.Remove(id);
        }

        /// <summary>按 Id 升序的实体快照列表（确定性迭代）。</summary>
        public List<EntityState> InDeterministicOrder()
        {
            var list = new List<EntityState>(_entities.Values);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        public void HashInto(ref Fnv64 h)
        {
            var list = InDeterministicOrder();
            h.Update((ulong)list.Count);
            foreach (var e in list)
            {
                h.Update(e.Id);
                h.Update(e.Kind);
                h.Update(e.Pos.X);
                h.Update(e.Pos.Y);
                h.Update(e.Aux);
                h.Update(e.Aux2);
                h.Update(e.Data);
                h.Update(e.FixX.Raw);
                h.Update(e.FixY.Raw);
                h.Update(e.Vx.Raw);
                h.Update(e.Vy.Raw);
                h.Update(e.MoveInput);
                h.Update(e.JumpInput);
                h.Update(e.ClimbInput);
                h.Update(e.Grounded);
            }
        }
    }
}
