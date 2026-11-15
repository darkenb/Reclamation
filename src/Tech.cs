using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TrashStation
{
    // Tech helpers from BUG_OreEnrichment: a tech added straight into the loaded tree once every mod has built it.
    static class Tech
    {
        // Costs what the awaited tech costs, so its matrices exist at that point of any tree; one blue matrix without one.
        internal static TechProto New(int id, string name, string desc, string icon, int gate, int[] unlocks, Vector2 position)
        {
            int[] items = { TechProto.matrixIds[0] }, points = { 10 };
            long hash = 36000L;
            bool lab = true;
            var like = gate > 0 ? LDB.techs.Select(gate) : null;
            if (like?.Items != null && like.Items.Length > 0 && like.ItemPoints != null && like.ItemPoints.Length == like.Items.Length && like.HashNeeded > 0)
            {
                items = (int[])like.Items.Clone();
                points = (int[])like.ItemPoints.Clone();
                hash = like.HashNeeded;
                lab = like.IsLabTech;
            }
            return new TechProto
            {
                ID = id,
                Name = name,
                Desc = desc,
                Conclusion = "",
                Published = true,
                IsLabTech = lab,
                IconPath = icon,
                IconTag = "",
                PreTechs = new int[0],
                PreTechsImplicit = gate > 0 ? new[] { gate } : new int[0],
                PreItem = new int[0],
                Items = items,
                ItemPoints = points,
                HashNeeded = hash,
                UnlockRecipes = unlocks,
                UnlockFunctions = new int[0],
                UnlockValues = new double[0],
                AddItems = new int[0],
                AddItemCounts = new int[0],
                PropertyOverrideItems = new int[0],
                PropertyItemCounts = new int[0],
                PropertyOverrideItemArray = new IDCNT[0],
                Position = position,
            };
        }

        internal static void Insert(TechProto[] techs)
        {
            var set = LDB.techs;
            var old = set.dataArray;
            set.Init(old.Length + techs.Length);
            Array.Copy(old, set.dataArray, old.Length);
            for (int i = 0; i < techs.Length; i++) set.dataArray[old.Length + i] = techs[i];
            var index = new Dictionary<int, int>();  // not OnAfterDeserialize: it resets every translated name to its key
            for (int i = 0; i < set.dataArray.Length; i++) index[set.dataArray[i].ID] = i;
            Traverse.Create(set).Field("dataIndices").SetValue(index);
            if (set.Select(techs[0].ID) != techs[0]) throw new InvalidOperationException("proto index not rebuilt");
            foreach (var p in techs) p.Preload();
            foreach (var p in techs) p.Preload2();
        }

        // The tech that unlocks a recipe making this item.
        internal static int Unlocking(int item)
        {
            var made = new HashSet<int>();
            foreach (var r in LDB.recipes.dataArray)
                if (r?.Results != null && Array.IndexOf(r.Results, item) >= 0) made.Add(r.ID);
            foreach (var t in LDB.techs.dataArray)
                if (t?.UnlockRecipes != null)
                    foreach (int id in t.UnlockRecipes) if (made.Contains(id)) return t.ID;
            return 0;
        }

        // A new row under the lowest row of the main tech page, on that page's own spacing and starting column.
        public static void Layout(TechProto[] techs, out float x0, out float dx, out float y0)
        {
            float minY = float.MaxValue;
            var rowStart = new Dictionary<float, float>();
            var ys = new SortedSet<float>();
            var xs = new SortedSet<float>();
            foreach (var t in techs)
            {
                if (t == null || t.page != 0 || !t.Published) continue;
                var p = t.Position;
                minY = Math.Min(minY, p.y);
                ys.Add(p.y);
                xs.Add(p.x);
                rowStart[p.y] = rowStart.TryGetValue(p.y, out float x) ? Math.Min(x, p.x) : p.x;
            }
            dx = CommonGap(xs, 4f);
            float dy = CommonGap(ys, 4f);
            x0 = Mode(rowStart.Values, 0f);
            y0 = (minY == float.MaxValue ? 0f : minY) - dy;
        }

        static float CommonGap(SortedSet<float> values, float fallback)
        {
            var gaps = new List<float>();
            float last = float.NaN;
            foreach (float v in values) { if (!float.IsNaN(last) && v - last > 0.5f) gaps.Add(v - last); last = v; }
            return Mode(gaps, fallback);
        }

        static float Mode(IEnumerable<float> values, float fallback)
        {
            var counts = new Dictionary<float, int>();
            float best = fallback;
            int most = 0;
            foreach (float v in values)
            {
                counts[v] = counts.TryGetValue(v, out int n) ? n + 1 : 1;
                if (counts[v] > most || counts[v] == most && v < best) { most = counts[v]; best = v; }
            }
            return best;
        }
    }
}
