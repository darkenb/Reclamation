using System;
using System.IO;
using UnityEngine;

namespace TrashStation
{
    // Our own building looks, made in Blender (blender/*.py, written by blender/export.py): one mesh, its loop animation
    // in the game's vertex-animation (verta) form, baked textures, and a floor drawn with the game's own foundation
    // pattern (reform-7, read from the game at load, not shipped). The game's building shader draws it like any other
    // building: the animation runs while the building works, the glow follows power.
    static class Model
    {
        const int Grid = 4, Swatch = 8;  // swatches per row in the palette texture, pixels per swatch
        const int Fps = 30;              // frames per second the exporter samples

        // Gives the building at desc the model in file, drawn with a copy of template (a building material of the game).
        internal static void Apply(PrefabDesc desc, string file, Material template)
        {
            using (var r = new BinaryReader(File.OpenRead(file)))
            {
                if (r.ReadInt32() != 0x324D5354) throw new InvalidDataException(file + " is not a model file");  // "TSM2"
                int count = r.ReadInt32(), still = r.ReadInt32(), frames = r.ReadInt32(), indices = r.ReadInt32(), floorIndices = r.ReadInt32(), swatches = r.ReadInt32();
                var palette = Floats(r, swatches * 8);
                var uv = Floats(r, count * 2);
                var tris = new int[indices];
                for (int i = 0; i < indices; i++) tris[i] = r.ReadInt32();
                var fixedPart = Floats(r, still * 9);
                var moving = Floats(r, frames * (count - still) * 9);

                // Every frame holds every vertex: the fixed ones repeat.
                var verta = new VertaBuffer();
                verta.Expand(VertType.VNT, count, frames);
                for (int f = 0; f < frames; f++)
                {
                    Array.Copy(fixedPart, 0, verta.data, f * count * 9, still * 9);
                    Array.Copy(moving, f * (count - still) * 9, verta.data, f * count * 9 + still * 9, (count - still) * 9);
                }

                // The mesh itself is frame 0: what shaders without the animation (build preview) show.
                var pos = new Vector3[count];
                var nor = new Vector3[count];
                var tan = new Vector4[count];
                var tex = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    int o = i * 9;
                    var d = verta.data;
                    pos[i] = new Vector3(d[o], d[o + 1], d[o + 2]);
                    nor[i] = new Vector3(d[o + 3], d[o + 4], d[o + 5]);
                    tan[i] = new Vector4(d[o + 6], d[o + 7], d[o + 8], 1);
                    tex[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
                }
                var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(file) };
                if (count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = pos;
                mesh.normals = nor;
                mesh.tangents = tan;
                mesh.uv = tex;
                // Sub-mesh 0 is the building, 1 the floor (the last floorIndices indices).
                mesh.subMeshCount = 2;
                var body = new int[indices - floorIndices];
                var floor = new int[floorIndices];
                Array.Copy(tris, body, body.Length);
                Array.Copy(tris, body.Length, floor, 0, floor.Length);
                mesh.SetTriangles(body, 0);
                mesh.SetTriangles(floor, 1);
                mesh.RecalculateBounds();

                var mat = Material(template, palette, swatches, mesh.name, Path.ChangeExtension(file, null));
                var floorMat = Floor(template, mesh.name);
                for (int i = 0; i < desc.lodCount; i++)
                {
                    desc.lodMeshes[i] = mesh;
                    desc.lodMaterials[i] = new[] { mat, floorMat };
                    desc.lodVertas[i] = verta;
                    var bp = desc.lodBlueprintMaterials?[i];
                    if (bp != null) { var b = bp.Length > 0 ? bp[0] : null; desc.lodBlueprintMaterials[i] = new[] { b, b }; }
                    if (desc.lodSubmeshIgnores != null) desc.lodSubmeshIgnores[i] = new bool[2];
                }
                // The whole loop is the working animation; the shader maps the building's animation time onto the frames.
                desc.anim_prepare_length = 0;
                desc.anim_working_length = (frames - 1) / (float)Fps;
            }
        }

        static float[] Floats(BinaryReader r, int n)
        {
            var bytes = r.ReadBytes(n * 4);
            if (bytes.Length != n * 4) throw new EndOfStreamException();
            var f = new float[n];
            Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
            return f;
        }

        // Palette entry: albedo rgb, metallic, smoothness, emission rgb. The shader reads metallic from R, opacity from G
        // and smoothness from A of _MS_Tex; emission from RGB of _EmissionTex.
        // Baked textures beside the model file (<model>_a.png albedo, _n.png normal, _s.png metallic/opacity/smoothness,
        // _e.png emission) win over the palette swatches.
        static Material Material(Material template, float[] palette, int swatches, string name, string baked)
        {
            var mat = Swatches(template, palette, swatches, name);
            var a = Png(baked + "_a.png", false);
            var s = Png(baked + "_s.png", true);
            var e = Png(baked + "_e.png", false);
            var n = Png(baked + "_n.png", true);
            if (a != null && s != null && e != null)
            {
                if (n != null) mat.SetTexture("_NormalTex", n);
                mat.SetTexture("_MainTex", a);
                mat.SetTexture("_MS_Tex", s);
                mat.SetTexture("_EmissionTex", e);
            }
            return mat;
        }

        // The floor: the game's foundation pattern reform-7 as it is (plates with grilles: colour and bumps), only a little
        // darker grey. Without the game's textures it is plain dark grey.
        const float FloorShade = 0.55f;  // multiplies the pattern's colour (linear): a little darker grey
        static Material Floor(Material template, string name)
        {
            Texture albedo = null, normal = null;
            var builtin = Configs.builtin;
            foreach (var proto in builtin != null ? new[] { builtin.planetReformMatProto0, builtin.planetReformMatProto1 } : new Material[0])
            {
                if (proto == null) continue;
                foreach (var prop in proto.GetTexturePropertyNames())
                {
                    var t = proto.GetTexture(prop);
                    if (t == null) continue;
                    if (t.name == "reform-7-a") albedo = t;
                    else if (t.name == "reform-7-n") normal = t;
                }
            }
            var mat = new Material(template) { name = name + "-floor" };
            var tint = albedo != null ? new Color(FloorShade, FloorShade, FloorShade, 1) : new Color(0.17f, 0.17f, 0.17f, 1);  // 0.17: the pattern's average once shaded
            mat.SetTexture("_MainTex", albedo != null ? albedo : Solid(Color.white, false, name + "-floor-a"));
            mat.SetTexture("_NormalTex", normal != null ? normal : Solid(new Color(1, 0.5f, 0.5f, 0.5f), true, name + "-floor-n"));
            mat.SetTexture("_MS_Tex", Solid(new Color(0.4f, 1, 0, 0.4f), true, name + "-floor-s"));
            mat.SetTexture("_EmissionTex", Solid(Color.clear, true, name + "-floor-e"));
            mat.SetColor("_Color", tint.gamma);
            mat.SetColor("_EmissionMask", Color.white);
            mat.SetFloat("_AlbedoMultiplier", 1f);
            mat.SetFloat("_EmissionMultiplier", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            if (albedo == null) Plugin.Log.LogWarning("Foundation pattern reform-7 not found; the floor is plain");
            return mat;
        }

        static Texture2D Solid(Color c, bool linear, string name)
        {
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            return Texture(px, 4, linear, name);
        }

        static Texture2D Png(string path, bool linear)
        {
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = Path.GetFileNameWithoutExtension(path), wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            if (!t.LoadImage(File.ReadAllBytes(path), false)) return null;
            t.Apply(true, true);
            return t;
        }

        static Material Swatches(Material template, float[] palette, int swatches, string name)
        {
            int size = Grid * Swatch;
            var albedo = new Color[size * size];
            var ms = new Color[size * size];
            var glow = new Color[size * size];
            for (int s = 0; s < swatches && s < Grid * Grid; s++)
            {
                int p = s * 8;
                var a = new Color(palette[p], palette[p + 1], palette[p + 2], 0);
                var m = new Color(palette[p + 3], 1, 0, palette[p + 4]);
                var e = new Color(palette[p + 5], palette[p + 6], palette[p + 7], 0);
                int x0 = s % Grid * Swatch, y0 = s / Grid * Swatch;
                for (int y = y0; y < y0 + Swatch; y++)
                    for (int x = x0; x < x0 + Swatch; x++) { albedo[y * size + x] = a; ms[y * size + x] = m; glow[y * size + x] = e; }
            }
            var flat = new Color[size * size];
            for (int i = 0; i < flat.Length; i++) flat[i] = new Color(1, 0.5f, 0.5f, 0.5f);

            var mat = new Material(template) { name = name };
            mat.SetTexture("_MainTex", Texture(albedo, size, false, name + "-a"));
            mat.SetTexture("_MS_Tex", Texture(ms, size, true, name + "-s"));
            mat.SetTexture("_EmissionTex", Texture(glow, size, true, name + "-e"));
            mat.SetTexture("_NormalTex", Texture(flat, size, true, name + "-n"));
            mat.SetColor("_Color", Color.white);
            mat.SetColor("_EmissionMask", Color.white);
            mat.SetFloat("_AlbedoMultiplier", 1f);
            mat.SetFloat("_EmissionMultiplier", 3f);
            mat.SetFloat("_EmissionJitter", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            return mat;
        }

        static Texture2D Texture(Color[] pixels, int size, bool linear, string name)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, linear)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            t.SetPixels(pixels);
            t.Apply(false, true);
            return t;
        }
    }
}
