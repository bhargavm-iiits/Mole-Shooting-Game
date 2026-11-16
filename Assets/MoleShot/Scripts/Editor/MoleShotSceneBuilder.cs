using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MoleShot.EditorTools
{
    /// <summary>
    /// Regenerates the whole Mole Shot prototype from code: generated textures, meshes, materials, config, prefabs,
    /// the scene, build settings and player settings. Idempotent: re-running wipes and rebuilds the generated
    /// content, so the scene is always reproducible.  Menu: Mole Shot/Build Scene.
    /// </summary>
    public static class MoleShotSceneBuilder
    {
        public const string Root = "Assets/MoleShot";
        const string GenDir = Root + "/Art/Generated";
        const string MatDir = Root + "/Materials";
        const string PrefabDir = Root + "/Prefabs";
        const string ConfigDir = Root + "/Config";
        const string SceneDir = Root + "/Scenes";
        public const string ScenePath = SceneDir + "/MoleShot.unity";
        public const string ConfigPath = ConfigDir + "/MoleShotConfig.asset";

        const string FootballFbx = "Assets/Game Assets/football/model/football.fbx";
        const string FootballDiff = "Assets/Game Assets/football/textures/football_diff.png";
        const string FootballNor = "Assets/Game Assets/football/textures/football_nor.png";
        const string FieldGlb = "Assets/Game Assets/football-field/source/pole.glb";
        const string MoleGlb = "Assets/Game Assets/mole/source/model.glb";
        const string MoleTex = "Assets/Game Assets/mole/textures/Image_0_0.jpeg";

        // ---- layout (metres) ----
        static readonly Vector3 BallPos = new Vector3(0f, 0.3f, 0f);
        const float BallDiameter = 0.6f;
        static readonly Vector3[] HolePositions =
        {
            new Vector3(-2.7f, 0f, 9.5f), new Vector3(0f, 0f, 9.5f), new Vector3(2.7f, 0f, 9.5f),   // back row
            new Vector3(-2.3f, 0f, 5f), new Vector3(0f, 0f, 5f), new Vector3(2.3f, 0f, 5f),   // front row
        };
        const int HintHoleIndex = 5; // front-right
        static readonly Vector3 CameraPos = new Vector3(0f, 4.75f, -4.25f);
        static readonly Vector3 CameraLookAt = new Vector3(0f, 0f, 5.5f);
        const float FieldYaw = 90f;           // pitch long axis (goals) along world Z
        const float FieldScale = 1.7f;
        const float FieldCenterZ = 0f;
        // Goals and the solid boards behind them would tower over the play area at this scale.
        // Cube.019 (1 m solid board) + Cube.044 (net above it) on the far-left fence section are replaced by a mirrored
        // copy of Cube.047, the full-height net on the far-right section, so both sides behind the holes match.
        static readonly string[] HiddenFieldParts = { "Cube.004", "Cube.005", "Cube.015", "Cube.016", "Cube.020", "Cube.019", "Cube.044" };
        const string SymmetrySource = "Cube.047";

        // ---- mole ----
        const float MoleWidth = 1.3f;          // arm span of the imported mesh
        const float MoleShowHeight = 0.78f;   // how much of the mole sticks out of the hole in its up pose
        const float MoleYaw = 180f;           // the imported mesh faces +Z; turn it to face the camera (-Z)
        const float RimMajor = 0.62f, RimMinor = 0.2f;
        const float PitRadius = 0.6f;

        // generated assets cache for one build
        static Font font;
        static Sprite roundSprite, stopwatchSprite, lockSprite, tickSprite, starSprite;
        static Texture2D dotTex, softTex, ringTex, pitTex, boltTex;
        static Material matBall, matMole, matRim, matPit, matLine, matRing, matArrow, matBolt, matGlow,
                        matParticles, matChalk, matSky, matHighlight, matStripes, matShadow, matBoard, matChalkSpot, matTrail, matBallShadow;
        static Mesh meshTorus, meshPit, meshFlatQuad, meshArrow, meshBoltQuad, meshUnitDisk;

        [MenuItem("Mole Shot/Build Scene")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("Exit Play Mode before building the scene."); return; }
            EnsureFolders();
            BuildArt();
            var config = BuildConfig();
            var holePrefab = BuildHolePrefab(config);
            var ballPrefab = BuildBallPrefab(config);
            BuildScene(config, holePrefab, ballPrefab);
            ConfigureBuildSettings();
            ConfigurePlayerSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[MoleShot] Scene built: " + ScenePath);
        }

        // =====================================================================================================
        static void EnsureFolders()
        {
            foreach (var d in new[] { Root + "/Art", GenDir, MatDir, PrefabDir, ConfigDir, SceneDir })
            {
                if (AssetDatabase.IsValidFolder(d)) continue;
                string parent = Path.GetDirectoryName(d).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(d));
            }
        }

        // =====================================================================================================
        static void BuildArt()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            dotTex = ProceduralArt.SavePng(ProceduralArt.Dot(192, 64, 24f), GenDir + "/Dot.png", i => { i.wrapModeU = TextureWrapMode.Repeat; i.wrapModeV = TextureWrapMode.Clamp; i.mipmapEnabled = true; });
            softTex = ProceduralArt.SavePng(ProceduralArt.SoftCircle(64), GenDir + "/SoftCircle.png", i => i.wrapMode = TextureWrapMode.Clamp);
            ringTex = ProceduralArt.SavePng(ProceduralArt.AimRing(256, 0.28f, 0.86f, 0.98f), GenDir + "/AimRing.png", i => { i.wrapMode = TextureWrapMode.Clamp; i.mipmapEnabled = true; });
            pitTex = ProceduralArt.SavePng(ProceduralArt.HolePit(128), GenDir + "/HolePit.png", i => i.wrapMode = TextureWrapMode.Clamp);
            boltTex = ProceduralArt.SavePng(ProceduralArt.Bolt(128), GenDir + "/Bolt.png", i => { i.wrapMode = TextureWrapMode.Clamp; i.mipmapEnabled = true; });
            ProceduralArt.SavePng(ProceduralArt.RoundedRect(64, 30f), GenDir + "/RoundedRect.png", i =>
            {
                i.textureType = TextureImporterType.Sprite; i.spriteImportMode = SpriteImportMode.Single;
                i.spriteBorder = new Vector4(31, 31, 31, 31); i.wrapMode = TextureWrapMode.Clamp; i.spritePixelsPerUnit = 100;
            });
            ProceduralArt.SavePng(ProceduralArt.Stopwatch(128), GenDir + "/Stopwatch.png", i =>
            {
                i.textureType = TextureImporterType.Sprite; i.spriteImportMode = SpriteImportMode.Single; i.mipmapEnabled = true;
            });
            roundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(GenDir + "/RoundedRect.png");
            stopwatchSprite = AssetDatabase.LoadAssetAtPath<Sprite>(GenDir + "/Stopwatch.png");
            foreach (var (file, tex) in new[] { ("Lock.png", ProceduralArt.Padlock(128)), ("Tick.png", ProceduralArt.Tick(128)), ("Star.png", ProceduralArt.Star(256)) })
                ProceduralArt.SavePng(tex, GenDir + "/" + file, i => { i.textureType = TextureImporterType.Sprite; i.spriteImportMode = SpriteImportMode.Single; i.mipmapEnabled = true; });
            lockSprite = AssetDatabase.LoadAssetAtPath<Sprite>(GenDir + "/Lock.png");
            tickSprite = AssetDatabase.LoadAssetAtPath<Sprite>(GenDir + "/Tick.png");
            starSprite = AssetDatabase.LoadAssetAtPath<Sprite>(GenDir + "/Star.png");

            // Football normal map must be imported as a normal map.
            if (AssetImporter.GetAtPath(FootballNor) is TextureImporter nor && nor.textureType != TextureImporterType.NormalMap)
            { nor.textureType = TextureImporterType.NormalMap; nor.SaveAndReimport(); }

            // Meshes
            meshTorus = SaveMesh(ProceduralArt.ChunkyTorus(RimMajor, RimMinor, 0.9f), "RimTorus");
            meshPit = SaveMesh(ProceduralArt.FlatDisk(PitRadius), "PitDisk");
            meshUnitDisk = SaveMesh(ProceduralArt.FlatDisk(0.5f), "UnitDisk");
            meshFlatQuad = SaveMesh(ProceduralArt.FlatQuad(1f, 1f), "FlatQuad");
            meshArrow = SaveMesh(ProceduralArt.Arrow(0.34f, 0.32f), "Arrow");
            meshBoltQuad = SaveMesh(ProceduralArt.UprightQuad(1f, 1f), "UprightQuad");

            // Materials
            matBall = Lit("Ball", Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(FootballDiff), AssetDatabase.LoadAssetAtPath<Texture2D>(FootballNor), 0.45f);
            matMole = Lit("Mole", Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(MoleTex), null, 0.25f);
            // glTFast flips V on import (glTF UV origin is top-left); flip it back for the Unity-imported texture.
            matMole.SetTextureScale("_BaseMap", new Vector2(1f, -1f));
            matMole.SetTextureOffset("_BaseMap", new Vector2(0f, 1f));
            var dirtTex = ProceduralArt.SavePng(ProceduralArt.Dirt(256, 64, 3), GenDir + "/Dirt.png", i => { i.wrapMode = TextureWrapMode.Repeat; i.mipmapEnabled = true; i.alphaIsTransparency = false; });
            matRim = Lit("DirtRim", Color.white, dirtTex, null, 0.05f);
            matPit = Unlit("HolePit", Color.white, pitTex, false);
            matLine = ParticleMat("TrajectoryDots", dotTex, false);
            matRing = Unlit("LandingRing", new Color(1, 1, 1, 0.4f), ringTex, true);
            matHighlight = Unlit("AimHighlight", new Color(0.85f, 1f, 0.7f, 0.45f), ringTex, true);
            matArrow = Unlit("ArrowHead", Color.white, null, true);
            matBolt = Unlit("FastBolt", Color.white, boltTex, true);
            matGlow = Unlit("FastGlow", new Color(1f, 0.62f, 0.1f, 0.55f), softTex, true);
            matParticles = ParticleMat("Particles", softTex, false);
            matChalk = Unlit("Chalk", new Color(1, 1, 1, 0.92f), null, true);
            matChalkSpot = Unlit("ChalkSpot", new Color(1f, 1f, 1f, 0.85f), softTex, true);
            matTrail = ParticleMat("BallTrail", null, false);
            matBallShadow = Unlit("BallShadow", new Color(0f, 0f, 0f, 0.5f), softTex, true);
            matShadow = Unlit("ContactShadow", new Color(0.05f, 0.08f, 0.02f, 0.45f), softTex, true);

            // The painted crowd backdrop was replaced by 3D people (CrowdBuilder); remove its old generated assets.
            AssetDatabase.DeleteAsset(GenDir + "/Crowd.png");
            AssetDatabase.DeleteAsset(MatDir + "/Crowd.mat");
            var stripeTex = ProceduralArt.SavePng(ProceduralArt.StripeTile(), GenDir + "/Stripes.png", i => { i.filterMode = FilterMode.Point; i.wrapMode = TextureWrapMode.Repeat; i.npotScale = TextureImporterNPOTScale.None; });
            matStripes = Unlit("MowingStripes", Color.white, stripeTex, true);
            matStripes.SetTextureScale("_BaseMap", new Vector2(4f, 1f));
            matBoard = Unlit("AdBoard", new Color(0.12f, 0.32f, 0.78f), null, false);

            matSky = LoadOrCreate<Material>(MatDir + "/Sky.mat", () => new Material(Shader.Find("Skybox/Procedural")));
            matSky.SetFloat("_SunSize", 0.03f);
            matSky.SetFloat("_AtmosphereThickness", 0.75f);
            matSky.SetColor("_SkyTint", new Color(0.35f, 0.55f, 0.95f));
            matSky.SetColor("_GroundColor", new Color(0.35f, 0.42f, 0.3f));
            matSky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(matSky);
        }

        static Mesh SaveMesh(Mesh m, string name)
        {
            string path = GenDir + "/" + name + ".asset";
            m.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(m, existing); Object.DestroyImmediate(m); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = create();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static Material Lit(string name, Color color, Texture2D baseMap, Texture2D normal, float smoothness)
        {
            var m = LoadOrCreate(MatDir + "/" + name + ".mat", () => new Material(Shader.Find("Universal Render Pipeline/Lit")));
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", baseMap);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string name, Color color, Texture2D tex, bool transparent)
        {
            var m = LoadOrCreate(MatDir + "/" + name + ".mat", () => new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            m.shader = Shader.Find("Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", tex);
            SetTransparent(m, transparent, false);
            m.SetFloat("_Cull", (float)CullMode.Off);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material ParticleMat(string name, Texture2D tex, bool additive)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = LoadOrCreate(MatDir + "/" + name + ".mat", () => new Material(sh));
            m.shader = sh;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            SetTransparent(m, true, additive);
            m.SetFloat("_Cull", (float)CullMode.Off);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetTransparent(Material m, bool transparent, bool additive)
        {
            if (!transparent)
            {
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = -1;
                return;
            }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        // =====================================================================================================
        static MoleShotConfig BuildConfig()
        {
            // The config asset is regenerated from code defaults so the build is reproducible; tune the defaults in
            // MoleShotConfig.cs (or edit the asset afterwards without re-running the builder).
            var fresh = ScriptableObject.CreateInstance<MoleShotConfig>();
            fresh.name = "MoleShotConfig";
            var cfg = AssetDatabase.LoadAssetAtPath<MoleShotConfig>(ConfigPath);
            if (cfg == null) { AssetDatabase.CreateAsset(fresh, ConfigPath); return fresh; }
            EditorUtility.CopySerialized(fresh, cfg);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        // =====================================================================================================
        static GameObject NewChild(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }

        static MeshRenderer AddMesh(GameObject go, Mesh mesh, Material mat, bool shadows)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = shadows;
            return r;
        }

        static GameObject BuildHolePrefab(MoleShotConfig cfg)
        {
            var root = new GameObject("Hole");
            var hole = root.AddComponent<HoleController>();

            var rim = NewChild("Rim", root.transform, new Vector3(0f, 0.02f, 0f));
            AddMesh(rim, meshTorus, matRim, true);

            var shadow = NewChild("ContactShadow", root.transform, new Vector3(0f, 0.006f, 0f));
            shadow.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
            AddMesh(shadow, meshFlatQuad, matShadow, false);

            var pit = NewChild("Pit", root.transform, new Vector3(0f, 0.012f, 0f));
            AddMesh(pit, meshPit, matPit, false);

            var hl = NewChild("AimHighlight", root.transform, new Vector3(0f, 0.03f, 0f));
            hl.transform.localScale = new Vector3(2.15f, 1f, 2.15f);
            var hlr = AddMesh(hl, meshFlatQuad, matHighlight, false);
            hlr.enabled = false;

            var glow = NewChild("FastGlow", root.transform, new Vector3(0f, 0.025f, 0f));
            glow.transform.localScale = new Vector3(2.6f, 1f, 2.6f);
            AddMesh(glow, meshFlatQuad, matGlow, false);

            // Mole: root moves vertically; visual pivots at ground level for squash/wobble.
            var moleRoot = NewChild("MoleRoot", root.transform, Vector3.zero);
            var visual = NewChild("MoleVisual", moleRoot.transform, Vector3.zero);
            var moleMesh = LoadMoleMesh();
            float scale = MoleWidth / Mathf.Max(0.01f, moleMesh.bounds.size.x);
            float halfH = moleMesh.bounds.extents.y * scale;
            var body = NewChild("MoleMesh", visual.transform, new Vector3(0f, MoleShowHeight - halfH - moleMesh.bounds.center.y * scale, 0f));
            body.transform.localRotation = Quaternion.Euler(0f, MoleYaw, 0f);
            body.transform.localScale = Vector3.one * scale;
            AddMesh(body, moleMesh, matMole, true);

            var bolt = NewChild("FastMarker", visual.transform, new Vector3(0f, MoleShowHeight + 0.38f, 0f));
            bolt.transform.localScale = new Vector3(0.62f, 0.62f, 0.62f);
            AddMesh(bolt, meshBoltQuad, matBolt, false);
            bolt.AddComponent<Billboard>();

            var hit = NewChild("Hitbox", moleRoot.transform, new Vector3(0f, MoleShowHeight * 0.5f, 0f));
            var sc = hit.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.5f;
            var rb = hit.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var hb = hit.AddComponent<MoleHitbox>();
            Wire(hb, "hole", hole);

            Wire(hole, "config", cfg);
            Wire(hole, "moleRoot", moleRoot.transform);
            Wire(hole, "moleVisual", visual.transform);
            Wire(hole, "moleHitbox", sc);
            Wire(hole, "fastMarker", bolt);
            Wire(hole, "fastGlow", glow);
            Wire(hole, "aimHighlight", hlr);
            SetFloat(hole, "hiddenY", -(MoleShowHeight + 0.25f));
            SetFloat(hole, "upY", 0f);
            SetFloat(hole, "radius", RimMajor + RimMinor * 0.5f);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Hole.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Mesh LoadMoleMesh()
        {
            var mesh = AssetDatabase.LoadAllAssetsAtPath(MoleGlb).OfType<Mesh>().FirstOrDefault();
            if (mesh == null) throw new System.Exception("Mole mesh not found in " + MoleGlb + " (glTFast import failed?)");
            return mesh;
        }

        static GameObject BuildBallPrefab(MoleShotConfig cfg)
        {
            var root = new GameObject("Ball");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 0.45f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;
            var col = root.AddComponent<SphereCollider>();
            col.radius = BallDiameter * 0.5f;
            var pm = LoadOrCreate(MatDir + "/BallBounce.asset", () => new PhysicsMaterial("BallBounce"));
            pm.bounciness = 0.35f;
            pm.dynamicFriction = 0.5f;
            pm.staticFriction = 0.5f;
            pm.bounceCombine = PhysicsMaterialCombine.Maximum;
            EditorUtility.SetDirty(pm);
            col.sharedMaterial = pm;
            var ballCtrl = root.AddComponent<BallController>();

            // Visual hierarchy: SquashPivot (kept world-aligned, carries the landing squash) -> Visual (copies the
            // body rotation). A short soft trail follows the ball while it flies.
            var pivot = NewChild("SquashPivot", root.transform, Vector3.zero);
            var trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.minVertexDistance = 0.04f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.42f), new Keyframe(1f, 0f));
            var tg = new Gradient();
            tg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = tg;
            trail.sharedMaterial = matTrail;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            trail.numCapVertices = 4;

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FootballFbx);
            var vis = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(vis, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            vis.name = "Visual";
            vis.transform.SetParent(pivot.transform, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;
            vis.transform.localScale = Vector3.one;
            var rends = vis.GetComponentsInChildren<Renderer>();
            Bounds b = rends[0].bounds;
            foreach (var r in rends) { b.Encapsulate(r.bounds); r.sharedMaterial = matBall; r.shadowCastingMode = ShadowCastingMode.On; }
            float s = BallDiameter / Mathf.Max(0.0001f, b.size.x);
            vis.transform.localScale = Vector3.one * s;
            vis.transform.localPosition = -b.center * s;
            foreach (var c in vis.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            Wire(ballCtrl, "squashPivot", pivot.transform);
            Wire(ballCtrl, "visual", vis.transform);
            Wire(ballCtrl, "trail", trail);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Ball.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // =====================================================================================================
        static CrowdController crowd;

        static void BuildScene(MoleShotConfig cfg, GameObject holePrefab, GameObject ballPrefab)
        {
            crowd = null;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- lighting / environment ----
            RenderSettings.skybox = matSky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.86f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.68f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.36f, 0.3f);
            // Light linear fog only reaches the stand (play area is < 20 m from the camera): it softens and
            // desaturates the busy crowd so the HUD capsules and the moles stay the clear focus.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.70f, 0.79f, 0.90f);
            RenderSettings.fogStartDistance = 22f;
            RenderSettings.fogEndDistance = 70f;

            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.35f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.65f;
            sun.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            RenderSettings.sun = light;

            // ---- camera ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = new Color(0.45f, 0.7f, 0.95f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            camGo.transform.position = CameraPos;
            camGo.transform.LookAt(CameraLookAt);
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var fitter = camGo.AddComponent<PortraitCameraFitter>();
            fitter.referencePoint = new Vector3(0f, 0f, 9.5f);
            fitter.halfWidth = 4.5f;
            fitter.minVerticalFov = 50f;
            fitter.Fit();

            // ---- stadium (visual only) ----
            var env = new GameObject("Environment");
            var fieldAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FieldGlb);
            if (fieldAsset != null)
            {
                var field = (GameObject)PrefabUtility.InstantiatePrefab(fieldAsset);
                field.name = "Stadium";
                field.transform.SetParent(env.transform, false);
                field.transform.localRotation = Quaternion.Euler(0f, FieldYaw, 0f);
                field.transform.localScale = Vector3.one * FieldScale;
                // Put the court surface (Plane.001) at y = 0 and centre it at (0, FieldCenterZ).
                var pitch = field.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "Plane.001");
                if (pitch != null)
                {
                    Bounds pb = pitch.bounds;
                    field.transform.position += new Vector3(-pb.center.x, -pb.max.y - 0.002f, FieldCenterZ - pb.center.z);
                }
                foreach (var r in field.GetComponentsInChildren<Renderer>()) { r.receiveShadows = true; }
                // The model's goals would tower over the play area at this scale; the reference shows open stands instead.
                foreach (var t in field.GetComponentsInChildren<Transform>(true))
                    if (HiddenFieldParts.Contains(t.name)) t.gameObject.SetActive(false);
                MirrorFieldPart(field.transform, SymmetrySource);
                foreach (var c in field.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            else Debug.LogWarning("[MoleShot] Field model missing: " + FieldGlb);

            // Mowing stripes over the court (the pitch model is a flat palette colour).
            var stripes = NewChild("MowingStripes", env.transform, new Vector3(0f, 0.004f, 0f));
            stripes.transform.localScale = new Vector3(22.8f, 1f, 34f);
            AddMesh(stripes, meshFlatQuad, matStripes, false);

            // Stadium stand behind the far fence: stepped tiers with a crowd of 3D people, plus advertising boards.
            crowd = CrowdBuilder.Build(env.transform);
            var boards = NewChild("AdBoards", env.transform, new Vector3(0f, 0.75f, 20.6f));
            boards.transform.localScale = new Vector3(70f, 1.5f, 1f);
            AddMesh(boards, meshBoltQuad, matBoard, false);

            // Post-processing: a little extra saturation/contrast for the bright, sunny look.
            BuildPostFx(env.transform);
            var mark = NewChild("LaunchMark", env.transform, new Vector3(0f, 0.006f, 0f));
            mark.transform.localScale = new Vector3(1.3f, 1f, 1.3f);
            AddMesh(mark, meshFlatQuad, matChalkSpot, false);

            // ---- gameplay ground (invisible, independent of the visual pitch) ----
            var ground = new GameObject("GroundCollider");
            var box = ground.AddComponent<BoxCollider>();
            box.size = new Vector3(60f, 1f, 60f);
            box.center = new Vector3(0f, -0.5f, 5f);

            // ---- holes ----
            var holesRoot = new GameObject("Holes");
            var holes = new HoleController[HolePositions.Length];
            for (int i = 0; i < HolePositions.Length; i++)
            {
                var h = (GameObject)PrefabUtility.InstantiatePrefab(holePrefab, holesRoot.transform);
                h.name = $"Hole_{(i < 3 ? "Back" : "Front")}{i % 3}";
                h.transform.position = HolePositions[i];
                holes[i] = h.GetComponent<HoleController>();
                foreach (var bb in h.GetComponentsInChildren<Billboard>(true)) Wire(bb, "targetCamera", cam);
            }

            // ---- ball ----
            var launchPoint = new GameObject("LaunchPoint");
            launchPoint.transform.position = BallPos;
            var ballGo = (GameObject)PrefabUtility.InstantiatePrefab(ballPrefab);
            ballGo.transform.position = BallPos;
            var ball = ballGo.GetComponent<BallController>();
            // Soft ground shadow blob under the ball (scales/fades with height for depth readability).
            var ballShadow = new GameObject("BallShadow");
            ballShadow.transform.position = new Vector3(BallPos.x, 0.014f, BallPos.z);
            ballShadow.transform.localScale = new Vector3(0.95f, 1f, 0.95f);
            var ballShadowR = AddMesh(ballShadow, meshFlatQuad, matBallShadow, false);
            Wire(ball, "shadow", ballShadow.transform);
            Wire(ball, "shadowRenderer", ballShadowR);

            // ---- trajectory preview ----
            var previewGo = new GameObject("TrajectoryPreview");
            var lr = previewGo.AddComponent<LineRenderer>();
            lr.sharedMaterial = matLine;
            lr.textureMode = LineTextureMode.Tile;
            lr.alignment = LineAlignment.View;
            lr.widthMultiplier = 0.16f;
            // Dots taper toward the end of the arc.
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.55f));
            lr.numCapVertices = 0;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.useWorldSpace = true;
            lr.positionCount = 0;
            var arrow = NewChild("ArrowHead", previewGo.transform, Vector3.zero);
            var arrowR = AddMesh(arrow, meshArrow, matArrow, false);
            var landing = NewChild("LandingRing", previewGo.transform, Vector3.zero);
            landing.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            var landingR = AddMesh(landing, meshFlatQuad, matRing, false);
            var preview = previewGo.AddComponent<TrajectoryPreview>();

            // ---- feedback particles ----
            var fxRoot = new GameObject("FX");
            var hitBurst = MakeBurst("HitBurst", fxRoot.transform, false);
            var dust = MakeBurst("DustPuff", fxRoot.transform, true);

            // ---- UI ----
            var ui = BuildUI(out var hud, out var popupLayer, out var popupTemplate);

            // ---- game systems ----
            var game = new GameObject("Game");
            var round = game.AddComponent<RoundManager>();
            var swipe = game.AddComponent<SwipeInput>();
            var shots = game.AddComponent<ShotController>();
            var feedback = game.AddComponent<FeedbackController>();
            game.AddComponent<AudioSource>();
            var audioFx = game.AddComponent<AudioFeedback>();
            var auto = game.AddComponent<AutoPlayer>();
            auto.enabled = false;

            Wire(round, "config", cfg);
            WireArray(round, "holes", holes);
            Wire(round, "ball", ball);
            Wire(round, "swipeInput", swipe);
            Wire(round, "shots", shots);
            Wire(round, "feedback", feedback);
            Wire(round, "audioFeedback", audioFx);
            Wire(round, "hud", hud);

            Wire(swipe, "config", cfg);
            Wire(swipe, "worldCamera", cam);
            Wire(swipe, "ball", ballGo.transform);

            Wire(shots, "config", cfg);
            Wire(shots, "swipeInput", swipe);
            Wire(shots, "ball", ball);
            Wire(shots, "preview", preview);
            Wire(shots, "round", round);
            Wire(shots, "hud", hud);
            Wire(shots, "audioFeedback", audioFx);
            Wire(shots, "viewCamera", cam);
            Wire(shots, "hintHole", holes[HintHoleIndex]);

            Wire(ball, "config", cfg);
            Wire(ball, "round", round);
            Wire(ball, "groundCollider", box);
            Wire(ball, "launchPoint", launchPoint.transform);

            Wire(preview, "config", cfg);
            Wire(preview, "round", round);
            Wire(preview, "line", lr);
            Wire(preview, "arrowHead", arrow.transform);
            Wire(preview, "arrowRenderer", arrowR);
            Wire(preview, "landingRing", landing.transform);
            Wire(preview, "landingRenderer", landingR);
            Wire(preview, "viewCamera", cam);

            Wire(audioFx, "config", cfg);
            Wire(feedback, "viewCamera", cam);
            Wire(feedback, "popupLayer", popupLayer);
            Wire(feedback, "popupTemplate", popupTemplate);
            Wire(feedback, "hitBurst", hitBurst);
            Wire(feedback, "dustPuff", dust);

            Wire(hud, "round", round);
            Wire(levelSelectUi, "round", round);
            Wire(hud, "audioFeedback", audioFx);

            if (crowd != null) { Wire(crowd, "config", cfg); Wire(crowd, "round", round); }

            Wire(auto, "shots", shots);
            Wire(auto, "round", round);
            Wire(auto, "hud", hud);
            Wire(auto, "config", cfg);

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void BuildPostFx(Transform parent)
        {
            string path = MatDir + "/PostFX.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(24f);
            ca.contrast.Override(10f);
            ca.postExposure.Override(0.12f);
            ca.name = "ColorAdjustments";
            AssetDatabase.AddObjectToAsset(ca, profile);
            EditorUtility.SetDirty(profile);
            var go = NewChild("PostFX", parent, Vector3.zero);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        /// <summary>Duplicates a field part and mirrors it across the pitch centre line (world x = 0).</summary>
        static void MirrorFieldPart(Transform field, string partName)
        {
            var src = field.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == partName);
            if (src == null) { Debug.LogWarning("[MoleShot] Symmetry source missing: " + partName); return; }
            var mirror = new GameObject(partName + "_MirrorRoot").transform;
            mirror.SetParent(field.parent, false);
            mirror.position = Vector3.zero;
            var copy = Object.Instantiate(src.gameObject, src.position, src.rotation);
            copy.name = partName + "_Mirrored";
            copy.transform.localScale = src.lossyScale;
            copy.transform.SetParent(mirror, true);
            mirror.localScale = new Vector3(-1f, 1f, 1f);   // reflect across x = 0
        }

        static ParticleSystem MakeBurst(string name, Transform parent, bool dust)
        {
            var go = NewChild(name, parent, Vector3.zero);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            if (dust)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
                main.startColor = new Color(0.86f, 0.78f, 0.6f, 0.75f);
                main.gravityModifier = -0.05f;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 0.2f;
                go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
            }
            else
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
                main.startColor = new Color(1f, 0.86f, 0.2f);
                main.gravityModifier = 1.1f;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.25f;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = matParticles;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // =====================================================================================================
        static readonly Color Navy = new Color(0.055f, 0.11f, 0.27f, 0.97f);
        static readonly Color ScoreYellow = new Color(1f, 0.84f, 0.12f);

        static GameObject BuildUI(out HudController hud, out RectTransform popupLayer, out Text popupTemplate)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            var module = esGo.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();

            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            hud = canvasGo.AddComponent<HudController>();

            // Popups live on the full canvas (they track world points).
            popupLayer = Rect("Popups", canvasGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            popupTemplate = Label("PopupText", popupLayer, "+1", 76, Color.white, TextAnchor.MiddleCenter);
            Center(popupTemplate.rectTransform, Vector2.zero, new Vector2(500, 120));
            var outline = popupTemplate.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.05f, 0.08f, 0.18f, 0.85f);
            outline.effectDistance = new Vector2(3f, -3f);
            popupTemplate.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -6f);

            var safe = Rect("SafeArea", canvasGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            // Everything shown only while a level runs lives under InLevel (hidden on the level select).
            var inLevel = Rect("InLevel", safe, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // ---- score capsule (top-left) ----
            var scorePill = Pill("ScorePill", inLevel, Navy);
            Anchor(scorePill, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -40), new Vector2(425, 128));
            var scoreLbl = Label("Label", scorePill, "SCORE", 50, Color.white, TextAnchor.MiddleCenter);
            Anchor(scoreLbl.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(34, 0), new Vector2(190, 100), new Vector2(0, 0.5f));
            var div = Img("Divider", scorePill, null, new Color(1, 1, 1, 0.35f));
            Anchor(div, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(228, 0), new Vector2(4, 66), new Vector2(0.5f, 0.5f));
            var scoreValue = Label("Value", scorePill, "00", 96, ScoreYellow, TextAnchor.MiddleCenter);
            Anchor(scoreValue.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(240, 2), new Vector2(170, 120), new Vector2(0, 0.5f));
            var scoreRoot = scorePill;

            var combo = Pill("ComboBadge", scorePill, new Color(1f, 0.55f, 0.1f, 0.97f));
            Anchor(combo, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, -50), new Vector2(124, 72), new Vector2(1, 0));
            var comboText = Label("Text", combo, "x2", 50, Color.white, TextAnchor.MiddleCenter);
            Stretch(comboText.rectTransform);

            // ---- timer capsule (top-right) ----
            var timerPill = Pill("TimerPill", inLevel, Navy);
            Anchor(timerPill, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-32, -40), new Vector2(400, 146), new Vector2(1, 1));
            var icon = Img("Stopwatch", timerPill, stopwatchSprite, Color.white);
            Anchor(icon, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -14), new Vector2(80, 80), new Vector2(0, 1));
            var timeText = Label("Time", timerPill, "0:50", 84, Color.white, TextAnchor.MiddleLeft);
            Anchor(timeText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(134, -8), new Vector2(250, 96), new Vector2(0, 1));
            var track = Pill("BarTrack", timerPill, new Color(0.36f, 0.43f, 0.56f, 1f));
            Anchor(track, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(330, 26), new Vector2(0.5f, 0));
            var fillImg = Img("BarFill", track, roundSprite, new Color(0.30f, 0.89f, 0.25f));
            fillImg.type = Image.Type.Sliced;
            fillImg.pixelsPerUnitMultiplier = 2.4f;
            var fill = fillImg.rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
            track.GetComponent<Image>().pixelsPerUnitMultiplier = 2.4f;

            // ---- SWIPE TO SHOOT pill (bottom) ----
            var hintGo = Rect("SwipeHint", inLevel, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 56), new Vector2(980, 110));
            hintGo.pivot = new Vector2(0.5f, 0);
            var hintCg = hintGo.gameObject.AddComponent<CanvasGroup>();
            hintCg.blocksRaycasts = false; hintCg.interactable = false;
            var hintPill = Pill("Pill", hintGo, new Color(0.02f, 0.06f, 0.03f, 0.38f));
            Anchor(hintPill, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 104), new Vector2(0.5f, 0.5f));
            var hintText = Label("Text", hintPill, "SWIPE TO SHOOT", 54, Color.white, TextAnchor.MiddleCenter);
            Stretch(hintText.rectTransform);
            var lineL = Img("LineL", hintGo, null, new Color(1, 1, 1, 0.6f));
            Anchor(lineL, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380, 0), new Vector2(100, 5), new Vector2(0.5f, 0.5f));
            var lineR = Img("LineR", hintGo, null, new Color(1, 1, 1, 0.6f));
            Anchor(lineR, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(380, 0), new Vector2(100, 5), new Vector2(0.5f, 0.5f));

            // ---- goal counter (under the score capsule) ----
            var goalPill = Pill("GoalPill", inLevel, Navy);
            Anchor(goalPill, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -186), new Vector2(330, 100));
            var goalText = Label("Text", goalPill, "MOLES 0/10", 44, Color.white, TextAnchor.MiddleLeft);
            Anchor(goalText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -6), new Vector2(290, 60), new Vector2(0, 1));
            var goalTrack = Pill("Track", goalPill, new Color(0.36f, 0.43f, 0.56f, 1f));
            Anchor(goalTrack, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(270, 16), new Vector2(0.5f, 0));
            var goalFillImg = Img("Fill", goalTrack, roundSprite, ScoreYellow);
            goalFillImg.type = Image.Type.Sliced;
            var goalFill = goalFillImg.rectTransform;
            goalFill.anchorMin = Vector2.zero; goalFill.anchorMax = new Vector2(0f, 1f); goalFill.offsetMin = goalFill.offsetMax = Vector2.zero;

            // ---- LEVEL n intro banner ----
            var introRt = Rect("IntroBanner", inLevel, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(780, 270));
            var introCg = introRt.gameObject.AddComponent<CanvasGroup>();
            introCg.blocksRaycasts = false; introCg.interactable = false; introCg.alpha = 0f;
            var introPill = Pill("Pill", introRt, Navy);
            Stretch(introPill);
            var introTitle = Label("Title", introRt, "LEVEL 1", 120, ScoreYellow, TextAnchor.MiddleCenter);
            Anchor(introTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(740, 150), new Vector2(0.5f, 1));
            var introSub = Label("Subtitle", introRt, "HIT 10 MOLES  ·  1:00", 46, Color.white, TextAnchor.MiddleCenter);
            Anchor(introSub.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(740, 70), new Vector2(0.5f, 0));

            // ---- results (LEVEL COMPLETE / TIME'S UP) ----
            var results = Rect("Results", canvasGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var dim = results.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0.02f, 0.08f, 0.6f);
            dim.raycastTarget = true;
            var resultsGroup = results.gameObject.AddComponent<CanvasGroup>();
            var resSafe = Rect("SafeArea", results, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            resSafe.gameObject.AddComponent<SafeAreaFitter>();
            var card = Pill("Card", resSafe, new Color(0.06f, 0.12f, 0.29f, 0.97f));
            Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(860, 1340), new Vector2(0.5f, 0.5f));
            card.GetComponent<Image>().pixelsPerUnitMultiplier = 0.5f;
            var title = Label("Title", card, "TIME'S UP", 84, ScoreYellow, TextAnchor.MiddleCenter);
            title.lineSpacing = 0.9f;
            Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(800, 220), new Vector2(0.5f, 1));
            title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -6f);
            // Star rating row (centre star larger and higher, like most mobile puzzle/arcade games).
            var resStars = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                float size = i == 1 ? 170f : 135f;
                var st = Img("Star" + i, card, starSprite, new Color(0.16f, 0.22f, 0.38f, 1f));
                Anchor(st, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2((i - 1) * 185f, i == 1 ? -245f : -275f), new Vector2(size, size), new Vector2(0.5f, 1));
                st.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                st.rectTransform.anchoredPosition += new Vector2(0f, -size * 0.5f);
                resStars[i] = st;
            }
            var resLine = Label("Line", card, "7/10 MOLES", 44, new Color(1, 1, 1, 0.92f), TextAnchor.MiddleCenter);
            Anchor(resLine.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -440), new Vector2(820, 70), new Vector2(0.5f, 1));
            var scoreCap = Label("ScoreCaption", card, "SCORE", 44, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter);
            Anchor(scoreCap.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -515), new Vector2(600, 60), new Vector2(0.5f, 1));
            var resScore = Label("Score", card, "0", 160, Color.white, TextAnchor.MiddleCenter);
            Anchor(resScore.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -560), new Vector2(700, 180), new Vector2(0.5f, 1));
            var resBest = Label("Best", card, "BEST  0", 48, new Color(1, 1, 1, 0.9f), TextAnchor.MiddleCenter);
            Anchor(resBest.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -740), new Vector2(700, 64), new Vector2(0.5f, 1));
            var newBest = Label("NewBest", card, "NEW BEST!", 44, new Color(0.45f, 1f, 0.4f), TextAnchor.MiddleCenter);
            Anchor(newBest.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -800), new Vector2(700, 56), new Vector2(0.5f, 1));
            var btnRoot = Rect("Buttons", card, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(860, 460));
            btnRoot.pivot = new Vector2(0.5f, 0);
            var btnGroup = btnRoot.gameObject.AddComponent<CanvasGroup>();
            var btnColors = new[] { new Color(0.27f, 0.78f, 0.25f, 1f), new Color(0.2f, 0.45f, 0.86f, 1f), new Color(0.32f, 0.38f, 0.52f, 1f) };
            var resButtons = new Button[3];
            var resLabels = new Text[3];
            var btnRts = new RectTransform[3];
            for (int i = 0; i < 3; i++)
            {
                var bRt = Pill("Button" + i, btnRoot, btnColors[i]);
                Vector2 bSize = i == 0 ? new Vector2(600, 125) : new Vector2(540, 108);
                Anchor(bRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, i == 0 ? 300 : 300 - 140 * i + 8), bSize, new Vector2(0.5f, 0));
                bRt.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -8f);
                var bImg = bRt.GetComponent<Image>();
                bImg.raycastTarget = true;
                var b = bRt.gameObject.AddComponent<Button>();
                b.targetGraphic = bImg;
                resLabels[i] = Label("Text", bRt, "BUTTON", i == 0 ? 60 : 52, Color.white, TextAnchor.MiddleCenter);
                Stretch(resLabels[i].rectTransform);
                resButtons[i] = b; btnRts[i] = bRt;
            }
            // Celebration: UI confetti burst above the card (pooled pieces, animated by ConfettiUI).
            var confettiRt = Rect("Confetti", results, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var confetti = confettiRt.gameObject.AddComponent<ConfettiUI>();
            Wire(confetti, "pieceSprite", roundSprite);
            Wire(hud, "confetti", confetti);
            Wire(hud, "resultsCard", card);
            Wire(hud, "resultsGroup", resultsGroup);
            Wire(hud, "resultButtonsGroup", btnGroup);
            Wire(hud, "resultButtonsRoot", btnRoot);
            WireArray(hud, "resultStars", resStars);
            results.gameObject.SetActive(false);

            // ---- level select ----
            var levelSelect = BuildLevelSelect(canvasGo.transform);

            Wire(hud, "levelSelect", levelSelect);
            Wire(hud, "hudRoot", inLevel.gameObject);
            Wire(hud, "scoreText", scoreValue);
            Wire(hud, "scoreRoot", scoreRoot);
            Wire(hud, "timerText", timeText);
            Wire(hud, "timerFill", fill);
            Wire(hud, "timerFillImage", fillImg);
            Wire(hud, "comboBadge", combo.gameObject);
            Wire(hud, "comboText", comboText);
            Wire(hud, "goalText", goalText);
            Wire(hud, "goalFill", goalFill);
            Wire(hud, "swipeHint", hintCg);
            Wire(hud, "introBanner", introCg);
            Wire(hud, "introTitle", introTitle);
            Wire(hud, "introSubtitle", introSub);
            Wire(hud, "resultsPanel", results.gameObject);
            Wire(hud, "resultsTitle", title);
            Wire(hud, "resultsLine", resLine);
            Wire(hud, "resultsScoreText", resScore);
            Wire(hud, "resultsBestText", resBest);
            Wire(hud, "newBestLabel", newBest.gameObject);
            WireArray(hud, "resultButtons", resButtons);
            WireArray(hud, "resultButtonLabels", resLabels);
            foreach (var rt in new[] { scorePill, combo, timerPill, track, hintPill, goalPill, goalTrack, introPill }) Round(rt);
            foreach (var rt in btnRts) Round(rt);
            fillImg.pixelsPerUnitMultiplier = track.GetComponent<Image>().pixelsPerUnitMultiplier;
            goalFillImg.pixelsPerUnitMultiplier = goalTrack.GetComponent<Image>().pixelsPerUnitMultiplier;
            combo.gameObject.SetActive(false);
            levelSelectUi = levelSelect;
            return canvasGo;
        }

        static LevelSelectUI levelSelectUi;

        static LevelSelectUI BuildLevelSelect(Transform canvas)
        {
            var root = Rect("LevelSelect", canvas, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0.01f, 0.05f, 0.16f, 0.62f);
            dim.raycastTarget = true;
            var safe = Rect("SafeArea", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var title = Label("Title", safe, "MOLE SHOT", 150, ScoreYellow, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(1000, 190), new Vector2(0.5f, 1));
            title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -8f);
            var sub = Label("Subtitle", safe, "SELECT LEVEL", 56, Color.white, TextAnchor.MiddleCenter);
            Anchor(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -370), new Vector2(900, 80), new Vector2(0.5f, 1));

            const int n = 3;
            var cards = new Button[n]; var images = new Image[n]; var titles = new Text[n]; var infos = new Text[n]; var statuses = new Text[n];
            var locks = new GameObject[n]; var ticks = new GameObject[n];
            var cardStars = new System.Collections.Generic.List<Image>();
            for (int i = 0; i < n; i++)
            {
                var c = Pill("Card" + (i + 1), safe, new Color(0.08f, 0.2f, 0.45f, 0.97f));
                Anchor(c, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -500 - i * 300), new Vector2(900, 260), new Vector2(0.5f, 1));
                var img = c.GetComponent<Image>();
                img.raycastTarget = true;
                img.pixelsPerUnitMultiplier = 0.5f;
                var b = c.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                titles[i] = Label("Title", c, "LEVEL " + (i + 1), 76, Color.white, TextAnchor.MiddleLeft);
                Anchor(titles[i].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -26), new Vector2(600, 100), new Vector2(0, 1));
                infos[i] = Label("Info", c, "Hit 10 moles", 46, new Color(1, 1, 1, 0.92f), TextAnchor.MiddleLeft);
                Anchor(infos[i].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -128), new Vector2(640, 64), new Vector2(0, 1));
                statuses[i] = Label("Status", c, "PLAY", 38, ScoreYellow, TextAnchor.MiddleLeft);
                Anchor(statuses[i].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -192), new Vector2(640, 52), new Vector2(0, 1));
                var lockImg = Img("Lock", c, lockSprite, new Color(1, 1, 1, 0.9f));
                Anchor(lockImg, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-60, 0), new Vector2(120, 120), new Vector2(1, 0.5f));
                var tickImg = Img("Tick", c, tickSprite, new Color(0.45f, 1f, 0.4f));
                Anchor(tickImg, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -30), new Vector2(84, 84), new Vector2(1, 1));
                for (int k = 0; k < 3; k++)
                {
                    var st = Img("Star" + k, c, starSprite, new Color(1f, 1f, 1f, 0.18f));
                    Anchor(st, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40 - (2 - k) * 78, 34), new Vector2(70, 70), new Vector2(1, 0));
                    cardStars.Add(st);
                }
                cards[i] = b; images[i] = img; locks[i] = lockImg.gameObject; ticks[i] = tickImg.gameObject;
            }
            var resetRt = Pill("ResetProgress", safe, new Color(1, 1, 1, 0.14f));
            Anchor(resetRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(420, 86), new Vector2(0.5f, 0));
            Round(resetRt);
            var resetImg = resetRt.GetComponent<Image>();
            resetImg.raycastTarget = true;
            var resetBtn = resetRt.gameObject.AddComponent<Button>();
            resetBtn.targetGraphic = resetImg;
            var resetText = Label("Text", resetRt, "RESET PROGRESS", 36, new Color(1, 1, 1, 0.85f), TextAnchor.MiddleCenter);
            Stretch(resetText.rectTransform);

            var ui = root.gameObject.AddComponent<LevelSelectUI>();
            Wire(ui, "root", root.gameObject);
            WireArray(ui, "cards", cards);
            WireArray(ui, "cardImages", images);
            WireArray(ui, "titles", titles);
            WireArray(ui, "infos", infos);
            WireArray(ui, "statuses", statuses);
            WireArray(ui, "locks", locks);
            WireArray(ui, "ticks", ticks);
            Wire(ui, "resetButton", resetBtn);
            WireArray(ui, "stars", cardStars.ToArray());
            return ui;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static void Anchor(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = pivot ?? aMin;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Center(RectTransform rt, Vector2 pos, Vector2 size) => Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size, new Vector2(0.5f, 0.5f));

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static Image Img(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static void Anchor(Image img, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size, Vector2 pivot) => Anchor(img.rectTransform, aMin, aMax, pos, size, pivot);

        static RectTransform Pill(string name, Transform parent, Color color)
        {
            var img = Img(name, parent, roundSprite, color);
            img.type = Image.Type.Sliced;
            return img.rectTransform;
        }

        /// <summary>Makes a sliced rounded sprite a full capsule for the given height.</summary>
        static void Round(RectTransform rt, float height = -1f)
        {
            var img = rt.GetComponent<Image>();
            float h = height > 0f ? height : rt.sizeDelta.y;
            if (img != null && h > 1f) img.pixelsPerUnitMultiplier = 31f / (h * 0.5f);
        }

        static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // =====================================================================================================
        static void Wire(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) throw new System.Exception($"{target.GetType().Name}.{field} not found");
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) throw new System.Exception($"{target.GetType().Name}.{field} not found");
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // =====================================================================================================
        static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.productName = "Mole Shot";
            PlayerSettings.companyName = "ImmersiWorks Prototype";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.immersiworks.moleshot");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if (System.Enum.TryParse("AndroidApiLevel26", out AndroidSdkVersions min) && PlayerSettings.Android.minSdkVersion < min)
                PlayerSettings.Android.minSdkVersion = min;
            PlayerSettings.runInBackground = false;
        }
    }
}
