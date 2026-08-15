# RedBolt.Network

Optional networking and replication for [RedBolt](https://github.com/RedBoltFramework/RedBolt), including HTTP, LAN discovery, and libdatachannel transports.

The source build expects RedBolt in `../RedBolt` by default. Override `RedBoltRoot` when the repositories use a different layout.

```sh
dotnet build RedBolt.Network.slnx --configuration Debug
```

Desktop and Android use the generated native libdatachannel binding. WebAssembly uses browser WebRTC through the module-owned `extension.js`, so the base RedBolt web host does not know about networking.

Desktop, WebAssembly, and Android sample catalogs are included under `src/RedBolt/Sample`. Tests remain in the sibling `RedBolt.Tests` repository.
