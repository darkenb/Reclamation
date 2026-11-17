using System.IO;
using HarmonyLib;
using UnityEngine;

namespace TrashStation
{
    // The buildings' own icons: 80x80 pictures of their models (blender/icon.py), beside the DLL. The game copies 80x80
    // textures into its icon atlas.
    static class Icons
    {
        internal static void Apply(ItemProto building, RecipeProto recipe, string file)
        {
            var sprite = Load(file);
            if (sprite == null) throw new FileNotFoundException("no icon", file);
            Traverse.Create(building).Field("_iconSprite").SetValue(sprite);
            Traverse.Create(recipe).Field("_iconSprite").SetValue(sprite);
        }

        // Points and heat: 80x80 pictures drawn by make-item-icons.py.
        internal static void Apply(ItemProto item, string file)
        {
            var sprite = Load(file);
            if (sprite == null) throw new FileNotFoundException("no icon", file);
            Traverse.Create(item).Field("_iconSprite").SetValue(sprite);
        }

        internal static void Apply(TechProto tech, ItemProto like) => Traverse.Create(tech).Field("_iconSprite").SetValue(like.iconSprite);

        static Sprite Load(string path)
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(path) };
            if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
    }
}
