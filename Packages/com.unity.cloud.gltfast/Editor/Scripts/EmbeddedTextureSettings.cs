// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace GLTFast.Editor
{
    /// <summary>Opt-in editor-only controls for the embedded texture import spike.</summary>
    [Serializable]
    internal sealed class EmbeddedTextureSettings
    {
        [SerializeField, Tooltip("Optimize embedded textures without changing source files. External textures keep their own importer settings.")]
        private bool enabled;

        [SerializeField, Tooltip("Largest dimension, reduced in half-size steps. Applies to every build target.")]
        private TextureSize maxSize = TextureSize.Size2048;

        [SerializeField, Tooltip("Compression for iOS import artifacts. Other targets retain uncompressed pixels.")]
        private IosTextureCompression iosCompression = IosTextureCompression.Astc6x6;

        [SerializeField, Range(0, 100), Tooltip("ASTC encoder effort. Higher values take longer to import.")]
        private int compressionQuality = 50;

        public bool Enabled => enabled;
        public int MaxSize => (int)maxSize;
        public IosTextureCompression IosCompression => iosCompression;
        public int CompressionQuality => Mathf.Clamp(compressionQuality, 0, 100);

        internal enum TextureSize
        {
            Size32 = 32,
            Size64 = 64,
            Size128 = 128,
            Size256 = 256,
            Size512 = 512,
            Size1024 = 1024,
            Size2048 = 2048,
            Size4096 = 4096,
            Size8192 = 8192
        }

        internal enum IosTextureCompression
        {
            Uncompressed,
            Astc4x4,
            Astc6x6,
            Astc8x8
        }
    }
}
