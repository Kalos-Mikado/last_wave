using LastWave.Config;
using UnityEngine;

namespace LastWave.Map
{
    /// <summary>
    /// Solid map edges (story 015): four static BoxCollider2D children (no Rigidbody2D) that keep players off the rocks
    /// and inside the view. Built by LastWaveSetup on a <c>Walls</c> object; placed here at runtime every time
    /// <see cref="MapBackground"/> refits, so they follow a resized window:
    /// <list type="bullet">
    /// <item>Top: inner edge <see cref="GameConfig.MapTopBandFraction"/> of the displayed picture's height below its top.</item>
    /// <item>Bottom: inner edge <see cref="GameConfig.MapBottomBandFraction"/> of that height above its bottom.</item>
    /// <item>Left and right: inner edges on the camera view's edges.</item>
    /// </list>
    /// Each wall is <see cref="GameConfig.MapWallThickness"/> thick, outward from its inner edge; top and bottom overlap
    /// the side walls at the corners. Enemies are kinematic and pass through; bullets ignore the walls
    /// (<c>Bullet</c> skips colliders under a MapWalls). Only the owner's dynamic player body collides with them, which
    /// matches the owner-authoritative movement; the top and bottom walls are the same on every machine (the view is
    /// always 12 units high), the side walls follow each machine's own window width.
    /// </summary>
    public sealed class MapWalls : MonoBehaviour
    {
        [SerializeField] private MapBackground _map;
        [SerializeField] private BoxCollider2D _top;
        [SerializeField] private BoxCollider2D _bottom;
        [SerializeField] private BoxCollider2D _left;
        [SerializeField] private BoxCollider2D _right;

        private void Start()
        {
            if (_map == null)
            {
                Debug.LogWarning("MapWalls: no MapBackground assigned; walls are not placed.");
                return;
            }
            _map.Fitted += Place;
            if (_map.HasFitted) Place(_map);
        }

        private void OnDestroy()
        {
            if (_map != null) _map.Fitted -= Place;
        }

        private void Place(MapBackground map)
        {
            var config = GameConfig.Instance;
            Rect picture = map.DisplayedRect;
            Rect view = map.ViewRect;
            float t = config.MapWallThickness;

            float topInner = picture.yMax - config.MapTopBandFraction * picture.height;
            float bottomInner = picture.yMin + config.MapBottomBandFraction * picture.height;
            float width = view.width + 2f * t;
            float height = (topInner - bottomInner) + 2f * t;
            float middleY = (topInner + bottomInner) * 0.5f;

            Set(_top, new Vector2(view.center.x, topInner + t * 0.5f), new Vector2(width, t));
            Set(_bottom, new Vector2(view.center.x, bottomInner - t * 0.5f), new Vector2(width, t));
            Set(_left, new Vector2(view.xMin - t * 0.5f, middleY), new Vector2(t, height));
            Set(_right, new Vector2(view.xMax + t * 0.5f, middleY), new Vector2(t, height));
        }

        private static void Set(BoxCollider2D wall, Vector2 centre, Vector2 size)
        {
            if (wall == null) return;
            wall.transform.position = new Vector3(centre.x, centre.y, 0f);
            wall.offset = Vector2.zero;
            wall.size = size;
        }
    }
}
