using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sim.Commands;
using Sim.Core;
using Sim.Data;
using Sim.Systems;

namespace SimHarness
{
    /// <summary>
    /// M1 确定性验证 harness（GDD §4 M1 验收标准）。
    /// 零第三方依赖：直接编译 _Sim 纯 C# 源码运行，exit code 0 = 全部通过（CI 友好）。
    /// 跨平台一致性：CI 需在 Windows/Linux 双平台跑同一命令脚本并比对哈希（GDD §3.3）。
    /// </summary>
    internal static class Program
    {
        private static int _total;
        private static int _failed;

        private static void Check(bool condition, string name)
        {
            _total++;
            if (condition)
            {
                Console.WriteLine("[PASS] " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name);
            }
        }

        private static int Main(string[] args)
        {
            // 报告模式：dotnet run -- report [输出路径]
            // 跑固定剧本并写出规范哈希报告（ASCII、LF 换行、UTF-8 无 BOM），
            // 供 CI 在 Windows/Linux 双平台各跑一份后 diff 比对（GDD §3.3 联机前置验收）。
            if (args.Length > 0 && args[0] == "report")
            {
                RunReport(args.Length > 1 ? args[1] : "hash-report.txt");
                return 0;
            }

            TestFix();
            TestDefLoading();
            TestCommandPipeline();
            TestItemGravity();
            TestCropGrowth();
            TestPlayerMovement();
            TestPickupAndInventory();
            TestCrafting();
            TestStructuralSupport();
            TestDeterminism();
            TestReplay();
            TestSaveLoad();

            Console.WriteLine();
            Console.WriteLine("==== " + (_total - _failed) + "/" + _total + " passed ====");
            return _failed == 0 ? 0 : 1;
        }

        private static Defs LoadDefs()
        {
            return DefLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Defs"));
        }

        private static KnownTiles KnownOf(Defs defs)
        {
            return new KnownTiles
            {
                Air = defs.TileIds["air"],
                Dirt = defs.TileIds["dirt"],
                Stone = defs.TileIds["stone"],
                Tree = defs.TileIds["tree"],
            };
        }

        private static Simulation BuildSim(ulong seed, Defs defs)
        {
            var sim = new Simulation(seed, defs.TileRules, defs.Recipes.ToArray());
            sim.RegisterSystem(new PlayerMovementSystem());
            sim.RegisterSystem(new ItemGravitySystem());
            sim.RegisterSystem(new PickupSystem());
            sim.RegisterSystem(new CropGrowthSystem());
            sim.RegisterSystem(new CraftingSystem());
            WorldGen.GenerateTerrain(sim, KnownOf(defs));
            int pid = WorldGen.SpawnDefaultPlayer(sim, 0);   // 玩家实体 Id 恒为 1（首个实体）

            // 初始物资（与客户端 SimBootstrap 一致：3 种子 + 6 木料 + 3 小麦）
            var inv = sim.Inventories.GetOrCreate(pid);
            inv.AddItem(defs.ItemIds["wheat_seed"], 3);
            inv.AddItem(defs.ItemIds["wood"], 6);
            inv.AddItem(defs.ItemIds["wheat"], 3);
            return sim;
        }

        private static int CountItems(Simulation sim, ushort itemId, int x, int yMin, int yMax)
        {
            int count = 0;
            foreach (var e in sim.Entities.InDeterministicOrder())
            {
                if (e.Kind == (ushort)EntityKind.DroppedItem && e.Aux == itemId &&
                    e.Pos.X == x && e.Pos.Y >= yMin && e.Pos.Y <= yMax)
                {
                    count++;
                }
            }
            return count;
        }

        private static void TestFix()
        {
            var three = Fix.FromInt(3);
            var two = Fix.FromInt(2);
            Check((three / two) * two == three, "Fix: (3/2)*2 == 3");
            Check((Fix.FromInt(7) / two).Raw == (3L << 16) + (1L << 15), "Fix: 7/2 == 3.5 (Q16.16)");
            Check((Fix.FromInt(-4) + Fix.FromInt(1)).Raw == -(3L << 16), "Fix: 负数运算");

            var a = new SimRandom(123).NextFix01();
            var b = new SimRandom(123).NextFix01();
            Check(a == b && a.Raw >= 0 && a.Raw < 65536, "SimRandom: 同种子序列一致，Fix01 范围正确");
        }

        private static void TestDefLoading()
        {
            var defs = LoadDefs();
            Check(defs.TileRules.Length == 9, "Defs: 方块数量 = 9");
            Check(defs.TileIds["air"] == 0, "Defs: air 固定为 0");
            Check(defs.TileRules[defs.TileIds["stone"]].Solid, "Defs: stone 为固体");
            var platform = defs.TileRules[defs.TileIds["platform"]];
            Check(platform.BlocksItems && !platform.Solid, "Defs: platform 拦截掉落物但非固体");
            Check(platform.NeedsSupport, "Defs: platform 受支撑规则约束");
            var crop = defs.TileRules[defs.TileIds["wheat_crop"]];
            Check(crop.GrowthTicks == 150, "Defs: 小麦生长 150 tick");
            Check(crop.HarvestItemId == defs.ItemIds["wheat"], "Defs: 小麦收获物映射到 wheat");
            Check(crop.RequiredItemId == defs.ItemIds["wheat_seed"], "Defs: 小麦种植需 wheat_seed");
            Check(defs.TileRules[defs.TileIds["platform"]].RequiredItemId == defs.ItemIds["wood"],
                "Defs: 平台放置需 wood");
            var workbench = defs.TileRules[defs.TileIds["workbench"]];
            Check(workbench.IsWorkbench && workbench.Solid && workbench.Placeable
                && workbench.RequiredItemId == defs.ItemIds["wood"] && workbench.RequiredItemCount == 2,
                "Defs: 工作台定义（固体可放置，需木料 x2）");
            Check(defs.ItemIds.Count == 7 && defs.Recipes.Count == 2, "Defs: 物品 7 个 / 配方 2 条");

            RecipeDef breadRecipe = null, brickRecipe = null;
            foreach (var r in defs.Recipes)
            {
                if (r.Id == "craft_bread") breadRecipe = r;
                if (r.Id == "craft_stone_bricks") brickRecipe = r;
            }
            Check(breadRecipe != null
                && breadRecipe.InputItemIds.Length == 1
                && breadRecipe.InputItemIds[0] == defs.ItemIds["wheat"] && breadRecipe.InputCounts[0] == 3
                && breadRecipe.OutputItemId == defs.ItemIds["bread"] && breadRecipe.OutputCount == 1
                && breadRecipe.CraftTicks == 30,
                "Defs: 面包配方解析（小麦 x3 → 面包 x1, 30 tick）");
            Check(brickRecipe != null
                && brickRecipe.InputItemIds[0] == defs.ItemIds["stone"] && brickRecipe.InputCounts[0] == 3
                && brickRecipe.OutputItemId == defs.ItemIds["stone_bricks"] && brickRecipe.OutputCount == 2,
                "Defs: 石砖配方解析（石块 x3 → 石砖 x2, outputCount 生效）");

            var defs2 = LoadDefs();
            bool stable = defs.TileNames.Length == defs2.TileNames.Length;
            if (stable)
            {
                for (int i = 0; i < defs.TileNames.Length; i++)
                {
                    if (defs.TileNames[i] != defs2.TileNames[i]) { stable = false; break; }
                }
            }
            Check(stable, "Defs: 数字 ID 分配跨加载稳定");
        }

        private static void TestCommandPipeline()
        {
            var defs = LoadDefs();
            var sim = BuildSim(100, defs);
            ushort platform = defs.TileIds["platform"];

            // 放置目标选在地表上方一格（platform 受支撑规则约束，悬空会被拒）
            int gy = 6;
            while (!sim.IsSolid(new GridPos(0, gy))) gy--;
            var target = new GridPos(0, gy + 1);
            sim.EnqueueCommand(new PlaceTileCommand(target, platform));
            sim.Step();
            Check(sim.World.GetTile(target) == platform, "命令: 放置平台生效（地表承接）");

            sim.EnqueueCommand(new PlaceTileCommand(target, platform));
            sim.Step();
            Check(sim.World.GetTile(target) == platform && sim.Commands.Applied.Count == 2, "命令: 非法重复放置被静默忽略");

            // 挖掘测试列用 x=3：玩家出生在 x=0，原地掉落物会被拾取系统吸收
            int y = 6;
            while (!sim.IsSolid(new GridPos(3, y))) y--;
            var digPos = new GridPos(3, y);
            sim.EnqueueCommand(new DigTileCommand(digPos));
            sim.Step();
            Check(sim.World.GetTile(digPos) == 0, "命令: 挖掘生效");
            bool dropped = CountItems(sim, defs.ItemIds["dirt"], digPos.X, digPos.Y - 4, digPos.Y + 1) > 0
                        || CountItems(sim, defs.ItemIds["stone"], digPos.X, digPos.Y - 4, digPos.Y + 1) > 0;
            Check(dropped, "命令: 挖掘产生掉落物");
            Check(sim.Commands.Applied.Count == 3, "命令: AppliedLog 记录完整 (3 条)");
        }

        private static void TestItemGravity()
        {
            var defs = LoadDefs();
            var sim = BuildSim(200, defs);

            int y = 12;
            while (!sim.IsSolid(new GridPos(5, y))) y--;
            int surfaceY = y;

            int id = sim.Entities.Spawn((ushort)EntityKind.DroppedItem,
                new GridPos(5, surfaceY + 10), defs.ItemIds["stone"], 1, 0);
            for (int i = 0; i < 200; i++) sim.Step();

            sim.Entities.TryGet(id, out var entity);
            Check(entity.Pos.Y == surfaceY + 1,
                "重力: 掉落物停在地表上方 (y=" + entity.Pos.Y + ", 地表=" + surfaceY + ")");
        }

        private static void TestCropGrowth()
        {
            var defs = LoadDefs();
            var sim = BuildSim(300, defs);
            ushort cropId = defs.TileIds["wheat_crop"];

            var pos = new GridPos(-3, 12);
            sim.EnqueueCommand(new PlaceTileCommand(pos, cropId));
            sim.Step();

            int matureState = defs.TileRules[cropId].GrowthTicks;
            for (int i = 0; i < (matureState + 2) * CropGrowthSystem.IntervalTicks + 10; i++) sim.Step();
            Check(sim.World.GetState(pos) >= matureState, "作物: 达到成熟状态");

            sim.EnqueueCommand(new DigTileCommand(pos));
            sim.Step();
            bool hasWheat = false, hasSeed = false;
            foreach (var e in sim.Entities.InDeterministicOrder())
            {
                if (e.Kind == (ushort)EntityKind.DroppedItem && e.Aux == defs.ItemIds["wheat"]) { hasWheat = true; }
                if (e.Kind == (ushort)EntityKind.DroppedItem && e.Aux == defs.ItemIds["wheat_seed"]) { hasSeed = true; }
            }
            Check(hasWheat, "作物: 成熟收获掉落小麦");
            Check(hasSeed, "作物: 成熟收获返还种子(可持续种植)");

            var pos2 = new GridPos(4, 12);
            sim.EnqueueCommand(new PlaceTileCommand(pos2, cropId));
            sim.Step();
            sim.EnqueueCommand(new DigTileCommand(pos2));
            sim.Step();
            int seedlingDrops = 0;
            foreach (var e in sim.Entities.InDeterministicOrder())
            {
                if (e.Kind == (ushort)EntityKind.DroppedItem && e.Pos.X == pos2.X &&
                    e.Pos.Y >= pos2.Y - 2 && e.Pos.Y <= pos2.Y + 2)
                {
                    seedlingDrops++;
                }
            }
            Check(seedlingDrops == 0, "作物: 幼苗铲除无产出");
        }

        /// <summary>专用平坦跑道世界（无地形随机性，聚焦角色物理、背包与加工断言）。</summary>
        private static Simulation BuildRunwaySim(Defs defs, ulong seed)
        {
            var sim = new Simulation(seed, defs.TileRules, defs.Recipes.ToArray());
            sim.RegisterSystem(new PlayerMovementSystem());
            sim.RegisterSystem(new ItemGravitySystem());
            sim.RegisterSystem(new PickupSystem());
            sim.RegisterSystem(new CropGrowthSystem());
            sim.RegisterSystem(new CraftingSystem());
            for (int x = -16; x <= 48; x++)
            {
                sim.World.SetTile(new GridPos(x, 0), defs.TileIds["stone"]);
            }
            return sim;
        }

        private static void TestPlayerMovement()
        {
            var defs = LoadDefs();
            var sim = BuildRunwaySim(defs, 400);
            int pid = sim.Entities.Spawn((ushort)EntityKind.Player, new GridPos(0, 1), 0, 0, 1);

            // 1) 出生下落 → 落地站稳
            for (int i = 0; i < 60; i++) sim.Step();
            sim.Entities.TryGet(pid, out var p1);
            Check(p1.Grounded == 1 && p1.FixY == Fix.FromInt(1), "玩家: 落地在跑道表面站稳 (y=" + p1.FixY + ")");

            // 2) 右移 3 秒
            sim.EnqueueCommand(new SetPlayerInputCommand(pid, 2, 0));
            for (int i = 0; i < 90; i++) sim.Step();
            sim.Entities.TryGet(pid, out var p2);
            Fix dx = p2.FixX - p1.FixX;
            Check(dx > Fix.FromInt(10), "玩家: 右移 3 秒位移 > 10 格 (" + dx + ")");

            // 3) 松开方向键 → 立即停止
            sim.EnqueueCommand(new SetPlayerInputCommand(pid, 0, 0));
            for (int i = 0; i < 5; i++) sim.Step();
            sim.Entities.TryGet(pid, out var p3);
            Fix x3 = p3.FixX;
            for (int i = 0; i < 30; i++) sim.Step();
            sim.Entities.TryGet(pid, out var p4);
            Check(p4.FixX == x3 && p4.Vx == Fix.Zero, "玩家: 松开方向键即停（速度=0）");

            // 4) 跳跃：升高 > 1 格，随后回落原地
            Fix y0 = p4.FixY;
            Fix maxY = y0;
            sim.EnqueueCommand(new SetPlayerInputCommand(pid, 0, 1));
            for (int i = 0; i < 40; i++)
            {
                sim.Step();
                sim.Entities.TryGet(pid, out var pj);
                if (pj.FixY > maxY) maxY = pj.FixY;
            }
            sim.EnqueueCommand(new SetPlayerInputCommand(pid, 0, 0));
            for (int i = 0; i < 60; i++) sim.Step();
            sim.Entities.TryGet(pid, out var pl);
            Check(maxY - y0 > Fix.FromInt(1), "玩家: 跳跃升高 > 1 格 (" + (maxY - y0) + ")");
            Check(pl.Grounded == 1 && (pl.FixY - y0) < Fix.FromRaw(3277), "玩家: 跳跃后回落原地 (y=" + pl.FixY + ")");

            // 5) 撞墙不穿透
            int wx = pl.Pos.X + 3;
            for (int wy = 1; wy <= 12; wy++)
            {
                sim.World.SetTile(new GridPos(wx, wy), defs.TileIds["stone"]);
            }
            sim.EnqueueCommand(new SetPlayerInputCommand(pid, 2, 0));
            for (int i = 0; i < 90; i++) sim.Step();
            sim.Entities.TryGet(pid, out var pw);
            Check(pw.FixX <= Fix.FromInt(wx) - Fix.FromRaw(19661), "玩家: 被石墙挡住不穿透 (x=" + pw.FixX + ", 墙=" + wx + ")");

            // 6) 平台单向支撑：从上方落到 platform 顶
            sim.Entities.TryGet(pid, out var pr);
            int px = pr.Pos.X + 6;
            sim.World.SetTile(new GridPos(px, 5), defs.TileIds["platform"]);
            int pid2 = sim.Entities.Spawn((ushort)EntityKind.Player, new GridPos(px, 9), 0, 0, 1);
            for (int i = 0; i < 90; i++) sim.Step();
            sim.Entities.TryGet(pid2, out var pp);
            Check(pp.Grounded == 1 && pp.FixY == Fix.FromInt(6), "玩家: 落在平台顶面 (y=" + pp.FixY + ")");

            // 7) 挖掘射程：超程被权威端忽略，射程内生效
            var far = new GridPos(pr.Pos.X + 30, 2);
            sim.World.SetTile(far, defs.TileIds["dirt"]);
            sim.EnqueueCommand(new DigTileCommand(far) { ByEntityId = pid });
            sim.Step();
            Check(sim.World.GetTile(far) == defs.TileIds["dirt"], "命令: 超射程挖掘被权威端忽略");

            var near = new GridPos(pr.Pos.X + 2, 2);
            sim.World.SetTile(near, defs.TileIds["dirt"]);
            sim.EnqueueCommand(new DigTileCommand(near) { ByEntityId = pid });
            sim.Step();
            Check(sim.World.GetTile(near) == 0, "命令: 射程内挖掘生效");

            // 8) 输入命令权威校验：非玩家实体拒绝写入
            sim.EnqueueCommand(new SetPlayerInputCommand(pid2 + 100, 2, 1));
            sim.Step();
            sim.Entities.TryGet(pid2, out var pOther);
            Check(pOther.MoveInput == 0 && pOther.JumpInput == 0, "命令: 非玩家实体输入被拒绝");
        }

        private static void TestPickupAndInventory()
        {
            var defs = LoadDefs();
            var sim = BuildRunwaySim(defs, 500);
            int pid = sim.Entities.Spawn((ushort)EntityKind.Player, new GridPos(0, 1), 0, 0, 1);
            for (int i = 0; i < 30; i++) sim.Step();
            var inv = sim.Inventories.GetOrCreate(pid);

            ushort stone = defs.ItemIds["stone"];
            ushort wood = defs.ItemIds["wood"];
            ushort dirt = defs.ItemIds["dirt"];

            // 1) 掉落物落至玩家身边被自动拾取
            sim.Entities.Spawn((ushort)EntityKind.DroppedItem, new GridPos(0, 3), stone, 1, 0);
            for (int i = 0; i < 90; i++) sim.Step();
            Check(inv.CountOf(stone) == 1 && CountDropped(sim) == 0, "背包: 掉落物被自动拾取");

            // 2) 多个同类掉落物堆叠进同一槽
            sim.Entities.Spawn((ushort)EntityKind.DroppedItem, new GridPos(0, 3), stone, 1, 0);
            sim.Entities.Spawn((ushort)EntityKind.DroppedItem, new GridPos(0, 3), stone, 1, 0);
            for (int i = 0; i < 90; i++) sim.Step();
            Check(inv.CountOf(stone) == 3, "背包: 同类物品堆叠 (x" + inv.CountOf(stone) + ")");

            // 3) 不同物品分槽存放
            sim.Entities.Spawn((ushort)EntityKind.DroppedItem, new GridPos(0, 3), wood, 1, 0);
            for (int i = 0; i < 90; i++) sim.Step();
            Check(inv.CountOf(wood) == 1 && inv.ItemIds[0] == stone && inv.ItemIds[1] == wood,
                "背包: 不同物品分槽");

            // 4) 全背包饱和：堆叠封顶 + 超量余量留在地上
            //    （初始 stone x3 + 600 大堆 → 槽位 0 补到 50 + 8 个空槽各 50 = 447 入包，余 153）
            sim.Entities.Spawn((ushort)EntityKind.DroppedItem, new GridPos(0, 3), stone, 600, 0);
            for (int i = 0; i < 30; i++) sim.Step();
            Check(inv.CountOf(stone) == 450, "背包: 全背包饱和 (stone x" + inv.CountOf(stone) + ")");
            bool leftoverOnGround = false;
            foreach (var e in sim.Entities.InDeterministicOrder())
            {
                if (e.Kind == (ushort)EntityKind.DroppedItem && e.Aux == stone && e.Aux2 == 153)
                {
                    leftoverOnGround = true; break;
                }
            }
            Check(leftoverOnGround, "背包: 超量余量 153 留在地上");

            // 5) 放置消耗材料（平台需 wood）
            var placePos = new GridPos(2, 1);
            sim.Entities.TryGet(pid, out var before);
            Check(inv.CountOf(wood) == 1, "背包: 放置前持有 wood x1");
            sim.EnqueueCommand(new PlaceTileCommand(placePos, defs.TileIds["platform"]) { ByEntityId = pid });
            sim.Step();
            Check(sim.World.GetTile(placePos) == defs.TileIds["platform"] && inv.CountOf(wood) == 0,
                "命令: 放置消耗所需材料");

            // 6) 材料不足放置被拒
            var placePos2 = new GridPos(3, 1);
            sim.EnqueueCommand(new PlaceTileCommand(placePos2, defs.TileIds["platform"]) { ByEntityId = pid });
            sim.Step();
            Check(sim.World.GetTile(placePos2) == 0, "命令: 材料不足放置被权威端拒绝");

            // 7) 挖掘→掉落→拾取闭环（独立世界：满背包世界里新掉落物无槽可入）
            var sim3 = BuildRunwaySim(defs, 501);
            int pid3 = sim3.Entities.Spawn((ushort)EntityKind.Player, new GridPos(0, 1), 0, 0, 1);
            for (int i = 0; i < 30; i++) sim3.Step();
            var inv3 = sim3.Inventories.GetOrCreate(pid3);
            sim3.EnqueueCommand(new PlaceTileCommand(new GridPos(1, 1), defs.TileIds["dirt"]));
            sim3.Step();
            sim3.EnqueueCommand(new DigTileCommand(new GridPos(1, 1)) { ByEntityId = pid3 });
            sim3.Step();
            for (int i = 0; i < 10; i++) sim3.Step();
            Check(inv3.CountOf(dirt) == 1, "闭环: 挖掘掉落物被拾回背包");

            // 8) 初始物资（BuildSim 路径与客户端一致）
            var sim2 = BuildSim(600, defs);
            sim2.Inventories.TryGet(1, out var startInv);
            Check(startInv != null && startInv.CountOf(defs.ItemIds["wheat_seed"]) == 3
                && startInv.CountOf(defs.ItemIds["wood"]) == 6
                && startInv.CountOf(defs.ItemIds["wheat"]) == 3,
                "背包: 新玩家初始物资 3 种子 + 6 木料 + 3 小麦");
        }

        private static int CountDropped(Simulation sim)
        {
            int count = 0;
            foreach (var e in sim.Entities.InDeterministicOrder())
            {
                if (e.Kind == (ushort)EntityKind.DroppedItem) count++;
            }
            return count;
        }

        private static int CountDroppedItem(Simulation sim, ushort itemId)
        {
            int count = 0;
            foreach (var e in sim.Entities.InDeterministicOrder())
            {
                if (e.Kind == (ushort)EntityKind.DroppedItem && e.Aux == itemId) count++;
            }
            return count;
        }

        /// <summary>支撑规则专项：放置校验（跨度/垂直塔）与挖掘级联坍塌（GDD §2.2）。</summary>
        private static void TestStructuralSupport()
        {
            var defs = LoadDefs();
            ushort platform = defs.TileIds["platform"];
            ushort beam = defs.TileIds["support_beam"];
            ushort wood = defs.ItemIds["wood"];

            // ---- 场景 A：放置校验（归属路径，含材料不扣断言）----
            var sim = BuildRunwaySim(defs, 800);
            int pid = sim.Entities.Spawn((ushort)EntityKind.Player, new GridPos(0, 1), 0, 0, 1);
            for (int i = 0; i < 30; i++) sim.Step();
            var inv = sim.Inventories.GetOrCreate(pid);
            inv.AddItem(wood, 20);

            // 1) 悬空放置被拒且不扣材料（目标在射程内，确保是支撑而非射程拒绝）
            var air = new GridPos(3, 5);
            sim.EnqueueCommand(new PlaceTileCommand(air, platform) { ByEntityId = pid });
            sim.Step();
            Check(sim.World.GetTile(air) == 0 && inv.CountOf(wood) == 20,
                "支撑: 悬空放置平台被拒且不扣材料");

            // 2) 地面承接放置成功
            var onGround = new GridPos(2, 1);
            sim.EnqueueCommand(new PlaceTileCommand(onGround, platform) { ByEntityId = pid });
            sim.Step();
            Check(sim.World.GetTile(onGround) == platform && inv.CountOf(wood) == 19,
                "支撑: 地面承接放置成功（消耗木料）");

            // 3) 空中悬链：柱顶 platform 为锚，跨度 1..6 合法，第 7 格被拒
            //    （放置用 creative 无归属路径——支撑校验对 creative 同样生效；玩家在射程外）
            sim.World.SetTile(new GridPos(10, 1), beam);   // 测试直接构造柱（世界写入不走命令）
            sim.World.SetTile(new GridPos(10, 2), beam);
            sim.World.SetTile(new GridPos(10, 0), defs.TileIds["stone"]);
            sim.EnqueueCommand(new PlaceTileCommand(new GridPos(10, 3), platform));  // 锚：柱顶
            for (int x = 11; x <= 16; x++)
            {
                sim.EnqueueCommand(new PlaceTileCommand(new GridPos(x, 3), platform));
            }
            sim.Step();
            bool chainOk = sim.World.GetTile(new GridPos(10, 3)) == platform;
            for (int x = 11; x <= 16; x++)
            {
                if (sim.World.GetTile(new GridPos(x, 3)) != platform) chainOk = false;
            }
            sim.EnqueueCommand(new PlaceTileCommand(new GridPos(17, 3), platform));
            sim.Step();
            Check(chainOk && sim.World.GetTile(new GridPos(17, 3)) == 0,
                "支撑: 悬链跨度 6 内可延伸，第 7 格被拒");

            // 4) 垂直堆叠塔不受跨度限制（下方 platform 即承接）
            for (int y = 4; y <= 6; y++)
            {
                sim.EnqueueCommand(new PlaceTileCommand(new GridPos(10, y), platform));
            }
            sim.Step();
            bool towerOk = true;
            for (int y = 4; y <= 6; y++)
            {
                if (sim.World.GetTile(new GridPos(10, y)) != platform) towerOk = false;
            }
            Check(towerOk, "支撑: 垂直堆叠塔不受水平跨度限制");

            // 5) 挖掉柱子 → 整链 + 塔级联塌落（等值回收掉落物）
            int woodBefore = CountDroppedItem(sim, wood);
            sim.EnqueueCommand(new DigTileCommand(new GridPos(10, 2)));
            sim.Step();
            for (int i = 0; i < 10; i++) sim.Step();  // 等掉落物落稳
            bool allCollapsed = true;
            for (int x = 10; x <= 16; x++)
            {
                if (sim.World.GetTile(new GridPos(x, 3)) != 0) allCollapsed = false;
            }
            for (int y = 4; y <= 6; y++)
            {
                if (sim.World.GetTile(new GridPos(10, y)) != 0) allCollapsed = false;
            }
            // 塌落 10 platform + 挖掉 1 beam = 11 个木料掉落物
            Check(allCollapsed && CountDroppedItem(sim, wood) - woodBefore == 11,
                "支撑: 挖柱级联塌落整链与塔（回收木料 x11）");

            // ---- 场景 B：挖断中段 → 远端塌、近端稳 ----
            var sim2 = BuildRunwaySim(defs, 801);
            sim2.World.SetTile(new GridPos(30, 0), defs.TileIds["stone"]);
            sim2.World.SetTile(new GridPos(30, 1), beam);
            sim2.World.SetTile(new GridPos(30, 2), beam);
            for (int x = 30; x <= 34; x++)
            {
                sim2.World.SetTile(new GridPos(x, 3), platform);
            }
            sim2.EnqueueCommand(new DigTileCommand(new GridPos(32, 3)));
            sim2.Step();
            for (int i = 0; i < 10; i++) sim2.Step();
            bool nearOk = sim2.World.GetTile(new GridPos(30, 3)) == platform
                       && sim2.World.GetTile(new GridPos(31, 3)) == platform;
            bool farCollapsed = sim2.World.GetTile(new GridPos(33, 3)) == 0
                             && sim2.World.GetTile(new GridPos(34, 3)) == 0;
            Check(nearOk && farCollapsed, "支撑: 挖断中段近端稳、远端塌");
            Check(CountDroppedItem(sim2, wood) == 3, "支撑: 断链回收木料 x3（挖1+塌2）");

            // ---- 场景 C：级联确定性（同种子同命令双实例哈希一致）----
            var simA = BuildRunwaySim(defs, 802);
            var simB = BuildRunwaySim(defs, 802);
            foreach (var s in new[] { simA, simB })
            {
                s.World.SetTile(new GridPos(20, 1), beam);
                s.World.SetTile(new GridPos(20, 2), beam);
                for (int x = 20; x <= 26; x++) s.World.SetTile(new GridPos(x, 3), platform);
                s.World.SetTile(new GridPos(20, 4), platform);
                s.World.SetTile(new GridPos(20, 5), platform);
                s.EnqueueCommand(new DigTileCommand(new GridPos(20, 2)));
            }
            for (int i = 0; i < 30; i++) { simA.Step(); simB.Step(); }
            Check(simA.ComputeHash() == simB.ComputeHash() && simA.World.GetTile(new GridPos(26, 3)) == 0,
                "支撑: 级联坍塌双实例哈希一致");
        }

        /// <summary>加工专项：放置工作台 → 权威校验 → 任务推进 → 产出拾取 → 全链闭环。</summary>
        private static void TestCrafting()
        {
            var defs = LoadDefs();
            var sim = BuildRunwaySim(defs, 700);
            int pid = sim.Entities.Spawn((ushort)EntityKind.Player, new GridPos(0, 1), 0, 0, 1);
            for (int i = 0; i < 30; i++) sim.Step();
            var inv = sim.Inventories.GetOrCreate(pid);

            ushort wood = defs.ItemIds["wood"];
            ushort wheat = defs.ItemIds["wheat"];
            ushort bread = defs.ItemIds["bread"];
            ushort stone = defs.ItemIds["stone"];
            var wbPos = new GridPos(2, 1);

            // 1) 木料不足：工作台放不了
            sim.EnqueueCommand(new PlaceTileCommand(wbPos, defs.TileIds["workbench"]) { ByEntityId = pid });
            sim.Step();
            Check(sim.World.GetTile(wbPos) == 0, "加工: 木料不足无法放置工作台");

            // 2) 足量木料 → 放置成功并消耗 2
            inv.AddItem(wood, 2);
            sim.EnqueueCommand(new PlaceTileCommand(wbPos, defs.TileIds["workbench"]) { ByEntityId = pid });
            sim.Step();
            Check(sim.World.GetTile(wbPos) == defs.TileIds["workbench"] && inv.CountOf(wood) == 0,
                "加工: 放置工作台消耗木料 x2");

            // 3) 合成必须归属玩家（ByEntityId=0 被拒）
            sim.EnqueueCommand(new CraftCommand(wbPos, "craft_bread"));
            sim.Step();
            Check(!sim.Crafting.HasJob(wbPos), "加工: 无归属合成命令被拒");

            // 4) 材料不足被拒
            sim.EnqueueCommand(new CraftCommand(wbPos, "craft_bread") { ByEntityId = pid });
            sim.Step();
            Check(!sim.Crafting.HasJob(wbPos), "加工: 材料不足合成被拒");

            // 5) 足量材料 → 任务登记 + 原子消耗
            inv.AddItem(wheat, 3);
            sim.EnqueueCommand(new CraftCommand(wbPos, "craft_bread") { ByEntityId = pid });
            sim.Step();
            Check(inv.CountOf(wheat) == 0 && sim.Crafting.HasJob(wbPos),
                "加工: 合成任务登记并消耗小麦 x3");

            // 6) 任务占用：重复合成被拒且不扣材料
            inv.AddItem(wheat, 3);
            sim.EnqueueCommand(new CraftCommand(wbPos, "craft_bread") { ByEntityId = pid });
            sim.Step();
            Check(sim.Crafting.Count == 1 && inv.CountOf(wheat) == 3,
                "加工: 任务进行中重复合成被拒");

            // 7) 非工作台格 / 未知配方被拒
            sim.EnqueueCommand(new CraftCommand(new GridPos(5, 1), "craft_bread") { ByEntityId = pid });
            sim.EnqueueCommand(new CraftCommand(wbPos, "no_such_recipe") { ByEntityId = pid });
            sim.Step();
            Check(sim.Crafting.Count == 1, "加工: 非工作台格与未知配方被拒");

            // 8) 到时产出：面包掉落物出现在工作台上方（玩家距 2 格不在拾取半径）
            for (int i = 0; i < 60; i++) sim.Step();
            bool breadDropped = false;
            foreach (var e in sim.Entities.InDeterministicOrder())
            {
                if (e.Kind == (ushort)EntityKind.DroppedItem && e.Aux == bread
                    && e.Pos.X == wbPos.X && e.Pos.Y >= wbPos.Y && e.Pos.Y <= wbPos.Y + 1)
                {
                    breadDropped = true; break;
                }
            }
            Check(breadDropped, "加工: 30 tick 后产出面包掉落物");
            Check(!sim.Crafting.HasJob(wbPos), "加工: 任务完成后清除");

            // 9) 玩家走近自动拾取面包
            //    （右移会被工作台挡在 x≈1.7，但拾取半径已覆盖 (2,2) 的面包）
            sim.EnqueueCommand(new SetPlayerInputCommand(pid, 2, 0));
            for (int i = 0; i < 90; i++) sim.Step();
            sim.EnqueueCommand(new SetPlayerInputCommand(pid, 0, 0));
            for (int i = 0; i < 5; i++) sim.Step();
            Check(inv.CountOf(bread) == 1, "加工: 产出面包被玩家自动拾取");

            // 10) 挖掉工作台 → 任务作废（玩家停在 x≈1.7，仍在第二工作台射程内）
            var wb2 = new GridPos(5, 1);
            inv.AddItem(wood, 2);
            sim.EnqueueCommand(new PlaceTileCommand(wb2, defs.TileIds["workbench"]) { ByEntityId = pid });
            sim.Step();
            inv.AddItem(wheat, 3);
            sim.EnqueueCommand(new CraftCommand(wb2, "craft_bread") { ByEntityId = pid });
            sim.Step();
            Check(sim.Crafting.HasJob(wb2), "加工: 第二工作台任务登记");
            sim.EnqueueCommand(new DigTileCommand(wb2) { ByEntityId = pid });
            sim.Step();
            Check(!sim.Crafting.HasJob(wb2), "加工: 挖掉工作台任务作废");

            // 11) 多配方：石砖 outputCount=2
            var sim2 = BuildRunwaySim(defs, 701);
            int pid2 = sim2.Entities.Spawn((ushort)EntityKind.Player, new GridPos(0, 1), 0, 0, 1);
            for (int i = 0; i < 30; i++) sim2.Step();
            var inv2 = sim2.Inventories.GetOrCreate(pid2);
            inv2.AddItem(wood, 2);
            var wb3 = new GridPos(3, 1);
            sim2.EnqueueCommand(new PlaceTileCommand(wb3, defs.TileIds["workbench"]) { ByEntityId = pid2 });
            sim2.Step();
            inv2.AddItem(stone, 3);
            sim2.EnqueueCommand(new CraftCommand(wb3, "craft_stone_bricks") { ByEntityId = pid2 });
            sim2.Step();
            Check(sim2.Crafting.HasJob(wb3) && inv2.CountOf(stone) == 0,
                "加工: 石砖配方消耗石块 x3");
            for (int i = 0; i < 60; i++) sim2.Step();
            bool bricksDropped = false;
            foreach (var e in sim2.Entities.InDeterministicOrder())
            {
                if (e.Kind == (ushort)EntityKind.DroppedItem
                    && e.Aux == defs.ItemIds["stone_bricks"] && e.Aux2 == 2)
                {
                    bricksDropped = true; break;
                }
            }
            Check(bricksDropped, "加工: 石砖产出 x2（outputCount 生效）");

            // 12) 全链闭环：种植 → 收获 → 加工 → 面包（M1 生产链验收）
            var sim3 = BuildRunwaySim(defs, 702);
            int pid3 = sim3.Entities.Spawn((ushort)EntityKind.Player, new GridPos(0, 1), 0, 0, 1);
            for (int i = 0; i < 30; i++) sim3.Step();
            var inv3 = sim3.Inventories.GetOrCreate(pid3);
            inv3.AddItem(wood, 2);
            var wb4 = new GridPos(-3, 1);
            sim3.EnqueueCommand(new PlaceTileCommand(wb4, defs.TileIds["workbench"]) { ByEntityId = pid3 });
            sim3.Step();
            // creative 种两株小麦（玩家两侧，收获掉落后落回拾取半径）
            sim3.EnqueueCommand(new PlaceTileCommand(new GridPos(1, 2), defs.TileIds["wheat_crop"]));
            sim3.EnqueueCommand(new PlaceTileCommand(new GridPos(-1, 2), defs.TileIds["wheat_crop"]));
            sim3.Step();
            for (int i = 0; i < 800; i++) sim3.Step();  // 生长 150 状态 × 5 tick
            sim3.EnqueueCommand(new DigTileCommand(new GridPos(1, 2)) { ByEntityId = pid3 });
            sim3.EnqueueCommand(new DigTileCommand(new GridPos(-1, 2)) { ByEntityId = pid3 });
            sim3.Step();
            for (int i = 0; i < 30; i++) sim3.Step();
            Check(inv3.CountOf(wheat) == 4, "全链: 收获小麦 x4 (2 株 × 2)");
            sim3.EnqueueCommand(new CraftCommand(wb4, "craft_bread") { ByEntityId = pid3 });
            sim3.Step();
            Check(inv3.CountOf(wheat) == 1 && sim3.Crafting.HasJob(wb4), "全链: 面包合成任务开始");
            for (int i = 0; i < 60; i++) sim3.Step();
            sim3.EnqueueCommand(new SetPlayerInputCommand(pid3, 1, 0));
            for (int i = 0; i < 30; i++) sim3.Step();
            sim3.EnqueueCommand(new SetPlayerInputCommand(pid3, 0, 0));
            for (int i = 0; i < 5; i++) sim3.Step();
            Check(inv3.CountOf(bread) == 1, "全链: 种植→收获→加工→面包 闭环");

            // 13) 加工确定性：同种子同命令双实例（CraftingStore 入哈希）哈希一致
            var simA = BuildRunwaySim(defs, 703);
            var simB = BuildRunwaySim(defs, 703);
            foreach (var s in new[] { simA, simB })
            {
                int p = s.Entities.Spawn((ushort)EntityKind.Player, new GridPos(0, 1), 0, 0, 1);
                s.Inventories.GetOrCreate(p).AddItem(wood, 2);
                s.Inventories.GetOrCreate(p).AddItem(wheat, 3);
                s.Inventories.GetOrCreate(p).AddItem(stone, 3);
                s.EnqueueCommand(new PlaceTileCommand(new GridPos(2, 1), defs.TileIds["workbench"]) { ByEntityId = p });
                s.EnqueueCommand(new CraftCommand(new GridPos(2, 1), "craft_bread") { ByEntityId = p });
                s.EnqueueCommand(new CraftCommand(new GridPos(2, 1), "craft_stone_bricks") { ByEntityId = p });
            }
            for (int i = 0; i < 100; i++) { simA.Step(); simB.Step(); }
            // 面包任务 30 tick 完成弹出（第二条 craft 因任务占用被拒，材料未扣），产物留在工作台上
            Check(simA.ComputeHash() == simB.ComputeHash() && CountDropped(simA) == 1,
                "加工: 同种子同命令双实例哈希一致（任务/背包入哈希）");
        }

        private static void TestDeterminism()
        {
            var defs = LoadDefs();
            var script = BuildScript(defs, 42);

            var simA = BuildSim(0xABCD1234, defs);
            var simB = BuildSim(0xABCD1234, defs);
            var simC = BuildSim(0xABCD5678, defs);

            foreach (var rec in script)
            {
                simA.Commands.Enqueue(rec.Command, rec.Tick);
                string line = CommandCodec.EncodeRecord(rec);
                var decoded = CommandCodec.DecodeRecord(line);
                simB.Commands.Enqueue(decoded.Command, decoded.Tick);
                simC.Commands.Enqueue(rec.Command, rec.Tick);
            }

            for (int i = 0; i < 1200; i++)
            {
                simA.Step();
                simB.Step();
                simC.Step();
            }

            ulong hashA = simA.ComputeHash();
            ulong hashB = simB.ComputeHash();
            ulong hashC = simC.ComputeHash();

            Check(hashA == hashB, "确定性: 同种子同命令（含编解码往返）哈希一致 [" + hashA.ToString("X16") + "]");
            Check(hashA != hashC, "确定性: 不同种子哈希不同（哈希有效性负样本）");
        }

        private static void TestReplay()
        {
            var defs = LoadDefs();
            var script = BuildScript(defs, 777);

            var original = BuildSim(0x5EED1234, defs);
            foreach (var rec in script)
            {
                original.Commands.Enqueue(rec.Command, rec.Tick);
            }
            for (int i = 0; i < 1200; i++) original.Step();
            ulong expectedHash = original.ComputeHash();
            long finalTick = original.Tick;

            // 存档格式：种子 + AppliedLog（命令行文本）
            var lines = new List<string>();
            foreach (var rec in original.Commands.Applied)
            {
                lines.Add(CommandCodec.EncodeRecord(rec));
            }

            var replayed = BuildSim(0x5EED1234, defs);
            foreach (var line in lines)
            {
                var rec = CommandCodec.DecodeRecord(line);
                replayed.Commands.Enqueue(rec.Command, rec.Tick);
            }
            while (replayed.Tick < finalTick) replayed.Step();

            Check(replayed.ComputeHash() == expectedHash,
                "回放: 种子+命令日志 重放出一致哈希 [" + expectedHash.ToString("X16") + "]");
        }

        /// <summary>
        /// 存档/读档（GDD §3.5）：游玩中途存档 → 磁盘文件往返 → 读档重建 →
        /// 继续同命令序列 → 与从未存档的原局哈希完全一致（读档后继续玩才是真验收）。
        /// </summary>
        private static void TestSaveLoad()
        {
            var defs = LoadDefs();
            var script = BuildScript(defs, 4242);
            ulong saveSeed = 0xA11CE00;

            // 1) 原局：玩 500 tick 存档，继续玩到 1200
            var simA = BuildSim(saveSeed, defs);
            foreach (var rec in script)
            {
                simA.Commands.Enqueue(rec.Command, rec.Tick);
            }
            for (int i = 0; i < 500; i++) simA.Step();
            ulong hashAtSave = simA.ComputeHash();
            int appliedAtSave = simA.Commands.Applied.Count;
            string saveText = SaveLoad.Serialize(simA);
            while (simA.Tick < 1200) simA.Step();
            ulong hashFull = simA.ComputeHash();

            // 2) 存档文本 → 磁盘文件往返 → 解析
            string path = Path.Combine(Path.GetTempPath(), "worldsim_m1_test.sav");
            File.WriteAllText(path, saveText);
            var data = SaveLoad.Parse(File.ReadAllText(path));
            Check(data.Seed == saveSeed && data.Tick == 500 && data.Records.Count == appliedAtSave,
                "存档: 头信息（种子/tick/命令数）与写出时一致");

            // 3) 读档重建：同种子 setup + 命令重放 → 与存档时刻状态一致
            var simB = BuildSim(data.Seed, defs);
            SaveLoad.ReplayInto(simB, data);
            Check(simB.Tick == 500 && simB.ComputeHash() == hashAtSave,
                "存档: 读档重建至存档点（哈希一致）");

            // 4) 读档后继续同命令序列 → 与原局完全一致
            foreach (var rec in script)
            {
                if (rec.Tick > data.Tick) simB.Commands.Enqueue(rec.Command, rec.Tick);
            }
            while (simB.Tick < 1200) simB.Step();
            Check(simB.ComputeHash() == hashFull,
                "存档: 读档后继续游玩与原局一致 [" + hashFull.ToString("X16") + "]");

            // 5) 空/损坏存档拒绝
            bool threwBad = false;
            try { SaveLoad.Parse("garbage|99|x"); }
            catch (FormatException) { threwBad = true; }
            bool threwEmpty = false;
            try { SaveLoad.Parse(""); }
            catch (FormatException) { threwEmpty = true; }
            Check(threwBad && threwEmpty, "存档: 损坏头/空文件拒绝解析");

            File.Delete(path);
        }

        /// <summary>
        /// CI 报告模式：固定剧本（种子 0x5EED1234 + 脚本 777）跑 1200 tick，
        /// 采样主线/回放/读档/读档继续四条路径的 WorldHash，写规范报告文件。
        /// 报告行全 ASCII、\n 换行、UTF-8 无 BOM —— 双平台产物可逐字节 diff；
        /// 任何一行不一致 = 存在平台相关的不确定性（联机红线）。
        /// </summary>
        private static void RunReport(string path)
        {
            const ulong reportSeed = 0x5EED1234;
            var defs = LoadDefs();
            var script = BuildScript(defs, 777);

            var lines = new List<string>
            {
                "worldsim-ci 1",
                "seed=0x5EED1234",
                "script=777",
                "ticks=1200",
            };

            // 主线：全量命令入队，采样 tick 0/300/600/900/1200；tick 600 时顺带存档
            var sim = BuildSim(reportSeed, defs);
            foreach (var rec in script)
            {
                sim.Commands.Enqueue(rec.Command, rec.Tick);
            }
            lines.Add("hash@0=" + sim.ComputeHash().ToString("X16"));

            string saveText = null;
            for (int i = 0; i < 1200; i++)
            {
                sim.Step();
                if (sim.Tick == 600)
                {
                    saveText = SaveLoad.Serialize(sim);
                    lines.Add("hash@600=" + sim.ComputeHash().ToString("X16"));
                }
                else if (sim.Tick == 300 || sim.Tick == 900 || sim.Tick == 1200)
                {
                    lines.Add("hash@" + sim.Tick + "=" + sim.ComputeHash().ToString("X16"));
                }
            }

            // 读档路径：从 tick 600 的存档重建 → 重放到 600 → 继续同命令到 1200
            var reloaded = BuildSim(reportSeed, defs);
            SaveLoad.ReplayInto(reloaded, SaveLoad.Parse(saveText));
            lines.Add("reload@600=" + reloaded.ComputeHash().ToString("X16"));
            foreach (var rec in script)
            {
                if (rec.Tick > 600) reloaded.Commands.Enqueue(rec.Command, rec.Tick);
            }
            while (reloaded.Tick < 1200) reloaded.Step();
            lines.Add("reload_continue@1200=" + reloaded.ComputeHash().ToString("X16"));

            // 回放路径：AppliedLog 逐条编解码往返 → 全新重放
            var replayed = BuildSim(reportSeed, defs);
            foreach (var rec in sim.Commands.Applied)
            {
                var decoded = CommandCodec.DecodeRecord(CommandCodec.EncodeRecord(rec));
                replayed.Commands.Enqueue(decoded.Command, decoded.Tick);
            }
            while (replayed.Tick < 1200) replayed.Step();
            lines.Add("replay@1200=" + replayed.ComputeHash().ToString("X16"));

            // 报告本体只写文件（保证跨平台字节一致）；路径等本地信息走控制台
            File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            Console.WriteLine("hash report written: " + Path.GetFullPath(path));
            foreach (var line in lines)
            {
                Console.WriteLine(line);
            }
        }

        /// <summary>生成一份固定的「玩家操作脚本」：混合放置/挖掘/生成实体/角色输入/合成/带归属挖掘。</summary>
        private static List<CommandRecord> BuildScript(Defs defs, ulong seed)
        {
            var rng = new SimRandom(seed);
            var script = new List<CommandRecord>();
            ushort platform = defs.TileIds["platform"];
            ushort support = defs.TileIds["support_beam"];
            ushort crop = defs.TileIds["wheat_crop"];
            ushort workbench = defs.TileIds["workbench"];
            ushort stoneItem = defs.ItemIds["stone"];

            // 固定前缀：放工作台 + 面包合成（初始小麦 3 → 成功路径，让 CraftingStore 进入哈希覆盖）
            script.Add(new CommandRecord
            {
                Tick = 3,
                Command = new PlaceTileCommand(new GridPos(2, 8), workbench) { ByEntityId = 1 },
            });
            script.Add(new CommandRecord
            {
                Tick = 5,
                Command = new CraftCommand(new GridPos(2, 8), "craft_bread") { ByEntityId = 1 },
            });
            // 固定前缀：柱顶放平台 + 挖柱级联（让支撑级联塌落进入哈希覆盖；
            // creative 无归属路径——跳过射程/材料，但支撑校验与级联同样生效）
            script.Add(new CommandRecord
            {
                Tick = 7,
                Command = new PlaceTileCommand(new GridPos(5, 8), support),
            });
            script.Add(new CommandRecord
            {
                Tick = 9,
                Command = new PlaceTileCommand(new GridPos(5, 9), platform),
            });
            script.Add(new CommandRecord
            {
                Tick = 11,
                Command = new DigTileCommand(new GridPos(5, 8)),
            });

            for (int i = 0; i < 100; i++)
            {
                long tick = 1 + i * 7;
                int op = rng.NextInt(0, 7);
                ISimCommand command;
                switch (op)
                {
                    case 0:
                        command = new PlaceTileCommand(
                            new GridPos(rng.NextInt(-32, 32), rng.NextInt(8, 24)),
                            rng.NextInt(0, 2) == 0 ? platform : support)
                        { ByEntityId = 1 };
                        break;
                    case 1:
                        command = new PlaceTileCommand(
                            new GridPos(rng.NextInt(-32, 32), rng.NextInt(8, 24)), crop)
                        { ByEntityId = 1 };
                        break;
                    case 2:
                        command = new DigTileCommand(new GridPos(rng.NextInt(-32, 32), rng.NextInt(-8, 4)));
                        break;
                    case 3:
                        command = new SpawnEntityCommand(
                            (ushort)EntityKind.DroppedItem,
                            new GridPos(rng.NextInt(-32, 32), rng.NextInt(10, 30)),
                            stoneItem, 1, 0);
                        break;
                    case 4:
                        // 角色输入状态变化（含编解码往返，联机命令同格式）
                        command = new SetPlayerInputCommand(1,
                            (byte)rng.NextInt(0, 3), (byte)rng.NextInt(0, 2));
                        break;
                    case 5:
                        // 随机位置/配方合成（大多被权威端拒绝，覆盖校验与编解码路径）
                        command = new CraftCommand(
                            new GridPos(rng.NextInt(-32, 32), rng.NextInt(0, 8)),
                            defs.Recipes[rng.NextInt(0, defs.Recipes.Count)].Id)
                        { ByEntityId = 1 };
                        break;
                    default:
                        // 带归属的挖掘（覆盖权威端射程校验路径，大多因超程被忽略）
                        command = new DigTileCommand(
                            new GridPos(rng.NextInt(-32, 32), rng.NextInt(-8, 4))) { ByEntityId = 1 };
                        break;
                }
                script.Add(new CommandRecord { Tick = tick, Command = command });
            }
            return script;
        }
    }
}
