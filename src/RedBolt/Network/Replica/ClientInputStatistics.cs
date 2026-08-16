// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Replica;

/// <summary>Utility to evaluate client input quality and preferred input buffering.</summary>
public class ClientInputStatistics
{
    public ClientInputStatistics(uint windowSize, uint maxInputLoss)
    {
        _windowSize = windowSize;
        _maxInputLoss = unchecked((int)maxInputLoss);
    }

    /// <summary>Notify that the input was received for given frame.</summary>
    public void OnInputReceived(NetworkFrame frame)
    {
        if (!_latestInputFrame.HasValue)
        {
            _latestInputFrame = frame;
            return;
        }

        var delta = unchecked((int)(frame - _latestInputFrame.Value));
        _latestInputFrame = frame;
        if (delta <= 0)
            return;

        var numLostFrames = System.Math.Min(delta, _maxInputLoss) - 1;
        for (var i = 0; i <= numLostFrames; ++i)
        {
            _numLostFrames.Enqueue((uint)i);
            if (_numLostFrames.Count > _windowSize)
                _numLostFrames.Dequeue();
        }

        UpdateHistogram();
        _bufferSize = GetMaxRepeatedLoss();
    }

    public uint RecommendedBufferSize => _bufferSize;

    private void UpdateHistogram()
    {
        _histogram.Clear();
        foreach (var numLost in _numLostFrames)
        {
            while (_histogram.Count <= numLost)
                _histogram.Add(0);
            ++_histogram[(int)numLost];
        }
    }

    private uint GetMaxRepeatedLoss()
    {
        for (var i = _histogram.Count - 1; i >= 0; --i)
        {
            if (_histogram[i] >= 2)
                return (uint)i;
        }
        return 0;
    }

    private readonly int _maxInputLoss;
    private readonly uint _windowSize;

    private NetworkFrame? _latestInputFrame;
    private readonly Queue<uint> _numLostFrames = new();

    private readonly List<uint> _histogram = [];
    private uint _bufferSize;
}
