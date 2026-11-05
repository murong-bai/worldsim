# 横板建造游戏（代号待定）

横板像素风轻模拟建造经营游戏，支持合作共建联机。设计与架构基准见 [docs/GDD.md](docs/GDD.md)（v0.3，M2 规划冻结）。

**当前状态：M2 可玩 Alpha 开发中 —— M2.0 技术清债 ✅ + M2.1 内容 T0–T1 ✅（2026-11-05：全局参数/矿脉 world.json 数据驱动、世界 512 格、自动存档、相机钳制、铜线/食物线/石砖等内容），92/92 项测试通过。下一子阶段：M2.2 小人任务 AI。**

---

## 目录结构

```
docs/GDD.md                    玩法与架构设计文档（基准文件）
UnityProject/                  Unity 2022.3 LTS 工程
  Assets/_Sim/                 纯 C# 模拟核心（无 UnityEngine 依赖，可独立编译测试）
    Sim.Core/                  世界/区块/实体/定点数/确定性随机/命令队列/模拟根/结构支撑
    Sim.Commands/              具体命令（放置/挖掘/生成/移动/输入/合成）+ 编解码器 + 存档序列化
    Sim.Data/                  零依赖 JSON 解析 + 方块/物品/配方定义加载
    Sim.Systems/               模拟系统（玩家物理 / 作物生长 / 掉落物重力 / 加工推进）
  Assets/_Client/              表现层（SimBootstrap / 渲染栈 / PlayerInputRouter 输入路由）
  Assets/Content/Defs/         内容定义（tiles/items/recipes/world JSON；world 为全局参数）
tests/SimHarness/              确定性验证 harness（net8.0 控制台，CI 友好）
```

## 快速开始

### 跑确定性测试（无需 Unity）

```bash
dotnet run --project tests/SimHarness              # 全部 92 项测试（exit code 0 门禁）
dotnet run --project tests/SimHarness -- report    # 固定剧本哈希报告（CI 双平台比对物）
```

报告模式输出 `hash-report.txt`（`worldsim-ci 1` 格式）：主线采样
`hash@0/300/600/900/1200` + `reload@600` / `reload_continue@1200` / `replay@1200`
四条路径的 WorldHash。报告全 ASCII、LF 换行、UTF-8 无 BOM —— 双平台产物可逐字节 diff。

exit code 0 = 全部通过。覆盖：定点数运算、确定性随机、定义加载与 ID 稳定性、
命令流水线（放置/挖掘/掉落）、掉落物重力、作物生长与收获、
**玩家角色物理**（落地/行走/停止/跳跃/撞墙/平台单向支撑）、**输入命令与权威端校验**
（射程校验、非玩家实体拒写）、**背包与拾取**（自动拾取/堆叠/分槽/全背包饱和余量留存/
放置材料消耗/材料不足拒绝/挖掘拾取闭环）、**生产链加工**（工作台放置/合成权威校验/
任务占用/产出数量/挖台作废/加工确定性双实例/**种植→收获→加工→面包全链闭环**）、
**支撑规则**（悬空拒绝不扣料/悬链跨度 6/垂直塔不限/挖柱整链级联坍塌等值回收/
断链近端稳远端塌/级联双实例哈希一致）、
**存档/读档**（头信息一致性/磁盘文件往返/读档重建至存档点/**读档后继续游玩与原局一致**/
损坏头与空文件拒绝）、
**同种子同命令双实例哈希一致**（联机可行性根基）、
**种子+命令日志回放一致**（存档格式验证）。

> 联机前置验收（GDD §3.3）：CI 需在 Windows/Linux 双平台跑同一命令脚本并比对哈希输出。

## CI：双平台确定性哈希比对（GitHub Actions）

`.github/workflows/ci.yml`，push / PR / 手动触发：

1. **harness job**（`ubuntu-latest` + `windows-latest` 双矩阵）：跑 92 项测试（exit code 门禁）
   → 报告模式生成各自平台的 `hash-report-<os>.txt` → 上传 artifact；
2. **compare job**：下载双平台报告并 `diff` —— **任何一行哈希不一致即构建失败**
   （存在平台相关的不确定性 = 合作共建联机的红线，GDD §3.3）。

启用方式：把本仓库推到 GitHub 即生效（Actions 免费额度内）。本地已验证：Windows 端
双运行报告逐字节一致，且四条路径哈希收敛（`reload@600 == hash@600`、
`reload_continue@1200 == replay@1200 == hash@1200`）。

### 打开 Unity 工程（需先安装 Unity 2022.3 LTS + Unity Hub）

1. Unity Hub → 打开 `UnityProject/`（首次打开会自动补全默认包与 meta 文件）
2. 新建空场景 → 创建空 GameObject → 挂载 `SimBootstrap` 组件 → Play
3. 应看到：横板地形（占位图块）、玩家色块出生在地表、天空色背景、鼠标格高亮、
   编辑器左上角实时 `Tick/Entities/WorldHash/Player`

**可玩操作（PlayerInputRouter，已接入命令管线）**：
- `A/D` 或 `←/→`：移动（碰撞/重力/台阶需跳跃，由 PlayerMovementSystem 权威模拟）
- `Space` / `W` / `↑`：跳跃（跳高约 2.4 格，可上 2 格台阶；按住落地即连跳）
- **鼠标左键**：挖掘（可按住连挖，0.22s 间隔；光标高亮变红 = 超出射程 5.5 格）
- **鼠标右键**：放置当前选中方块（**消耗背包内所需材料**，不足时权威端拒绝；
  platform 受支撑规则约束，高亮变黄 = 该格无支撑）
- `1-9`：切换可放置方块（选中方块下方显示所需材料与持有量）
- `F`：切换合成配方（面板显示材料需求与持有量）
- `C`：对射程内最近的工作台开始加工（权威端校验材料并原子扣除）
- **自动拾取**：走到掉落物附近（半径 1.5 格）自动收入背包；左上角面板显示背包内容
- `F5`：存档（写 `persistentDataPath/slot1.sav`）；`F9`：读档（重建世界 + 命令重放到存档点）
  读档在单帧内完成重放，长档会有一瞬卡顿（后续可优化为周期快照 + 增量回放）

**玩法闭环（已可体验）**：开局 3 种子 + 6 木料 + 3 小麦 → 挖泥土/砍树 → 拾取材料 →
搭立柱 + 平台（**平台需支撑：悬链最多伸出 6 格必须回柱，垂直堆叠不限**；柱被挖掉会级联坍塌）→
放工作台（消耗木料 x2）→ 种小麦（消耗种子）→ 等待成熟（由绿转金）→
收获 2 小麦 + 1 种子（可持续扩种）→ 工作台加工面包（小麦 x3，30 tick 后弹出产物）或石砖（石块 x3 → 石砖 x2）。

输入模型说明（GDD §3.2/§3.6 的联机同构设计）：客户端仅在**按键状态变化时**发送
`SetPlayerInputCommand`（每秒数条，而非每帧一条），角色物理完全在模拟核心内以 Fix 定点数推进；
挖掘/放置命令携带 `ByEntityId`，权威端校验归属与射程后执行 —— 单人与联机走同一套命令管线。

**渲染已实现的内容**：程序化占位图块（16px Point 采样、按方块名确定性噪点纹理；
platform 自动画成半格平台）；`WorldTilemapRenderer` 全量首绘 + 每 tick 增量刷新
（数据源为 `Sim.World.Changes`，同一份日志未来直接供联机增量广播）；
作物按生长进度从绿渐变到金；`EntitySpriteRenderer` 精灵池渲染掉落物下落。
正式像素美术到位后仅需替换 `TilePalette` / `PlaceholderSprites` 的资产来源。

**注意事项**：
- 若方块/实体显示为粉色（URP 材质未就绪），在编辑器中选中 Tiles 对象 → TilemapRenderer → Material 赋 `Universal Render Pipeline/2D-Sprite`（或切换工程到 Built-in 渲染管线）；
- 相机为运行时程序创建（SimBootstrap 内），无需手动摆放。

## 确定性编码规约（GDD §3.3 摘要）

- 一切非整数运算用 `Fix`（Q16.16），禁止 float/double 参与模拟
- 随机只用 `SimRandom`（splitmix64），禁止 System.Random
- 遍历 `Dictionary/HashSet` 禁止直接 foreach，走 `InDeterministicOrder()` / `ChunksInDeterministicOrder()`
- 系统执行顺序 = 注册顺序；真实时间不参与模拟（只有逻辑帧）
- 每次改动模拟逻辑后跑 harness；哈希值变化 = 行为变化，需要在提交说明中记录

## 存档格式（GDD §3.5）

```
worldsim|1|{种子}|{存档时tick}     ← 头行：魔数|版本
{tick}|{type}|{args...}            ← 命令行，与 CommandCodec / 联机命令传输同格式
```

- 存档 = 种子 + 已执行命令日志（`SaveLoad.Serialize/Parse/ReplayInto`，纯文本）；
- 读档 = 同种子重建世界（确定性 setup）→ 逐 tick 重放到存档点。文件极小且天然防作弊
  （命令流可完整审计），录像/分享/断线重连未来复用同一机制；
- 只记录已执行命令：读档瞬间挂起的输入会丢失，客户端读档后强制重发输入状态
  （`PlayerInputRouter.ForceResend`）；
- 头损坏 / 版本不匹配 / 空文件 → `FormatException` 拒绝加载。

## M1 剩余任务（垂直切片）

- [x] Tilemap 渲染映射（tileId → 占位 Tile，chunk 全量首绘 + WorldChanges 增量刷新，作物状态着色）
- [x] 角色操控输入 → 命令（移动/跳跃/挖掘/放置 + 权威端射程校验；触控 = 替换 PlayerInputRouter 的输入采集）
- [x] 拾取掉落物 + 背包（InventoryStore 入哈希、PickupSystem 自动拾取、放置材料消耗、种子返还闭环）
- [x] 一条完整生产链闭环（种植 → 收获 → 加工：工作台 tile + CraftingStore 任务板 + CraftCommand 权威合成 + CraftingSystem 推进产出 + F/C 客户端交互）
- [x] 支撑规则（StructuralSupport：BFS 锚点+跨度判定、放置校验在材料消耗前、挖掘级联坍塌等值回收、塌落自动进 WorldChanges）
- [x] 存档/读档（SaveLoad：种子 + 命令日志文本格式，F5/F9，读档后继续游玩与原局一致）
- [x] CI：双平台跑 harness 比对哈希（GitHub Actions workflow + harness 报告模式；本地 WSL 被安全策略拦截，Linux 端验证由 CI 承担）

## M1 收尾（工程代码之外）

- [ ] Unity 编辑器内全流程真机验证：挂 `SimBootstrap` → Play → 挖/建/种/加工 → F5 存档 → F9 读档继续玩
- [ ] 推送 GitHub 激活 CI，确认双平台哈希比对绿灯

## 版本记录

- 2026-11-05 **M1 收官 ✅**：推送 GitHub（`murong-bai/worldsim`）后 CI 双平台哈希比对**全绿**（ubuntu + windows 各 81/81 + compare 一致）——联机可行性双平台实证；Unity 真机编译/渲染验证通过；GDD §4 里程碑表标记 M1 ✅
- 2026-11-05 M1-CI：harness 报告模式（`dotnet run -- report`，固定剧本四路径哈希采样：主线/读档重建/读档继续/编解码回放；报告全 ASCII/LF/UTF-8 无 BOM，可逐字节 diff）；GitHub Actions 双平台 workflow（ubuntu+windows 矩阵 + compare job diff 报告，不一致即红）；git 初始化首次提交。**哈希不变**（零模拟逻辑改动）；本地验证：双运行报告逐字节一致、四路径哈希收敛（`66672E7D…`）

- 2026-11-05 M1-存档：Sim.Commands/SaveLoad（`worldsim|1|seed|tick` 头 + 命令行，与联机命令同格式；Serialize/Parse/ReplayInto）；客户端 F5 存档 / F9 读档（persistentDataPath/slot1.sav，单槽），读档 = 同种子重建 + 重放 + 全量重绘 + 输入强制重发；SimBootstrap 重构（BuildSimulation/BindSimulation 提取，开新局与读档共用）；PlayerInputRouter 拆分 Init（非模拟依赖）/Rebind/ForceResend；harness 81/81（存档专项 4 项：头信息一致/读档重建/读档后继续与原局一致/损坏拒绝）；**哈希不变**（零模拟逻辑改动）

- 2026-11-05 M1-支撑：StructuralSupport（受支撑 = BFS 沿平台邻接图距锚点水平步 ≤6，垂直堆叠不计步；锚点 = 下方固体的平台）；TileRule 加 NeedsSupport（platform 开启）；PlaceTileCommand 支撑校验在材料消耗前（creative 同样生效）；DigTileCommand 触发级联坍塌（失撑平台塌落为等值掉落物，固定上→左→右入队序，走 WorldChanges 自动同步）；客户端放置预检 + 高亮黄色提示；harness 77/77（支撑专项 9 项）；**哈希基准更新**（级联入哈希：确定性 `35C39936…`、回放 `66672E7D…`）

- 2026-11-05 M1-加工：工作台 tile（placeCount=2、workbench 标志）、RecipeDef 移入 Sim.Core（数字 ID 版）、CraftingStore 加工任务板（long 打包坐标、排序遍历，入 WorldHash）、CraftCommand（玩家/射程/工作台/任务槽/材料五重权威校验，注册 Codec）、CraftingSystem（倒计时完成弹出产物、与拾取闭环）、recipes.json outputCount；初始物资 3 种子 + 6 木料 + 3 小麦；harness 68/68（含种植→收获→加工→面包全链闭环）；**哈希基准更新**（CraftingStore 入哈希 + 初始物资：确定性 `402274A7…`、回放 `6535872C…`）

- 2026-11-05 M1-背包：InventoryStore（10 槽×50 堆叠，入 WorldHash）、PickupSystem（半径 1.5 自动拾取、余量留存）、PlaceTile 材料消耗（tiles.json placeItem）、成熟收获 2 产物 + 1 种子（wheat_seed）、初始物资 3 种子 2 木料、背包 GUI 面板；harness 47/47；**哈希基准更新**（背包入哈希：确定性 `A2D768F4…`、回放 `2710D319…`）
- 2026-11-05 M1-输入：PlayerMovementSystem 玩家物理（Fix 定点、X/Y 碰撞解析、平台单向支撑）、SetPlayerInputCommand 输入命令、Dig/Place 归属+射程权威校验、PlayerInputRouter 客户端输入路由与相机跟随；harness 34/34（含玩家物理 10 项）；**哈希基准更新**（EntityState 扩展物理字段参与哈希：确定性 `0CE9A9C8…`、回放 `C5F315BA…`）
- 2026-11-05 M1-渲染：WorldChanges 变更日志（渲染/联机共用）、客户端渲染栈（TilePalette / WorldTilemapRenderer / EntitySpriteRenderer / 程序化占位精灵），harness 24/24 复验通过、哈希不变
- 2026-11-05 M1 骨架：Sim.Core / Sim.Commands / Sim.Data / Sim.Systems + harness，24/24 通过
- 2026-11-05 M0 设计冻结：GDD v0.2
