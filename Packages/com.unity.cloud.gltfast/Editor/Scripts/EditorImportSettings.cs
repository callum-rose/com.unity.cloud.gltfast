// SPDX-FileCopyrightText: 2023 Unity Technologies and the glTFast authors
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace GLTFast.Editor
{

    /// <summary>
    /// Editor Import specific settings (not relevant at runtime)
    /// </summary>
    [Serializable]
    class EditorImportSettings
    {

        /// <summary>
        /// Creates a secondary UV set on all meshes, if there is none present already.
        /// Often used for lightmaps.
        /// </summary>
        [Tooltip("Generate Lightmap UVs")]
        public bool generateSecondaryUVSet;

        /// <summary>
        /// Keeps a CPU copy of mesh data in players, so scripts can read it.
        /// When off, meshes are saved like a ModelImporter mesh with Read/Write off.
        /// </summary>
        [Tooltip("Keep mesh data readable from scripts in players")]
        public bool meshesReadable = true;
    }
}
