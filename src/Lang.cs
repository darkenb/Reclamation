using System.Collections.Generic;

namespace TrashStation
{
    public enum Language { English, Chinese, Turkish }

    // The mod's own texts; the English text is the key. Chosen in the config file: Turkish players run the game in its
    // English slot, so the game's language cannot tell.
    public static class Lang
    {
        public static Language Current = Language.English;

        // English -> { Chinese, Turkish }
        static readonly Dictionary<string, string[]> texts = new Dictionary<string, string[]>
        {
            ["Trash Converter"] = new[] { "垃圾转换器", "Çöp Dönüştürücü" },
            [Plugin.ConverterDesc] = new[]
            {
                "焚烧分拣器送入的任何物品（包括弹药和建筑），并将其点数转换为配方所产出的黑雾物品。每种物品按其原材料拥有点数，从最普通到最稀有；点数在转换器中累积，每达到配方价格便产出一个物品。它产出的物品永远不会被接收。",
                "Sıralayıcıların getirdiği her eşyayı, mermi ve binalar dahil, yakar ve puanını tarifinin ürettiği Kara Sis eşyasına çevirir. Her eşyanın ham maddesine göre, en basitten en nadire, bir puanı vardır; puanlar dönüştürücüde birikir ve tarifin fiyatına her ulaştığında bir eşya üretilir. Ürettiği eşyaları asla kabul etmez.",
            },
            ["Incinerator"] = new[] { "焚化炉", "Yakma Fırını" },
            [Plugin.IncineratorDesc] = new[]
            {
                "焚烧分拣器送入的任何物品（包括弹药和建筑）来发电。燃料按其在发电厂中的方式燃烧；其他物品按稀有度提供能量，普通物品很少，最稀有的物品还以更高的功率燃烧。垃圾转换器产出的物品和海洋液体永远不会被接收。",
                "Sıralayıcıların getirdiği her eşyayı, mermi ve binalar dahil, enerji için yakar. Yakıtlar bir santraldeki gibi yanar; diğer her şey nadirliğine göre enerji verir, basit eşyalar az, en nadirleri ayrıca daha yüksek güçte yanar. Çöp dönüştürücünün ürettiği eşyaları ve okyanus sıvılarını asla kabul etmez.",
            },
            ["Points"] = new[] { "点数", "Puan" },
            [Plugin.PointsDesc] = new[] { "被焚烧物品的点数，存放在垃圾转换器中。", "Yakılan eşyaların puanı; bir çöp dönüştürücüde bekler." },
            ["Heat"] = new[] { "热量", "Isı" },
            [Plugin.HeatDesc] = new[]
            {
                "被焚烧物品的热量，存放在焚化炉中：每份为 100 kJ 燃料。只有焚化炉能燃烧它。",
                "Yakılan eşyaların ısısı; bir yakma fırınında bekler: her biri 100 kJ yakıttır. Yalnızca yakma fırını yakar.",
            },
            ["Trash Processing"] = new[] { "垃圾处理", "Çöp İşleme" },
            ["Buildings that burn any item for points: a trash converter that turns them into Dark Fog items, and an incinerator that turns them into power."] = new[]
            {
                "将任何物品焚烧为点数的建筑：把点数转换为黑雾物品的垃圾转换器，以及把点数转化为电力的焚化炉。",
                "Her eşyayı puana yakan binalar: puanı Kara Sis eşyalarına çeviren çöp dönüştürücü ve enerjiye çeviren yakma fırını.",
            },
            ["Energy"] = new[] { "能量", "Enerji" },
            ["Trash points: {0} ({1} of power in an incinerator)"] = new[] { "垃圾点数：{0}（在焚化炉中发电 {1}）", "Çöp puanı: {0} (yakma fırınında {1} elektrik)" },
            ["{0} to go"] = new[] { "还差 {0}", "{0} kaldı" },
            ["Burned"] = new[] { "已焚烧", "Yakılanlar" },
            ["Item"] = new[] { "物品", "Eşya" },
            ["Count"] = new[] { "数量", "Adet" },
            ["This item cannot be burned"] = new[] { "该物品无法焚烧", "Bu eşya yakılamaz" },
            ["Stock {0} ≈ {1}"] = new[] { "库存 {0} ≈ {1}", "Stok {0} ≈ {1}" },
            ["{0} s"] = new[] { "{0} 秒", "{0} sn" },
            ["{0} min"] = new[] { "{0} 分", "{0} dk" },
            ["{0} h"] = new[] { "{0} 时", "{0} sa" },
            ["The building is full"] = new[] { "建筑已满", "Bina dolu" },
            ["Nothing burned yet"] = new[] { "尚未焚烧任何物品", "Henüz bir şey yakılmadı" },
            ["Burned: {0} items = {1}"] = new[] { "已焚烧：{0} 个物品 = {1}", "Yakılan: {0} eşya = {1}" },
            ["Burned: {0} items, {1} points"] = new[] { "已焚烧：{0} 个物品，{1} 点", "Yakılan: {0} eşya, {1} puan" },
        };

        public static string T(string english) =>
            Current != Language.English && texts.TryGetValue(english, out var t) ? t[Current == Language.Chinese ? 0 : 1] : english;

        public static string F(string english, params object[] args) => string.Format(System.Globalization.CultureInfo.InvariantCulture, T(english), args);

        public static IEnumerable<KeyValuePair<string, string[]>> All => texts;
    }
}
