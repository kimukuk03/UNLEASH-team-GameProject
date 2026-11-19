using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace CowMeat.Destruction
{
    /// <summary>
    /// Per-frame cost breakdown for short destruction benchmarks: frame time, CPU main / render thread, GPU,
    /// camera render, shadows, particles, physics, GC and batches. Keeps the worst frames with their breakdown
    /// so spikes can be attributed (e.g. to a block fracture or a GC allocation).
    /// </summary>
    public class BenchRecorder : System.IDisposable
    {
        private struct Rec
        {
            public string label;
            public ProfilerRecorder r;
            public bool ns; // value in ns (time) or a plain count/bytes
        }

        private readonly List<Rec> recs = new List<Rec>();
        private readonly List<float[]> frames = new List<float[]>();
        private readonly List<string> frameEvents = new List<string>();

        public BenchRecorder()
        {
            Add("main", ProfilerCategory.Internal, "Main Thread", true);
            Add("render", ProfilerCategory.Render, "CPU Render Thread Frame Time", true);
            Add("gpu", ProfilerCategory.Render, "GPU Frame Time", true);
            Add("camRender", ProfilerCategory.Render, "Camera.Render", true);
            Add("shadows", ProfilerCategory.Render, "Shadows.RenderShadowMap", true);
            Add("psUpdate", new ProfilerCategory("PlayerLoop"), "PostLateUpdate.ParticleSystemEndUpdateAll", true);
            Add("psDraw", ProfilerCategory.Render, "ParticleSystem.Draw", true);
            Add("physics", ProfilerCategory.Physics, "Physics.Simulate", true);
            Add("physStep", new ProfilerCategory("PlayerLoop"), "FixedUpdate.PhysicsFixedUpdate", true); // simulate + contact callbacks
            Add("update", new ProfilerCategory("PlayerLoop"), "Update.ScriptRunBehaviourUpdate", true);
            Add("lateUpd", new ProfilerCategory("PlayerLoop"), "PreLateUpdate.ScriptRunBehaviourLateUpdate", true);
            Add("gcKB", ProfilerCategory.Memory, "GC Allocated In Frame", false);
            Add("batches", ProfilerCategory.Render, "Batches Count", false);
            Add("setpass", ProfilerCategory.Render, "SetPass Calls Count", false);
            Add("tris", ProfilerCategory.Render, "Triangles Count", false);
        }

        private void Add(string label, ProfilerCategory cat, string name, bool ns)
        {
            var r = ProfilerRecorder.StartNew(cat, name, 4);
            recs.Add(new Rec { label = label, r = r, ns = ns });
        }

        /// <summary>Call once per frame. <paramref name="evt"/> describes what happened this frame (fractures, spawns...).</summary>
        public void Sample(float frameMs, string evt)
        {
            var v = new float[recs.Count + 1];
            v[0] = frameMs;
            for (int i = 0; i < recs.Count; i++)
            {
                var rec = recs[i];
                if (!rec.r.Valid) { v[i + 1] = -1f; continue; }
                long raw = rec.r.LastValue;
                v[i + 1] = rec.ns ? raw / 1e6f : (rec.label == "gcKB" ? raw / 1024f : raw);
            }
            frames.Add(v);
            frameEvents.Add(evt);
        }

        public int Count => frames.Count;

        public float Avg(int col)
        {
            double s = 0; int n = 0;
            foreach (var f in frames) if (f[col] >= 0f) { s += f[col]; n++; }
            return n > 0 ? (float)(s / n) : -1f;
        }

        public float Percentile(int col, float p)
        {
            var l = new List<float>();
            foreach (var f in frames) if (f[col] >= 0f) l.Add(f[col]);
            if (l.Count == 0) return -1f;
            l.Sort();
            return l[Mathf.Clamp((int)(l.Count * p), 0, l.Count - 1)];
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append($"frame avg {Avg(0):0.0} p95 {Percentile(0, 0.95f):0.0} max {Percentile(0, 1f):0.0} ms");
            for (int i = 0; i < recs.Count; i++)
            {
                float a = Avg(i + 1);
                if (a < 0f) { sb.Append($" | {recs[i].label} n/a"); continue; }
                sb.Append(recs[i].ns ? $" | {recs[i].label} {a:0.00}/{Percentile(i + 1, 1f):0.0}" : $" | {recs[i].label} {a:0}/{Percentile(i + 1, 1f):0}");
            }
            return sb.ToString();
        }

        public string WorstFrames(int count)
        {
            var idx = new List<int>();
            for (int i = 0; i < frames.Count; i++) idx.Add(i);
            idx.Sort((a, b) => frames[b][0].CompareTo(frames[a][0]));
            var sb = new StringBuilder();
            for (int k = 0; k < Mathf.Min(count, idx.Count); k++)
            {
                int i = idx[k];
                var f = frames[i];
                sb.Append($"\n   #{i} {f[0]:0.0} ms:");
                for (int c = 0; c < recs.Count; c++)
                    if (f[c + 1] >= 0f) sb.Append(recs[c].ns ? $" {recs[c].label}={f[c + 1]:0.0}" : $" {recs[c].label}={f[c + 1]:0}");
                if (!string.IsNullOrEmpty(frameEvents[i])) sb.Append($" [{frameEvents[i]}]");
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            foreach (var r in recs) r.r.Dispose();
        }
    }
}
