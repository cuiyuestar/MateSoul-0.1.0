# UniVRM Spring Bone Algorithm

Source: https://github.com/vrm-c/UniVRM/tree/v0.99.4

The Verlet integration, rest-direction stiffness, drag and sphere projection
in `Runtime/HairSpringSolver.cs` are adapted from `VRMSpringBone.cs`, whose
original algorithm is credited upstream to @ricopin416.

This is a small adapted kernel, **not the complete UniVRM package or its Burst
job implementation**. Changes: System.Numerics math for standalone numerical
validation, explicit joint arrays and tip lengths, fixed 90 Hz update,
limited bending, collision iteration, quality masks. Unity lifecycle,
teleport reset and profiling live in separate project-owned components.

`VRMSpringBone.cs.txt` is the unmodified upstream reference (not compiled).
`LICENSE.txt` applies to the upstream algorithm and source.

Do not interpret a standalone .NET benchmark as Unity/IL2CPP or PICO timing.
