using System;
using System.Collections.Concurrent;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TrashStation
{
    // Both buildings take any item but the converter's outputs and turn it into points. The converter is an assembler
    // whose recipes ask for points: an item a sorter brings adds its points to the recipe's input. The incinerator is a
    // thermal plant whose only fuel is points (fuel mask 32): an item adds its points as fuel, 100 kJ each.
    // Sorters ask the target what to pick; for these two any item will do. Prefixes, so other mods' transpilers on these
    // methods (GenesisBook) keep working for every other building.
    static class Trash
    {
        // heat: asked by an incinerator (energy), else by a converter (points).
        static bool Taken(int item, bool heat = false) => heat ? Plugin.HeatOf(item) > 0 : Plugin.PointsOf(item) > 0;

        // The item each building took last, for its window: key factory index, building kind, assembler or generator id. Not saved.
        static readonly ConcurrentDictionary<long, int> lastBurned = new ConcurrentDictionary<long, int>();
        static long Key(PlanetFactory factory, int id, bool generator) => (long)factory.index << 33 | (generator ? 1L << 32 : 0) | (uint)id;

        static bool Converter(PlanetFactory factory, int asmId)
        {
            var pool = factory.factorySystem?.assemblerPool;
            if (pool == null || asmId <= 0 || asmId >= pool.Length || pool[asmId].id != asmId) return false;
            var requires = pool[asmId].recipeExecuteData?.requires;
            return requires != null && requires.Length == 1 && requires[0] == Plugin.PointsItem;
        }

        static bool Incinerator(PlanetFactory factory, int genId)
        {
            var pool = factory.powerSystem?.genPool;
            return pool != null && genId > 0 && genId < pool.Length && pool[genId].id == genId && pool[genId].fuelMask == Plugin.FuelMask;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.InsertInto),
            new[] { typeof(int), typeof(int), typeof(int), typeof(byte), typeof(byte), typeof(byte) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
        static bool InsertByEntity(PlanetFactory __instance, int entityId, int itemId, byte itemCount, ref byte remainInc, ref int __result)
        {
            if (entityId <= 0 || entityId >= __instance.entityPool.Length || __instance.entityPool[entityId].beltId != 0) return true;
            int asmId = __instance.entityPool[entityId].assemblerId, genId = __instance.entityPool[entityId].powerGenId;
            if (Converter(__instance, asmId)) __result = Feed(__instance, asmId, itemId, itemCount, ref remainInc);
            else if (Incinerator(__instance, genId)) __result = Burn(__instance, genId, itemId, itemCount, ref remainInc);
            else return true;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.InsertInto),
            new[] { typeof(uint), typeof(int), typeof(int), typeof(byte), typeof(byte), typeof(byte) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
        static bool InsertByTarget(PlanetFactory __instance, uint ioTargetTypedId, int itemId, byte itemCount, ref byte remainInc, ref int __result)
        {
            var type = (EFactoryIOTargetType)(ioTargetTypedId & (uint)EFactoryIOTargetType.TypeMask);
            int id = (int)(ioTargetTypedId & (uint)EFactoryIOTargetType.IdMask);
            if (type == EFactoryIOTargetType.Assembler && Converter(__instance, id)) __result = Feed(__instance, id, itemId, itemCount, ref remainInc);
            else if (type == EFactoryIOTargetType.PowerGen && Incinerator(__instance, id)) __result = Burn(__instance, id, itemId, itemCount, ref remainInc);
            else return true;
            return false;
        }

        // A sorter feeding a converter asks for points (the recipe's only input): any item that gives points will do.
        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.PickFrom),
            new[] { typeof(uint), typeof(int), typeof(int), typeof(int[]), typeof(byte), typeof(byte) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Out })]
        static bool PickForConverter(PlanetFactory __instance, uint ioTargetTypedId, int offset, int filter, int[] needs, ref byte stack, ref byte inc, ref int __result)
        {
            if (needs == null || needs.Length == 0 || needs[0] != Plugin.PointsItem) return true;
            __result = PickAny(__instance, ioTargetTypedId, offset, filter, false, out stack, out inc);
            return false;
        }

        // A sorter feeding an incinerator asks for its fuel: the same, until enough points wait.
        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.PickFuelForPowerGenFrom))]
        static bool PickForIncinerator(PlanetFactory __instance, uint pickTargetTypedId, int insertPowerGenId, int offset, int filter, ref byte stack, ref byte inc, ref bool fuelFull, ref int __result)
        {
            if (!Incinerator(__instance, insertPowerGenId)) return true;
            stack = 1;
            inc = 0;
            fuelFull = __instance.powerSystem.genPool[insertPowerGenId].fuelCount >= Value.FullPoints;
            __result = fuelFull ? 0 : PickAny(__instance, pickTargetTypedId, offset, filter, true, out stack, out inc);
            return false;
        }

        // The first item that gives points, from a belt, a storage, a station or an assembler's products; the game's own
        // pick then takes it as if the sorter's filter were set to it. Anything else gives nothing.
        static int PickAny(PlanetFactory f, uint typed, int offset, int filter, bool heat, out byte stack, out byte inc)
        {
            stack = 1;
            inc = 0;
            if (filter != 0 && !Taken(filter, heat)) return 0;
            int id = (int)(typed & (uint)EFactoryIOTargetType.IdMask), item = 0;
            if (id <= 0) return 0;
            switch ((EFactoryIOTargetType)(typed & (uint)EFactoryIOTargetType.TypeMask))
            {
                case EFactoryIOTargetType.Belt:
                {
                    var traffic = f.cargoTraffic;
                    if (id >= traffic.beltPool.Length || traffic.beltPool[id].id != id) return 0;
                    var path = traffic.GetCargoPath(traffic.beltPool[id].segPathId);
                    if (path == null) return 0;
                    int index = traffic.beltPool[id].pivotOnPath + offset - 2;  // the window the game's own belt pick looks at
                    lock (path.buffer)
                    {
                        item = OnBelt(path, index, 5, filter, heat);
                        return item == 0 ? 0 : path.TryPickItem(index, 5, item, out stack, out inc);
                    }
                }
                case EFactoryIOTargetType.Storage:
                {
                    var pool = f.factoryStorage?.storagePool;
                    var bottom = pool != null && id < pool.Length ? pool[id] : null;
                    for (var s = bottom?.topStorage; s != null && item == 0; s = s == bottom ? null : s.previousStorage)
                        for (int g = s.grids.Length - 1; g >= 0 && item == 0; g--)
                            if (s.grids[g].count > 0 && Wanted(s.grids[g].itemId, filter, heat)) item = s.grids[g].itemId;
                    break;
                }
                case EFactoryIOTargetType.Station:
                {
                    var pool = f.transport?.stationPool;
                    var store = pool != null && id < pool.Length ? pool[id]?.storage : null;
                    for (int i = 0; store != null && i < store.Length && item == 0; i++)
                        if (store[i].count > 0 && Wanted(store[i].itemId, filter, heat)) item = store[i].itemId;
                    break;
                }
                case EFactoryIOTargetType.Assembler:
                {
                    var pool = f.factorySystem?.assemblerPool;
                    if (pool == null || id >= pool.Length || pool[id].id != id) return 0;
                    var products = pool[id].recipeExecuteData?.products;
                    var produced = pool[id].produced;
                    for (int i = 0; products != null && produced != null && i < products.Length && i < produced.Length && item == 0; i++)
                        if (produced[i] > 0 && Wanted(products[i], filter, heat)) item = products[i];
                    break;
                }
                // A full ejector or silo passes its sails or rockets on, as it does to the next one in a row.
                case EFactoryIOTargetType.Ejector:
                {
                    var pool = f.factorySystem?.ejectorPool;
                    if (pool != null && id < pool.Length && pool[id].id == id && pool[id].bulletCount > 0 && Wanted(pool[id].bulletId, filter, heat)) item = pool[id].bulletId;
                    break;
                }
                case EFactoryIOTargetType.Silo:
                {
                    var pool = f.factorySystem?.siloPool;
                    if (pool != null && id < pool.Length && pool[id].id == id && pool[id].bulletCount > 0 && Wanted(pool[id].bulletId, filter, heat)) item = pool[id].bulletId;
                    break;
                }
                case EFactoryIOTargetType.Lab:
                {
                    var pool = f.factorySystem?.labPool;
                    if (pool == null || id >= pool.Length || pool[id].id != id) return 0;
                    var products = pool[id].recipeExecuteData?.products;
                    var produced = pool[id].produced;
                    for (int i = 0; products != null && produced != null && i < products.Length && i < produced.Length && item == 0; i++)
                        if (produced[i] > 0 && Wanted(products[i], filter, heat)) item = products[i];
                    break;
                }
            }
            return item == 0 ? 0 : f.PickFrom(typed, offset, item, null, out stack, out inc);
        }

        static bool Wanted(int item, int filter, bool heat) => item > 0 && (filter == 0 || item == filter) && Taken(item, heat);

        // The first wanted item in a belt window, read the way CargoPath.TryPickItem reads it.
        static int OnBelt(CargoPath path, int index, int length, int filter, bool heat)
        {
            var buffer = path.buffer;
            int end = Math.Min(Math.Max(index, 0) + length, path.pathLength);
            for (int i = Math.Max(0, Math.Min(index, path.pathLength - 1)); i < end; i++)
            {
                if (buffer[i] < 246) continue;
                i += 250 - buffer[i];
                if (i + 4 >= buffer.Length) break;
                int cargo = buffer[i + 1] - 1 + (buffer[i + 2] - 1) * 100 + (buffer[i + 3] - 1) * 10000 + (buffer[i + 4] - 1) * 1000000;
                int item = path.cargoContainer.cargoPool[cargo].item;
                if (Wanted(item, filter, heat)) return item;
            }
            return 0;
        }

        // Converter: the item's points join the recipe's input. Spray on burned items is lost.
        static int Feed(PlanetFactory factory, int asmId, int itemId, byte count, ref byte remainInc)
        {
            int p = Plugin.PointsOf(itemId);
            if (p <= 0 || count == 0) return 0;
            ref var a = ref factory.factorySystem.assemblerPool[asmId];
            lock (factory.entityMutexs[a.entityId])
                a.served[0] = (int)Math.Min(int.MaxValue, (long)a.served[0] + (long)p * count);
            remainInc = 0;
            lastBurned[Key(factory, asmId, false)] = itemId;
            Ledger.Add(Key(factory, asmId, false), itemId, count, p);
            Burned(factory, itemId, count);
            return count;
        }

        // Incinerator: the item's energy joins the fuel, counted in 100 kJ units of heat; what does not fill a unit, and
        // what is past the generator's short counter, goes straight into its energy. The item's power level joins the
        // units as spray does on a real fuel, so the game burns them at that power.
        static int Burn(PlanetFactory factory, int genId, int itemId, byte count, ref byte remainInc, bool byHand = false)
        {
            long e = Plugin.HeatOf(itemId);
            if (e <= 0 || count == 0) return 0;
            ref var g = ref factory.powerSystem.genPool[genId];
            lock (factory.entityMutexs[g.entityId])
            {
                if (!byHand && g.fuelCount >= Value.FullPoints) return 0;
                if (g.fuelId == Plugin.PointsItem) g.fuelId = Plugin.HeatItem;  // a save from before heat was its own item
                if (g.fuelId != Plugin.HeatItem || g.fuelCount <= 0)
                {
                    g.fuelId = Plugin.HeatItem;
                    g.fuelCount = 0;
                    g.fuelInc = 0;
                    g.fuelHeat = Value.JoulesPerPoint;
                }
                long joules = e * count, all = g.fuelCount + joules / Value.JoulesPerPoint;
                int before = g.fuelCount;
                g.fuelCount = (short)Math.Min(short.MaxValue, all);
                g.fuelEnergy += (all - g.fuelCount) * Value.JoulesPerPoint + joules % Value.JoulesPerPoint;
                g.fuelInc = (short)Math.Min(short.MaxValue, g.fuelInc + (long)Plugin.LevelOf(itemId) * (g.fuelCount - before));
            }
            lastBurned[Key(factory, genId, true)] = itemId;
            Ledger.Add(Key(factory, genId, true), itemId, count, e);
            remainInc = 0;
            Burned(factory, itemId, count);
            return count;
        }

        // Burned items show as consumed in the statistics; points moved as items are not burned.
        static void Burned(PlanetFactory factory, int itemId, int count)
        {
            if (itemId == Plugin.PointsItem || itemId == Plugin.HeatItem) return;
            var stats = GameMain.statistics?.production?.factoryStatPool;
            if (stats != null && factory.index < stats.Length && stats[factory.index] != null)
                Interlocked.Add(ref stats[factory.index].consumeRegister[itemId], count);
        }

        // One short line under the fuel rate: the power still waiting in the plant and how long it lasts at full load.
        static Text stockText;

        static void Stock(UIPowerGeneratorWindow window, in PowerGeneratorComponent g)
        {
            if (stockText == null)
            {
                stockText = UnityEngine.Object.Instantiate(window.fuelText2, window.fuelText2.transform.parent.parent);
                stockText.name = "trash-stock-text";
                stockText.alignment = TextAnchor.MiddleLeft;
                stockText.horizontalOverflow = HorizontalWrapMode.Overflow;
                stockText.raycastTarget = false;
                var rt = stockText.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(180f, 20f);
                rt.anchoredPosition = new Vector2(-40f, -88f);  // BUG_GeneratorFuelRate's line sits at -70
            }
            if (!stockText.gameObject.activeSelf) stockText.gameObject.SetActive(true);
            double fuel = (double)g.fuelCount * Value.JoulesPerPoint + Math.Max(0, g.fuelEnergy);
            double seconds = g.useFuelPerTick > 0 ? fuel / (g.useFuelPerTick * 60.0) : 0;
            stockText.text = Lang.F("Stock {0} ≈ {1}", Value.Energy(fuel * Power(g) / Value.JoulesPerPoint), Span(seconds));
        }

        static string Span(double seconds) =>
            seconds < 100 ? Lang.F("{0} s", Math.Round(seconds)) : seconds < 6000 ? Lang.F("{0} min", Math.Round(seconds / 60, 1)) : Lang.F("{0} h", Math.Round(seconds / 3600, 1));

        // The fuel slot shows the item burned last and its points each; the count stays the points waiting.
        [HarmonyPostfix, HarmonyPatch(typeof(UIPowerGeneratorWindow), "_OnUpdate")]
        static void Window(UIPowerGeneratorWindow __instance)
        {
            var factory = __instance.factory;
            int genId = __instance.generatorId;
            if (factory == null || !Incinerator(factory, genId))
            {
                incineratorPanel.Hide();
                if (stockText != null && stockText.gameObject.activeSelf) stockText.gameObject.SetActive(false);
                return;
            }
            Stock(__instance, factory.powerSystem.genPool[genId]);
            var icon = __instance.fuelIcon2.rectTransform;
            var corners = new Vector3[4];
            icon.GetWorldCorners(corners);
            // Right of the fuel slot, centred on it.
            var middle = (corners[2] + corners[3]) / 2;
            incineratorPanel.Show((RectTransform)__instance.transform, __instance.fuelText2.font, Key(factory, genId, true), middle + icon.TransformVector(new Vector3(18, 13, 0)));
            if (factory.powerSystem.genPool[genId].fuelCount <= 0) return;
            var item = lastBurned.TryGetValue(Key(factory, genId, true), out int id) ? LDB.items.Select(id) : null;
            if (item == null) return;
            __instance.fuelIcon2.sprite = item.iconSprite;
            __instance.fuelIcon2.enabled = item.iconSprite != null;
            ref var g = ref factory.powerSystem.genPool[genId];
            int level = Plugin.LevelOf(id);
            __instance.fuelText2.text = item.name + " · " + Value.Energy(Plugin.HeatOf(id) * Power(g) / Value.JoulesPerPoint) + (level > 0 ? " · +" + level * 25 + "%" : "");
        }

        // The converter's input slot shows the item taken last; only the picture changes, the slot still holds points, so
        // taking from it by hand gives points.
        [HarmonyPostfix, HarmonyPatch(typeof(UIAssemblerWindow), nameof(UIAssemblerWindow.SyncServingStorage), new[] { typeof(AssemblerComponent) }, new[] { ArgumentType.Ref })]
        static void Serving(UIAssemblerWindow __instance, ref AssemblerComponent assembler)
        {
            var factory = __instance.factory;
            var box = __instance.servingBox;
            if (factory == null || box == null || !Converter(factory, assembler.id) || assembler.served[0] <= 0) return;
            if (!lastBurned.TryGetValue(Key(factory, assembler.id, false), out int id) || id <= 0 || id >= GameMain.iconSet.itemIconIndex.Length) return;
            var t = Traverse.Create(box);
            var icons = t.Field<uint[]>("iconIndexArray").Value;
            var buffer = t.Field<UnityEngine.ComputeBuffer>("iconIndexBuffer").Value;
            if (icons == null || buffer == null) return;
            icons[0] = GameMain.iconSet.itemIconIndex[id] | icons[0] & 0xFF000000u;  // keep the spray arrows
            buffer.SetData(icons);
        }

        // The converter's input: the points it holds under the slot and the points still missing for the next product above
        // it, the same size, short and centred, with the "Burned" list button beside the slot. They hang on the window, not
        // on the slot grid. The grid's own count and Assembler UI's label for this input are hidden meanwhile; every other
        // assembler gets the window back as it was.
        const int NumberSize = 16;
        static Text leftText, countText, gridText;
        static GameObject hiddenLabel;
        static bool searched;
        // The converter's burned list: pixels right of the input slot's left edge and above its top, and its size.
        // Set from the config file before the first window opens.
        internal static int ListX = 170, ListUp = 25, ListWidth = 525, ListHeight = 147;
        static readonly BurnPanel converterPanel = new BurnPanel(false, new Vector2(ListWidth, ListHeight)), incineratorPanel = new BurnPanel(true, new Vector2(300, 0));

        [HarmonyPostfix, HarmonyPatch(typeof(UIAssemblerWindow), "_OnUpdate"), HarmonyAfter("dsp.assemblerUI.speedMod")]
        static void Numbers(UIAssemblerWindow __instance)
        {
            var factory = __instance.factory;
            int id = __instance.assemblerId;
            var grid = __instance.servingBox;
            var texts = grid != null ? Traverse.Create(grid).Field<Text[]>("numTexts").Value : null;
            var template = texts != null && texts.Length > 0 ? texts[0] : null;
            var gridRect = grid != null ? grid.GetComponent<RectTransform>() : null;
            if (factory == null || template == null || gridRect == null || !Converter(factory, id)) { Unstyle(); return; }
            ref var a = ref factory.factorySystem.assemblerPool[id];
            long held = Math.Max(0, a.served[0]), price = a.recipeExecuteData.requireCounts[0];

            if (gridText != template) { if (gridText != null) gridText.enabled = true; gridText = template; }
            template.enabled = false;
            countText = countText ?? Make(template.font, __instance.windowTrans, "trash-points-count");
            leftText = leftText ?? Make(template.font, __instance.windowTrans, "trash-points-left");
            // Cell 0 is the 50 x 50 square 2 px in from the grid's top left corner.
            var corners = new Vector3[4];
            gridRect.GetWorldCorners(corners);
            var topLeft = corners[1];
            var down = gridRect.TransformVector(new Vector3(0, -1, 0));
            var right = gridRect.TransformVector(new Vector3(1, 0, 0));
            var color = held >= price ? Color.white : grid.numLackColor;
            color.a = 1;
            Place(countText, Value.Short(held), topLeft + right * 27 + down * 54, new Vector2(0.5f, 1), TextAnchor.UpperCenter, color);
            Place(leftText, Lang.F("{0} to go", Value.Short(Math.Max(0, price - held))), topLeft + right * 27 - down * 2, new Vector2(0.5f, 0), TextAnchor.LowerCenter, Color.white);
            converterPanel.Size(new Vector2(ListWidth, ListHeight));
            converterPanel.Show(__instance.windowTrans, template.font, Key(factory, id, false), topLeft + right * 62 + down * 14,
                topLeft + right * ListX - down * ListUp);  // the empty middle of the window's top half

            if (!searched)
            {
                searched = true;
                foreach (var t in __instance.windowTrans.GetComponentsInChildren<Transform>())
                    if (t.name == "assembler-speed-in-item0") { hiddenLabel = t.gameObject; break; }
            }
            if (hiddenLabel != null && hiddenLabel.activeSelf) hiddenLabel.SetActive(false);
        }

        // New texts, not copies of the grid's: copies of its number text stayed invisible on the window.
        static Text Make(Font font, Transform parent, string name)
        {
            var t = BurnPanel.Label(parent, font, NumberSize, TextAnchor.MiddleCenter, Color.white);
            t.name = name;
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(160, NumberSize + 6);
            return t;
        }

        static void Place(Text text, string value, Vector3 world, Vector2 pivot, TextAnchor align, Color color)
        {
            if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);
            text.rectTransform.pivot = pivot;
            text.rectTransform.position = world;
            text.alignment = align;
            text.color = color;
            text.text = value;
        }

        static void Unstyle()
        {
            if (leftText != null && leftText.gameObject.activeSelf) leftText.gameObject.SetActive(false);
            if (countText != null && countText.gameObject.activeSelf) countText.gameObject.SetActive(false);
            if (gridText != null) { gridText.enabled = true; gridText = null; }
            if (hiddenLabel != null) { if (!hiddenLabel.activeSelf) hiddenLabel.SetActive(true); hiddenLabel = null; }
            searched = false;
            converterPanel.Hide();
        }

        // Power one point gives in this plant: its fuel energy times the plant's efficiency (a thermal plant's is 80%).
        static double Power(in PowerGeneratorComponent g) =>
            g.useFuelPerTick > 0 ? Value.JoulesPerPoint * (double)g.genEnergyPerTick / g.useFuelPerTick : Value.JoulesPerPoint;

        // Points past the generator's counter sit in its energy (a big burn by hand): taking the incinerator down gives
        // those back too, as whole points; the game itself returns the counted ones.
        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.TakeBackItemsInEntity))]
        static void Refund(PlanetFactory __instance, Player player, int entityId)
        {
            if (player == null || entityId <= 0 || entityId >= __instance.entityPool.Length) return;
            int genId = __instance.entityPool[entityId].powerGenId;
            if (!Incinerator(__instance, genId)) return;
            ref var g = ref __instance.powerSystem.genPool[genId];
            int extra = (int)Math.Min(int.MaxValue, g.fuelEnergy / Value.JoulesPerPoint);
            if (extra <= 0) return;
            g.fuelEnergy -= extra * Value.JoulesPerPoint;
            UIItemup.Up(Plugin.HeatItem, player.TryAddItemToPackage(Plugin.HeatItem, extra, 0, true, entityId));
        }

        // A building taken down takes its list along: the next one built gets the same id.
        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemoveEntityWithComponents))]
        static void Removed(PlanetFactory __instance, int id)
        {
            if (id <= 0 || id >= __instance.entityPool.Length) return;
            int asmId = __instance.entityPool[id].assemblerId, genId = __instance.entityPool[id].powerGenId;
            if (asmId > 0) Forget(Key(__instance, asmId, false));
            if (genId > 0) Forget(Key(__instance, genId, true));
        }

        static void Forget(long key)
        {
            lastBurned.TryRemove(key, out _);
            Ledger.Remove(key);
        }

        // A new game or save starts with empty lists.
        [HarmonyPostfix, HarmonyPatch(typeof(GameMain), nameof(GameMain.Begin))]
        static void Fresh()
        {
            lastBurned.Clear();
            Ledger.Clear();
            // Each generator keeps its own power in the save: incinerators built at another setting get the current one.
            var data = GameMain.data;
            if (data?.factories == null || Plugin.UseFuelPerTick <= 0) return;
            for (int f = 0; f < data.factoryCount; f++)
            {
                var power = data.factories[f]?.powerSystem;
                if (power?.genPool == null) continue;
                for (int i = 1; i < power.genCursor; i++)
                    if (power.genPool[i].id == i && power.genPool[i].fuelMask == Plugin.FuelMask)
                    {
                        if (power.genPool[i].fuelId == Plugin.PointsItem) power.genPool[i].fuelId = Plugin.HeatItem;
                        if (power.genPool[i].curFuelId == Plugin.PointsItem) power.genPool[i].curFuelId = Plugin.HeatItem;
                        power.genPool[i].useFuelPerTick = Plugin.UseFuelPerTick;
                        power.genPool[i].genEnergyPerTick = Plugin.GenEnergyPerTick;
                    }
            }
        }

        // By hand into the incinerator: what the player holds goes in until the plant is full; an empty hand takes nothing out.
        [HarmonyPrefix, HarmonyPatch(typeof(UIPowerGeneratorWindow), nameof(UIPowerGeneratorWindow.OnFuelButtonClick))]
        static bool Hand(UIPowerGeneratorWindow __instance)
        {
            var player = __instance.player;
            if (__instance.factory == null || player == null || !Incinerator(__instance.factory, __instance.generatorId)) return true;
            int id = player.inhandItemId, left = player.inhandItemCount;
            if (id <= 0 || left <= 0) return false;
            if (!Taken(id, true)) { UIRealtimeTip.Popup(Lang.T("This item cannot be burned")); return false; }
            // As many as fit below the stock sorters stop at; the rest stays in the hand.
            long room = (Value.FullPoints - __instance.factory.powerSystem.genPool[__instance.generatorId].fuelCount) * Value.JoulesPerPoint;
            int burn = Math.Min(left, Fits(room, Plugin.HeatOf(id)));
            if (burn <= 0) { UIRealtimeTip.Popup(Lang.T("The building is full")); return false; }
            int rest = left - burn;
            byte inc = 0;
            while (burn > 0 && Burn(__instance.factory, __instance.generatorId, id, (byte)Math.Min(burn, 255), ref inc, true) > 0) burn -= Math.Min(burn, 255);
            Spent(player, rest + burn);
            return false;
        }

        // The tip over either building shows what its window shows: the item burned last in place of the heat or points it became.
        [HarmonyPostfix, HarmonyPatch(typeof(EntityBriefInfo), nameof(EntityBriefInfo.SetBriefInfo))]
        static void Tip(EntityBriefInfo __instance, PlanetFactory _factory, int _entityId)
        {
            if (_factory == null || _entityId <= 0 || _entityId >= _factory.entityPool.Length) return;
            int genId = _factory.entityPool[_entityId].powerGenId, asmId = _factory.entityPool[_entityId].assemblerId;
            bool gen = Incinerator(_factory, genId);
            if (!gen && !Converter(_factory, asmId)) return;
            int held = gen ? Plugin.HeatItem : Plugin.PointsItem;  // the converter's tip likewise, for its points
            if (!lastBurned.TryGetValue(Key(_factory, gen ? genId : asmId, gen), out int id) || id <= 0 || id == held) return;
            var burning = __instance.resource?.items;
            if (burning != null && burning.TryGetValue(held, out int n)) { burning.Remove(held); burning[id] = n; }
            var stock = __instance.storage?.items;
            if (stock != null && stock.TryGetValue(held, out var c)) { stock.Remove(held); stock[id] = c; }
        }

        // How many items worth `each` it takes to fill `room`: the one that crosses the line still goes in, as a sorter's does.
        static int Fits(long room, long each) => room <= 0 || each <= 0 ? 0 : (int)Math.Min(int.MaxValue, (room + each - 1) / each);

        static void Spent(Player player, int left)
        {
            player.SetHandItemCount_Unsafe(left);
            if (left <= 0) { player.SetHandItemId_Unsafe(0); player.SetHandItemCount_Unsafe(0); player.SetHandItemInc_Unsafe(0); }
            VFAudio.Create("transfer-item", null, Vector3.zero, true, 0, -1, -1L);
        }

        // By hand into the converter, as a sorter would: any item with points put on its input slot is burned for them
        // (a right click burns one). Points themselves and items without points go the game's own way.
        static bool FeedHand(Player player, int count)
        {
            var window = UIRoot.instance?.uiGame?.assemblerWindow;
            if (window == null || !window.active || window.factory == null || player == null || !Converter(window.factory, window.assemblerId)) return false;
            int id = player.inhandItemId, held = player.inhandItemCount;
            if (id <= 0 || held <= 0 || id == Plugin.PointsItem) return false;
            if (!Taken(id)) { UIRealtimeTip.Popup(Lang.T("This item cannot be burned")); return true; }
            // As many as fit below the stock the converter asks sorters for (AssemblerComponent.UpdateNeeds); the rest stays.
            ref var a = ref window.factory.factorySystem.assemblerPool[window.assemblerId];
            int times = Math.Max(2, a.speedOverride * 180 / Math.Max(1, a.recipeExecuteData.timeSpend) + 1);
            long room = (long)a.recipeExecuteData.requireCounts[0] * times - a.served[0];
            int burn = Math.Min(count <= 0 || count > held ? held : count, Fits(room, Plugin.PointsOf(id))), left = burn;
            if (burn <= 0) { UIRealtimeTip.Popup(Lang.T("The building is full")); return true; }
            byte inc = 0;
            while (left > 0 && Feed(window.factory, window.assemblerId, id, (byte)Math.Min(left, 255), ref inc) > 0) left -= Math.Min(left, 255);
            Spent(player, held - burn + left);
            window.SyncServingStorage();
            return true;
        }

        // The fill key (Tab) on either building: one item per call, the one in hand or else the last one in the inventory
        // that gives points, while the building has room as it has for sorters. The game calls this every frame the
        // key is held, so holding it keeps feeding.
        static readonly ItemBundle filled = new ItemBundle();

        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.EntityFastFillIn))]
        static bool Fill(PlanetFactory __instance, int entityId, bool fromPackage, ref ItemBundle itemBundle)
        {
            if (entityId <= 0 || entityId >= __instance.entityPool.Length || __instance.entityPool[entityId].id != entityId) return true;
            int asmId = __instance.entityPool[entityId].assemblerId, genId = __instance.entityPool[entityId].powerGenId;
            bool converter = Converter(__instance, asmId);
            if (!converter && !Incinerator(__instance, genId)) return true;
            filled.Clear();
            itemBundle = filled;
            var player = __instance.gameData?.mainPlayer;
            if (player == null) return false;
            bool room = converter ? __instance.factorySystem.assemblerPool[asmId].needs[0] == Plugin.PointsItem
                : __instance.powerSystem.genPool[genId].fuelCount < Value.FullPoints;
            if (!room) return false;
            int id = 0;
            if (!fromPackage)
            {
                if (player.inhandItemCount <= 0 || !Taken(player.inhandItemId, !converter)) return false;
                id = player.inhandItemId;
                player.SetHandItemCount_Unsafe(player.inhandItemCount - 1);
                if (player.inhandItemCount <= 0) { player.SetHandItemId_Unsafe(0); player.SetHandItemCount_Unsafe(0); player.SetHandItemInc_Unsafe(0); }
            }
            else
            {
                var grids = player.package.grids;
                for (int g = player.package.size - 1; g >= 0 && id == 0; g--)
                {
                    if (grids[g].count <= 0 || !Taken(grids[g].itemId, !converter)) continue;
                    int item = grids[g].itemId, one = 1;
                    player.package.TakeItemFromGrid(g, ref item, ref one, out _);
                    if (one == 1) id = item;
                }
                if (id == 0) return false;
            }
            byte inc = 0;
            if (converter) Feed(__instance, asmId, id, 1, ref inc); else Burn(__instance, genId, id, 1, ref inc, true);
            filled.Alter(id, 1);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIStorageGrid), "HandPut")]
        static bool PutOnSlot(UIStorageGrid __instance, Player player, int count)
        {
            var window = UIRoot.instance?.uiGame?.assemblerWindow;
            return window == null || __instance != window.servingBox || !FeedHand(player, count);
        }

        // Shift or Ctrl click in the inventory while the converter's window is open sends the stack to it.
        [HarmonyPostfix, HarmonyPatch(typeof(Player), nameof(Player.IntendToTransferItems))]
        static void Sent(Player __instance) => FeedHand(__instance, 0);
    }
}
