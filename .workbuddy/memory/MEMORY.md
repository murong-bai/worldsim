# 项目记忆（长期）

## 项目：横板建造游戏（代号待定）

**定位**：横板像素风轻模拟建造经营，参考《多洛可小镇》（循环调性、垂直平台堆叠）与《缺氧》（物流/自动化深度，不做物理模拟）。路线：PC (Steam) → 移动端 → 联机。

**已冻结决策**：
1. 玩法方向：轻模拟 + 建造（物品物流、电网等轻量系统；明确不做逐格气体/流体/热力学）
2. 引擎：Unity，URP 2D，Tilemap + Cinemachine
3. 联机：合作共建同一基地（2~4 人），权威主机 listen server，命令广播 + 确定性并行模拟 + 周期 World Hash 校验 + 快照纠偏/中途加入
4. 架构铁律：逻辑/表现分离（_Sim 纯 C# asmdef 无 UnityEngine 引用）；一切操作皆 Command；30Hz 固定逻辑帧；确定性规范（整数/定点数、受控随机、禁无序遍历）；存档 = 种子 + 命令回放；内容数据驱动（JSON）
5. 联机架构必须在 M1 垂直切片就埋（CI 双平台确定性哈希测试），网络层 M3 才实现
6. 玩家视角：角色操控 + 建造模式（非上帝视角）
7. 联机时间控制：仅房主可开启暂停/加速，全端同步（档位 1×/2×/3×，切换需预告）
8. 世界生成：固定手工主基地 + 程序生成郊外（种子驱动）
9. 商业模式：全平台买断制（移动端不做 F2P）
10. 美术风格：像素风（16×16 基准格 + 有限调色板）

**基准文档**：docs/GDD.md（v0.2，2026-11-05，M0 设计冻结完成）——重大决策变更需更新此文档并记录版本。

**工程状态（2026-11-05）**：M1 工程代码全部完成（骨架/渲染/角色操控/背包拾取/生产链加工/支撑规则/存档读档/CI），git 已初始化（main，首次提交 9dacac4）。收尾待办：用户 Unity 真机全流程验证（工作区已出现 Library/.meta——用户已开过工程）、推 GitHub 激活 CI。`UnityProject/Assets/_Sim`（纯 C# 四程序集）+ `tests/SimHarness`（确定性测试 81/81 通过）。跑测试：`dotnet run --project tests/SimHarness`（本机 bash 需用全路径 `/c/Program Files/dotnet/dotnet.exe`）；报告模式：`dotnet run -- project tests/SimHarness -- report [路径]`（固定剧本四路径哈希，ASCII/LF/UTF-8 无 BOM 可跨平台 diff）。CI：`.github/workflows/ci.yml`（ubuntu+windows 矩阵 + compare job diff 报告）；本机 WSL 被安全策略黑名单拦截，Linux 端验证靠 CI。命令行格式 `tick|type|args`（CommandCodec），新命令必须注册进 Codec。改动模拟逻辑后必须重跑 harness；提交说明需记录哈希变化（M1-支撑 后基准：确定性 `35C39936…`、回放 `66672E7D…`；M1-存档/CI 零模拟改动、哈希不变）。角色操控模型：客户端仅按键变化时发 `SetPlayerInputCommand`，物理全在 `PlayerMovementSystem`（Fix 定点、X/Y 碰撞、平台单向支撑）；挖/放命令带 `ByEntityId` 走射程校验（5.5 格）。背包：`Simulation.Inventories`（InventoryStore 按实体 Id，10 槽×50 堆叠，入 WorldHash）；`PickupSystem` 半径 1.5 自动拾取（满包余量留在地上，腾出空槽会回流拾取）；放置消耗 `TileRule.RequiredItemId`×`RequiredItemCount`（tiles.json "placeItem"/"placeCount"）。加工：工作台为 tile（"workbench":true）；`RecipeDef` 在 Sim.Core（数字 ID）；`Simulation.Crafting`（CraftingStore：格子→任务，long 打包坐标 key、OrderedKeys 排序遍历，入 WorldHash）；`CraftCommand`（必须带 ByEntityId，五重校验：玩家/射程/工作台/任务槽占用/材料原子消耗）；`CraftingSystem` 倒计时完成在工作台上方弹 DroppedItem 产物；挖掉工作台任务作废；配方 outputCount 支持多产出。支撑规则（GDD §2.2）：`Sim.Core/StructuralSupport`（IsSupported = BFS 沿平台邻接图〔左右 platform 水平步+1、下方 platform 垂直不计步〕到达锚点〔下方是固体的平台〕且水平步 ≤ MaxHorizontalSpan=6 常量；垂直塔不限高）；TileRule.NeedsSupport（仅 platform 开启，solid 柱自由放置且可作锚）；PlaceTileCommand 支撑校验在材料消耗之前（creative 路径同样生效）；DigTileCommand 挖掘后 `OnTileRemoved` 级联坍塌（固定上→左→右入队序，失撑平台塌落为 DropItemId×1 等值掉落物，塌落走 World.SetTile 自动进 WorldChanges 供渲染/联机同步）；直接 World.SetTile（世界构造/测试）不走校验与级联。成熟收获=harvest×2+种子×1；初始物资 3 种子+6 木料+3 小麦（BuildSim 与 SimBootstrap 一致）。实体位置唯一权威源 `FixX/FixY`（X=碰撞盒中心，Y=底边），`Pos` 派生。Unity 客户端：空场景挂 `_Client/SimBootstrap`（A/D 移动、Space 跳、左挖右放、1-9 换方块、F 换配方、C 最近工作台加工、自动拾取、OnGUI 背包/配方/任务进度面板、放置高亮超程红/无支撑黄）；本机未装 Unity，表现层待用户开工程验证；URP 材质问题见 README。`World.Changes` 为渲染/联机共用每 tick 变更日志。存档（GDD §3.5）：`Sim.Commands/SaveLoad`（文本格式：头行 `worldsim|版本|种子|tick` + 命令行，与联机命令同格式；Serialize/Parse/ReplayInto，文件 IO 由调用方负责）；读档 = 同种子重建世界（确定性 setup）+ 逐 tick 重放至存档点（单帧内完成，长档有一瞬卡顿，后续可优化为周期快照+增量回放）；只记录已执行命令——挂起输入会丢，读档后 `PlayerInputRouter.ForceResend` 重发；头损坏/版本不符抛 FormatException。客户端 F5 存档 / F9 读档（`persistentDataPath/slot1.sav` 单槽）；SimBootstrap 拆分 BuildSimulation/BindSimulation（开新局与读档共用装配）；PlayerInputRouter 拆分 Init（仅非模拟依赖：相机/Defs/快捷栏/高亮）与 Rebind（sim+playerId）。M1 剩余：CI 双平台。

**用户工作习惯**：先说明思路、执行前征求确认（custom instructions）。
