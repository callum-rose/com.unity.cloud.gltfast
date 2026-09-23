using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Marble.GlbImporterSpike
{
    /// <summary>Generated fixtures supplement the real shack without changing any source asset.</summary>
    public static class EdgeCases
    {
        public static void Run()
        {
            var output = Environment.GetEnvironmentVariable("MARBLE_GLB_SPIKE_RESULTS");
            try
            {
                Require(QualitySettings.activeColorSpace == ColorSpace.Linear, "Harness must use Linear color space.");
                var pixels = new Color32[64 * 64];
                for (var y = 0; y < 64; y++)
                {
                    for (var x = 0; x < 64; x++)
                    {
                        var value = (byte)((x + y) % 2 == 0 ? 0 : 255);
                        pixels[y * 64 + x] = new Color32(value, value, value, 64);
                    }
                }
                var image = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                byte[] png;
                try
                {
                    image.SetPixels32(pixels);
                    image.Apply();
                    png = image.EncodeToPNG();
                }
                finally
                {
                    Object.DestroyImmediate(image);
                }
                var uri = "data:image/png;base64," + Convert.ToBase64String(png);
                const string Embedded = "Assets/SharedEmbedded.gltf";
                File.WriteAllText(Embedded, Document(uri, uri));
                AssetDatabase.ImportAsset(Embedded, ImportAssetOptions.ForceSynchronousImport);
                Configure(Embedded, false);
                CheckPixels(Embedded);
                Configure(Embedded, true);
                CheckPixels(Embedded);

                // An external PNG must never be mutated by this processing path.
                const string External = "Assets/External.gltf";
                const string PngPath = "Assets/External.png";
                File.WriteAllBytes(PngPath, png);
                AssetDatabase.ImportAsset(PngPath, ImportAssetOptions.ForceSynchronousImport);
                var externalTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(PngPath);
                var format = externalTexture.format;
                var metadata = File.ReadAllText(PngPath + ".meta");
                File.WriteAllText(External, Document("External.png", "External.png"));
                AssetDatabase.ImportAsset(External, ImportAssetOptions.ForceSynchronousImport);
                Configure(External, true);
                Require(!AssetDatabase.LoadAllAssetsAtPath(External).OfType<Texture2D>().Any(), "External textures must not become subassets.");
                Require(externalTexture.width == 64 && externalTexture.format == format, "External texture must keep its own dimensions and format.");
                Require(File.ReadAllText(PngPath + ".meta") == metadata, "External TextureImporter settings must not change.");
                Require(File.ReadAllBytes(PngPath).SequenceEqual(png), "External source bytes must not change.");
                File.WriteAllText(Path.Combine(output, "edge-checks.txt"),
                    "PASS: shared texture deduplication; two materials; color/data encoding; alpha; sampler preservation; 32px resizing with and without mips; external PNG source/settings unchanged.\n");
                Debug.Log("GLB importer edge fixtures PASS");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                File.WriteAllText(Path.Combine(output, "edge-checks.txt"), exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string Document(string colorUri, string dataUri)
        {
            return "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"name\":\"Root\"}],"
                + "\"images\":[{\"name\":\"Color\",\"uri\":\"" + colorUri + "\"},{\"name\":\"Data\",\"uri\":\"" + dataUri + "\"}],"
                + "\"samplers\":[{\"magFilter\":9728,\"minFilter\":9728,\"wrapS\":33071,\"wrapT\":33648}],"
                + "\"textures\":[{\"source\":0,\"sampler\":0},{\"source\":0,\"sampler\":0},{\"source\":1,\"sampler\":0}],"
                + "\"materials\":[{\"name\":\"A\",\"alphaMode\":\"BLEND\",\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0},\"metallicRoughnessTexture\":{\"index\":2}}},"
                + "{\"name\":\"B\",\"alphaMode\":\"BLEND\",\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":1}}}]}";
        }

        private static void Configure(string path, bool mips)
        {
            var importer = AssetImporter.GetAtPath(path);
            using var serialized = new SerializedObject(importer);
            serialized.FindProperty("embeddedTextureSettings.enabled").boolValue = true;
            serialized.FindProperty("embeddedTextureSettings.maxSize").intValue = 32;
            serialized.FindProperty("embeddedTextureSettings.iosCompression").intValue = 0;
            serialized.FindProperty("importSettings.texturesReadable").boolValue = true;
            serialized.FindProperty("importSettings.generateMipMaps").boolValue = mips;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
        }

        private static void CheckPixels(string path)
        {
            var objects = AssetDatabase.LoadAllAssetsAtPath(path);
            var textures = objects.OfType<Texture2D>().ToArray();
            Require(textures.Length == 2, "Shared color texture must be processed once alongside one linear data texture.");
            var color = textures.Single(x => x.isDataSRGB);
            var data = textures.Single(x => !x.isDataSRGB);
            Require(color.width == 32 && data.width == 32, "Both textures must resize to 32px.");
            Require(Math.Abs(color.GetPixel(8, 8).r - Mathf.LinearToGammaSpace(0.5f)) < 0.015f, "Color must downsample in linear light.");
            Require(Math.Abs(data.GetPixel(8, 8).r - 0.5f) < 0.015f, "Packed data must downsample without gamma conversion.");
            foreach (var texture in textures)
            {
                Require(Math.Abs(texture.GetPixel(8, 8).a - 64f / 255) < 0.01f, "Alpha must be retained.");
                Require(texture.filterMode == FilterMode.Point && texture.wrapModeU == TextureWrapMode.Clamp
                    && texture.wrapModeV == TextureWrapMode.Mirror, "Sampler state must be retained.");
            }
            var materials = objects.OfType<Material>().ToArray();
            Require(materials.Length == 2, "Both materials must remain.");
            foreach (var material in materials)
            {
                var refs = material.GetTexturePropertyNames().Select(material.GetTexture).Where(x => x != null).ToArray();
                Require(refs.Contains(color) && refs.All(x => textures.Contains(x)), "Every material must reference the shared imported objects.");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
