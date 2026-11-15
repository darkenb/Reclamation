using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TrashStation
{
    // What each building has burned this session: per item, how many and the points they gave. Not saved.
    public static class Ledger
    {
        public sealed class Line { public long Count, Points; }

        static readonly ConcurrentDictionary<long, Dictionary<int, Line>> books = new ConcurrentDictionary<long, Dictionary<int, Line>>();

        public static void Add(long building, int item, int count, long each)
        {
            var book = books.GetOrAdd(building, _ => new Dictionary<int, Line>());
            lock (book)
            {
                if (!book.TryGetValue(item, out var line)) book[item] = line = new Line();
                line.Count += count;
                line.Points += each * count;
            }
        }

        // The building's lines, most points first.
        public static List<KeyValuePair<int, Line>> Read(long building)
        {
            var list = new List<KeyValuePair<int, Line>>();
            if (!books.TryGetValue(building, out var book)) return list;
            lock (book)
                foreach (var kv in book) list.Add(new KeyValuePair<int, Line>(kv.Key, new Line { Count = kv.Value.Count, Points = kv.Value.Points }));
            list.Sort((a, b) => b.Value.Points.CompareTo(a.Value.Points));
            return list;
        }

        public static void Remove(long building) => books.TryRemove(building, out _);

        public static void Clear() => books.Clear();
    }
}
