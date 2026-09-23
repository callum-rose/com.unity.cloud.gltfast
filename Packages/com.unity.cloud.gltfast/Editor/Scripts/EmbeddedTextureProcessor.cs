// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GLTFast.Editor
{
    /// <summary>
    /// Mutates the decoded object in place so materials and glTFast's subasset identifiers stay intact.
    /// Uses CPU pixels only, including in batch mode and asset import workers.
    /// </summary>
    internal static class EmbeddedTextureProcessor
    {
        public static void Process(Texture2D texture, EmbeddedTextureSettings settings,
            BuildTarget target, bool keepReadable, AssetImportContext context)
        {
            // Already-compressed KTX/Basis data must not be decoded or recompressed here.
            if (GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat))
            {
                context.LogImportWarning($"Embedded texture '{texture.name}' is already compressed; size and compression overrides were skipped.");
                ReleasePixels(texture, keepReadable);
                return;
            }

            if (texture.format != TextureFormat.RGB24 && texture.format != TextureFormat.RGBA32
                && texture.format != TextureFormat.ARGB32)
            {
                context.LogImportWarning($"Embedded texture '{texture.name}' uses unsupported format {texture.format}; optimization was skipped.");
                ReleasePixels(texture, keepReadable);
                return;
            }

            if (!texture.isReadable)
            {
                throw new InvalidOperationException($"Embedded texture '{texture.name}' was not decoded with readable pixels.");
            }

            if (settings.MaxSize < 32 || settings.MaxSize > 8192 || !Mathf.IsPowerOfTwo(settings.MaxSize))
            {
                throw new InvalidOperationException("Embedded texture maximum size must be a power of two between 32 and 8192.");
            }

            Resize(texture, settings.MaxSize);

            if (target == BuildTarget.iOS && settings.IosCompression != EmbeddedTextureSettings.IosTextureCompression.Uncompressed)
            {
                var format = GetCompressionFormat(settings.IosCompression);
                EditorUtility.CompressTexture(texture, format, settings.CompressionQuality);
                // Unity may leave the source unchanged if compression is unsupported.
                if (texture.format != format)
                {
                    throw new InvalidOperationException($"Failed to compress embedded texture '{texture.name}' to {format}; got {texture.format}.");
                }
            }

            // Never regenerate mipmaps after compression; upload the complete encoded chain.
            texture.Apply(false, !keepReadable);
        }

        private static TextureFormat GetCompressionFormat(EmbeddedTextureSettings.IosTextureCompression compression)
        {
            switch (compression)
            {
                case EmbeddedTextureSettings.IosTextureCompression.Astc4x4:
                    return TextureFormat.ASTC_4x4;
                case EmbeddedTextureSettings.IosTextureCompression.Astc6x6:
                    return TextureFormat.ASTC_6x6;
                case EmbeddedTextureSettings.IosTextureCompression.Astc8x8:
                    return TextureFormat.ASTC_8x8;
                default:
                    throw new ArgumentOutOfRangeException(nameof(compression));
            }
        }

        private static void ReleasePixels(Texture2D texture, bool keepReadable)
        {
            if (texture.isReadable && !keepReadable)
            {
                texture.Apply(false, true);
            }
        }

        private static void Resize(Texture2D texture, int maxSize)
        {
            if (texture.width <= maxSize && texture.height <= maxSize)
            {
                return;
            }

            var width = texture.width;
            var height = texture.height;
            while (width > maxSize || height > maxSize)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }

            var hasMips = texture.mipmapCount > 1;
            // glTFast's generated mip levels can be averaged in encoded sRGB space.
            // Always filter the original level so resizing cannot inherit that darkening.
            var pixels = Downsample(texture, width, height);
            // Retain the original graphics format, including sRGB versus linear data encoding.
            if (!texture.Reinitialize(width, height, texture.graphicsFormat, hasMips))
            {
                throw new InvalidOperationException($"Could not resize embedded texture '{texture.name}'.");
            }

            texture.SetPixels(pixels);
            texture.Apply(hasMips, false);
        }

        private static Color[] Downsample(Texture2D texture, int width, int height)
        {
            var source = texture.GetPixels();
            var result = new Color[width * height];
            var sourceWidth = texture.width;
            var sourceHeight = texture.height;
            var scaleX = (float)sourceWidth / width;
            var scaleY = (float)sourceHeight / height;
            var srgb = texture.isDataSRGB;

            // Area filtering includes fractional edge coverage for non-power-of-two sources.
            // Average color in linear light, but preserve alpha and packed data as stored.
            for (var y = 0; y < height; y++)
            {
                var top = y * scaleY;
                var bottom = (y + 1) * scaleY;
                for (var x = 0; x < width; x++)
                {
                    var left = x * scaleX;
                    var right = (x + 1) * scaleX;
                    var sum = Color.clear;
                    var totalWeight = 0f;
                    for (var sy = (int)top; sy < Math.Min(sourceHeight, Mathf.CeilToInt(bottom)); sy++)
                    {
                        var weightY = Math.Min(sy + 1, bottom) - Math.Max(sy, top);
                        for (var sx = (int)left; sx < Math.Min(sourceWidth, Mathf.CeilToInt(right)); sx++)
                        {
                            var weight = weightY * (Math.Min(sx + 1, right) - Math.Max(sx, left));
                            var pixel = source[sy * sourceWidth + sx];
                            sum += (srgb ? pixel.linear : pixel) * weight;
                            totalWeight += weight;
                        }
                    }

                    var average = sum / totalWeight;
                    result[y * width + x] = srgb ? average.gamma : average;
                }
            }

            return result;
        }
    }
}
