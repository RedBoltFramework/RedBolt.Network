# RedBolt.Network workspace instructions

- RedBolt is expected at `../RedBolt` unless `RedBoltRoot` is supplied.
- Tests remain in `../RedBolt.Tests`.
- Preserve the public `RedBolt.Network` and `RedBolt.Bindings.LibDataChannel` assemblies and namespaces.
- Use generated SharpCpp bindings for libdatachannel. Do not add manual native bindings.
- Keep browser WebRTC glue in this module; the base RedBolt web host exposes only the generic extension seam.
- Keep desktop, WebAssembly, and Android builds working.
- Follow the monorepo `doc/VERIFICATION.md`: build affected projects and run focused tests; validate affected platform samples and packages when their behavior or integration changes. Run the full supported matrix for release validation or when requested.
