# RedBolt.Network NuGet packages

RedBolt.Network versions independently from RedBolt, but every package records exact RedBolt and SharpCpp package versions. Produce the matching base package set first in `<build root>/package/nuget/RedBolt/out` (`<build root>` is the monorepo `build/` inside the monorepo and the repository's own `build/` standalone; `REDBOLT_BUILD_ROOT` overrides it).

Create Linux x64 packages from Release outputs, building the add-on first:

```sh
dotnet run --file packaging/nuget/package.cs -- create --rids linux-x64
```

Use `--no-build` when all selected outputs already exist. Select additional prebuilt targets with a comma-separated list such as `--rids linux-x64,android-x64,android-arm64,browser-wasm`. Override RedBolt version discovery only for an intentional local feed with `--redbolt-version VERSION`.

The command writes independently versioned packages below `<build root>/package/nuget/RedBolt.Network/out`:

- `RedBolt.Network`
- `RedBolt.Network.Runtime.<rid>`

The runtime facade brings in the managed networking package, the matching `RedBolt.Core.Runtime.<rid>` package, the generated libdatachannel binding assembly, and the module native library for the selected RID. The browser runtime instead carries the browser-specific managed assembly and WebRTC build integration.

Validate package contents, exact dependency versions, and a package-only consumer with:

```sh
dotnet run --file packaging/nuget/package.cs -- validate --rids linux-x64
```
