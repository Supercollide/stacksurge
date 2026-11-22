using StackSurge.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// Sprites the UI needs that are simple geometry: rounded panels (9-sliced), the tile body, and the
    /// colorblind shape glyphs. Generated once at startup from signed-distance functions (anti-aliased,
    /// no art import settings to maintain) and cached.
    /// </summary>
    public static class UiSprites
    {
        // Rounded-rect source radius in pixels. Small radii (thin bars, pills) use a smaller source so the
        // corner isn't minified 5:1 into a jaggy edge.
        const int RoundedRadiusPx = 32;
        const int SmallRoundedRadiusPx = 8;
        const float SmallRadiusBelow = 12f;
        // Rounded sprites use a high pixels-per-unit so a Sliced image's preferred size (border sum / PPU) stays
        // tiny and layout groups don't grow children that were sprite-less; the multiplier compensates below.
        const float RoundedSpritePpu = 1000f;
        const float CanvasReferencePpu = 100f;
        const int TileSizePx = 128;
        const int GlyphSizePx = 96;

        static Sprite _rounded;
        static Sprite _roundedSmall;
        static Sprite _roundedOutline;
        static Sprite _tile;
        static Sprite _circle;
        static Sprite _fadeDown;
        static Sprite _ring;
        static Sprite _bolt;
        static readonly Sprite[] _glyphs = new Sprite[5];

        /// <summary>White rounded rectangle, 9-sliced. Use <see cref="ApplyRounded"/> to pick the corner radius.</summary>
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = BuildRounded(RoundedRadiusPx));

        static Sprite RoundedSmall => _roundedSmall != null ? _roundedSmall : (_roundedSmall = BuildRounded(SmallRoundedRadiusPx));

        /// <summary>White rounded outline (4 px ring at multiplier 1), 9-sliced. Use <see cref="ApplyRoundedOutline"/>.</summary>
        public static Sprite RoundedOutline => _roundedOutline != null ? _roundedOutline : (_roundedOutline = BuildRounded(RoundedRadiusPx, 4f));

        /// <summary>True if the sprite is one of the generated rounded panels (already skinned).</summary>
        public static bool IsRounded(Sprite sprite) =>
            sprite != null && (sprite == _rounded || sprite == _roundedSmall || sprite == _roundedOutline);

        /// <summary>Tile body: rounded square with a soft top-to-bottom shade and a darker bottom lip (tinted by Image.color).</summary>
        public static Sprite Tile => _tile != null ? _tile : (_tile = BuildTile());

        /// <summary>White, fully opaque at the top fading to transparent at the bottom (glows, veils).</summary>
        public static Sprite FadeDown => _fadeDown != null ? _fadeDown : (_fadeDown = BuildFadeDown());

        /// <summary>White anti-aliased ring (shockwaves).</summary>
        public static Sprite Ring => _ring != null ? _ring : (_ring = BuildRing());

        /// <summary>White lightning bolt like the one in the logo (the profile card's tier-up).</summary>
        public static Sprite Bolt => _bolt != null ? _bolt : (_bolt = BuildBolt());

        /// <summary>White anti-aliased disc.</summary>
        public static Sprite Circle => _circle != null ? _circle : (_circle = BuildGlyph(GlyphShape.Circle, 0.48f));

        /// <summary>Gives an Image rounded corners of the given radius (canvas units) without changing its colour.</summary>
        public static void ApplyRounded(Image image, float radius)
        {
            if (image == null) return;
            radius = Mathf.Max(1f, radius);
            bool small = radius < SmallRadiusBelow;
            image.sprite = small ? RoundedSmall : Rounded;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = MultiplierFor(small ? SmallRoundedRadiusPx : RoundedRadiusPx, radius);
        }

        /// <summary>
        /// Sliced corner size in canvas units = borderPx / (spritePpu / canvasRefPpu * multiplier); solve for a corner of
        /// <paramref name="radius"/> units.
        /// </summary>
        static float MultiplierFor(int sourceRadiusPx, float radius) =>
            sourceRadiusPx * CanvasReferencePpu / (RoundedSpritePpu * radius);

        /// <summary>Same as <see cref="ApplyRounded"/> but only the border is drawn (outlined buttons).</summary>
        public static void ApplyRoundedOutline(Image image, float radius)
        {
            if (image == null) return;
            image.sprite = RoundedOutline;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = MultiplierFor(RoundedRadiusPx, Mathf.Max(1f, radius));
        }

        /// <summary>
        /// Shape that pairs with each tile colour so tiles can be told apart without colour
        /// (red circle, blue diamond, green triangle, yellow star, purple square). Null for non-colour tiles.
        /// </summary>
        public static Sprite GlyphFor(TileKind kind)
        {
            int index;
            switch (kind)
            {
                case TileKind.Red: index = 0; break;
                case TileKind.Blue: index = 1; break;
                case TileKind.Green: index = 2; break;
                case TileKind.Yellow: index = 3; break;
                case TileKind.Purple: index = 4; break;
                default: return null;
            }
            if (_glyphs[index] == null)
            {
                _glyphs[index] = index switch
                {
                    0 => BuildGlyph(GlyphShape.Circle, 0.36f),
                    1 => BuildGlyph(GlyphShape.Diamond, 0.44f),
                    2 => BuildGlyph(GlyphShape.Triangle, 0.40f),
                    3 => BuildGlyph(GlyphShape.Star, 0.46f),
                    _ => BuildGlyph(GlyphShape.Square, 0.33f),
                };
            }
            return _glyphs[index];
        }

        /// <summary>Builds everything up front (call during loading).</summary>
        public static void Warm()
        {
            _ = Rounded;
            _ = RoundedSmall;
            _ = RoundedOutline;
            _ = Tile;
            _ = Circle;
            _ = FadeDown;
            _ = Ring;
            foreach (TileKind kind in new[] { TileKind.Red, TileKind.Blue, TileKind.Green, TileKind.Yellow, TileKind.Purple })
                GlyphFor(kind);
        }

        // ── Generation ───────────────────────────────────────────────────────
        enum GlyphShape { Circle, Diamond, Triangle, Star, Square }

        static Texture2D NewTexture(int w, int h, string name)
        {
            // No DontSave: the static fields keep these alive, and without it they don't leak across editor play sessions.
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            return tex;
        }

        /// <param name="ringWidth">0 for a filled shape, otherwise the outline thickness in pixels.</param>
        static Sprite BuildRounded(int r, float ringWidth = 0f)
        {
            int size = r * 2 + 4;
            var tex = NewTexture(size, size, ringWidth > 0f ? "UiRoundedOutline" : "UiRounded");
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedBoxDistance(x + 0.5f - half, y + 0.5f - half, half, half, r);
                    if (ringWidth > 0f) d = Mathf.Abs(d + ringWidth * 0.5f) - ringWidth * 0.5f;
                    pixels[y * size + x] = new Color32(255, 255, 255, Coverage(d));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), RoundedSpritePpu, 0,
                SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
        }

        static Sprite BuildTile()
        {
            int size = TileSizePx;
            const float radius = 22f;
            const float lip = 7f; // darker band along the bottom edge
            var tex = NewTexture(size, size, "UiTile");
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - half, py = y + 0.5f - half;
                    float d = RoundedBoxDistance(px, py, half, half, radius);
                    // Shade: 1.0 at the top to 0.86 at the bottom, then a lip at the very bottom.
                    float shade = Mathf.Lerp(0.86f, 1f, (float)y / (size - 1));
                    float lipD = RoundedBoxDistance(px, py - lip * 0.5f, half, half - lip * 0.5f, radius);
                    if (lipD > 0f && y < size / 2) shade *= 0.78f;
                    byte v = (byte)Mathf.RoundToInt(255f * shade);
                    pixels[y * size + x] = new Color32(v, v, v, Coverage(d));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        static Sprite BuildGlyph(GlyphShape shape, float radius01)
        {
            int size = GlyphSizePx;
            var tex = NewTexture(size, size, "UiGlyph_" + shape);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float r = radius01 * size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - half, py = y + 0.5f - half;
                    float d = shape switch
                    {
                        GlyphShape.Circle => Mathf.Sqrt(px * px + py * py) - r,
                        GlyphShape.Diamond => (Mathf.Abs(px) + Mathf.Abs(py) - r) * 0.70710678f,
                        GlyphShape.Triangle => TriangleDistance(px, py + r * 0.18f, r),
                        GlyphShape.Star => StarDistance(px, py, r, 0.42f),
                        _ => RoundedBoxDistance(px, py, r, r, r * 0.22f),
                    };
                    pixels[y * size + x] = new Color32(255, 255, 255, Coverage(d));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        static Sprite BuildRing()
        {
            const int size = 128;
            const float radius = 56f, thickness = 7f;
            var tex = NewTexture(size, size, "UiRing");
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Abs(new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude - radius) - thickness * 0.5f;
                    pixels[y * size + x] = new Color32(255, 255, 255, Coverage(d));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        // The bolt's outline in a 0..1 box, y up: top-right stroke down to the lower point, back up the right side.
        static readonly Vector2[] BoltShape =
        {
            new Vector2(0.66f, 1f), new Vector2(0.18f, 0.42f), new Vector2(0.46f, 0.42f), new Vector2(0.30f, 0f),
            new Vector2(0.84f, 0.62f), new Vector2(0.55f, 0.62f), new Vector2(0.80f, 1f)
        };

        static Sprite BuildBolt()
        {
            const int w = 96, h = 144;
            var tex = NewTexture(w, h, "UiBolt");
            var pixels = new Color32[w * h];
            var poly = new Vector2[BoltShape.Length];
            for (int i = 0; i < poly.Length; i++) poly[i] = new Vector2(BoltShape[i].x * w, BoltShape[i].y * h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = new Color32(255, 255, 255, Coverage(PolygonDistance(new Vector2(x + 0.5f, y + 0.5f), poly)));
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Signed distance to a closed polygon, negative inside (after Inigo Quilez).</summary>
        static float PolygonDistance(Vector2 p, Vector2[] v)
        {
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float sign = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) sign = -sign;
            }
            return sign * Mathf.Sqrt(d);
        }

        static Sprite BuildFadeDown()
        {
            const int w = 4, h = 64;
            var tex = NewTexture(w, h, "UiFadeDown");
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / (h - 1); // 0 at the bottom row, 1 at the top
                byte a = (byte)Mathf.RoundToInt(255f * t * t);
                for (int x = 0; x < w; x++) pixels[y * w + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Signed distance (pixels) → 8-bit coverage with a 1 px anti-aliased edge.</summary>
        static byte Coverage(float distance) => (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - distance) * 255f);

        /// <summary>Box of half-size (hx, hy) with corner radius r, centred on the origin.</summary>
        static float RoundedBoxDistance(float px, float py, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(px) - hx + r;
            float qy = Mathf.Abs(py) - hy + r;
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>Equilateral triangle pointing up with circumradius r (after Inigo Quilez).</summary>
        static float TriangleDistance(float px, float py, float r)
        {
            const float k = 1.7320508f;
            float side = r * k * 0.5f; // half side length for circumradius r... scaled for a balanced look
            px = Mathf.Abs(px) - side;
            py = py + side / k;
            if (px + k * py > 0f)
            {
                float nx = (px - k * py) * 0.5f;
                float ny = (-k * px - py) * 0.5f;
                px = nx;
                py = ny;
            }
            px -= Mathf.Clamp(px, -2f * side, 0f);
            return -new Vector2(px, py).magnitude * Mathf.Sign(py);
        }

        /// <summary>Five-point star, outer radius r, inner/outer ratio rf, one point straight up (after Inigo Quilez).</summary>
        static float StarDistance(float px, float py, float r, float rf)
        {
            Vector2 k1 = new Vector2(0.809016994375f, -0.587785252292f);
            Vector2 k2 = new Vector2(-k1.x, k1.y);
            var p = new Vector2(Mathf.Abs(px), py);
            p -= 2f * Mathf.Max(Vector2.Dot(k1, p), 0f) * k1;
            p -= 2f * Mathf.Max(Vector2.Dot(k2, p), 0f) * k2;
            p.x = Mathf.Abs(p.x);
            p.y -= r;
            Vector2 ba = rf * new Vector2(-k1.y, k1.x) - new Vector2(0f, 1f);
            float h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0f, r);
            Vector2 d = p - ba * h;
            return d.magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
        }
    }
}
