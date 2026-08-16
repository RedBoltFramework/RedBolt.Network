// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

// ID used to identify unique NetworkObject within Scene.
global using NetworkId = RedBolt.Scene.ComponentReference;
global using PacketTypeFlags = RedBolt.Network.PacketType;
global using PositionAndVelocity = RedBolt.Replica.ValueWithDerivative<RedBolt.Math.BaseRealVector3<double>>;
