using System;
using System.Collections.Generic;
using CommonAPI.Systems;
using HarmonyLib;
using UnityEngine;

namespace TrashStation
{
    // The converter's recipe picker shows only its outputs: one row as wide as they need, without the tabs. Any other
    // picker gets the layout back. Sizes are measured from the picker as it is (GenesisBook widens it), not assumed.
    static class Picker
    {
        const float Cell = 46f, MinWidth = 330f;
        static Vector2 pickerSize, contentSize, contentPos;
        static bool saved;
        static readonly List<GameObject> hidden = new List<GameObject>();

        // The assembler window opens it through CommonAPI with a filter that passes only the converter's recipe type.
        static bool ForConverter(UIRecipePicker picker)
        {
            if (Traverse.Create(picker).Field<ERecipeType>("filter").Value == Plugin.RecipeType) return true;
            var filter = UIRecipePickerExtension.currentFilter;
            var recipe = LDB.recipes.Select(Plugin.FirstOutputRecipe);
            return filter != null && recipe != null && filter(recipe);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIRecipePicker), "_OnOpen")]
        static void Open(UIRecipePicker __instance)
        {
            Restore(__instance);
            if (!ForConverter(__instance)) return;
            var content = __instance.transform.Find("content") as RectTransform;
            var parent = content?.parent as RectTransform;
            if (content == null || parent == null) return;

            // The outputs, in order, at the start of the grid.
            var t = Traverse.Create(__instance);
            var protos = t.Field<RecipeProto[]>("protoArray").Value;
            var icons = t.Field<uint[]>("indexArray").Value;
            Array.Clear(protos, 0, protos.Length);
            Array.Clear(icons, 0, icons.Length);
            int n = 0;
            foreach (var r in Plugin.OutputRecipes)
                if (n < protos.Length && GameMain.history.RecipeUnlocked(r.ID)) { protos[n] = r; icons[n] = GameMain.iconSet.recipeIconIndex[r.ID]; n++; }
            int cols = Math.Max(1, n);
            t.Field<Material>("iconMat").Value?.SetVector("_Grid", new Vector4(cols, 1, 0.04f, 0.04f));

            // The grid moves up into the tab row and shrinks to one row; the window shrinks with it.
            saved = true;
            pickerSize = __instance.pickerTrans.sizeDelta;
            contentSize = content.sizeDelta;
            contentPos = content.anchoredPosition;
            float up = __instance.typeButton1 != null ? Math.Max(0, Top(__instance.typeButton1.transform as RectTransform, parent) - Top(content, parent)) : 0;
            foreach (var tab in __instance.pickerTrans.GetComponentsInChildren<UITabButton>()) Hide(tab.gameObject);
            Hide(__instance.typeButton1?.gameObject);
            Hide(__instance.typeButton2?.gameObject);
            content.sizeDelta = new Vector2(cols * Cell, Cell);
            content.anchoredPosition = contentPos + new Vector2(0, up);
            __instance.pickerTrans.sizeDelta = new Vector2(Math.Max(MinWidth, pickerSize.x - (contentSize.x - cols * Cell)), pickerSize.y - up - (contentSize.y - Cell));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIRecipePicker), "_OnClose")]
        static void Close(UIRecipePicker __instance) => Restore(__instance);

        static void Restore(UIRecipePicker picker)
        {
            foreach (var go in hidden) if (go != null) go.SetActive(true);
            hidden.Clear();
            if (!saved) return;
            saved = false;
            picker.pickerTrans.sizeDelta = pickerSize;
            var content = picker.transform.Find("content") as RectTransform;
            if (content == null) return;
            content.sizeDelta = contentSize;
            content.anchoredPosition = contentPos;
        }

        static void Hide(GameObject go)
        {
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            hidden.Add(go);
        }

        // The top edge of a rect, in the given parent's space.
        static float Top(RectTransform rt, RectTransform space)
        {
            if (rt == null) return 0;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return space.InverseTransformPoint(corners[1]).y;
        }
    }
}
