// Copied into the isolated validation project's Assets/Editor directory by the runbook.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace Marble.GlbImporterSpike
{
    public static class Validation
    {
        private const string Fixture = "Assets/Art/Props/VehicleShopBuilding/Shack_W2_Shingle.glb";
        private const string SourceHash = "4b85ad1698dc0bbd2f20a913f7811d9f81c1580b60dcaa2c0b014957c574e5cf";
        private static readonly List<string> s_checks = new List<string>();
        private static string Output => Environment.GetEnvironmentVariable("MARBLE_GLB_SPIKE_RESULTS");

        public static void RunIos()
        {
            Run(() =>
            {
                Require(EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS, "iOS target selected");
                Require(Hash(File.ReadAllBytes(Fixture)) == SourceHash, "original source fixture hash");
                Configure(false);
                var baseline = Capture();
                File.WriteAllText(Path.Combine(Output, "baseline.json"), JsonUtility.ToJson(baseline, true));
                Require(baseline.Format == "RGB24" || baseline.Format == "RGBA32" || baseline.Format == "ARGB32", "baseline is uncompressed");
                Require(baseline.Width == 2048 && baseline.Mips == 12 && !baseline.Readable, "baseline dimensions, mip chain and readability");
                Require(baseline.Identities.Contains("Texture2D:Baked_BaseColor:-8983268971147348089"), "original texture identity from handoff");
                Require(baseline.Identities.Contains("Mesh:Mesh1.0:-2185137405254905566"), "original mesh identity from handoff");
                Require(baseline.Identities.Contains("GameObject:Shack_W2_Shingle:1899824663561904589"), "original root identity from handoff");
                SavePreview("before.png");

                Configure(true);
                var optimized = Capture();
                CheckEquivalent(baseline, optimized);
                CheckTexture(optimized, "ASTC_6x6", 2048, 12, false);
                SavePreview("after.png");
                File.WriteAllText(Path.Combine(Output, "optimized-ios.json"), JsonUtility.ToJson(optimized, true));
                var metadata = File.ReadAllText(Fixture + ".meta");
                AssetDatabase.ImportAsset(Fixture, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                CheckEquivalent(optimized, Capture());
                Require(File.ReadAllText(Fixture + ".meta") == metadata, "settings survive forced reimport");

                Configure(true, 1024);
                var smaller = Capture();
                CheckEquivalent(baseline, smaller);
                CheckTexture(smaller, "ASTC_6x6", 1024, 11, false);
                Configure(true, 2048, true);
                var readable = Texture();
                Require(readable.isReadable, "explicit readable setting retained");
                Require(readable.GetRawTextureData().Length == AstcBytes(2048, 2048, 6), "ASTC 6x6 encoded mip-chain byte size");
                var encodedHash = Hash(readable.GetRawTextureData());
                AssetDatabase.ImportAsset(Fixture, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                Require(Hash(Texture().GetRawTextureData()) == encodedHash, "compression is deterministic across repeated imports");

                Configure(true, 2048, false, 1);
                CheckTexture(Capture(), "ASTC_4x4", 2048, 12, false);
                Configure(true, 2048, false, 3);
                CheckTexture(Capture(), "ASTC_8x8", 2048, 12, false);
                Configure(true, 2048, false, 0);
                Require(!UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(Texture().graphicsFormat), "iOS uncompressed option");
                Configure(true, 2048, false, 2, false);
                CheckTexture(Capture(), "ASTC_6x6", 2048, 1, false);
                Configure(true, 512, false, 2, false);
                CheckTexture(Capture(), "ASTC_6x6", 512, 1, false);

                Configure(true);
                CheckEquivalent(baseline, Capture());
                Require(Hash(File.ReadAllBytes(Fixture)) == SourceHash, "source bytes unchanged after every settings variation");
                File.Copy(Fixture + ".meta", Path.Combine(Output, "Shack_W2_Shingle.glb.meta"), true);
            }, "ios-checks.json");
        }

        public static void RunDesktop()
        {
            Run(() =>
            {
                Require(EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneOSX, "macOS target selected");
                // Do not explicitly reimport: target dependency must invalidate the previous iOS artifact.
                var baseline = JsonUtility.FromJson<Snapshot>(File.ReadAllText(Path.Combine(Output, "baseline.json")));
                var desktop = Capture();
                CheckEquivalent(baseline, desktop);
                Require(desktop.Format != "ASTC_6x6", "target switch automatically creates an uncompressed desktop artifact");
                Require(desktop.Width == 2048 && desktop.Mips == 12 && !desktop.Readable, "desktop texture settings");
                Require(Hash(File.ReadAllBytes(Fixture)) == SourceHash, "source bytes unchanged after target switch");
                File.WriteAllText(Path.Combine(Output, "optimized-desktop.json"), JsonUtility.ToJson(desktop, true));
            }, "desktop-checks.json");
        }

        public static void RunIosReturn()
        {
            Run(() =>
            {
                var baseline = JsonUtility.FromJson<Snapshot>(File.ReadAllText(Path.Combine(Output, "baseline.json")));
                var returned = Capture();
                CheckEquivalent(baseline, returned);
                CheckTexture(returned, "ASTC_6x6", 2048, 12, false);
                Require(Hash(File.ReadAllBytes(Fixture)) == SourceHash, "source bytes unchanged on return to iOS");
            }, "ios-return-checks.json");
        }

        private static void Configure(bool enabled, int maxSize = 2048, bool readable = false, int compression = 2, bool mips = true)
        {
            var importer = AssetImporter.GetAtPath(Fixture);
            using var serialized = new SerializedObject(importer);
            var settings = serialized.FindProperty("embeddedTextureSettings");
            Require(settings != null, "modified glTFast importer is active");
            settings.FindPropertyRelative("enabled").boolValue = enabled;
            settings.FindPropertyRelative("maxSize").intValue = maxSize;
            settings.FindPropertyRelative("iosCompression").intValue = compression;
            settings.FindPropertyRelative("compressionQuality").intValue = 50;
            serialized.FindProperty("importSettings.texturesReadable").boolValue = readable;
            serialized.FindProperty("importSettings.generateMipMaps").boolValue = mips;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
        }

        private static Snapshot Capture()
        {
            var objects = AssetDatabase.LoadAllAssetsAtPath(Fixture);
            var textures = objects.OfType<Texture2D>().ToArray();
            Require(textures.Length == 1, "exactly one texture subasset (no uncompressed duplicate)");
            var texture = textures[0];
            foreach (var material in objects.OfType<Material>())
            {
                var references = material.GetTexturePropertyNames().Select(material.GetTexture).Where(x => x != null).ToArray();
                Require(references.Length > 0 && references.All(x => x == texture), "all imported material texture references use the optimized object");
            }

            var identities = objects.Select(x =>
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(x, out string guid, out long id);
                Require(guid == "ce41aa8785ace4b78974e44e017af3e1", "source GUID retained");
                return $"{x.GetType().Name}:{x.name}:{id}";
            }).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Fixture);
            var mesh = objects.OfType<Mesh>().Single();
            var geometry = new Geometry(mesh, root);
            return new Snapshot(texture, identities, Hash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(geometry))));
        }

        private static void CheckEquivalent(Snapshot before, Snapshot after)
        {
            Require(before.Identities.SequenceEqual(after.Identities), "all subasset identities retained");
            Require(before.GeometryHash == after.GeometryHash, "geometry, UVs, normals, tangents and hierarchy retained");
            Require(before.Srgb == after.Srgb, "color-space flag retained");
            Require(before.Filter == after.Filter && before.WrapU == after.WrapU && before.WrapV == after.WrapV && before.AnisoLevel == after.AnisoLevel, "sampler state retained");
        }

        private static void CheckTexture(Snapshot snapshot, string format, int size, int mips, bool readable)
        {
            Require(snapshot.Format == format && snapshot.Width == size && snapshot.Height == size
                && snapshot.Mips == mips && snapshot.Readable == readable, $"{format} {size}px {mips} mips readable={readable}");
        }

        private static Texture2D Texture() => AssetDatabase.LoadAllAssetsAtPath(Fixture).OfType<Texture2D>().Single();

        private static void SavePreview(string name)
        {
            var texture = Texture();
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var preview = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
            var srgbWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = true;
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                preview.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                preview.Apply();
                File.WriteAllBytes(Path.Combine(Output, name), preview.EncodeToPNG());
            }
            finally
            {
                GL.sRGBWrite = srgbWrite;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(preview);
            }
        }

        private static long AstcBytes(int width, int height, int block)
        {
            long bytes = 0;
            while (true)
            {
                bytes += ((width + block - 1) / block) * ((height + block - 1) / block) * 16L;
                if (width == 1 && height == 1)
                {
                    return bytes;
                }
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
            s_checks.Add(message);
        }

        private static void Run(Action action, string resultFile)
        {
            try
            {
                Directory.CreateDirectory(Output);
                action();
                File.WriteAllText(Path.Combine(Output, resultFile), JsonUtility.ToJson(new Result(true, s_checks.ToArray()), true));
                Debug.Log("GLB importer spike PASS: " + resultFile);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                File.WriteAllText(Path.Combine(Output, resultFile), JsonUtility.ToJson(new Result(false, s_checks.ToArray(), exception.ToString()), true));
                EditorApplication.Exit(1);
            }
        }

        [Serializable]
        private sealed class Result
        {
            [SerializeField] private bool passed;
            [SerializeField] private string[] checks;
            [SerializeField] private string error;

            public Result(bool passed, string[] checks, string error = null)
            {
                this.passed = passed;
                this.checks = checks;
                this.error = error;
            }
        }

        [Serializable]
        private sealed class TransformData
        {
            [SerializeField] private Vector3 position;
            [SerializeField] private Quaternion rotation;
            [SerializeField] private Vector3 scale;

            public TransformData(Transform transform)
            {
                position = transform.localPosition;
                rotation = transform.localRotation;
                scale = transform.localScale;
            }
        }

        [Serializable]
        private sealed class Geometry
        {
            [SerializeField] private Vector3[] vertices;
            [SerializeField] private Vector3[] normals;
            [SerializeField] private Vector4[] tangents;
            [SerializeField] private Vector2[] uv;
            [SerializeField] private int[] triangles;
            [SerializeField] private string[] hierarchy;

            public Geometry(Mesh mesh, GameObject root)
            {
                vertices = mesh.vertices;
                normals = mesh.normals;
                tangents = mesh.tangents;
                uv = mesh.uv;
                triangles = mesh.triangles;
                hierarchy = root.GetComponentsInChildren<Transform>(true)
                    .Select(transform => transform.name + ":" + JsonUtility.ToJson(new TransformData(transform)))
                    .ToArray();
            }
        }

        [Serializable]
        private sealed class Snapshot
        {
            [SerializeField] private string[] identities;
            [SerializeField] private string geometryHash;
            [SerializeField] private int width;
            [SerializeField] private int height;
            [SerializeField] private int mips;
            [SerializeField] private int anisoLevel;
            [SerializeField] private bool readable;
            [SerializeField] private bool srgb;
            [SerializeField] private string format;
            [SerializeField] private string filter;
            [SerializeField] private string wrapU;
            [SerializeField] private string wrapV;
            [SerializeField] private long editorRuntimeBytes;
            [SerializeField] private long estimatedEncodedBytes;

            public IReadOnlyList<string> Identities => identities;
            public string GeometryHash => geometryHash;
            public int Width => width;
            public int Height => height;
            public int Mips => mips;
            public int AnisoLevel => anisoLevel;
            public bool Readable => readable;
            public bool Srgb => srgb;
            public string Format => format;
            public string Filter => filter;
            public string WrapU => wrapU;
            public string WrapV => wrapV;

            public Snapshot(Texture2D texture, string[] identities, string geometryHash)
            {
                this.identities = identities;
                this.geometryHash = geometryHash;
                width = texture.width;
                height = texture.height;
                mips = texture.mipmapCount;
                anisoLevel = texture.anisoLevel;
                readable = texture.isReadable;
                srgb = texture.isDataSRGB;
                format = texture.format.ToString();
                filter = texture.filterMode.ToString();
                wrapU = texture.wrapModeU.ToString();
                wrapV = texture.wrapModeV.ToString();
                editorRuntimeBytes = Profiler.GetRuntimeMemorySizeLong(texture);
                estimatedEncodedBytes = texture.format == TextureFormat.ASTC_6x6
                    ? AstcBytes(texture.width, texture.height, 6)
                    : 0;
            }
        }
    }
}
