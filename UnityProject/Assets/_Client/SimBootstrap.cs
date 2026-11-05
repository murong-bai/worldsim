using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;
using Sim.Commands;
using Sim.Core;
using Sim.Data;
using Sim.Systems;

namespace Sim.Client
{
    /// <summary>
    /// M1 客户端引导：装配模拟核心，并在运行时自动搭建渲染栈与输入路由
    /// （正交相机 / Grid / Tilemap / 世界渲染 / 实体精灵池 / 玩家输入）。
    ///
    /// 用法：新建空场景 → 创建空 GameObject → 挂载本组件 → Play。
    /// 可玩内容：A/D 移动、Space 跳、左键挖掘、右键放置、1-9 切换方块。
    ///
    /// TODO(M1)：拾取掉落物+背包（物品消耗）、支撑规则、Cinemachine、美术资产替换。
    /// </summary>
    public sealed class SimBootstrap : MonoBehaviour
    {
        [SerializeField] private ulong seed = 0x1234ABCD;
        [SerializeField] private float cameraOrthoSize = 11f;

        private Simulation _sim;
        private WorldTilemapRenderer _tiles;
        private EntitySpriteRenderer _entities;
        private PlayerInputRouter _input;
        private Tilemap _map;
        private TilePalette _palette;
        private Defs _defs;
        private int _playerId;
        private ulong _hash;
        private string[] _itemNames;
        private string _statusMsg;

        /// <summary>存档槽（M1 单槽；多槽/选档 UI 属 M2）。</summary>
        private string SavePath => Path.Combine(Application.persistentDataPath, "slot1.sav");

        private void Awake()
        {
            Time.fixedDeltaTime = 1f / Simulation.LogicFrameHz;

            _defs = DefLoader.LoadFromDirectory(
                Path.Combine(Application.dataPath, "Content", "Defs"));
            _itemNames = _defs.ItemNames;
            _statusMsg = "F5 存档  F9 读档  (存档路径: " + SavePath + ")";

            BuildRenderingStack(_defs);
            StartNewGame(seed);
        }

        /// <summary>开新局：装配模拟（系统 + 地形 + 玩家 + 初始物资）并绑定渲染。</summary>
        private void StartNewGame(ulong worldSeed)
        {
            _sim = BuildSimulation(worldSeed);
            BindSimulation();
        }

        /// <summary>读档：同种子重建世界 + 命令重放到存档点（GDD §3.5）。</summary>
        private void LoadGame()
        {
            if (!File.Exists(SavePath))
            {
                _statusMsg = "无存档: " + SavePath;
                return;
            }
            try
            {
                var data = SaveLoad.Parse(File.ReadAllText(SavePath));
                _sim = BuildSimulation(data.Seed);
                SaveLoad.ReplayInto(_sim, data);
                BindSimulation();
                _hash = _sim.ComputeHash();
                _statusMsg = "已读档: tick " + data.Tick + ", 命令 " + data.Records.Count
                    + " 条 (seed " + data.Seed + ")";
            }
            catch (Exception ex)
            {
                _statusMsg = "读档失败: " + ex.Message;
            }
        }

        private void SaveGame()
        {
            try
            {
                File.WriteAllText(SavePath, SaveLoad.Serialize(_sim));
                _statusMsg = "已存档: tick " + _sim.Tick + ", 命令 "
                    + _sim.Commands.Applied.Count + " 条 → " + SavePath;
            }
            catch (Exception ex)
            {
                _statusMsg = "存档失败: " + ex.Message;
            }
        }

        /// <summary>把当前 _sim 绑定到渲染/输入（开新局与读档共用）。</summary>
        private void BindSimulation()
        {
            _tiles.Init(_sim, _map, _palette);
            _tiles.RenderFull();
            _entities.Init(_sim, _defs);
            _input.Rebind(_sim, _playerId);
        }

        /// <summary>模拟装配：系统注册顺序 = 每 tick 执行顺序（确定性约束，与 harness BuildSim 一致）。</summary>
        private Simulation BuildSimulation(ulong worldSeed)
        {
            var sim = new Simulation(worldSeed, _defs.TileRules, _defs.Recipes.ToArray(), _defs.Global);
            sim.RegisterSystem(new PlayerMovementSystem());
            sim.RegisterSystem(new ItemGravitySystem());
            sim.RegisterSystem(new PickupSystem());
            sim.RegisterSystem(new CropGrowthSystem());
            sim.RegisterSystem(new CraftingSystem());

            WorldGen.GenerateTerrain(sim, new KnownTiles
            {
                Air = _defs.TileIds["air"],
                Dirt = _defs.TileIds["dirt"],
                Stone = _defs.TileIds["stone"],
                Tree = _defs.TileIds["tree"],
            });

            // 出生点：世界初始化阶段生成（确定性 setup，与回放一致）
            _playerId = WorldGen.SpawnDefaultPlayer(sim, 0);

            // 初始物资：3 种子（种地）+ 6 木料（工作台×1 + 平台×4）+ 3 小麦（开局即可体验加工）
            var startInv = sim.Inventories.GetOrCreate(_playerId);
            startInv.AddItem(_defs.ItemIds["wheat_seed"], 3);
            startInv.AddItem(_defs.ItemIds["wood"], 6);
            startInv.AddItem(_defs.ItemIds["wheat"], 3);
            return sim;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F5)) SaveGame();
            if (Input.GetKeyDown(KeyCode.F9)) LoadGame();
        }

        /// <summary>自动存档间隔（tick）：3000 tick = 100 秒（M2.0；防手滑忘按 F5）。</summary>
        private const long AutoSaveIntervalTicks = 3000;
        private long _nextAutoSaveTick = AutoSaveIntervalTicks;

        private void FixedUpdate()
        {
            _sim.Step();
            _tiles.SyncTick();
            _entities.SyncTick();
            if (_sim.Tick % 30 == 0)
            {
                _hash = _sim.ComputeHash();
            }
            if (_sim.Tick >= _nextAutoSaveTick)
            {
                AutoSave();
                _nextAutoSaveTick = _sim.Tick + AutoSaveIntervalTicks;
            }
        }

        /// <summary>自动存档（覆盖单槽，与手动 F5 同格式；失败不中断游戏）。</summary>
        private void AutoSave()
        {
            try
            {
                File.WriteAllText(SavePath, SaveLoad.Serialize(_sim));
                _statusMsg = "自动存档: tick " + _sim.Tick + " → " + SavePath;
            }
            catch (Exception ex)
            {
                _statusMsg = "自动存档失败: " + ex.Message;
            }
        }

        private void BuildRenderingStack(Defs defs)
        {
            // 正交相机（横板侧视）：初始看向出生点，运行后由输入路由接管跟随
            var camGo = new GameObject("SimCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = cameraOrthoSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PlaceholderSprites.ParseColor("#9AD1E8"); // 天空
            cam.transform.position = new Vector3(0f, 6f, -10f);
            cam.farClipPlane = 50f;

            // Grid + Tilemap
            var gridGo = new GameObject("Grid");
            gridGo.AddComponent<Grid>();
            var mapGo = new GameObject("Tiles");
            mapGo.transform.SetParent(gridGo.transform, false);
            var map = mapGo.AddComponent<Tilemap>();
            var mapRenderer = mapGo.AddComponent<TilemapRenderer>();
            mapRenderer.sortingOrder = 0;
            _map = map;
            _palette = new TilePalette(defs);

            // 渲染/输入组件（挂在引导物体上，与引导同生命周期）；
            // 对模拟实例的绑定在 BindSimulation（开新局/读档共用）
            _tiles = gameObject.AddComponent<WorldTilemapRenderer>();
            _entities = gameObject.AddComponent<EntitySpriteRenderer>();
            _input = gameObject.AddComponent<PlayerInputRouter>();
            _input.Init(cam, defs);
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            GUI.Label(new Rect(8, 8, 720, 20),
                "Tick=" + _sim.Tick
                + "  Entities=" + _sim.Entities.Count
                + "  WorldHash=" + _hash.ToString("X16")
                + "  Player=#" + _playerId);
            GUI.Label(new Rect(8, 28, 720, 20),
                "A/D 移动  Space/W 跳跃  左键挖掘(可按住)  右键放置  1-9 切换方块"
                + "  F 切换配方  C 在最近工作台加工  F5 存档  F9 读档");
            if (!string.IsNullOrEmpty(_statusMsg))
            {
                GUI.Label(new Rect(740, 8, 520, 36), _statusMsg);
            }

            // 快捷栏：可放置方块 + 所需材料持有量（不足标红）
            var hotbar = "选中方块: " + _input.SelectedTileName;
            ushort required = _input.SelectedRequiredItemId;
            if (required != 0)
            {
                int have = _sim.Inventories.TryGet(_playerId, out var inv) ? inv.CountOf(required) : 0;
                hotbar += _input.SelectedHasMaterial
                    ? "  (需 " + ItemName(required) + " x" + NeedCount(SelectedTileIdForCount) + ", 持有 " + have + ")"
                    : "  (缺 " + ItemName(required) + "!)";
            }
            GUI.Label(new Rect(8, 48, 720, 20), hotbar);

            // 配方行：当前配方 + 材料持有量 + 最近工作台状态
            var recipe = _input.SelectedRecipe;
            if (recipe != null)
            {
                string inputs = "";
                for (int i = 0; i < recipe.InputItemIds.Length; i++)
                {
                    int have = _sim.Inventories.TryGet(_playerId, out var rInv)
                        ? rInv.CountOf(recipe.InputItemIds[i]) : 0;
                    inputs += (i > 0 ? " + " : "") + ItemName(recipe.InputItemIds[i])
                        + " x" + recipe.InputCounts[i] + " (持有" + have + ")";
                }
                var wb = _input.NearestWorkbench;
                string recipeLine = "配方: " + RecipeLabel(recipe)
                    + " = " + ItemName(recipe.OutputItemId) + " x" + recipe.OutputCount
                    + "  [" + inputs + "]"
                    + (_input.SelectedHasRecipeMaterials ? "" : "  材料不足")
                    + (wb.HasValue ? "  工作台" + wb.Value : "  附近无工作台");
                GUI.Label(new Rect(8, 68, 900, 20), recipeLine);
            }

            // 工作台任务进度
            var jobKeys = _sim.Crafting.OrderedKeys();
            int jobRow = 0;
            for (int j = 0; j < jobKeys.Count && jobRow < 4; j++)
            {
                CraftJob job;
                if (!_sim.Crafting.TryGet(jobKeys[j], out job)) continue;
                if (job.RecipeIndex < 0 || job.RecipeIndex >= _sim.Recipes.Length) continue;
                var jobRecipe = _sim.Recipes[job.RecipeIndex];
                var jobPos = CraftingStore.Unpack(jobKeys[j]);
                int total = jobRecipe.CraftTicks < 1 ? 1 : jobRecipe.CraftTicks;
                GUI.Label(new Rect(8, 88 + jobRow * 18, 900, 18),
                    "加工中: " + RecipeLabel(jobRecipe) + " @ " + jobPos
                    + "  进度 " + (total - job.RemainingTicks) + "/" + total);
                jobRow++;
            }

            // 背包面板：非空槽位列表
            int row = 0;
            int bagY = 88 + 4 * 18 + 4;
            if (_sim.Inventories.TryGet(_playerId, out var bag))
            {
                for (int i = 0; i < Inventory.SlotCount; i++)
                {
                    if (bag.Counts[i] == 0) continue;
                    GUI.Label(new Rect(8, bagY + row * 18, 480, 18),
                        "[" + (i + 1) + "] " + ItemName(bag.ItemIds[i]) + " x" + bag.Counts[i]);
                    row++;
                }
            }
        }

        private ushort SelectedTileIdForCount => _input.SelectedTileId;

        private int NeedCount(ushort tileId)
        {
            return tileId < _sim.TileRules.Length && _sim.TileRules[tileId].RequiredItemId != 0
                ? (_sim.TileRules[tileId].RequiredItemCount < 1 ? 1 : _sim.TileRules[tileId].RequiredItemCount)
                : 1;
        }

        private string RecipeLabel(RecipeDef recipe)
        {
            // 配方 ID craft_bread → "面包(面包)"？用输出物名做主标签
            return ItemName(recipe.OutputItemId);
        }

        private string ItemName(ushort itemId)
        {
            return itemId < _itemNames.Length ? _itemNames[itemId] : "?";
        }
#endif
    }
}
