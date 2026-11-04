using System.Collections.Generic;
using UnityEngine;

namespace SimpleTools.Replay
{
    // Recorded poses of one object over time. Unchanged samples are dropped, so an object at rest costs almost
    // nothing; a hold key is added when it starts moving again, so sampling never interpolates across the pause.
    public sealed class TransformTrack
    {
        private const float PositionEpsilon = 0.00005f;
        private const float RotationDotEpsilon = 0.0000005f;

        private readonly List<float> _times = new List<float>();
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Quaternion> _rotations = new List<Quaternion>();
        private readonly List<bool> _visible = new List<bool>();
        private float _lastSeenTime;

        public int KeyCount => _times.Count;

        public void Add(float time, Vector3 position, Quaternion rotation, bool visible = true)
        {
            int last = _times.Count - 1;
            if (last >= 0)
            {
                bool samePose = (position - _positions[last]).sqrMagnitude < PositionEpsilon * PositionEpsilon &&
                                1f - Mathf.Abs(Quaternion.Dot(rotation, _rotations[last])) < RotationDotEpsilon &&
                                visible == _visible[last];
                if (samePose)
                {
                    _lastSeenTime = time;
                    return;
                }

                if (_lastSeenTime > _times[last])
                {
                    Append(_lastSeenTime, _positions[last], _rotations[last], _visible[last]);
                }
            }

            Append(time, position, rotation, visible);
            _lastSeenTime = time;
        }

        // Closes the track, so the last pose holds until the given time
        public void End(float time)
        {
            int last = _times.Count - 1;
            if (last >= 0 && time > _times[last])
            {
                Append(time, _positions[last], _rotations[last], _visible[last]);
            }
        }

        // Pose at a time, clamped to the recorded range. False if the track is empty. A visibility change is a cut:
        // the pose is not interpolated across it.
        public bool Sample(float time, out Vector3 position, out Quaternion rotation, out bool visible)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            visible = false;
            int count = _times.Count;
            if (count == 0)
            {
                return false;
            }

            int low = 0;
            int high = count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (_times[middle] <= time)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            int next = Mathf.Min(low + 1, count - 1);
            visible = _visible[low];
            if (next == low || time <= _times[low] || _visible[next] != visible)
            {
                position = _positions[low];
                rotation = _rotations[low];
                return true;
            }

            float blend = (time - _times[low]) / (_times[next] - _times[low]);
            position = Vector3.Lerp(_positions[low], _positions[next], blend);
            rotation = Quaternion.Slerp(_rotations[low], _rotations[next], blend);
            return true;
        }

        private void Append(float time, Vector3 position, Quaternion rotation, bool visible)
        {
            _times.Add(time);
            _positions.Add(position);
            _rotations.Add(rotation);
            _visible.Add(visible);
        }
    }
}
