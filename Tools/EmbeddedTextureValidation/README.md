# Embedded texture importer validation

Reusable Editor regression tests for this fork's opt-in embedded texture processing.
They run outside the package in a disposable Unity project; they do not ship to consumers.
The harness and original fixture were moved from Marble PR #2960.

## Run

Install Unity 6000.4.3f1 with iOS and macOS build support, Python 3 and Git LFS.
Hydrate the fixture with `git lfs pull --include="Tools/EmbeddedTextureValidation/fixtures/**"`.
From the fork checkout, validate a published package commit:

```sh
python3 Tools/EmbeddedTextureValidation/run.py \
  --unity /Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity \
  --dependency 'https://github.com/callum-rose/com.unity.cloud.gltfast.git?path=Packages/com.unity.cloud.gltfast#<full-commit-sha>'
```

Alternatively, replace `--dependency` with `--marble-root /path/to/marble` to read
Marble's exact manifest pin. The runner rejects embedded glTFast packages in that
checkout. No Marble checkout is required with `--dependency`.

Every run creates a fresh project and UPM cache, installs the exact Git dependency
plus URP 17.4.0 and KTX 3.7.0, and asserts resolved source, URL and commit.
It then runs iOS → macOS → iOS fixture checks and generated edge cases.
Use `--output /new/directory` to retain the project, logs, JSON reports and previews
at a chosen location. Generated results are not committed here.

The pinned original fixture SHA-256 is
`4b85ad1698dc0bbd2f20a913f7811d9f81c1580b60dcaa2c0b014957c574e5cf`.
It is independent of Marble's production extraction workaround. Keep its `.meta`
and source bytes unchanged: tests assert the original GUID and subasset IDs.

## Coverage

158 fixture assertions cover source hashes, all seven subasset IDs, geometry and
hierarchy fingerprints, material references, deduplication, resizing, mip and
readability settings, deterministic ASTC compression and automatic build-target
invalidation. Generated fixtures cover shared textures, color/data filtering,
alpha, samplers, resize filtering with and without mips, and external texture isolation.

The established iOS result is 2048² ASTC 6×6 with 12 mip levels, Read/Write off,
and 2,497,712 encoded bytes. Editor memory measurements are not device savings.
JPEG, KTX/Basis, normal-map renormalization, distinct sampler variants, NPOT mip
chains, source re-exports and concurrent import workers still need wider coverage.
This suite does not validate a full Marble player build or runtime GLB downloads.

## Update a Marble dependency through UPM

```sh
python3 Tools/EmbeddedTextureValidation/run.py \
  --unity /Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity \
  --marble-root /path/to/marble \
  --resolve-marble 'https://github.com/callum-rose/com.unity.cloud.gltfast.git?path=Packages/com.unity.cloud.gltfast#<full-commit-sha>'
```

This mode copies Marble's manifest, lock and other embedded packages into a
disposable project and uses Unity PackageManager Client to resolve the new pin.
It copies the generated manifest and lock back only if unrelated dependencies are
unchanged. Hydrate other embedded package assets first. It never opens Marble's
production project or saves its scenes. Then run the normal suite using
`--marble-root` to validate the updated pin. Do not pass `-quit` to the asynchronous
package bootstrap; the installer exits when resolution completes.
