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
11. 小人移动能力：不能跳跃，仅攀爬梯子 + 跨上一格高方块（寻路图简化；玩家保留跳跃 = 能力超集，同一 PlayerMovementSystem 复用）
12. 里程碑仅作提示与引导，不作蓝图/配方解锁条件（解锁一律走科技树）

**基准文档**：docs/GDD.md（v0.3，2026-11-05，M2 规划冻结含 §4.1 细化拆解；§4 已标 M1 ✅）——重大决策变更需更新此文档并记录版本。

**工程状态（2026-11-05）**：M1 完成 ✅ + **M2.0 技术清债完成 ✅**（提交 `bf8e45d` 已推送）——全局参数数据驱动（GlobalDef + world.json：跨度 6/世界 -256..255 共 512 格 16 chunk）、自动存档（客户端每 3000 tick 覆盖单槽）、相机水平钳制。测试 **84/84**；哈希基准：确定性 `75F4C70DE68B2F55`、回放 `A36E1F63C035FEE2`（世界扩 512 格所致，属预期）。GlobalDef.cs/world.json 的 .meta 待用户开 Unity 后补提交。远端仓库 `https://github.com/murong-bai/worldsim`（GitHub 账号 murong-bai）。下一步：M2.1 内容 T0–T1（石/木/食物三线 + 铜煤矿脉，验收=零核心代码改动）→ M2.2 小人任务 AI（确定性 A*，无跳跃）→ M2.3 电力。

**git 推送要点（本机网络特殊，重要）**：本机走 Watt Toolkit（SteamTools）系统代理（127.0.0.1:26561，**端口可能随重启变化**，失效时查注册表 `HKCU\...\Internet Settings\ProxyServer`），对 HTTPS 做 MITM。git 需 OpenSSL 后端 + 信任 SteamTools 根证书（`steamtools-root.pem` 在工作区根目录、已 gitignore），三参数已写入**仓库本地 git config**（http.proxy / http.sslBackend / http.sslCAInfo）——config 优先级高于环境变量，所以 agent shell 里直接 `git push` 也能通。推送授权已缓存（GCM）。`api.github.com` 不被 Watt Toolkit 加速（查询 CI 状态用浏览器看 Actions 页）。仓库 git 身份：muron / muron@local。

**开发规范**：跑测试：`dotnet run --project tests/SimHarness`（本机 bash 需用全路径 `/c/Program Files/dotnet/dotnet.exe`）；报告模式：`dotnet run --project tests/SimHarness -- report [路径]`（固定剧本四路径哈希，ASCII/LF/UTF-8 无 BOM 可跨平台 diff）。CI：`.github/workflows/ci.yml`（ubuntu+windows 矩阵 + compare job diff 报告）；本机 WSL 被安全策略黑名单拦截，Linux 端验证靠 CI。命令行格式 `tick|type|args`（CommandCodec），新命令必须注册进 Codec。改动模拟逻辑后必须重跑 harness；提交说明需记录哈希变化（当前基准 M2.0 后：确定性 `75F4C70D…`、回放 `A36E1F63…`；历史：M1 收官 `35C39936…`/`66672E7D…` → M2.0 世界扩 512 格变更）。

**各系统实现要点**：
- 角色操控：客户端仅按键变化时发 `SetPlayerInputCommand`，物理全在 `PlayerMovementSystem`（Fix 定点、X/Y 碰撞、平台单向支撑）；挖/放命令带 `ByEntityId` 走射程校验（5.5 格）——联机权威端校验同构。实体位置唯一权威源 `FixX/FixY`（X=碰撞盒中心，Y=底边），`Pos` 派生。
- 背包：`Simulation.Inventories`（InventoryStore 按实体 Id，10 槽×50 堆叠，入 WorldHash）；`PickupSystem` 半径 1.5 自动拾取（满包余量留地上，腾空槽回流拾取）；放置消耗 `TileRule.RequiredItemId`×`RequiredItemCount`（tiles.json "placeItem"/"placeCount"）。
- 加工：工作台为 tile（"workbench":true）；`RecipeDef` 在 Sim.Core（数字 ID）；`Simulation.Crafting`（CraftingStore：格子→任务，long 打包坐标 key、OrderedKeys 排序遍历，入 WorldHash）；`CraftCommand`（必须带 ByEntityId，五重校验：玩家/射程/工作台/任务槽占用/材料原子消耗）；`CraftingSystem` 倒计时完成在工作台上方弹 DroppedItem 产物；挖掉工作台任务作废；配方 outputCount 多产出。
- 支撑（GDD §2.2）：`Sim.Core/StructuralSupport`（IsSupported = BFS 沿平台邻接图〔左右 platform 水平步+1、下方 platform 垂直不计步〕到达锚点〔下方是固体的平台〕且水平步 ≤ MaxHorizontalSpan=6；垂直塔不限高）；TileRule.NeedsSupport（仅 platform）；放置校验在材料消耗之前（creative 同样生效）；挖后 `OnTileRemoved` 级联坍塌（固定上→左→右入队序，塌落为等值掉落物，走 World.SetTile 进变更日志）。
- 存档（GDD §3.5）：`Sim.Commands/SaveLoad`（文本格式头行 `worldsim|版本|种子|tick` + 命令行，与联机命令同格式）；读档 = 同种子重建 + 逐 tick 重放（单帧完成）；只记已执行命令，读档后 `PlayerInputRouter.ForceResend` 重发输入；损坏头抛 FormatException。客户端 F5/F9（persistentDataPath/slot1.sav 单槽）。
- 作物：成熟收获 = harvest×2 + 种子×1；初始物资 3 种子 + 6 木料 + 3 小麦（BuildSim 与 SimBootstrap 一致）。
- 客户端：空场景挂 `_Client/SimBootstrap`（A/D 移动、Space 跳、左挖右放、1-9 换方块、F 换配方、C 最近工作台加工、自动拾取、OnGUI 背包/配方/任务进度面板、放置高亮超程红/无支撑黄）。SimBootstrap 拆分 BuildRenderingStack/BuildSimulation/BindSimulation（开新局与读档共用）；PlayerInputRouter 拆 Init/Rebind。
- `World.Changes` 为渲染/联机共用每 tick 变更日志（改动世界必须走 World.SetTile/SetState 才能被渲染/同步感知；直接 SetTile 不走校验与级联）。

**用户工作习惯**：先说明思路、执行前征求确认（custom instructions）。
