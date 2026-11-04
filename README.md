# MateSoul

MateSoul is a Unity mixed-reality companion project for PICO headsets.

## Development environment

- Unity `2022.3.38f1`
- Universal Render Pipeline `14.0.11`
- Android Build Support with SDK, NDK, and OpenJDK
- Android ARM64 / IL2CPP
- PICO Unity Integration SDK
- XR Interaction Toolkit
- Animation Rigging

## Clone and open

Install [Git LFS](https://git-lfs.com/) before cloning, then initialize it for
your user account:

```bash
git lfs install
git clone https://github.com/picoxr/VibeCodingSampleProject_MateSoul.git
```

Open the cloned repository's `MateSoul/` directory as the Unity project. Do not
open the repository root in Unity.

The project vendors its non-registry dependencies in the same repository:

- `XR/` provides `com.bytedance.pico.xr`.
- `Dependencies/Lengyue/UniVRM-0.131.3/` provides UniGLTF and UniVRM.
- `MateSoul/Packages/manifest.json` references both locations with relative
  `file:` paths.

On the first import, Unity recreates `Library/`, resolves registry packages, and
may take several minutes to compile shaders and scripts. These generated files
are intentionally not versioned.

## Runtime configuration

API keys and other credentials are not stored in Git. Configure the AI provider
from the in-app settings on each device. Local `.env` files, Android debug keys,
build outputs, logs, and user-specific Unity settings are ignored.

The optional historical Node gateway is under `services/ai-gateway/` and requires
Node.js `>=20.18.1`.

## Repository layout

- `MateSoul/Assets/`: game code, scenes, production assets, tests, and PICO settings
- `MateSoul/Packages/`: Unity package manifest and lock file
- `MateSoul/ProjectSettings/`: shared Unity project settings
- `XR/`: vendored PICO Unity SDK package, including its debugger and samples
- `Dependencies/`: vendored Unity package dependencies
- `services/ai-gateway/`: optional historical TypeScript gateway
- `项目沉淀资料/`: plans, technical notes, and device verification reports
- `ArtSource/LocalStudyOnly/Onigawara_Lengyue_20261012/eye_shape_reference_v12_dcc_real/`:
  the Lengyue production DCC chain, from `Fix54` to `Fix59`

Development tooling is versioned too: `MateSoul/Assets/MateSoul/Tests/` holds the
EditMode and Engine test suites, `MateSoul/Assets/Samples/XR Interaction Toolkit/`
holds the editor device simulator, and the validation scenes and device probes
stay in place. Build output, runtime logs, and working renders are not versioned.

## Character appearance work

The Unity side of the Lengyue character is fully versioned: the runtime prefab
`MateSoul/Assets/MateSoul/Content/Lengyue_Runtime.prefab`, its materials and
textures under `MateSoul/Assets/LengyueIntegration/Direct59/`, the visual
variants under
`MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/QualityUpgrade/v003/`, and
the runtime clips.

The Blender chain that produces the body, face, and hair geometry is versioned
as `Fix54` through `Fix59` under the path listed above. Each numbered directory
holds the `.blend` file, the build scripts, the exported buffers, and the gate
reports produced by that step. Working renders, `.log` files and delivered
bundles stay out of Git.

Wardrobe history, EyeLab experiments, the archived standalone Unity6Study
project, shipped asset packages, APKs, and runtime logs are local-only. Rebuild
them locally if a task needs them.

Vide Lab project developed by 冯致雨.
