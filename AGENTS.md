# RedBolt.Network workspace instructions

- RedBolt is expected at `../RedBolt` unless `RedBoltRoot` is supplied.
- Tests remain in `../RedBolt.Tests`.
- Preserve the public `RedBolt.Network` and `RedBolt.Bindings.LibDataChannel` assemblies and namespaces.
- Use generated SharpCpp bindings for libdatachannel. Do not add manual native bindings.
- Keep browser WebRTC glue in this module; the base RedBolt web host exposes only the generic extension seam.
- Keep desktop, WebAssembly, and Android builds working.
- At completion, build the solution and platform samples, create packages, run focused Network tests, and review a captured sample screenshot.
