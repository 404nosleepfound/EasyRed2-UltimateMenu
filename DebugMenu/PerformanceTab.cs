using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System.Linq;

namespace EasyRed2Mod
{
    // Built-in profiler and diagnostics tab: tracks FPS, execution time (in milliseconds), and exception counts per feature
    public static class PerformanceTab
    {
        // Tracks timing metrics and error counts for an individual feature
        private class ProfilerEntry
        {
            public Stopwatch watch = new Stopwatch();
            public float lastTimeMs = 0f;
            public int errorCount = 0;
            public long calls = 0;
            public float lastUpdateFrame = 0;
        }

        private static Dictionary<string, ProfilerEntry> _entries = new Dictionary<string, ProfilerEntry>();
        private static float _fps = 0f;
        private static float _deltaTime = 0f;

        // Starts or restarts a high-precision stopwatch for a specific subsystem
        public static void Begin(string key)
        {
            if (!_entries.ContainsKey(key)) _entries[key] = new ProfilerEntry();
            _entries[key].watch.Restart();
            _entries[key].calls++;
            _entries[key].lastUpdateFrame = Time.frameCount;
        }

        // Stops the stopwatch and smooths the execution duration using linear interpolation (Lerp)
        public static void End(string key)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                entry.watch.Stop();
                float currentMs = (float)entry.watch.Elapsed.TotalMilliseconds;
                // Exponential moving average (10% weight to current measurement to eliminate jitter)
                entry.lastTimeMs = Mathf.Lerp(entry.lastTimeMs, currentMs, 0.1f);
            }
        }

        // Increments error counter when a feature catches an exception in a try-catch block
        public static void LogError(string key)
        {
            if (!_entries.ContainsKey(key)) _entries[key] = new ProfilerEntry();
            _entries[key].errorCount++;
        }

        // Calculates smoothed frames per second using unscaled delta time (unaffected by slow motion)
        public static void UpdateFPS()
        {
            _deltaTime += (Time.unscaledDeltaTime - _deltaTime) * 0.1f;
            _fps = 1.0f / _deltaTime;
        }

        // Renders the profiler table inside the menu GUI
        public static void Draw(Rect rect)
        {
            GUI.Box(rect, "");

            float x = rect.x + 20;
            float y = rect.y + 20;
            float lineHeight = 25f;

            // 1. FPS Counter with color-coded performance thresholds
            string fpsColor = _fps < 30 ? "red" : (_fps < 60 ? "yellow" : "#00FF00");
            GUI.Label(new Rect(x, y, 300, 30), $"<b>CURRENT FPS: <color={fpsColor}>{Mathf.Ceil(_fps)}</color></b>");

            y += 40;

            // 2. Table Header
            GUI.Label(new Rect(x, y, 200, 30), "<b>FEATURE NAME</b>");
            GUI.Label(new Rect(x + 220, y, 100, 30), "<b>TIME (ms)</b>");
            GUI.Label(new Rect(x + 330, y, 100, 30), "<b>ERRORS</b>");

            y += 30;
            GUI.Box(new Rect(x, y, rect.width - 40, 2), ""); // Horizontal separator line
            y += 10;

            // 3. Render Profiler Entries
            if (_entries.Count == 0)
            {
                GUI.Label(new Rect(x, y, 400, 25), "<color=grey><i>NO DATA COLLECTED YET. ACTIVATE FEATURES FIRST.</i></color>");
            }
            else
            {
                string[] keys = _entries.Keys.ToArray();

                foreach (var key in keys)
                {
                    var entry = _entries[key];

                    // Hide inactive features that haven't run in the last 100 frames
                    if (Time.frameCount - entry.lastUpdateFrame > 100) continue;

                    // Highlight slow features taking more than 3ms or 10ms of frame time
                    string timeColor = entry.lastTimeMs > 10f ? "red" : (entry.lastTimeMs > 3f ? "yellow" : "white");
                    string errColor = entry.errorCount > 0 ? "magenta" : "grey";

                    GUI.Label(new Rect(x, y, 200, 25), key);
                    GUI.Label(new Rect(x + 220, y, 100, 25), $"<color={timeColor}>{entry.lastTimeMs:0.00} ms</color>");
                    GUI.Label(new Rect(x + 330, y, 100, 25), $"<color={errColor}>{entry.errorCount}</color>");

                    y += lineHeight;
                }
            }
        }
    }
}