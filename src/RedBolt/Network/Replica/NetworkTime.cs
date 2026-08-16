// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Replica;

using System.Globalization;

/// <summary>
/// Represents network-synchronized time of client and server.
/// Consists of deterministic frame index and approximate sub-frame factor that
/// indicates relative time between the beginning of the frame and the next frame.
/// Sub-frame factor is always in range <c>[0, 1)</c>.
/// Overflow of integer frame index is supported.
/// Deltas should be relatively small in order to work as expected: <c>|delta| &lt; 2kkk</c>.
/// </summary>
public struct NetworkTime : IEquatable<NetworkTime>
{
    public NetworkTime(NetworkFrame frame, float subFrame = 0.0f)
    {
        _frame = frame;
        _fraction = subFrame;
        Normalize();
    }

    public static NetworkTime FromDouble(double frame)
    {
        var result = new NetworkTime();
        result += frame;
        return result;
    }

    public readonly NetworkFrame Frame => _frame;

    public readonly float Fraction => _fraction;

    public override readonly string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"#{(long)_frame}:{_fraction:F2}");

    public static NetworkTime operator +(NetworkTime lhs, double rhs)
    {
        lhs.AddDelta(rhs);
        return lhs;
    }

    public static NetworkTime operator -(NetworkTime lhs, double rhs)
    {
        lhs.AddDelta(-rhs);
        return lhs;
    }

    public static double operator -(NetworkTime lhs, NetworkTime rhs) => lhs.GetDelta(rhs);

    public readonly bool Equals(NetworkTime other) => _frame == other._frame && _fraction == other._fraction;

    public override readonly bool Equals(object? obj) => obj is NetworkTime other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(_frame, _fraction);

    public static bool operator ==(NetworkTime lhs, NetworkTime rhs) => lhs.Equals(rhs);

    public static bool operator !=(NetworkTime lhs, NetworkTime rhs) => !lhs.Equals(rhs);

    private void Normalize()
    {
        while (_fraction < 0.0)
        {
            --_frame;
            _fraction += 1.0f;
        }

        while (_fraction >= 1.0)
        {
            ++_frame;
            _fraction -= 1.0f;
        }
    }

    private void AddDelta(double delta)
    {
        var deltaInt = (long)delta;
        var deltaFract = delta - deltaInt;
        _frame += deltaInt;
        _fraction += (float)deltaFract;
        Normalize();
    }

    private readonly double GetDelta(NetworkTime origin)
    {
        var deltaInt = _frame - origin._frame;
        var deltaFract = _fraction - origin._fraction;
        return deltaInt + deltaFract;
    }

    private NetworkFrame _frame;
    private float _fraction;
}

/// <summary>Helper class to smoothly adjust NetworkTime on client.</summary>
public class SoftNetworkTime
{
    public SoftNetworkTime(
        int updateFrequency, float snapThreshold, float tolerance, float minTimeScale, float maxTimeScale)
    {
        _updateFrequency = updateFrequency;
        _snapThreshold = snapThreshold;
        _tolerance = tolerance;
        _minTimeScale = minTimeScale;
        _maxTimeScale = maxTimeScale;
    }

    /// <summary>Reset time unconditionally.</summary>
    public void Reset(NetworkTime targetTime)
    {
        _smoothTime = targetTime;
    }

    /// <summary>Update time, result should be as close to target as possible.</summary>
    public float Update(float timeStep, NetworkTime targetTime)
    {
        var timeError = (float)((targetTime - _smoothTime) / _updateFrequency) - timeStep;

        if (System.Math.Abs(timeError) < _tolerance)
        {
            _smoothTime += timeStep * _updateFrequency;
            return timeStep;
        }

        if (System.Math.Abs(timeError) >= _snapThreshold)
        {
            _smoothTime = targetTime;
            return timeStep;
        }

        var dilatedTimeStep = System.Math.Clamp(
            timeStep + timeError, timeStep * _minTimeScale, timeStep * _maxTimeScale);
        _smoothTime += dilatedTimeStep * _updateFrequency;
        return dilatedTimeStep;
    }

    public NetworkTime Time => _smoothTime;

    private readonly int _updateFrequency;
    private readonly float _snapThreshold;
    private readonly float _tolerance;
    private readonly float _minTimeScale;
    private readonly float _maxTimeScale;

    private NetworkTime _smoothTime;
}
