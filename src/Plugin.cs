using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CommonAPI;
using CommonAPI.Systems;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace TrashStation
{
    [BepInPlugin(Guid, "BUG_Reclamation", "1.0.1")]
    [BepInDependency(CommonAPIPlugin.GUID)]
    [BepInDependency("me.xiaoye97.plugin.Dyson.LDBTool")]
    [CommonAPISubmoduleDependency(nameof(ProtoRegistry))]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bug.dsp.trashstation";
        // Ids free in vanilla, GenesisBook, MoreMegaStructure, They Come From Void and the BUG mods (checked 2026-11-14).
        // Models stay below 728: without GenesisBook the game sizes its model tables to the model count + 64.
        public const int ConverterItem = 9701, IncineratorItem = 9702, PointsItem = 9703, FirstOutputRecipe = 9704, TechId = 1964;
        public const int HeatItem = 9704;  // an item; recipes 9704+ are a separate table
        public const int LegacyModel = 700, IncineratorModel = 701, ConverterModel = 702;
        public const int AssemblerItem = 2303, PlantItem = 2204;
        // The converter's own recipe type: no other building runs its recipes, and the picker lists only them for it.
        public const ERecipeType RecipeType = (ERecipeType)97;
        // CommonAPI reads the product's cell when a recipe is given cell 0 and a name, and fails; Place sets the real one. Below 1101 the replicator hides it.
        const int Unplaced = 1;
        public const short FuelMask = 32;  // no fuel uses this bit (vanilla and GenesisBook use 1-16), so only points fit
        internal static ManualLogSource Log;
        static ItemProto converter, incinerator, points, heat;
        static RecipeProto converterRecipe, incineratorRecipe;
        static readonly List<RecipeProto> outputRecipes = new List<RecipeProto>();
        static ModelProto converterModel, incineratorModel;
        static bool ready;
        static string dir;  // the mod's folder: the model files sit beside the dll

        // Points per item id; 0 = not taken (the outputs). Filled once all mods have loaded.
        internal static int[] Points = new int[0];
        // Power one point gives in an incinerator: 100 kJ of fuel times the plant's efficiency, read from the building.
        internal static double PowerPerPoint = Value.JoulesPerPoint;
        // Full load: five inputs, each fed until 1000 points wait, per minute. Fuel burned and power made per tick follow.
        internal static int MaxPointsPerMinute = 5000;
        internal static long UseFuelPerTick, GenEnergyPerTick;
        // Incinerator: fuel energy per item in J (a fuel's own heat value, else by its points) and the power level it burns at.
        internal static long[] Heat = new long[0];
        internal static byte[] Levels = new byte[0];
        internal static long HeatOf(int item) => item > 0 && item < Heat.Length ? Heat[item] : 0;
        internal static int LevelOf(int item) => item > 0 && item < Levels.Length ? Levels[item] : 0;
        internal static int PointsOf(int item) => item > 0 && item < Points.Length ? Points[item] : 0;
        internal static IEnumerable<RecipeProto> OutputRecipes => outputRecipes;
        internal static bool IsOutputRecipe(int recipeId) => recipeId >= FirstOutputRecipe && recipeId < FirstOutputRecipe + Value.Outputs.Length;

        void Awake()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Log = Logger;
            dir = System.IO.Path.GetDirectoryName(Info.Location);
            Lang.Current = Config.Bind("General", "Language", Language.English,
                "Language of the mod's own texts: English, Chinese or Turkish (applies after restarting the game).").Value;
            const string box = "Converter window";
            Trash.ListX = Config.Bind(box, "ListX", 170, new ConfigDescription(
                "Burned list: pixels from the input slot's left edge to the list's left edge.", new AcceptableValueRange<int>(60, 400))).Value;
            Trash.ListUp = Config.Bind(box, "ListUp", 25, new ConfigDescription(
                "Burned list: pixels its top sits above the input slot's top (negative = lower).", new AcceptableValueRange<int>(-30, 30))).Value;
            Trash.ListWidth = Config.Bind(box, "ListWidth", 525, new ConfigDescription(
                "Burned list: width in pixels.", new AcceptableValueRange<int>(300, 700))).Value;
            Trash.ListHeight = Config.Bind(box, "ListHeight", 147, new ConfigDescription(
                "Burned list: height in pixels.", new AcceptableValueRange<int>(90, 200))).Value;
            MaxPointsPerMinute = Config.Bind("Incinerator", "MaxPointsPerMinute", 5000, new ConfigDescription(
                "Points an incinerator burns per minute at full load (each is 100 kJ of fuel, 80 kJ of power): 5000 is 6.67 MW. The thermal plant it copies burns 1620 (2.16 MW). Applies to built incinerators too, after restarting the game.",
                new AcceptableValueRange<int>(1620, 100000))).Value;
            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin));
            harmony.PatchAll(typeof(Trash));
            harmony.PatchAll(typeof(Picker));
            using (ProtoRegistry.StartModLoad(Guid))
            {
                // Cells and build keys are picked once every mod's items are known (Place). Wreck ids are the thermal plant's
                // and the storage's: the wreck prefab is read while the models load, before the copied buildings can be asked.
                converter = ProtoRegistry.RegisterItem(ConverterItem, Lang.T("Trash Converter"), Lang.T(ConverterDesc), "Icons/ItemRecipe/assembler-1", 0, 50, EItemType.Production);
                converterModel = ProtoRegistry.RegisterModel(ConverterModel, converter, "Entities/Prefabs/assembler-mk-1", null, new[] { 22, 11, 12, 1, 40 }, 0);
                Ruins(converterModel, 388, 4, 30000);
                // Saves from the first test build hold the old storage-based station: it stays a plain storage.
                Ruins(ProtoRegistry.RegisterModel(LegacyModel, "Entities/Prefabs/storage-1"), 390, 4, 24000);
                incinerator = ProtoRegistry.RegisterItem(IncineratorItem, Lang.T("Incinerator"), Lang.T(IncineratorDesc), "Icons/ItemRecipe/fuel-plant", 0, 50, EItemType.Production);
                incineratorModel = ProtoRegistry.RegisterModel(IncineratorModel, incinerator, "Entities/Prefabs/power-generator", null, new[] { 53, 4, 5, 6, 1, 40 }, 0);
                Ruins(incineratorModel, 388, 1, 27000);
                // A converter or incinerator taken down gives its points back as items: one stack holds them all.
                points = ProtoRegistry.RegisterItem(PointsItem, Lang.T("Points"), Lang.T(PointsDesc), "Icons/ItemRecipe/accumulator-full", 0, 10_000_000, EItemType.Material);
                // What waits inside an incinerator, in 100 kJ units; its own item so that it cannot become converter points.
                heat = ProtoRegistry.RegisterItem(HeatItem, Lang.T("Heat"), Lang.T(HeatDesc), "Icons/ItemRecipe/accumulator-full", 0, 10_000_000, EItemType.Material);
                heat.FuelType = FuelMask;
                heat.HeatValue = Value.JoulesPerPoint;
                converterRecipe = ProtoRegistry.RegisterRecipe(ConverterItem, ERecipeType.Assemble, 180, new[] { AssemblerItem, 1103, 1301 }, new[] { 1, 8, 4 },
                    new[] { ConverterItem }, new[] { 1 }, "", 0, Unplaced, Lang.T("Trash Converter"), "Icons/ItemRecipe/assembler-1");
                incineratorRecipe = ProtoRegistry.RegisterRecipe(IncineratorItem, ERecipeType.Assemble, 180, new[] { PlantItem, 1103, 1301 }, new[] { 1, 8, 4 },
                    new[] { IncineratorItem }, new[] { 1 }, "", 0, Unplaced, Lang.T("Incinerator"), "Icons/ItemRecipe/fuel-plant");
                // Points -> one output each; name and icon become the output's once the output is final (Adopt). No replicator
                // cell: the converter's recipe picker lists them itself (Picker).
                for (int i = 0; i < Value.Outputs.Length; i++)
                {
                    int output = Value.Outputs[i];
                    var r = ProtoRegistry.RegisterRecipe(FirstOutputRecipe + i, RecipeType, 60, new[] { PointsItem }, new[] { Value.Price(output) },
                        new[] { output }, new[] { 1 }, "", 0, Unplaced, Lang.T("Trash Converter"), "Icons/ItemRecipe/assembler-1");
                    r.Handcraft = false;
                    outputRecipes.Add(r);
                }
            }
            Log.LogInfo("Registered in " + clock.ElapsedMilliseconds + " ms");
        }

        public const string ConverterDesc = "Burns any item that sorters bring, ammunition and buildings included, and turns its points into the Dark Fog item its recipe makes. Every item is worth points by its raw materials, from the simplest to the rarest; the points gather in the converter and each time they reach the recipe's price one item is made. The items it makes are never taken in.";
        public const string IncineratorDesc = "Burns any item that sorters bring, ammunition and buildings included, for power. Fuels burn as they do in a power plant; anything else gives energy by its rarity, little for simple items, and the rarest also burn at a higher power. The items the trash converter makes and ocean liquids are never taken in.";
        public const string PointsDesc = "Points of burned items, waiting inside a trash converter.";
        public const string HeatDesc = "Heat of burned items, waiting inside an incinerator: each is 100 kJ of fuel. Only an incinerator burns it.";

        // The copied building's wreck and toughness; set before the models load, the wreck prefab is read then.
        static void Ruins(ModelProto m, int ruin, int symmetry, int hp)
        {
            m.ObjectType = EObjectType.Entity;
            m.RuinType = ERuinType.Normal;
            m.RuinId = ruin;
            m.RuinCount = 1;
            m.RotSymmetry = symmetry;
            m.HpMax = hp;
        }

        // Last of all, like BUG_OreEnrichment: content mods finish their items, recipes and techs in their own load steps.
        [HarmonyPostfix, HarmonyPatch(typeof(VFPreload), "InvokeOnLoadWorkEnded")]
        [HarmonyAfter("me.xiaoye97.plugin.Dyson.LDBTool", "dsp.common-api.CommonAPI", "org.LoShin.GenesisBook", "bug.dsp.oreenrichment",
            "bug.dsp.deliveryitemchain", "bug.dsp.dronecoverage", "Gnimaerd.DSP.plugin.MoreMegaStructure", "com.ckcz123.DSP_Battle",
            "org.kremnev8.plugin.RebindBuildBar")]
        [HarmonyPriority(Priority.Last)]  // Rebind Build Bar applies its saved bar at priority 200; the bar is placed after it
        static void Setup()
        {
            if (ready) return;
            ready = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { Adopt(); }
            catch (Exception e) { Log.LogError("Buildings keep CommonAPI's defaults: " + e); }
            try { Ingredients(converterRecipe); Ingredients(incineratorRecipe); }
            catch (Exception e) { Log.LogWarning("Recipe inputs not checked: " + e.Message); }
            try { Place(); }
            catch (Exception e) { Log.LogError("Buildings not placed in the replicator or build bar: " + e); }
            try { BuildTech(); }
            catch (Exception e) { Log.LogError("Research not added, the buildings cannot be unlocked: " + e); }
            try { BuildPoints(); }
            catch (Exception e) { Log.LogError("Item points not computed, nothing is taken in: " + e); Points = new int[0]; Heat = new long[0]; }
            try
            {
                Icons.Apply(converter, converterRecipe, System.IO.Path.Combine(dir, "converter_icon.png"));
                Icons.Apply(incinerator, incineratorRecipe, System.IO.Path.Combine(dir, "incinerator_icon.png"));
                var tech = LDB.techs.Select(TechId);
                if (tech != null) Icons.Apply(tech, converter);
            }
            catch (Exception e) { Log.LogWarning("Buildings keep the copied icons: " + e.Message); }
            Log.LogInfo("Set up in " + clock.ElapsedMilliseconds + " ms");
        }

        // Toughness and tip lines as the copied buildings have them in this game (content mods change them), the converter's
        // recipe type, the incinerator's fuel, and each output recipe's name and icon.
        static void Adopt()
        {
            void Copy(ItemProto mine, ModelProto myModel, int source)
            {
                var item = LDB.items.Select(source);
                var model = item != null ? LDB.models.Select(item.ModelIndex) : null;
                if (item == null || model == null) return;
                mine.HpMax = item.HpMax;
                if (item.DescFields != null) mine.DescFields = (int[])item.DescFields.Clone();
                myModel.HpMax = model.HpMax;
                myModel.HpUpgrade = model.HpUpgrade;
                myModel.HpRecover = model.HpRecover;
            }
            Copy(converter, converterModel, AssemblerItem);
            Copy(incinerator, incineratorModel, PlantItem);
            var asm = converterModel.prefabDesc;
            if (asm == null || !asm.isAssembler) throw new InvalidOperationException("the copied assembler has no assembler");
            asm.assemblerRecipeType = RecipeType;
            if (converter.prefabDesc != null) converter.prefabDesc.assemblerRecipeType = RecipeType;
            var gen = incineratorModel.prefabDesc;
            if (gen == null || !gen.isPowerGen) throw new InvalidOperationException("the copied thermal plant has no generator");
            gen.fuelMask = FuelMask;
            // Full load burns MaxPointsPerMinute at the copied plant's efficiency (a thermal plant's is 80%).
            double efficiency = gen.useFuelPerTick > 0 ? (double)gen.genEnergyPerTick / gen.useFuelPerTick : 0.8;
            UseFuelPerTick = (long)(MaxPointsPerMinute * (double)Value.JoulesPerPoint / 3600);
            GenEnergyPerTick = (long)(UseFuelPerTick * efficiency);
            PowerPerPoint = Value.JoulesPerPoint * efficiency;
            foreach (var d in new[] { gen, incinerator.prefabDesc })
            {
                if (d == null) continue;
                d.fuelMask = FuelMask;
                d.useFuelPerTick = UseFuelPerTick;
                d.genEnergyPerTick = GenEnergyPerTick;
            }
            ItemProto.InitFuelNeeds();
            // Our own looks on the copied buildings; drawn with the assembler's material. A missing or broken file leaves the copy's look.
            var template = LDB.items.Select(AssemblerItem)?.prefabDesc?.lodMaterials?[0]?[0];
            void Look(PrefabDesc desc, string file)
            {
                try { if (template != null) Model.Apply(desc, System.IO.Path.Combine(dir, file), template); }
                catch (Exception e) { Log.LogWarning("Model " + file + " not used: " + e.Message); }
            }
            Look(asm, "converter.tsm");
            Look(gen, "incinerator.tsm");
            foreach (var r in outputRecipes)
            {
                var item = LDB.items.Select(r.Results[0]);
                if (item == null) continue;
                r.Name = item.Name;
                r.name = item.name;
                r.IconPath = item.IconPath;
                Traverse.Create(r).Field("_iconSprite").SetValue(item.iconSprite);
            }
        }

        // A content mod can remove an input from the game (GenesisBook has no gears): an input no recipe makes and no vein
        // gives is dropped, so the building stays craftable. The copied building itself always stays.
        static void Ingredients(RecipeProto recipe)
        {
            var made = new HashSet<int>();
            foreach (var r in LDB.recipes.dataArray) if (r?.Results != null && r != converterRecipe && r != incineratorRecipe) made.UnionWith(r.Results);
            var keep = new List<int>();
            for (int i = 0; i < recipe.Items.Length; i++)
            {
                var item = LDB.items.Select(recipe.Items[i]);
                if (i == 0 || item != null && (made.Contains(item.ID) || item.Type == EItemType.Resource)) keep.Add(i);
                else Log.LogWarning(recipe.name + ": input " + recipe.Items[i] + " is not made in this game, left out");
            }
            if (keep.Count == recipe.Items.Length) return;
            recipe.Items = keep.Select(i => recipe.Items[i]).ToArray();
            recipe.ItemCounts = keep.Select(i => recipe.ItemCounts[i]).ToArray();
        }

        // Free replicator cells next to the copied buildings and free build-bar keys in their rows, among what every mod uses.
        // The bar itself may already be loaded and rearranged (Rebind Build Bar keeps the player's layout and empties the
        // keys it saved as unused): a key counts as taken when the loaded bar has something there, and a building the
        // player already put on the bar stays where it is. BuildIndex is the default the bar's reset goes back to.
        static void Place()
        {
            var t = Traverse.Create(typeof(UIBuildMenu));
            var protos = t.Field<bool>("staticLoaded").Value ? t.Field<ItemProto[,]>("protos").Value : null;
            var mine = new HashSet<Proto>(outputRecipes) { converter, incinerator, converterRecipe, incineratorRecipe };
            var cells = new HashSet<int>();
            foreach (var i in LDB.items.dataArray) if (i != null && !mine.Contains(i)) cells.Add(i.GridIndex);
            foreach (var r in LDB.recipes.dataArray) if (r != null && !mine.Contains(r)) cells.Add(r.GridIndex);
            var keys = new HashSet<int>();
            foreach (var i in LDB.items.dataArray) if (i != null && !mine.Contains(i)) keys.Add(i.BuildIndex);
            var shown = new HashSet<ItemProto>();
            if (protos != null)
                for (int r = 0; r < protos.GetLength(0); r++)
                    for (int c = 0; c < protos.GetLength(1); c++)
                        if (protos[r, c] != null) { keys.Add(r * 100 + c); shown.Add(protos[r, c]); }
            int Tab(int source) => Math.Max(1, (LDB.items.Select(source)?.GridIndex ?? 2000) / 1000);
            int Row(int source, int fallback) { int b = LDB.items.Select(source)?.BuildIndex ?? 0; return b > 0 ? b / 100 : fallback; }
            int Take(int tab) { int cell = Value.Cell(cells, tab); cells.Add(cell); return cell; }
            void Put(ItemProto item, RecipeProto recipe, int source, params int[] rows)
            {
                item.GridIndex = recipe.GridIndex = Take(Tab(source));
                int key = Value.Slot(keys, rows);
                keys.Add(key);
                item.BuildIndex = key;
                if (protos != null && key > 0 && !shown.Contains(item)) protos[key / 100, key % 100] = item;
            }
            Put(converter, converterRecipe, AssemblerItem, Row(AssemblerItem, 3));
            Put(incinerator, incineratorRecipe, PlantItem, Row(PlantItem, 1), converter.BuildIndex / 100);  // power row full (GenesisBook): beside the converter
            Log.LogInfo("Trash converter at cell " + converter.GridIndex + " key " + converter.BuildIndex + ", incinerator at cell " + incinerator.GridIndex + " key " + incinerator.BuildIndex);
        }

        // Points of every item, and a line with them in each item's description. The outputs take none: they are never burned.
        // Neither is anything a pump draws from an ocean (water, sulfuric acid, a content mod's seas): free without end.
        static void BuildPoints()
        {
            var recipes = LDB.recipes.dataArray.Where(x => x?.Items != null && x.Results != null && x.ItemCounts != null && x.ResultCounts != null
                    && x.Items.Length == x.ItemCounts.Length && x.Results.Length == x.ResultCounts.Length && x.Type != RecipeType)
                .Select(x => new Value.Recipe { In = x.Items, InN = x.ItemCounts, Out = x.Results, OutN = x.ResultCounts }).ToList();
            var costs = Value.Costs(recipes, Value.FixedCosts);
            var table = new int[LDB.items.dataArray.Max(i => i?.ID ?? 0) + 1];
            var heats = new long[table.Length];
            var levels = new byte[table.Length];
            double efficiency = PowerPerPoint / Value.JoulesPerPoint;
            var oceans = new HashSet<int> { 1000, 1116 };
            foreach (var theme in LDB.themes.dataArray) if (theme != null && theme.WaterItemId > 0) oceans.Add(theme.WaterItemId);
            foreach (var item in LDB.items.dataArray)
            {
                if (item == null || item.ID == PointsItem || item.ID == HeatItem || Array.IndexOf(Value.Outputs, item.ID) >= 0 || oceans.Contains(item.ID)) continue;
                table[item.ID] = Value.Points(costs.TryGetValue(item.ID, out double c) ? c : 0, item.HeatValue);
                bool fuel = item.HeatValue > 0;
                heats[item.ID] = fuel ? item.HeatValue : Value.Heat(table[item.ID]);
                levels[item.ID] = (byte)(fuel ? 0 : Value.Level(table[item.ID]));
                item.description += "\n" + Lang.F("Trash points: {0} ({1} of power in an incinerator)", table[item.ID], Value.Energy(heats[item.ID] * efficiency))
                    + (levels[item.ID] > 0 ? " +" + levels[item.ID] * 25 + "%" : "");
            }
            table[PointsItem] = 1;
            heats[HeatItem] = Value.JoulesPerPoint;
            Points = table;
            Heat = heats;
            Levels = levels;
            Log.LogInfo("Points for " + table.Count(p => p > 0) + " items, from " + recipes.Count + " recipes; prices " +
                string.Join(", ", Value.Outputs.Select(o => o + "=" + Value.Price(o))));
        }

        // One tech on a new row under the main tech page, unlocked with the thermal plant (or whatever opens it on this tree).
        static void BuildTech()
        {
            if (LDB.techs.Exist(TechId)) throw new InvalidOperationException("tech id " + TechId + " is taken by another mod");
            Tech.Layout(LDB.techs.dataArray, out float x0, out float dx, out float y0);
            var tech = Tech.New(TechId, Lang.T("Reclamation"),
                Lang.T("Buildings that burn any item for points: a trash converter that turns them into Dark Fog items, and an incinerator that turns them into power."),
                "Icons/ItemRecipe/fuel-plant", Tech.Unlocking(PlantItem), new[] { ConverterItem, IncineratorItem }, new Vector2(x0, y0));
            Tech.Insert(new[] { tech });
            converterRecipe.preTech = incineratorRecipe.preTech = tech;
            foreach (var r in outputRecipes) r.preTech = tech;
            Log.LogInfo("Tech " + TechId + " added at x " + x0 + " y " + y0);
        }

        // The output recipes open with the tech without being listed on it (its card shows the two buildings only);
        // saves that researched the tech before a recipe joined it get the recipe too.
        [HarmonyPostfix, HarmonyPatch(typeof(GameHistoryData), nameof(GameHistoryData.RecipeUnlocked))]
        static void Unlocked(GameHistoryData __instance, int recipeId, ref bool __result)
        {
            if (!__result && (IsOutputRecipe(recipeId) || recipeId == ConverterItem || recipeId == IncineratorItem)) __result = __instance.TechUnlocked(TechId);
        }

        // The recipe tip names the building that runs it.
        [HarmonyPostfix, HarmonyPatch(typeof(RecipeProto), nameof(RecipeProto.madeFromString), MethodType.Getter)]
        static void MadeFrom(RecipeProto __instance, ref string __result)
        {
            if (__instance.Type == RecipeType && converter != null) __result = converter.name;
        }
    }
}
