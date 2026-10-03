# Building RedBolt.Network

Keep this repository beside RedBolt and RedBolt.Tests:

```text
RedBolt/
RedBolt.Network/
RedBolt.Tests/
```

Build the Linux x64 networking module and native libdatachannel wrapper:

```sh
dotnet build src/RedBolt/Native/LibDataChannel/RedBolt.Bindings.LibDataChannel.csproj --configuration Debug
dotnet build src/RedBolt/Network/RedBolt.Network.csproj --configuration Debug
```

The solution accepts the same `RedBoltEnable`, module, and sample selection properties as RedBolt. Its saved selection is written to the active build root's `BuildConfig.props`: a standalone checkout keeps its own under `build/`; inside the monorepo the module solution shares the monorepo `build/BuildConfig.props` with the root solution. The build root is selected statically rather than recorded: inside the monorepo every entry point (solution or direct project) uses the monorepo `build/`, a standalone checkout uses its own `build/`, and `-p:RedBoltBuildRoot=<dir>` overrides both.

Build the aggregate WebAssembly or Android sample hosts directly:

```sh
dotnet build src/RedBolt/Sample/Web/RedBolt.Sample.Web.csproj --configuration Release -p:RedBoltEnable=linux-x64%2cbrowser-wasm
dotnet build src/RedBolt/Sample/Android/RedBolt.Sample.Android.csproj --configuration Debug -f net10.0-android -p:RedBoltEnable=linux-x64%2candroid
```

Outputs are written below the active build root. Inside the monorepo this is the monorepo `build/`; a standalone checkout uses its own `build/`.
