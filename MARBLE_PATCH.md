# Marble embedded texture importer patch

Maintained in callum-rose/com.unity.cloud.gltfast on `marble/6.14.1`.
Upstream: Unity-Technologies/com.unity.cloud.gltfast, release/6.14.1,
commit `20fc683570b1c3d52511255324b8c02f12e1524e`.

The editor importer optionally resizes embedded textures and compresses them to
ASTC for iOS before registering subassets. Processing is off by default.
Runtime loading, importer identity, script GUIDs and subasset naming are preserved.
The patch changes GltfImporter.cs and GltfImporterEditor.cs and adds
EmbeddedTextureSettings.cs and EmbeddedTextureProcessor.cs with their original metas.

The patch is copied byte-for-byte from Marble spike commit
`b7d5cf0a676e88a6e3f8340d4773ca0489df83ee`. Comparison of the tested registry
snapshot with this upstream release found only the six patch files and registry
packaging differences: registry-added package.json metadata, omitted Documentation~,
and registry ValidationExceptions.json files. Package name, version, dependencies,
licenses, notices and all remaining package resources are unchanged.

## Updating

Keep the branch maintained by the fork owner; do not move consumers automatically.
Compare a new upstream release, reapply these six files as a reviewed patch, preserve
GUIDs and bump the importer version if artifact semantics change. Run this fork's
`Tools/EmbeddedTextureValidation/run.py --dependency <Git-URL-with-full-SHA>` against the new published commit, including iOS →
macOS → iOS invalidation and generated edge cases. Verify a cold Git/LFS fetch.
Update Marble through Unity PackageManager Client and commit its generated manifest
and lockfile together. Preserve upstream license and notices.

The package is under `Packages/com.unity.cloud.gltfast`; use that Git URL path and
a full commit SHA in UPM. Git LFS must be installed for upstream binary resources.
Validation is editor-only; it does not establish player builds or device savings.

Reusable harness instructions and the pinned fixture live in [Tools/EmbeddedTextureValidation](Tools/EmbeddedTextureValidation/README.md). Generated spike reports and previews are kept as run artifacts.
