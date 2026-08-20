// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

#:project ../../../RedBolt/eng/RedBolt.ModuleBuild/Packaging/RedBolt.ExternalModule.Packaging.csproj

using System.Runtime.CompilerServices;
using RedBolt.ExternalModule.Packaging;

return ExternalModulePackager.Run(args, new ExternalModulePackageManifest
{
    Name = "RedBolt.Network",
    BasePackages = ["RedBolt.Core", "SharpCpp.Std", "SharpCpp.Support"],
    RequireBaseRuntime = true,
    SupportedRids = ["linux-x64", "win-x64", "android-x64", "android-arm64", "browser-wasm"],
    DefaultRids = ["linux-x64"],
    BuildSteps =
    [
        new("Native/LibDataChannel/RedBolt.Bindings.LibDataChannel.csproj", BuildWhen.Linux, true),
        new("Network/RedBolt.Network.csproj", BuildWhen.Linux, true, "-p:BuildProjectReferences=false"),
        new("Network/RedBolt.Network.csproj", BuildWhen.Windows, false,
            "-p:RedBoltPlatform=win-x64"),
        new("Sample/Web/RedBolt.Sample.Web.csproj", BuildWhen.Web, false,
            "-p:RedBoltEnablePlatforms=web"),
        new("Sample/Android/RedBolt.Sample.Android.csproj", BuildWhen.Android, false,
            "--framework", "net10.0-android", "-p:RedBoltEnablePlatforms=android",
            "-p:RedBoltEnableArchitectures=all")
    ],
    ManagedPackages =
    [
        new(
            "RedBolt.Network",
            "Networking and replication with HTTP plus LAN discovery and data-channel transports for RedBolt.",
            [
                new("build/bin/Release/linux-x64/core/RedBolt.Network.dll", "lib/net10.0/RedBolt.Network.dll"),
                new("build/bin/Release/linux-x64/extern/RedBolt.Bindings.LibDataChannel.dll", "lib/net10.0/RedBolt.Bindings.LibDataChannel.dll"),
                new("build/obj/managed/browser-wasm/staging/RedBolt.Network/Release/RedBolt.Network.dll", "runtimes/browser-wasm/lib/net10.0/RedBolt.Network.dll", "browser-wasm"),
                new("src/RedBolt/Sample/Web/wwwroot/extension.js", "buildTransitive/RedBolt.Network/extension.js"),
                new("packaging/nuget/RedBolt.Network.targets", "buildTransitive/RedBolt.Network.targets")
            ],
            [
                new("RedBolt.Core", PackageVersions.RedBolt),
                new("SharpCpp.Std", PackageVersions.RedBolt),
                new("SharpCpp.Support", PackageVersions.RedBolt)
            ])
    ],
    RuntimePackage = new(
        "RedBolt.Network.Runtime",
        "libdatachannel",
        "*RedBolt.Native.LibDataChannel*",
        "",
        [
            new("RedBolt.Network", PackageVersions.Module),
            new("RedBolt.Core.Runtime.{rid}", PackageVersions.RedBolt)
        ]),
    Consumer = new(
        "RedBolt.Network.Runtime.linux-x64",
        """
        using System;
        Console.WriteLine(typeof(RedBolt.Network.Network).FullName);
        """,
        OnlyWithLinuxRuntime: true)
}, SourcePath());

static string SourcePath([CallerFilePath] string path = "") => path;
