using System.Collections.Generic;
using UnityEngine;
using Sim.Commands;
using Sim.Core;
using Sim.Data;

namespace Sim.Client
{
    /// <summary>
    /// 输入路由：把本机输入翻译为命令入队（GDD §3.2 命令上行）。
    ///
    /// - 移动/跳跃：仅按键状态「变化时」发送 SetPlayerInputCommand（每秒数条，
    ///   带宽友好；联机走同一模型，权威端按射程/归属校验后推进物理）；
    /// - 挖掘/放置：鼠标操作转 Dig/PlaceTileCommand，携带 ByEntityId 供权威端校验；
    /// - 数字键 1-9 选择可放置方块（快捷栏雏形，背包/物品消耗接 M1 后续任务）；
    /// - 相机平滑跟随玩家。
    ///
    /// 移动端适配（M5）：只需替换本类的输入采集（虚拟摇杆/触屏手势），
    /// 命令格式与模拟核心零改动。
    /// </summary>
    public sealed class PlayerInputRouter : MonoBehaviour
    {
        private Simulation _sim;
        private int _playerId;
        private Camera _camera;
        private Defs _defs;

        private ushort[] _placeable;       // 快捷栏：可放置方块 tileId 列表
        private int _selected;             // 当前选中索引
        private byte _lastMove = 255;      // 255 = 尚未发送过（首帧必发一次对齐）
        private byte _lastJump = 255;
        private byte _lastClimb = 255;
        private float _digRepeatTimer;
        private SpriteRenderer _highlight; // 鼠标格高亮
        private int _recipeIndex;          // 当前选中配方
        private ushort _workbenchTileId;
        private ushort _chestTileId;

        /// <summary>挖掘任务格标记（黄框池，cap 32）。</summary>
        private readonly List<SpriteRenderer> _jobMarkers = new List<SpriteRenderer>();

        private const float DigRepeatInterval = 0.22f; // 按住连挖间隔（秒）
        private const float CameraLerp = 10f;

        public ushort SelectedTileId => _placeable.Length > 0 ? _placeable[_selected] : (ushort)0;
        public string SelectedTileName => SelectedTileId < _defs.TileNames.Length
            ? _defs.TileNames[SelectedTileId] : "-";

        /// <summary>当前选中方块的放置所需物品 Id（0 = 无消耗）。</summary>
        public ushort SelectedRequiredItemId => SelectedTileId < _defs.TileRules.Length
            ? _defs.TileRules[SelectedTileId].RequiredItemId : (ushort)0;

        /// <summary>当前选中放置是否材料充足（客户端预检；权威端在命令 Apply 时校验）。</summary>
        public bool SelectedHasMaterial
        {
            get
            {
                ushort required = SelectedRequiredItemId;
                if (required == 0) return true;
                return _sim.Inventories.TryGet(_playerId, out var inv) && inv.CountOf(required) > 0;
            }
        }

        /// <summary>当前选中配方（无配方定义时为 null）。</summary>
        public RecipeDef SelectedRecipe => _defs.Recipes != null && _defs.Recipes.Count > 0
            ? _defs.Recipes[_recipeIndex % _defs.Recipes.Count]
            : null;

        /// <summary>选中配方材料是否充足（客户端预检；权威端在命令 Apply 时校验）。</summary>
        public bool SelectedHasRecipeMaterials
        {
            get
            {
                var r = SelectedRecipe;
                if (r == null) return false;
                if (!_sim.Inventories.TryGet(_playerId, out var inv)) return false;
                for (int i = 0; i < r.InputItemIds.Length; i++)
                {
                    if (inv.CountOf(r.InputItemIds[i]) < r.InputCounts[i]) return false;
                }
                return true;
            }
        }

        /// <summary>射程内最近的工作台（普通台 + 电动台，按 IsWorkbench 规则匹配）。</summary>
        public GridPos? NearestWorkbench
        {
            get
            {
                if (!_sim.Entities.TryGet(_playerId, out var p)) return null;
                GridPos? best = null;
                int bestDistSq = int.MaxValue;
                for (int dy = -6; dy <= 6; dy++)
                {
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        var pos = new GridPos(p.Pos.X + dx, p.Pos.Y + dy);
                        ushort tile = _sim.World.GetTile(pos);
                        if (tile == 0 || tile >= _defs.TileRules.Length) continue;
                        if (!_defs.TileRules[tile].IsWorkbench) continue;
                        if (!_sim.WithinReach(_playerId, pos)) continue;
                        int distSq = dx * dx + dy * dy;
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            best = pos;
                        }
                    }
                }
                return best;
            }
        }

        /// <summary>
        /// 初始化非模拟依赖部分（相机/定义/快捷栏/光标高亮）。
        /// 对模拟实例与玩家 Id 的绑定走 Rebind（开新局/读档共用，见 SimBootstrap.BindSimulation）。
        /// </summary>
        public void Init(Camera camera, Defs defs)
        {
            _camera = camera;
            _defs = defs;

            var list = new List<ushort>();
            for (int i = 1; i < defs.TileRules.Length; i++)
            {
                if (defs.TileRules[i].Placeable) list.Add((ushort)i);
            }
            _placeable = list.ToArray();
            _workbenchTileId = defs.TileIds.ContainsKey("workbench") ? defs.TileIds["workbench"] : (ushort)0;
            _chestTileId = defs.TileIds.ContainsKey("chest") ? defs.TileIds["chest"] : (ushort)0;

            var hlGo = new GameObject("CursorHighlight");
            _highlight = hlGo.AddComponent<SpriteRenderer>();
            _highlight.sprite = PlaceholderSprites.SolidSprite(16);
            _highlight.color = new Color(1f, 1f, 1f, 0.25f);
            _highlight.sortingOrder = 20;
        }

        /// <summary>读档后重绑到新模拟实例（玩家 Id 在世界 setup 中恒定分配）。</summary>
        public void Rebind(Simulation sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
            ForceResend();
        }

        /// <summary>
        /// 强制重发输入状态：读档只回放已执行命令，挂起的输入命令已丢失，
        /// 复位哨兵值让下一帧必发一次 SetPlayerInputCommand 对齐权威端状态。
        /// </summary>
        public void ForceResend()
        {
            _lastMove = 255;
            _lastJump = 255;
            _lastClimb = 255;
        }

        private void Update()
        {
            if (_sim == null) return;
            HandleMoveJump();
            HandleMouse();
            HandleHotbar();
            HandleCraft();
            HandleJobCommands();
            SyncJobMarkers();
        }

        /// <summary>移动/跳跃/攀爬 → 输入状态命令（仅变化时发送）。</summary>
        private void HandleMoveJump()
        {
            int axis = 0;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) axis -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) axis += 1;
            byte move = (byte)(axis < 0 ? 1 : (axis > 0 ? 2 : 0));

            // W/上：跳跃（梯上自动转为攀爬上行，物理端裁决）；S/下：攀爬下行
            byte jump = (byte)((Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W)
                || Input.GetKey(KeyCode.UpArrow)) ? 1 : 0);
            byte climb = (byte)((Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) ? 2
                : ((Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) ? 1 : 0));

            if (move != _lastMove || jump != _lastJump || climb != _lastClimb)
            {
                _sim.EnqueueCommand(new SetPlayerInputCommand(_playerId, move, jump, climb));
                _lastMove = move;
                _lastJump = jump;
                _lastClimb = climb;
            }
        }

        /// <summary>X 标记/取消挖掘任务；V 把背包倒入射程内最近箱子；E 给射程内最近燃煤发电机加一份煤。</summary>
        private void HandleJobCommands()
        {
            if (Input.GetKeyDown(KeyCode.X))
            {
                _sim.EnqueueCommand(new MarkDigCommand(MouseCell()));
            }
            if (Input.GetKeyDown(KeyCode.V))
            {
                var chest = NearestTile(_chestTileId);
                if (chest.HasValue)
                {
                    _sim.EnqueueCommand(new DepositCommand(chest.Value, _playerId));
                }
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                var gen = NearestFuelGenerator();
                if (gen.HasValue)
                {
                    _sim.EnqueueCommand(new FuelCommand(gen.Value, _playerId));
                }
            }
        }

        /// <summary>射程内最近的燃煤发电机（无燃料型发电机不需要加煤）。</summary>
        private GridPos? NearestFuelGenerator()
        {
            if (!_sim.Entities.TryGet(_playerId, out var p)) return null;
            GridPos? best = null;
            int bestDistSq = int.MaxValue;
            for (int dy = -6; dy <= 6; dy++)
            {
                for (int dx = -6; dx <= 6; dx++)
                {
                    var pos = new GridPos(p.Pos.X + dx, p.Pos.Y + dy);
                    ushort tile = _sim.World.GetTile(pos);
                    if (tile == 0 || tile >= _defs.TileRules.Length) continue;
                    var rule = _defs.TileRules[tile];
                    if (rule.PowerGenW <= 0 || rule.FuelItemId == 0) continue;
                    if (!_sim.WithinReach(_playerId, pos)) continue;
                    int distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = pos;
                    }
                }
            }
            return best;
        }

        /// <summary>射程内最近的目标类方块（工作台/箱子共用扫描）。</summary>
        private GridPos? NearestTile(ushort tileId)
        {
            if (tileId == 0 || !_sim.Entities.TryGet(_playerId, out var p)) return null;
            GridPos? best = null;
            int bestDistSq = int.MaxValue;
            for (int dy = -6; dy <= 6; dy++)
            {
                for (int dx = -6; dx <= 6; dx++)
                {
                    var pos = new GridPos(p.Pos.X + dx, p.Pos.Y + dy);
                    if (_sim.World.GetTile(pos) != tileId) continue;
                    if (!_sim.WithinReach(_playerId, pos)) continue;
                    int distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = pos;
                    }
                }
            }
            return best;
        }

        /// <summary>挖掘任务格黄框同步（表现层，读任务板，cap 32）。</summary>
        private void SyncJobMarkers()
        {
            int count = 0;
            for (int i = 0; i < _sim.Jobs.Count && count < 32; i++)
            {
                var job = _sim.Jobs.Get(i);
                if (job.Kind != JobKind.DigTile) continue;

                while (_jobMarkers.Count <= count)
                {
                    var go = new GameObject("JobMarker" + _jobMarkers.Count);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = PlaceholderSprites.SolidSprite(16);
                    sr.color = new Color(1f, 0.85f, 0.2f, 0.35f);
                    sr.sortingOrder = 15;
                    _jobMarkers.Add(sr);
                }

                var marker = _jobMarkers[count];
                if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);
                marker.transform.position = new Vector3(job.TargetPos.X + 0.5f, job.TargetPos.Y + 0.5f, 0f);
                count++;
            }
            for (int i = count; i < _jobMarkers.Count; i++)
            {
                if (_jobMarkers[i].gameObject.activeSelf) _jobMarkers[i].gameObject.SetActive(false);
            }
        }

        /// <summary>鼠标：左键挖（按住连挖）、右键放、光标格高亮随射程/支撑状态变色。</summary>
        private void HandleMouse()
        {
            GridPos cell = MouseCell();
            bool inRange = _sim.WithinReach(_playerId, cell);

            if (_highlight != null)
            {
                _highlight.transform.position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, 0f);
                if (!inRange)
                {
                    _highlight.color = new Color(1f, 0.3f, 0.3f, 0.15f);   // 超射程：红
                }
                else if (!CanPlaceStructurally(cell))
                {
                    _highlight.color = new Color(1f, 0.85f, 0.2f, 0.2f);   // 无支撑：黄
                }
                else
                {
                    _highlight.color = new Color(1f, 1f, 1f, 0.25f);       // 正常：白
                }
            }

            if (Input.GetMouseButton(0))
            {
                _digRepeatTimer -= Time.deltaTime;
                if (_digRepeatTimer <= 0f && inRange)
                {
                    _sim.EnqueueCommand(new DigTileCommand(cell) { ByEntityId = _playerId });
                    _digRepeatTimer = DigRepeatInterval;
                }
            }
            else
            {
                _digRepeatTimer = 0f;
            }

            if (Input.GetMouseButtonDown(1) && inRange && SelectedHasMaterial && CanPlaceStructurally(cell))
            {
                _sim.EnqueueCommand(new PlaceTileCommand(cell, SelectedTileId) { ByEntityId = _playerId });
            }
        }

        /// <summary>选中方块在该格是否满足结构支撑（客户端预检；权威端在命令 Apply 时校验）。</summary>
        private bool CanPlaceStructurally(GridPos cell)
        {
            ushort tileId = SelectedTileId;
            if (tileId == 0 || tileId >= _defs.TileRules.Length) return true;
            var rule = _defs.TileRules[tileId];
            if (!rule.NeedsSupport) return true;
            return StructuralSupport.IsSupported(_sim, cell);
        }

        /// <summary>数字键 1-9 选择快捷栏方块。</summary>
        private void HandleHotbar()
        {
            for (int i = 0; i < 9 && i < _placeable.Length; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    _selected = i;
                    break;
                }
            }
        }

        /// <summary>F 循环选择配方；C 对射程内最近工作台开始加工（权威端校验材料）。</summary>
        private void HandleCraft()
        {
            int recipeCount = _defs.Recipes != null ? _defs.Recipes.Count : 0;
            if (recipeCount > 0)
            {
                if (Input.GetKeyDown(KeyCode.F))
                {
                    _recipeIndex = (_recipeIndex + 1) % recipeCount;
                }
                if (Input.GetKeyDown(KeyCode.C))
                {
                    var wb = NearestWorkbench;
                    var recipe = SelectedRecipe;
                    if (wb.HasValue && recipe != null)
                    {
                        _sim.EnqueueCommand(new CraftCommand(wb.Value, recipe.Id) { ByEntityId = _playerId });
                    }
                }
            }
        }

        /// <summary>鼠标位置 → 世界格坐标（正交相机下 ScreenToWorldPoint 的 x/y 即平面坐标）。</summary>
        private GridPos MouseCell()
        {
            var world = _camera.ScreenToWorldPoint(Input.mousePosition);
            return new GridPos(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
        }

        /// <summary>
        /// 相机平滑跟随玩家（表现层行为，不影响模拟），并钳制在世界水平边界内
        /// （M2.0：512 格世界，防止视野长期跑出边界外露空底色）。
        /// </summary>
        private void LateUpdate()
        {
            if (_sim == null || !_sim.Entities.TryGet(_playerId, out var e)) return;
            var target = new Vector3(e.FixX.ToFloat(), e.FixY.ToFloat() + 1f, -10f);

            // 边界钳制：视野半宽 = orthographicSize × aspect（世界格 = 1 单位）
            float halfWidth = _camera.orthographicSize * _camera.aspect;
            float minX = _sim.Global.WorldLeft + halfWidth;
            float maxX = _sim.Global.WorldRight + 1f - halfWidth;
            if (minX < maxX)
            {
                target.x = Mathf.Clamp(target.x, minX, maxX);
            }

            _camera.transform.position = Vector3.Lerp(
                _camera.transform.position, target, CameraLerp * Time.deltaTime);
        }
    }
}
