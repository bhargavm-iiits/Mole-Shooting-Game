using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MoleShot.EditorTools
{
    /// <summary>Procedural textures and meshes used by the scene builder (written as assets, so builds are self-contained).</summary>
    public static class ProceduralArt
    {
        // ---------------------------------------------------------------- textures

        public static Texture2D Paint(int w, int h, Func<float, float, Color> f, int ss = 3)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            float inv = 1f / (ss * ss);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color acc = Color.clear;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    Color c = f(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss);
                    // premultiply for correct averaging of coverage
                    acc.r += c.r * c.a; acc.g += c.g * c.a; acc.b += c.b * c.a; acc.a += c.a;
                }
                acc *= inv;
                if (acc.a > 1e-5f) { acc.r /= acc.a; acc.g /= acc.a; acc.b /= acc.a; }
                px[y * w + x] = acc;
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        public static Texture2D SavePng(Texture2D tex, string assetPath, Action<TextureImporter> configure)
        {
            File.WriteAllBytes(Path.GetFullPath(assetPath), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            configure?.Invoke(imp);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        static float Smooth(float edge0, float edge1, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));
        static float Cov(float sd) => Mathf.Clamp01(0.5f - sd); // signed distance (px) -> coverage

        /// <summary>Dot tile for the dotted trajectory line (wider than tall so dots stay round at the chosen spacing).</summary>
        public static Texture2D Dot(int w, int h, float radius)
        {
            return Paint(w, h, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(w * 0.5f, h * 0.5f)) - radius;
                return new Color(1, 1, 1, Cov(d));
            });
        }

        public static Texture2D SoftCircle(int s)
        {
            return Paint(s, s, (x, y) =>
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
                float a = Mathf.Clamp01(1f - r);
                return new Color(1, 1, 1, a * a);
            }, 1);
        }

        public static Texture2D RoundedRect(int s, float radius)
        {
            return Paint(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(Mathf.Abs(x - s * 0.5f), Mathf.Abs(y - s * 0.5f));
                Vector2 b = new Vector2(s * 0.5f - radius, s * 0.5f - radius);
                Vector2 q = new Vector2(Mathf.Max(p.x - b.x, 0f), Mathf.Max(p.y - b.y, 0f));
                float d = q.magnitude + Mathf.Min(Mathf.Max(p.x - b.x, p.y - b.y), 0f) - radius;
                return new Color(1, 1, 1, Cov(d));
            });
        }

        /// <summary>Filled translucent disk with a solid rim (aim/landing ring), white (tinted by material).</summary>
        public static Texture2D AimRing(int s, float fillAlpha, float rimInner, float rimOuter)
        {
            return Paint(s, s, (x, y) =>
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(s * 0.5f, s * 0.5f));
                float px = s * 0.5f;
                float outerCov = Cov(r - rimOuter * px);   // 1 inside the outer edge
                float innerCov = Cov(rimInner * px - r);   // 1 inside the inner edge
                float a = outerCov * (1f - innerCov) + innerCov * fillAlpha;
                return new Color(1, 1, 1, a);
            });
        }

        public static Texture2D HolePit(int s)
        {
            Color center = new Color(0.03f, 0.02f, 0.015f);
            Color edge = new Color(0.20f, 0.12f, 0.06f);
            return Paint(s, s, (x, y) =>
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
                float t = Mathf.Pow(Mathf.Clamp01(r), 3f);
                Color c = Color.Lerp(center, edge, t);
                c.a = 1f;
                return c;
            }, 1);
        }

        public static Texture2D Stopwatch(int s)
        {
            Vector2 c = new Vector2(s * 0.5f, s * 0.45f);
            float R = s * 0.34f, th = s * 0.075f;
            return Paint(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float d = Mathf.Abs(Vector2.Distance(p, c) - R) - th * 0.5f;           // ring
                float knob = Box(p, new Vector2(s * 0.5f, c.y + R + s * 0.08f), new Vector2(s * 0.06f, s * 0.05f));
                float cap = Box(p, new Vector2(s * 0.5f, c.y + R + s * 0.15f), new Vector2(s * 0.12f, s * 0.035f));
                Vector2 btnC = c + (Vector2)(Quaternion.Euler(0, 0, -45f) * Vector2.up) * (R + s * 0.07f);
                float btn = Vector2.Distance(p, btnC) - s * 0.045f;
                float hand = Segment(p, c, c + (Vector2)(Quaternion.Euler(0, 0, -40f) * Vector2.up) * (R * 0.62f)) - th * 0.45f;
                float hub = Vector2.Distance(p, c) - th * 0.6f;
                float sd = Mathf.Min(Mathf.Min(d, knob), Mathf.Min(Mathf.Min(cap, btn), Mathf.Min(hand, hub)));
                return new Color(1, 1, 1, Cov(sd));
            });
        }

        public static Texture2D Bolt(int s)
        {
            // Lightning bolt polygon in unit space (y up).
            Vector2[] poly =
            {
                new Vector2(0.56f, 0.97f), new Vector2(0.20f, 0.45f), new Vector2(0.45f, 0.45f),
                new Vector2(0.34f, 0.03f), new Vector2(0.80f, 0.58f), new Vector2(0.55f, 0.58f), new Vector2(0.76f, 0.97f)
            };
            for (int i = 0; i < poly.Length; i++) poly[i] *= s;
            Color fill = new Color(1f, 0.80f, 0.12f), line = new Color(0.95f, 0.42f, 0.02f);
            float lw = s * 0.045f;
            return Paint(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float dEdge = float.MaxValue;
                for (int i = 0; i < poly.Length; i++) dEdge = Mathf.Min(dEdge, Segment(p, poly[i], poly[(i + 1) % poly.Length]));
                bool inside = Inside(p, poly);
                float sd = inside ? -dEdge : dEdge;          // signed distance to the polygon
                float outer = sd - lw;                        // outline grows outward
                if (outer > 1f) return Color.clear;
                Color c = sd < -lw * 0.2f ? fill : line;
                c.a = Cov(outer);
                return c;
            });
        }

        /// <summary>Stadium crowd: stepped tiers with rows of colourful spectators (cheap backdrop texture).</summary>
        public static Texture2D Crowd(int w, int h, int rows, int seed = 3)
        {
            var rnd = new System.Random(seed);
            var px = new Color[w * h];
            Color[] shirts =
            {
                new Color(0.85f, 0.2f, 0.2f), new Color(0.95f, 0.8f, 0.2f), new Color(0.2f, 0.45f, 0.9f), new Color(0.95f, 0.95f, 0.95f),
                new Color(0.95f, 0.55f, 0.15f), new Color(0.3f, 0.75f, 0.35f), new Color(0.55f, 0.3f, 0.7f), new Color(0.15f, 0.2f, 0.45f)
            };
            Color[] skins = { new Color(0.95f, 0.8f, 0.65f), new Color(0.8f, 0.6f, 0.45f), new Color(0.55f, 0.38f, 0.28f) };
            float rowH = h / (float)rows;
            for (int y = 0; y < h; y++)
            {
                int r = Mathf.Min(rows - 1, (int)(y / rowH));
                float inRow = (y - r * rowH) / rowH;
                Color step = (r % 2 == 0) ? new Color(0.30f, 0.33f, 0.40f) : new Color(0.36f, 0.39f, 0.46f);
                if (inRow < 0.12f) step *= 0.7f; // step edge shadow
                step.a = 1f;
                for (int x = 0; x < w; x++) px[y * w + x] = step;
            }
            for (int r = 0; r < rows; r++)
            {
                float baseY = r * rowH + rowH * 0.15f;
                float spacing = rowH * 0.62f;
                for (float x = (float)rnd.NextDouble() * spacing; x < w; x += spacing * (0.8f + 0.5f * (float)rnd.NextDouble()))
                {
                    if (rnd.NextDouble() < 0.08) continue; // empty seat
                    Color shirt = shirts[rnd.Next(shirts.Length)];
                    Color skin = skins[rnd.Next(skins.Length)];
                    float bw = spacing * 0.36f, bh = rowH * 0.42f, hr = spacing * 0.17f;
                    float bx = x + (float)(rnd.NextDouble() - 0.5) * 2f;
                    FillEllipse(px, w, h, bx, baseY + bh * 0.5f, bw, bh * 0.55f, shirt);
                    FillEllipse(px, w, h, bx, baseY + bh + hr * 0.6f, hr, hr, skin);
                    if (rnd.NextDouble() < 0.25) FillEllipse(px, w, h, bx + bw * 0.9f, baseY + bh * 1.2f, bw * 0.22f, bh * 0.45f, skin); // raised arm
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static void FillEllipse(Color[] px, int w, int h, float cx, float cy, float rx, float ry, Color c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(cx + rx));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(cy + ry));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                float d = dx * dx + dy * dy;
                if (d > 1f) continue;
                float a = Mathf.Clamp01((1f - d) * 4f);
                px[y * w + x] = Color.Lerp(px[y * w + x], new Color(c.r, c.g, c.b, 1f), a);
            }
        }

        /// <summary>
        /// Packed-dirt texture for the hole rim: brown value noise, lighter top, dark grooves between
        /// <paramref name="blocks"/> clods per tile (matches ChunkyTorus' block layout when tiled 6x around 18 blocks).
        /// </summary>
        public static Texture2D Dirt(int w, int h, int blocks)
        {
            var rnd = new System.Random(11);
            var lattice = new float[32 * 8];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rnd.NextDouble();
            float Noise(float x, float y)
            {
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
                float fx = x - xi, fy = y - yi;
                float L(int a, int b) => lattice[((b % 8 + 8) % 8) * 32 + ((a % 32 + 32) % 32)];
                float u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
                return Mathf.Lerp(Mathf.Lerp(L(xi, yi), L(xi + 1, yi), u), Mathf.Lerp(L(xi, yi + 1), L(xi + 1, yi + 1), u), v);
            }
            Color dark = new Color(0.36f, 0.22f, 0.11f), mid = new Color(0.58f, 0.39f, 0.21f), light = new Color(0.72f, 0.53f, 0.32f);
            return Paint(w, h, (x, y) =>
            {
                float u = x / w, v = y / h;
                float n = Noise(u * 32f, v * 8f) * 0.6f + Noise(u * 32f * 2.3f + 5f, v * 8f * 2.3f) * 0.4f;
                // v wraps around the tube: v=0.25 is the top of the torus (sin = 1)
                float top = Mathf.Clamp01(Mathf.Sin(v * Mathf.PI * 2f) * 0.5f + 0.5f);
                Color c = Color.Lerp(mid, light, top * 0.7f + n * 0.3f);
                c = Color.Lerp(c, dark, Mathf.Clamp01((0.35f - n) * 1.5f));
                float bu = u * blocks, frac = bu - Mathf.Floor(bu);
                float groove = Mathf.Clamp01(1f - Mathf.Min(frac, 1f - frac) * 14f);
                c = Color.Lerp(c, dark * 0.8f, groove * 0.85f);
                c.a = 1f;
                return c;
            }, 1);
        }

        /// <summary>2x1 stripe tile (point filtered): darker / lighter mowing bands.</summary>
        public static Texture2D StripeTile()
        {
            var tex = new Texture2D(2, 1, TextureFormat.RGBA32, false);
            tex.SetPixels(new[] { new Color(0f, 0.12f, 0f, 0.16f), new Color(0.75f, 1f, 0.6f, 0.07f) });
            tex.Apply();
            return tex;
        }

        /// <summary>White padlock icon (shackle ring + rounded body) for locked level cards.</summary>
        public static Texture2D Padlock(int s)
        {
            return Paint(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float body = Box(p, new Vector2(s * 0.5f, s * 0.34f), new Vector2(s * 0.3f, s * 0.22f)) - s * 0.04f;
                Vector2 c = new Vector2(s * 0.5f, s * 0.58f);
                float ring = Mathf.Abs(Vector2.Distance(p, c) - s * 0.19f) - s * 0.055f;
                if (p.y < c.y) ring = Mathf.Max(ring, Mathf.Abs(p.x - s * 0.5f) < s * 0.13f ? 1e3f : ring);
                if (p.y < s * 0.5f) ring = 1e3f; // shackle only above the body
                float hole = Vector2.Distance(p, new Vector2(s * 0.5f, s * 0.36f)) - s * 0.05f;
                float sd = Mathf.Min(body, ring);
                float a = Cov(sd) * (1f - Cov(hole));
                return new Color(1, 1, 1, a);
            });
        }

        /// <summary>Five-point star with a soft inner bevel (white; tinted gold / dark by the UI).</summary>
        public static Texture2D Star(int s)
        {
            var poly = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = (i % 2 == 0 ? 0.47f : 0.2f) * s;
                poly[i] = new Vector2(s * 0.5f + Mathf.Cos(a) * r, s * 0.48f + Mathf.Sin(a) * r);
            }
            float round = s * 0.03f;
            return Paint(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float d = float.MaxValue;
                for (int i = 0; i < poly.Length; i++) d = Mathf.Min(d, Segment(p, poly[i], poly[(i + 1) % poly.Length]));
                float sd = (Inside(p, poly) ? -d : d) - round;
                float bevel = Mathf.Clamp01(-sd / (s * 0.12f));
                float shade = Mathf.Lerp(0.78f, 1f, bevel);
                return new Color(shade, shade, shade, Cov(sd));
            });
        }

        /// <summary>White check mark icon for completed level cards.</summary>
        public static Texture2D Tick(int s)
        {
            Vector2 a = new Vector2(s * 0.2f, s * 0.52f), b = new Vector2(s * 0.42f, s * 0.28f), c = new Vector2(s * 0.82f, s * 0.74f);
            return Paint(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float d = Mathf.Min(Segment(p, a, b), Segment(p, b, c)) - s * 0.075f;
                return new Color(1, 1, 1, Cov(d));
            });
        }

        /// <summary>Small white rectangle used for UI confetti pieces.</summary>
        public static Texture2D Confetti(int w, int h)
        {
            return Paint(w, h, (x, y) => new Color(1, 1, 1, 1f), 1);
        }

        static float Box(Vector2 p, Vector2 c, Vector2 half)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x - c.x) - half.x, Mathf.Abs(p.y - c.y) - half.y);
            return new Vector2(Mathf.Max(d.x, 0), Mathf.Max(d.y, 0)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        static bool Inside(Vector2 p, Vector2[] poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                    c = !c;
            }
            return c;
        }

        // ---------------------------------------------------------------- meshes

        /// <summary>Chunky dirt rim: a flattened torus whose tube radius varies in blocks, like stacked clods.</summary>
        public static Mesh ChunkyTorus(float major, float minor, float flatten, int seg = 64, int sides = 12, int blocks = 18, int seed = 7)
        {
            var rnd = new System.Random(seed);
            var blockScale = new float[blocks];
            for (int i = 0; i < blocks; i++) blockScale[i] = 0.86f + (float)rnd.NextDouble() * 0.28f;

            var verts = new Vector3[(seg + 1) * (sides + 1)];
            var uvs = new Vector2[verts.Length];
            var tris = new int[seg * sides * 6];
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg;
                float a = u * Mathf.PI * 2f;
                // block profile: flat-ish inside each block, a groove between blocks
                float bf = u * blocks;
                int bi = Mathf.FloorToInt(bf) % blocks;
                float local = bf - Mathf.Floor(bf);
                float groove = Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(Mathf.Min(local, 1f - local) * 8f));
                float rr = minor * blockScale[bi] * groove;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int j = 0; j <= sides; j++)
                {
                    float v = j / (float)sides;
                    float b = v * Mathf.PI * 2f;
                    Vector3 p = dir * (major + Mathf.Cos(b) * rr) + Vector3.up * (Mathf.Sin(b) * rr * flatten);
                    if (p.y < -0.02f) p.y = -0.02f; // flatten the underside
                    verts[i * (sides + 1) + j] = p;
                    uvs[i * (sides + 1) + j] = new Vector2(u * 6f, v);
                }
            }
            int t = 0;
            for (int i = 0; i < seg; i++)
            for (int j = 0; j < sides; j++)
            {
                int a0 = i * (sides + 1) + j, a1 = a0 + 1, b0 = a0 + sides + 1, b1 = b0 + 1;
                tris[t++] = a0; tris[t++] = a1; tris[t++] = b0;
                tris[t++] = b0; tris[t++] = a1; tris[t++] = b1;
            }
            var m = new Mesh { name = "ChunkyTorus" };
            m.vertices = verts; m.uv = uvs; m.triangles = tris;
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Flat disk / quad in the XZ plane facing up, UV 0..1 over the bounds.</summary>
        public static Mesh FlatDisk(float radius, int seg = 48)
        {
            var verts = new Vector3[seg + 1];
            var uvs = new Vector2[seg + 1];
            var tris = new int[seg * 3];
            verts[0] = Vector3.zero; uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                uvs[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
                tris[i * 3] = 0; tris[i * 3 + 1] = 1 + (i + 1) % seg; tris[i * 3 + 2] = 1 + i;
            }
            var m = new Mesh { name = "FlatDisk" };
            m.vertices = verts; m.uv = uvs; m.triangles = tris;
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        public static Mesh FlatQuad(float sizeX, float sizeZ)
        {
            float x = sizeX * 0.5f, z = sizeZ * 0.5f;
            var m = new Mesh { name = "FlatQuad" };
            m.vertices = new[] { new Vector3(-x, 0, -z), new Vector3(-x, 0, z), new Vector3(x, 0, z), new Vector3(x, 0, -z) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Upright quad in the XY plane (for billboards), pivot at centre.</summary>
        public static Mesh UprightQuad(float w, float h)
        {
            float x = w * 0.5f, y = h * 0.5f;
            var m = new Mesh { name = "UprightQuad" };
            m.vertices = new[] { new Vector3(-x, -y, 0), new Vector3(-x, y, 0), new Vector3(x, y, 0), new Vector3(x, -y, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Arrowhead triangle in the XY plane pointing +Y, tip at the origin.</summary>
        public static Mesh Arrow(float width, float length)
        {
            var m = new Mesh { name = "Arrow" };
            m.vertices = new[] { new Vector3(0, 0, 0), new Vector3(-width * 0.5f, -length, 0), new Vector3(0, -length * 0.7f, 0), new Vector3(width * 0.5f, -length, 0) };
            m.uv = new[] { new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(0.5f, 0.3f), new Vector2(1, 0) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
