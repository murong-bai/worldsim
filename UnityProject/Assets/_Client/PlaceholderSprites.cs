using System;
using UnityEngine;

namespace Sim.Client
{
    /// <summary>
    /// 程序化占位精灵（16px、Point 采样、确定性噪点纹理）。
    /// 正式像素美术资产到位后整体替换本类，TilePalette / EntitySpriteRenderer 对外接口不变。
    /// 注意：本类属表现层，允许使用浮点与真实随机——但纹理生成仍用整数哈希，保证视觉稳定。
    /// </summary>
    public static class PlaceholderSprites
    {
        /// <summary>方块占位纹理：基色 + 每像素确定性明度扰动；platform 只画下半（平台外观）。</summary>
        public static Sprite TileSprite(string tileName, Color color)
        {
            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            int hash = StableStringHash(tileName);
            int bottomRow = tileName == "platform" ? 6 : 0; // platform：仅下 10 行（贴细胞下半）

            var pixels = new Color[size * size];
            for (int y = 0; y < bottomRow; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = Color.clear; // 上半透明
                }
            }
            for (int y = bottomRow; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int h = (x * 374761393 + y * 668265263 + hash * 1274126177) & 0xFFFF;
                    float shade = 0.86f + (h / 65535f) * 0.28f; // 0.86 ~ 1.14
                    pixels[y * size + x] = new Color(color.r * shade, color.g * shade, color.b * shade, 1f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        /// <summary>纯白色块精灵（由 SpriteRenderer.color 染色）。</summary>
        public static Sprite SolidSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var white = new Color(1f, 1f, 1f, 1f);
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = white;
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        /// <summary>"#RRGGBB" → Color。非法输入返回品红（占位期间显眼易查）。</summary>
        public static Color ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.magenta;
            string s = hex.TrimStart('#');
            if (s.Length != 6) return Color.magenta;
            int rgb;
            try
            {
                rgb = Convert.ToInt32(s, 16);
            }
            catch (FormatException)
            {
                return Color.magenta;
            }
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f);
        }

        private static int StableStringHash(string s)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return h;
            }
        }
    }
}
