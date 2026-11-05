using System.Collections.Generic;
using UnityEngine;
using Sim.Core;
using Sim.Data;

namespace Sim.Client
{
    /// <summary>
    /// 实体渲染（M1 占位：色块精灵池）。
    /// 玩家 = 16px 蓝白块；小人（Worker）= 16px 绿白块；掉落物 = 12px 物品配色块。
    /// 每逻辑帧全量同步位置（实体数量小；后续按需改为变更驱动）。
    /// </summary>
    public sealed class EntitySpriteRenderer : MonoBehaviour
    {
        private static readonly Color PlayerColor = new Color(0.42f, 0.68f, 1f);
        private static readonly Color WorkerColor = new Color(0.55f, 0.85f, 0.55f);

        private Simulation _sim;
        private Sprite _playerSprite;
        private Sprite _itemSprite;
        private Color[] _itemColors;
        private Transform _root;
        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();

        public void Init(Simulation sim, Defs defs)
        {
            _sim = sim;
            _playerSprite = PlaceholderSprites.SolidSprite(16);
            _itemSprite = PlaceholderSprites.SolidSprite(12);

            _itemColors = new Color[defs.ItemColors.Length];
            for (int i = 0; i < defs.ItemColors.Length; i++)
            {
                _itemColors[i] = defs.ItemColors[i] == null
                    ? Color.magenta
                    : PlaceholderSprites.ParseColor(defs.ItemColors[i]);
            }

            var rootGo = new GameObject("Entities");
            _root = rootGo.transform;
        }

        public void SyncTick()
        {
            var entities = _sim.Entities.InDeterministicOrder();

            while (_pool.Count < entities.Count)
            {
                var go = new GameObject("Entity" + _pool.Count);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 10; // 实体绘制在方块之上
                _pool.Add(sr);
            }

            for (int i = 0; i < _pool.Count; i++)
            {
                var sr = _pool[i];
                bool active = i < entities.Count;
                if (sr.gameObject.activeSelf != active) sr.gameObject.SetActive(active);
                if (!active) continue;

                var e = entities[i];
                bool isPlayer = e.Kind == (ushort)EntityKind.Player;
                bool isWorker = e.Kind == (ushort)EntityKind.Worker;
                sr.sprite = (isPlayer || isWorker) ? _playerSprite : _itemSprite;
                sr.color = isPlayer ? PlayerColor : (isWorker ? WorkerColor : ItemColor(e.Aux));

                // Fix 精确位置：X = 碰撞盒中心；Y = 底边 + 半高（贴地）
                float halfHeight = (isPlayer || isWorker) ? 0.5f : 0.375f;
                sr.transform.position = new Vector3(
                    e.FixX.ToFloat(), e.FixY.ToFloat() + halfHeight, 0f);

                if (isPlayer || isWorker)
                {
                    bool faceRight = e.Data == 1;
                    var scale = sr.transform.localScale;
                    float absX = Mathf.Abs(scale.x);
                    scale.x = faceRight ? absX : -absX;
                    sr.transform.localScale = scale;
                }
            }
        }

        private Color ItemColor(ushort itemId)
        {
            return itemId < _itemColors.Length ? _itemColors[itemId] : Color.magenta;
        }
    }
}
