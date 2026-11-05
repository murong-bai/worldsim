using System;
using System.Collections.Generic;
using System.IO;
using Sim.Core;

namespace Sim.Data
{
    /// <summary>
    /// 定义加载器：JSON → Defs（字符串 ID → 有序数字 ID）。
    /// 加载失败抛出带上下文的异常（开发期尽早暴露配置错误）。
    /// </summary>
    public static class DefLoader
    {
        public static Defs LoadFromDirectory(string directory)
        {
            string tiles = ReadFile(Path.Combine(directory, "tiles.json"));
            string items = ReadFile(Path.Combine(directory, "items.json"));
            string recipes = ReadFile(Path.Combine(directory, "recipes.json"));
            string worldPath = Path.Combine(directory, "world.json");
            string world = File.Exists(worldPath) ? File.ReadAllText(worldPath) : null;
            return Load(tiles, items, recipes, world);
        }

        private static string ReadFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("缺定义文件: " + path);
            return File.ReadAllText(path);
        }

        public static Defs Load(string tilesJson, string itemsJson, string recipesJson, string worldJson = null)
        {
            var defs = new Defs();
            LoadItems(defs, itemsJson);
            LoadTiles(defs, tilesJson);
            LoadRecipes(defs, recipesJson);
            if (worldJson != null) LoadWorld(defs, worldJson);
            return defs;
        }

        private static void LoadItems(Defs defs, string itemsJson)
        {
            var root = Json.Parse(itemsJson);
            if (root.Type != JsonValue.Kind.Array) throw new FormatException("items.json: 顶层应为数组");

            var ids = new List<string>();
            foreach (var item in root.Items)
            {
                string id = item.GetString("id", null);
                if (id == null) throw new FormatException("items.json: 缺 id 字段");
                ids.Add(id);
            }
            ids.Sort(StringComparer.Ordinal); // Ordinal：跨平台稳定排序

            defs.ItemIds = new Dictionary<string, ushort>();
            var names = new List<string> { "<none>" }; // itemId 0 = 无效
            var itemColors = new List<string> { null };

            var colorById = new Dictionary<string, string>();
            foreach (var item in root.Items)
            {
                string color = item.GetString("color", null);
                if (color != null) colorById[item.GetString("id", "")] = color;
            }

            foreach (var id in ids)
            {
                defs.ItemIds[id] = (ushort)names.Count;
                names.Add(id);
                string color;
                itemColors.Add(colorById.TryGetValue(id, out color) ? color : null);
            }
            defs.ItemNames = names.ToArray();
            defs.ItemColors = itemColors.ToArray();
        }

        private static ushort ItemIdOf(Defs defs, string name, string context)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            ushort id;
            if (!defs.ItemIds.TryGetValue(name, out id))
                throw new FormatException(context + ": 未定义的物品 '" + name + "'");
            return id;
        }

        private static void LoadTiles(Defs defs, string tilesJson)
        {
            var root = Json.Parse(tilesJson);
            if (root.Type != JsonValue.Kind.Array) throw new FormatException("tiles.json: 顶层应为数组");

            JsonValue airDef = null;
            var others = new List<JsonValue>();
            foreach (var tile in root.Items)
            {
                string id = tile.GetString("id", null);
                if (id == null) throw new FormatException("tiles.json: 缺 id 字段");
                if (id == "air") airDef = tile;
                else others.Add(tile);
            }
            if (airDef == null) throw new FormatException("tiles.json: 缺少 'air' 定义");
            others.Sort((a, b) => string.CompareOrdinal(a.GetString("id", ""), b.GetString("id", "")));

            defs.TileIds = new Dictionary<string, ushort>();
            var rules = new List<TileRule>();
            var names = new List<string>();
            var tileColors = new List<string>();

            // air 固定为 0
            defs.TileIds["air"] = 0;
            rules.Add(RuleOf(airDef, defs, "tiles.json[air]"));
            names.Add("air");
            tileColors.Add(airDef.GetString("color", null));

            foreach (var tile in others)
            {
                string id = tile.GetString("id", "");
                defs.TileIds[id] = (ushort)rules.Count;
                rules.Add(RuleOf(tile, defs, "tiles.json[" + id + "]"));
                names.Add(id);
                tileColors.Add(tile.GetString("color", null));
            }

            defs.TileRules = rules.ToArray();
            defs.TileNames = names.ToArray();
            defs.TileColors = tileColors.ToArray();
        }

        private static TileRule RuleOf(JsonValue tile, Defs defs, string context)
        {
            bool solid = tile.GetBool("solid", false);
            return new TileRule(
                solid,
                tile.GetBool("placeable", false),
                tile.GetBool("blocksItems", solid), // 默认与 solid 一致（平台需显式声明）
                ItemIdOf(defs, tile.GetString("dropItem", null), context),
                ItemIdOf(defs, tile.GetString("harvestItem", null), context),
                tile.GetInt("growthTicks", 0),
                ItemIdOf(defs, tile.GetString("placeItem", null), context),
                tile.GetInt("placeCount", 1),
                tile.GetBool("workbench", false),
                tile.GetBool("needsSupport", false));
        }

        /// <summary>world.json → GlobalDef（全局模拟参数）。数值即模拟输入，异常值尽早抛出。</summary>
        private static void LoadWorld(Defs defs, string worldJson)
        {
            var root = Json.Parse(worldJson);
            if (root.Type != JsonValue.Kind.Object) throw new FormatException("world.json: 顶层应为对象");

            var g = new GlobalDef();
            g.MaxHorizontalSpan = root.GetInt("maxHorizontalSpan", g.MaxHorizontalSpan);
            g.WorldLeft = root.GetInt("worldLeft", g.WorldLeft);
            g.WorldRight = root.GetInt("worldRight", g.WorldRight);

            if (g.MaxHorizontalSpan < 1 || g.MaxHorizontalSpan > 64)
                throw new FormatException("world.json: maxHorizontalSpan 应在 1..64");
            if (g.WorldRight <= g.WorldLeft || g.Width > 4096)
                throw new FormatException("world.json: 世界宽度应在 1..4096 格");
            if (g.WorldLeft % 32 != 0 || (g.WorldRight + 1) % 32 != 0)
                throw new FormatException("world.json: worldLeft/worldRight+1 须与 32 格 chunk 对齐");

            // 矿脉（可选数组）：tile 字符串名 → 数字 ID（tiles 已加载）
            var ores = root.Get("ores");
            if (ores != null)
            {
                if (ores.Type != JsonValue.Kind.Array) throw new FormatException("world.json: ores 应为数组");
                foreach (var ore in ores.Items)
                {
                    string tileName = ore.GetString("tile", null);
                    if (tileName == null) throw new FormatException("world.json[ores]: 缺 tile 字段");
                    if (!defs.TileIds.ContainsKey(tileName))
                        throw new FormatException("world.json[ores]: 未定义的方块 '" + tileName + "'");
                    var vein = new GlobalDef.OreVein
                    {
                        TileId = defs.TileIds[tileName],
                        ChancePerMille = ore.GetInt("chancePerMille", 0),
                        YTop = ore.GetInt("yTop", 0),
                        YBottom = ore.GetInt("yBottom", 0),
                    };
                    if (vein.ChancePerMille < 1 || vein.ChancePerMille > 500)
                        throw new FormatException("world.json[ores]: chancePerMille 应在 1..500");
                    if (vein.YBottom >= vein.YTop)
                        throw new FormatException("world.json[ores]: yBottom 应深于 yTop（数值更小）");
                    g.Ores.Add(vein);
                }
            }

            defs.Global = g;
        }

        private static void LoadRecipes(Defs defs, string recipesJson)
        {
            var root = Json.Parse(recipesJson);
            if (root.Type != JsonValue.Kind.Array) throw new FormatException("recipes.json: 顶层应为数组");

            defs.Recipes = new List<RecipeDef>();
            foreach (var recipe in root.Items)
            {
                // 输入按物品名 Ordinal 排序（多输入配方的确定性顺序）
                var inputItems = new List<string>();
                var inputCounts = new List<int>();
                var inputs = recipe.Get("inputs");
                if (inputs != null && inputs.Type == JsonValue.Kind.Object)
                {
                    var keys = new List<string>();
                    foreach (var field in inputs.Fields) keys.Add(field.Key);
                    keys.Sort(StringComparer.Ordinal);
                    foreach (var key in keys)
                    {
                        inputItems.Add(key);
                        inputCounts.Add(inputs.GetInt(key, 0));
                    }
                }

                string context = "recipes.json[" + recipe.GetString("id", "") + "]";
                var inputIds = new ushort[inputItems.Count];
                for (int i = 0; i < inputItems.Count; i++)
                {
                    inputIds[i] = ItemIdOf(defs, inputItems[i], context);
                }

                defs.Recipes.Add(new RecipeDef
                {
                    Id = recipe.GetString("id", ""),
                    InputItemIds = inputIds,
                    InputCounts = inputCounts.ToArray(),
                    OutputItemId = ItemIdOf(defs, recipe.GetString("output", null), context),
                    OutputCount = recipe.GetInt("outputCount", 1),
                    CraftTicks = recipe.GetInt("craftTicks", 0),
                });
            }
        }
    }
}
