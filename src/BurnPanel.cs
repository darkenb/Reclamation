using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TrashStation
{
    // A small "Burned" button on a building's window and the list it opens: each item burned this session, how many and
    // the points they gave, most points first, scrolling when it is longer than its box. One per window kind; it lives
    // on that window and hides with it. The button is just its word, lighting up under the mouse.
    // besideWindow: the list stands against the window's right edge, as tall as the window, closed until asked for.
    // Otherwise it fills a box of the given size at the spot the window names, open from the start.
    sealed class BurnPanel
    {
        readonly bool besideWindow;
        Vector2 size;
        internal BurnPanel(bool besideWindow, Vector2 size) { this.besideWindow = besideWindow; this.size = size; open = !besideWindow; }

        // A new size takes effect by building the list again.
        internal void Size(Vector2 value) { if (value != size) { size = value; window = null; } }

        const int RowHeight = 20, Pad = 8, Bar = 6, Top = Pad + 42;  // Top: summary line, then the column titles
        static readonly Color Back = new Color(0f, 44 / 255f, 55 / 255f, 0.96f);
        static readonly Color Shade = new Color(0f, 0f, 0f, 0.3f);  // on the window's own glass: just a little darker
        static readonly Color Line = new Color(167 / 255f, 233 / 255f, 1f, 1f);
        static readonly Color Word = new Color(1f, 1f, 1f, 0.8f);
        // Where the generator window's background sprite (window-trsl-1) shows inside its rect: right edge, top line
        // (the title tab rises above it on the left only), bottom. Read from the game's sprite.
        // shortcut: the left inset is assumed equal to the right one, measure it if the mirrored frame sits off.
        const float BgRight = 15, BgTop = 30, BgBottom = 13, BgLeft = 15;

        RectTransform window, button, panel, content;
        Font font;
        Text header;
        readonly List<Image> icons = new List<Image>();
        readonly List<Text> names = new List<Text>(), counts = new List<Text>(), points = new List<Text>();
        bool open;
        long shown = -1;
        float nextRefresh;

        // Called every frame while the window shows building `key`. topLeft: world point for the button's top left
        // corner; listAt: world point for the list's top left corner (unused beside the window).
        internal void Show(RectTransform owner, Font font, long key, Vector3 topLeft, Vector3 listAt = default(Vector3))
        {
            if (window != owner || button == null) Build(owner, font);
            if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);
            button.position = topLeft;
            panel.gameObject.SetActive(open);
            if (!open) return;
            if (key != shown || Time.unscaledTime >= nextRefresh) Fill(key);
            if (besideWindow)
            {
                // Against the window's visible right edge, from its top line down to its bottom.
                var bg = owner.Find("panel-bg") as RectTransform ?? owner;
                var r = bg.rect;
                panel.position = bg.TransformPoint(new Vector3(r.xMax - BgRight, r.yMax - BgTop, 0));
                panel.sizeDelta = new Vector2(size.x, r.height - BgTop - BgBottom);
            }
            else panel.position = listAt;
        }

        internal void Hide()
        {
            if (button != null && button.gameObject.activeSelf) button.gameObject.SetActive(false);
            if (panel != null && panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
        }

        void Build(RectTransform owner, Font font)
        {
            if (button != null) Object.Destroy(button.gameObject);
            if (panel != null) Object.Destroy(panel.gameObject);
            icons.Clear(); names.Clear(); counts.Clear(); points.Clear();
            window = owner;
            this.font = font;
            shown = -1;

            UnityEngine.Events.UnityAction toggle = () => { open = !open; shown = -1; };
            button = TextButton(owner, font, Lang.T("Burned"), toggle);

            var bg = besideWindow ? owner.Find("panel-bg") : null;
            panel = Box("trash-burned-list", owner, size, bg != null ? Color.clear : besideWindow ? Back : Shade);
            if (bg != null)
            {
                // The window's own background (glass, colour, frame) mirrored left to right, so its right-hand corners
                // meet the window's and the frame reads as one going on. Its children (title, buttons) are not wanted.
                var glass = (RectTransform)Object.Instantiate(bg.gameObject, panel, false).transform;
                glass.name = "glass";
                // Its plain pictures (the bright corner brackets) stay; anything with a text or a button goes.
                for (int i = glass.childCount - 1; i >= 0; i--)
                {
                    var child = glass.GetChild(i);
                    if (child.GetComponentInChildren<Text>(true) != null || child.GetComponentInChildren<Button>(true) != null
                        || child.GetComponentInChildren<UIButton>(true) != null)
                        Object.DestroyImmediate(child.gameObject);
                }
                glass.anchorMin = Vector2.zero; glass.anchorMax = Vector2.one; glass.pivot = new Vector2(0.5f, 0.5f);
                glass.offsetMin = new Vector2(-BgRight, -BgBottom); glass.offsetMax = new Vector2(BgLeft, BgTop);
                glass.localScale = new Vector3(-1, 1, 1);
                glass.SetAsFirstSibling();
            }
            float columns = size.x - 2 * Pad - Bar - 4;  // same columns as AddRow, shifted by the view's Pad
            Title(font, "Item", TextAnchor.MiddleLeft, Pad + RowHeight + 4, 100);
            Title(font, "Count", TextAnchor.MiddleRight, Pad + columns - 130, 56);
            Title(font, besideWindow ? "Energy" : "Points", TextAnchor.MiddleRight, Pad + columns - 70, 70);
            header = Label(panel, font, 14, TextAnchor.UpperLeft, Line);
            At(header.rectTransform, Pad, -Pad, size.x - 2 * Pad, 18);

            var view = new GameObject("view", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            view.SetParent(panel, false);
            view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(Pad, Pad); view.offsetMax = new Vector2(-Pad - Bar - 4, -Top);
            content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(view, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1);
            content.sizeDelta = Vector2.zero;

            var bar = Box("bar", panel, Vector2.zero, new Color(1f, 1f, 1f, 0.08f));
            bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(1, 1);
            bar.offsetMin = new Vector2(-Pad - Bar, Pad); bar.offsetMax = new Vector2(-Pad, -Top);
            var handle = Box("handle", bar, Vector2.zero, new Color(Line.r, Line.g, Line.b, 0.6f));
            handle.offsetMin = handle.offsetMax = Vector2.zero;  // the Scrollbar drives its anchors
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };

            // On the panel itself: its image catches the mouse wheel anywhere over the list.
            panel.gameObject.AddComponent<UIScrollZone>();  // else the game gives the wheel to the camera as well
            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = RowHeight * 2;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            panel.gameObject.SetActive(false);
        }

        // Beside the incinerator the list counts fuel energy in J: shown as the power it gives.
        static double Efficiency => Plugin.PowerPerPoint / Value.JoulesPerPoint;

        void Title(Font font, string english, TextAnchor align, float x, float width)
        {
            var t = Label(panel, font, 12, align, new Color(1f, 1f, 1f, 0.55f));
            t.text = Lang.T(english);
            At(t.rectTransform, x, -Pad - 22, width, 18);
        }

        void AddRow()
        {
            float y = -icons.Count * RowHeight, width = size.x - 2 * Pad - Bar - 4;
            var icon = new GameObject("icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(content, false);
            icon.raycastTarget = false;
            At(icon.rectTransform, 0, y, RowHeight - 2, RowHeight - 2);
            icons.Add(icon);
            var name = Label(content, font, 13, TextAnchor.MiddleLeft, Color.white);
            At(name.rectTransform, RowHeight + 4, y, width - 130 - RowHeight - 8, RowHeight);
            names.Add(name);
            var count = Label(content, font, 13, TextAnchor.MiddleRight, Color.white);
            At(count.rectTransform, width - 130, y, 56, RowHeight);
            counts.Add(count);
            var pts = Label(content, font, 13, TextAnchor.MiddleRight, Line);
            At(pts.rectTransform, width - 70, y, 70, RowHeight);
            points.Add(pts);
        }

        void Fill(long key)
        {
            shown = key;
            nextRefresh = Time.unscaledTime + 0.5f;
            var lines = Ledger.Read(key);
            long total = 0, items = 0;
            foreach (var l in lines) { total += l.Value.Points; items += l.Value.Count; }
            header.text = lines.Count == 0 ? Lang.T("Nothing burned yet") : besideWindow ? Lang.F("Burned: {0} items = {1}", Value.Short(items), Value.Energy(total * Efficiency))
                : Lang.F("Burned: {0} items, {1} points", Value.Short(items), Value.Short(total));
            while (icons.Count < lines.Count) AddRow();
            for (int i = 0; i < icons.Count; i++)
            {
                bool on = i < lines.Count;
                icons[i].gameObject.SetActive(on); names[i].gameObject.SetActive(on); counts[i].gameObject.SetActive(on); points[i].gameObject.SetActive(on);
                if (!on) continue;
                var item = LDB.items.Select(lines[i].Key);
                icons[i].sprite = item?.iconSprite;
                icons[i].enabled = icons[i].sprite != null;
                names[i].text = item != null ? item.name : "#" + lines[i].Key;
                counts[i].text = "×" + Value.Short(lines[i].Value.Count);
                points[i].text = besideWindow ? Value.Energy(lines[i].Value.Points * Efficiency) : Value.Short(lines[i].Value.Points);
            }
            content.sizeDelta = new Vector2(0, lines.Count * RowHeight);
        }

        // The word alone in the window's text colour; the mouse lights it up.
        static RectTransform TextButton(Transform parent, Font font, string text, UnityEngine.Events.UnityAction onClick)
        {
            var label = Label(parent, font, 15, TextAnchor.MiddleLeft, Color.white);
            label.name = "trash-burned-button";
            label.text = text;
            label.raycastTarget = true;
            var r = label.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0, 1);
            r.sizeDelta = new Vector2(label.preferredWidth + 8, 24);
            var b = label.gameObject.AddComponent<Button>();
            b.targetGraphic = label;
            var c = b.colors;
            c.normalColor = c.selectedColor = Word;
            c.highlightedColor = Line;
            c.pressedColor = new Color(1f, 1f, 1f, 0.55f);
            c.fadeDuration = 0.08f;
            b.colors = c;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(onClick);
            return r;
        }

        static RectTransform Box(string name, Transform parent, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0, 1);
            r.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            return r;
        }

        internal static Text Label(Transform parent, Font font, int size, TextAnchor align, Color color)
        {
            var t = new GameObject("text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            t.transform.SetParent(parent, false);
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // Top-left placement inside the panel, in pixels from its top left corner.
        static void At(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
        }
    }
}
