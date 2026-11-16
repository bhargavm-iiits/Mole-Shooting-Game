using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoleShot.EditorTools
{
    /// <summary>
    /// Builds the stadium stand behind the far fence: stepped concrete tiers, the blue ad boards, and a crowd of
    /// 3D people (Microsoft Rocketbox avatars, MIT licence - see Assets/MoleShot/Art/People/CREDITS.txt).
    /// The people were posed (idle / cheer), atlased to one 1K texture and decimated to ~2.5k triangles each in Blender.
    /// Placement is seeded, so re-running the builder gives the same crowd.
    /// </summary>
    public static class CrowdBuilder
    {
        const string PeopleDir = MoleShotSceneBuilder.Root + "/Art/People";
        const string MatDir = MoleShotSceneBuilder.Root + "/Materials";
        static readonly string[] People = { "Female_Adult_01", "Female_Adult_03", "Male_Adult_01", "Male_Adult_08", "Male_Adult_12" };
        static readonly string[] Poses = { "idle", "cheer" };
        static readonly Color[] Tints = { Color.white, new Color(0.86f, 0.9f, 1f) };

        // Stand layout (metres). The far fence is at z ~ 19.7.
        const int Rows = 4;
        const float FirstRowZ = 21.6f, RowDepth = 1.3f, FirstRowTop = 0.9f, RowRise = 0.9f;
        const float HalfWidth = 12.5f;
        const float CheerChance = 0.3f;
        const float PersonHeight = 1.75f;
        /// <summary>Yaw that turns the imported people to face the camera (-Z).</summary>
        const float FacingYaw = 180f;

        public static int LastPeopleCount { get; private set; }
        public static long LastTriangleCount { get; private set; }

        public static CrowdController Build(Transform parent)
        {
            var root = new GameObject("CrowdStand").transform;
            root.SetParent(parent, false);

            // --- tiers (simple stepped geometry) + back wall ---
            var concrete = Lit("StandConcrete", new Color(0.44f, 0.48f, 0.56f));
            var wall = Lit("StandWall", new Color(0.18f, 0.24f, 0.38f));
            for (int r = 0; r < Rows; r++)
            {
                float top = FirstRowTop + r * RowRise;
                float zc = FirstRowZ + r * RowDepth;
                Box($"Tier_{r}", root, new Vector3(0f, top * 0.5f, zc), new Vector3(70f, top, RowDepth), concrete);
            }
            float backZ = FirstRowZ + Rows * RowDepth - RowDepth * 0.5f + 0.3f;
            Box("BackWall", root, new Vector3(0f, 3.6f, backZ), new Vector3(70f, 7.2f, 0.6f), wall);

            // --- people ---
            var meshes = new Mesh[People.Length, Poses.Length];
            var mats = new Material[People.Length, Tints.Length];
            for (int p = 0; p < People.Length; p++)
            {
                for (int q = 0; q < Poses.Length; q++) meshes[p, q] = LoadPersonMesh($"{PeopleDir}/{People[p]}_{Poses[q]}.fbx");
                var tex = LoadAtlas($"{PeopleDir}/{People[p]}_atlas.png");
                for (int t = 0; t < Tints.Length; t++) mats[p, t] = PersonMaterial($"Crowd_{People[p]}_{t}", tex, Tints[t]);
            }

            var rnd = new System.Random(20261121);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            int count = 0; long tris = 0;
            var filters = new List<MeshFilter>();
            var rest = new List<Mesh>();
            var cheer = new List<Mesh>();
            var peopleRoot = new GameObject("People").transform;
            peopleRoot.SetParent(root, false);
            for (int r = 0; r < Rows; r++)
            {
                float top = FirstRowTop + r * RowRise;
                float zc = FirstRowZ + r * RowDepth;
                for (float x = -HalfWidth + R(0f, 0.5f); x < HalfWidth; x += R(0.72f, 1.02f))
                {
                    if (rnd.NextDouble() < 0.05) continue; // empty seat
                    int p = rnd.Next(People.Length);
                    int q = rnd.NextDouble() < CheerChance ? 1 : 0;
                    int t = rnd.Next(Tints.Length);
                    var mesh = meshes[p, q];
                    var go = new GameObject($"{People[p]}_{Poses[q]}_{count}");
                    go.transform.SetParent(peopleRoot, false);
                    go.transform.localPosition = new Vector3(x, top, zc + R(-0.15f, 0.15f));
                    go.transform.localRotation = Quaternion.Euler(0f, FacingYaw + R(-18f, 18f), 0f);
                    float s = PersonHeight / Mathf.Max(0.01f, mesh.bounds.size.y) * R(0.93f, 1.07f);
                    go.transform.localScale = Vector3.one * s;
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    filters.Add(mf); rest.Add(mesh); cheer.Add(meshes[p, 1]);
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mats[p, t];
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    // Not static: CrowdController moves people (jump / sway). GPU instancing on the materials batches them.
                    count++; tris += mesh.triangles.Length / 3;
                }
            }
            LastPeopleCount = count;
            LastTriangleCount = tris;
            Debug.Log($"[MoleShot] Crowd: {count} people, {tris} triangles");

            var ctrl = root.gameObject.AddComponent<CrowdController>();
            var so = new SerializedObject(ctrl);
            void Arr<T>(string field, List<T> list) where T : Object
            {
                var prop = so.FindProperty(field);
                prop.arraySize = list.Count;
                for (int i = 0; i < list.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            }
            Arr("people", filters); Arr("restMeshes", rest); Arr("cheerMeshes", cheer);
            so.ApplyModifiedPropertiesWithoutUndo();
            return ctrl;
        }

        static void Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        static Mesh LoadPersonMesh(string path)
        {
            if (AssetImporter.GetAtPath(path) is ModelImporter mi)
            {
                bool dirty = mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.animationType != ModelImporterAnimationType.None ||
                             mi.importAnimation || mi.importCameras || mi.importLights || mi.meshCompression != ModelImporterMeshCompression.Medium;
                if (dirty)
                {
                    mi.materialImportMode = ModelImporterMaterialImportMode.None;
                    mi.animationType = ModelImporterAnimationType.None;
                    mi.importAnimation = false;
                    mi.importCameras = false;
                    mi.importLights = false;
                    mi.isReadable = false;
                    mi.meshCompression = ModelImporterMeshCompression.Medium;
                    mi.SaveAndReimport();
                }
            }
            var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();
            if (mesh == null) throw new System.Exception("Crowd mesh missing: " + path);
            return mesh;
        }

        static Texture2D LoadAtlas(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && (ti.maxTextureSize != 1024 || !ti.alphaIsTransparency))
            {
                ti.maxTextureSize = 1024;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material PersonMaterial(string name, Texture2D tex, Color tint)
        {
            var m = Lit(name, tint);
            m.SetTexture("_BaseMap", tex);
            // Hair and eyelashes come from the opacity cell of the atlas: alpha-tested, not blended.
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_Smoothness", 0.12f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Lit(string name, Color c)
        {
            string path = MatDir + "/" + name + ".mat";
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.1f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
