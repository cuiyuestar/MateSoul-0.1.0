using System.Collections;
using UnityEngine;

namespace MateSoul.Tests
{
    // EditMode's runner only accepts null frames, even after EnterPlayMode.
    internal static class EngineTestFrames
    {
        public static IEnumerator Seconds(float seconds)
        {
            var until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        public static IEnumerator FixedStep()
        {
            var previous = Time.fixedTime;
            while (Time.fixedTime <= previous) yield return null;
        }
    }
}
