using System;
using System.Collections.Generic;
using System.Linq;

namespace TrashStation
{
    // Points, prices and placement as plain data, checked by check.cs without the game.
    public static class Value
    {
        // What the converter makes, simplest first: Energy Shard, Dark Fog Matrix (memory unit), Silicon-based Neuron,
        // Matter Recombinator, Negentropy Singularity, Core Element (virtual particle), Dark Fog Graviton Lens.
        public static readonly int[] Outputs = { 5206, 5201, 5202, 5203, 5204, 5205, 1211 };
        // A drop's worth in raw materials (the lens is made by a recipe, so it has none here) (an iron ore, a stone or a water each count 1); the same scale as BUG_OreEnrichment's recycling.
        public static readonly Dictionary<int, double> Worth = new Dictionary<int, double>
        {
            { 5206, 3 }, { 5201, 20 }, { 5202, 25 }, { 5203, 30 }, { 5204, 40 }, { 5205, 60 },
        };
        // Critical photons come from no recipe but take a Dyson sphere: priced, or antimatter would come cheap.
        public static readonly Dictionary<int, double> FixedCosts = Worth.Concat(new[] { new KeyValuePair<int, double>(1208, 8) }).ToDictionary(p => p.Key, p => p.Value);

        public const int PointsPerRaw = 10;            // an item worth one raw material gives 10 points
        // Points per output, simplest to rarest: an item worth 50 points takes 20 000 of itself for a Core Element.
        public static readonly Dictionary<int, int> Prices = new Dictionary<int, int>
        {
            { 5206, 10_000 }, { 5201, 50_000 }, { 5202, 100_000 }, { 5203, 200_000 }, { 5204, 500_000 }, { 5205, 1_000_000 }, { 1211, 2_000_000 },
        };
        public const long JoulesPerPoint = 100_000;    // energy building: 1 point = 100 kJ, so a raw material's worth = 1 MJ
        public const int FullPoints = 1000;            // energy building: sorters stop feeding with this many points waiting

        public class Recipe { public int[] In, InN, Out, OutN; }

        // Cheapest raw-material cost of every item over the loaded recipes; an item no recipe makes costs 1. Never above
        // what any real route costs, so burning can never pay more than making.
        public static Dictionary<int, double> Costs(IList<Recipe> recipes, IDictionary<int, double> fixedCosts)
        {
            var cost = new Dictionary<int, double>(fixedCosts);
            var made = new HashSet<int>(recipes.SelectMany(r => r.Out));
            foreach (var r in recipes)
                foreach (int i in r.In)
                    if (!made.Contains(i) && !cost.ContainsKey(i)) cost[i] = 1;
            for (int pass = 0, changed = 1; pass < 200 && changed > 0; pass++)
            {
                changed = 0;
                foreach (var r in recipes)
                {
                    double sum = 0;
                    bool known = r.In.Length > 0;
                    for (int j = 0; j < r.In.Length && known; j++)
                        if (cost.TryGetValue(r.In[j], out double c)) sum += c * r.InN[j]; else known = false;
                    int outs = r.OutN.Sum();
                    if (!known || outs <= 0) continue;
                    double each = sum / outs;
                    foreach (int o in r.Out)
                        if (!fixedCosts.ContainsKey(o) && (!cost.TryGetValue(o, out double old) || each < old - 1e-9)) { cost[o] = each; changed++; }
                }
            }
            return cost;
        }

        // An item's points: its worth times ten, at least 1 (an item of unknown worth still burns). A fuel gives at most
        // half the energy a power plant gets from it, so burning it here never beats burning it there.
        public static int Points(double cost, long heatValue)
        {
            double p = cost > 0 && !double.IsInfinity(cost) ? Math.Round(cost * PointsPerRaw) : 1;
            if (heatValue > 0) p = Math.Min(p, heatValue / 2 / JoulesPerPoint);
            return (int)Math.Max(1, Math.Min(p, 1_000_000));
        }

        public static int Price(int output) => Prices[output];

        // Incinerator, anything that is not a fuel: fuel energy by the item's points, rising faster than they do. A raw
        // material (10 points) gives 100 kJ, a tenth of the full rate; from 1000 points on every point is 100 kJ.
        public static long Heat(int points) => points <= 0 ? 0 : (long)(JoulesPerPoint * points * Math.Min(1.0, Math.Sqrt(points / 1000.0)));

        // And the power it burns at, as the game's spray levels (each +25%): none below 32 points, two more for every
        // ten times the points, ten (+250%) from a million.
        public static int Level(int points) => points < 10 ? 0 : Math.Max(0, Math.Min(10, (int)(2 * Math.Log10(points / 10.0) + 1e-9)));

        // A big count in few characters, so it stays readable at a large size: 9999, 589.8k, 45M.
        public static string Short(double n)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            double a = Math.Abs(n);
            if (a < 10_000) return Math.Round(n).ToString(c);
            if (a < 1e6) return (n / 1e3).ToString("0.#", c) + "k";
            if (a < 1e9) return (n / 1e6).ToString("0.#", c) + "M";
            return (n / 1e9).ToString("0.#", c) + "B";
        }

        // Energy in the game's units: 500 kJ, 6.8 MJ, 1.25 GJ.
        public static string Energy(double joules)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            if (joules < 1e6) return (joules / 1e3).ToString("0.#", c) + " kJ";
            if (joules < 1e9) return (joules / 1e6).ToString("0.##", c) + " MJ";
            return (joules / 1e9).ToString("0.##", c) + " GJ";
        }

        // A free replicator cell (tab*1000 + row*100 + col): the first free column of the lowest row that has one.
        public static int Cell(ICollection<int> used, int tab, int rows = 7, int cols = 14)
        {
            for (int row = rows; row >= 1; row--)
                for (int col = 1; col <= cols; col++)
                    if (!used.Contains(tab * 1000 + row * 100 + col)) return tab * 1000 + row * 100 + col;
            return 0;
        }

        // A free build-bar key (row*100 + col): in the first wanted row with room, else the first row with room. The bar has ten buttons per
        // category (keys 1-0); a key beyond them has no button and breaks the bar every frame. Categories 1-8 are the ones
        // Rebind Build Bar manages.
        public const int Rows = 8, Buttons = 10;
        public static int Slot(ICollection<int> used, params int[] rows)
        {
            foreach (int r in rows.Where(r => r >= 1 && r <= Rows).Concat(Enumerable.Range(1, Rows)))
                for (int col = 1; col <= Buttons; col++)
                    if (!used.Contains(r * 100 + col)) return r * 100 + col;
            return 0;
        }
    }
}
