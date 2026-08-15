# Building RedBolt.Network

Keep this repository beside RedBolt and RedBolt.Tests:

```text
RedBolt/
RedBolt.Network/
RedBolt.Tests/
```

Build the Linux x64 networking module and native libdatachannel wrapper:

```sh
dotnet build src/RedBolt/Native/RedBolt.Network.Native.csproj --configuration Debug
dotnet build src/RedBolt/Network/RedBolt.Network.csproj --configuration Debug
```

The solution accepts the same platform, architecture, module, and sample selection properties as RedBolt. Its saved selection is independent and is written below `build/BuildConfig.props`.

Build the aggregate WebAssembly or Android sample hosts directly:

```sh
dotnet build src/RedBolt/Sample/Web/RedBolt.Sample.Web.csproj --configuration Release -p:RedBoltEnablePlatforms=web
dotnet build src/RedBolt/Sample/Android/RedBolt.Sample.Android.csproj --configuration Debug -f net10.0-android -p:RedBoltEnablePlatforms=android -p:RedBoltEnableArchitectures=all
```

Generated bindings, native libraries, managed assemblies, sample outputs, and packages are written below this repository's `build/` directory. Base RedBolt outputs remain below `../RedBolt/build/`.
