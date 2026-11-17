using System;
using System.Diagnostics;
using System.Text;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using Unity.Scripting.LifecycleManagement;

namespace Unity.U2D.Physics
{
    // Diagnostic timings for the stages a tilemap area's build and update spend their time in.
    // Every coarse stage carries a profiler marker, which costs nothing unless the Profiler is recording.
    // With logStageTimings set, also logs each stage's time once per build or update.
    internal static partial class TilemapBuildTimings
    {
        internal enum Stage
        {
            ShapesGenerate,
            ShapesUpdate,
            PolygonReadTiles,
            PolygonGather,
            PolygonPlace,
            PolygonDestroy,
            PolygonCreate,
            PolygonOwnerData,
            PolygonCellTable,
            SegmentReadTiles,
            UnclippedEntries,
            UnclippedEmit,
            UnclippedGhosts,
            UnclippedCreate,
            UnclippedStamp,
            UnclippedUpdate,
            ClippedEntries,
            ClippedEmit,
            ClippedGhosts,
            ClippedCreate,
            ClippedStamp,
            ClippedDiff,
            ClippedUpdateEmit,
            ClippedUpdateDestroy,
            ClippedUpdateReshape,
            ClippedUpdateCreate,
            Count
        }

        // Time a stage entered a handful of times per build: always marked for the Profiler, and timed when logging is on.
        public static Scope Measure(Stage stage) => new(stage, marked: true, timed: logStageTimings);

        // Time a stage entered once per cell: neither marked nor timed unless logging is on, so a build pays nothing per cell for it otherwise.
        public static Scope MeasureCell(Stage stage) => logStageTimings ? new Scope(stage, marked: false, timed: true) : default;

        // Log and reset the stages run since the last flush, when logging is on.
        public static void Flush(string label, string memoryReport)
        {
            if (!logStageTimings)
                return;

            var report = new StringBuilder(label).Append(" stage timings (ms):");

            for (var i = 0; i < (int)Stage.Count; ++i)
            {
                if (s_Counts[i] == 0)
                    continue;

                var milliseconds = s_Ticks[i] * 1000d / Stopwatch.Frequency;
                report.Append(' ').Append((Stage)i).Append('=').Append(milliseconds.ToString("F2"));

                if (s_Counts[i] > 1)
                    report.Append(" x").Append(s_Counts[i]);

                s_Ticks[i] = 0;
                s_Counts[i] = 0;
            }

            if (!string.IsNullOrEmpty(memoryReport))
                report.Append(" | memory: ").Append(memoryReport);

            UnityEngine.Debug.Log(report.ToString());
        }

        // Approximate bytes a native list holds, from its capacity rather than its length.
        public static long ListBytes<T>(NativeList<T> list) where T : unmanaged => list.IsCreated ? (long)list.Capacity * UnsafeUtility.SizeOf<T>() : 0;

        // Approximate bytes a native hash container holds: a key, a value and a next link per entry, plus two buckets per entry.
        // A set has no value, so it passes a zero-sized one.
        public static long HashBytes<TKey, TValue>(int capacity) where TKey : unmanaged where TValue : unmanaged =>
            (long)capacity * (UnsafeUtility.SizeOf<TKey>() + UnsafeUtility.SizeOf<TValue>() + sizeof(int) * 3);

        // Append one named size to a memory report, in kilobytes.
        public static void AppendBytes(StringBuilder report, string name, long bytes)
        {
            if (report.Length > 0)
                report.Append(", ");

            report.Append(name).Append('=').Append((bytes / 1024d).ToString("F0")).Append("KB");
        }

        private static ProfilerMarker[] CreateMarkers()
        {
            var markers = new ProfilerMarker[(int)Stage.Count];

            for (var i = 0; i < markers.Length; ++i)
                markers[i] = new ProfilerMarker("PhysicsAreaTilemap." + (Stage)i);

            return markers;
        }

        // One entry into a stage, closed by disposing it. A default scope does nothing.
        internal struct Scope : IDisposable
        {
            public Scope(Stage stage, bool marked, bool timed)
            {
                m_Stage = stage;
                m_Marked = marked;
                m_Timed = timed;
                m_Start = timed ? Stopwatch.GetTimestamp() : 0;

                if (marked)
                    s_Markers[(int)stage].Begin();
            }

            public void Dispose()
            {
                if (m_Marked)
                    s_Markers[(int)m_Stage].End();

                if (!m_Timed)
                    return;

                s_Ticks[(int)m_Stage] += Stopwatch.GetTimestamp() - m_Start;
                s_Counts[(int)m_Stage] += 1;
            }

            private readonly Stage m_Stage;
            private readonly bool m_Marked;
            private readonly bool m_Timed;
            private readonly long m_Start;
        }

        #region Internal

        // Whether stages are timed and logged, on top of the profiler markers they always carry.
        [AutoStaticsCleanup] internal static bool logStageTimings;

        // One marker per stage, named after it.
        [NoAutoStaticsCleanup] private static readonly ProfilerMarker[] s_Markers = CreateMarkers();

        // Elapsed ticks and entry counts per stage since the last flush.
        [NoAutoStaticsCleanup] private static readonly long[] s_Ticks = new long[(int)Stage.Count];
        [NoAutoStaticsCleanup] private static readonly int[] s_Counts = new int[(int)Stage.Count];

        #endregion
    }
}
