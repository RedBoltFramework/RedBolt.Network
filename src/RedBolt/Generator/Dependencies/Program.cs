// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Generator.Dependencies;

using System;
using System.IO;

using SharpCpp.Model.Logging;

internal static class Program
{
    private static int Main(string[] args)
    {
        StreamWriter? logWriter = null;

        try
        {
            var options = GeneratorOptions.Parse(args);
            var generator = CreateGenerator(options.Surface, options.NativeLibraryName);
            Directory.CreateDirectory(options.OutputDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(options.LogPath)!);
            logWriter = new StreamWriter(options.LogPath, false);
            SharpCppLog.Configure(fileWriter: logWriter);

            var summary = generator.Generate(options);
            Console.WriteLine(summary);
            logWriter.WriteLine(summary);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            logWriter?.WriteLine(ex);
            PrintUsage();
            return 1;
        }
        finally
        {
            SharpCppLog.Configure();
            logWriter?.Dispose();
        }
    }

    private static BindingTargetGenerator CreateGenerator(string surface, string nativeLibraryName)
    {
        return surface.Equals("libdatachannel", StringComparison.OrdinalIgnoreCase)
            ? new LibDataChannelGenerator(nativeLibraryName)
            : throw new ArgumentException($"Unknown binding surface '{surface}'.", nameof(surface));
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: RedBolt.Network.Generator.Dependencies --surface libdatachannel --header <path> --include-name <include> --output-dir <dir> --file-stem <stem> --metadata <path> --log <path> --sharpcpp-root <path> --redbolt-root <path> --native-library-name <name> --compiler <path> [--compiler-target <triple>] [--compiler-sysroot <path>] [--include <dir>]... [--define <name[=value]>]... [--owned-header <path>]...");
    }
}
