using System.Collections.Generic;
using DolocTown;
using DolocTown.UI;
using RedSaw;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// Renders tracked entries using the game's own mission-tip prefab
    /// (LocPfbs.UI_ELEMENT_MISSIONTIPEX), pooled under a canvas we own.
    ///
    /// Why clone the prefab instead of pushing into MissionTipManager: AddMissionTip only
    /// accepts ids that resolve through QueryMission, i.e. real IMission objects registered in
    /// MissionManager -- faction missions and seed research are neither. Its slotRoot is
    /// private, it caps at maxShowNum = 3, and UpdateAllMissionPos() repositions the whole
    /// stack on every change, so injected tips would fight it for placement. Sharing only the
    /// prefab keeps the native look while leaving layout to us. The text fields are TMP with
    /// the font already baked in, so no font substitution is needed either.
    ///
    /// Prefab shape (450x56):
    ///     root      VerticalLayoutGroup, padding l8 r8 t6 b6, childAlign MiddleRight,
    ///               childControlWidth = false, childControlHeight = false
    ///       title   TMP, align Right
    ///       row     HorizontalLayoutGroup
    ///         guide     TMP + LayoutElement
    ///         progress  TMP + ContentSizeFitter   (we switch this off; see ApplyLayout)
    ///
    /// Two display modes, chosen by the one config entry this class takes ([Overlay] Marquee):
    ///     true   one line, never truncated, scrolled inside a RectMask2D on the row. Keeps
    ///            the prefab's authored width, so our tips match the game's own exactly.
    ///     false  TMP wraps over up to WrapMaxLines lines, then ellipsises. Needs the wider
    ///            WrapWidth to be comfortable, so these are visibly wider than the game's.
    /// Every other number here is a constant below -- the widths, the scroll speed and pause,
    /// the wrapped line count. Horizontal alignment with the game's tips is a separate
    /// concern, settled by Core's OffsetX on our root.
    ///
    /// Because childControlWidth is false, the root, the title and the row all have to be
    /// widened together -- a child left at its authored 434 gets clipped even when the root is
    /// wider.
    /// </summary>
    internal sealed class TipOverlay
    {
        private const string CanvasName = "DolocTownQuestTrackerCanvas";
        private const string Separator = "   ";
        private const float LineHeight = 20f;
        private const float PrefabPadding = 16f;   // VerticalLayoutGroup l8 + r8

        // Scrolling keeps the prefab's authored width so our tips are exactly as wide as the
        // game's own. The fallback covers a prefab that cannot be measured or reads back
        // implausibly narrow -- checking it here is what lets ApplyLayout trust the width.
        private const float FallbackTipWidth = 450f;
        private const float MinTipWidth = 200f;
        private const float WrapWidth = 700f;      // wrapping: needs more room than the prefab
        private const float MarqueeSpeed = 30f;    // px/s
        private const float MarqueePause = 3f;     // s held at each end
        private const int WrapMaxLines = 3;
        private const float RowSpacing = 8f;       // px between stacked tips

        private Canvas canvas;
        private RectTransform root;
        private ObjectPool<MissionTip> pool;
        private readonly List<Marquee> marquees = new List<Marquee>();

        private bool visible = true;
        private bool marquee = true;
        private float prefabWidth;   // measured from the prefab in TryBuild

        private Vector2 appliedOffset;
        private bool offsetApplied;

        /// <summary>
        /// One scrolling line. In the row's local space the right edge is 0, the text rect
        /// spans [dx - width, dx] and the visible window is [-viewport, 0]. So dx = Start
        /// (width - viewport) shows the head and dx = 0 shows the tail. One-way: run to the
        /// tail, hold, snap back to the head, hold.
        /// </summary>
        private struct Marquee
        {
            public RectTransform Rect;
            public float Start;
            public float Offset;
            public float PauseLeft;
            public bool HoldingTail;
        }

        internal void Configure(bool useMarquee)
        {
            marquee = useMarquee;
        }

        /// <summary>Each mode needs its own width; which one applies is not the caller's call.</summary>
        private float TipWidth
        {
            get
            {
                if (!marquee)
                {
                    return WrapWidth;
                }
                return prefabWidth > 0f ? prefabWidth : FallbackTipWidth;
            }
        }

        internal bool IsBuilt
        {
            get { return canvas != null && root != null && pool != null; }
        }

        internal int LiveCount
        {
            get { return pool != null ? pool.ActiveCount : 0; }
        }

        internal void SetVisible(bool value)
        {
            visible = value;
            if (root != null && root.gameObject.activeSelf != value)
            {
                root.gameObject.SetActive(value);
            }
        }

        internal void SetOffset(Vector2 anchoredOffset)
        {
            if (root == null)
            {
                return;
            }
            // Unity's setter does not compare first: writing the same value every frame still
            // dirties the rect and re-registers the whole tip stack for a canvas rebuild.
            if (offsetApplied && appliedOffset == anchoredOffset)
            {
                return;
            }
            appliedOffset = anchoredOffset;
            offsetApplied = true;
            root.anchoredPosition = anchoredOffset;
        }

        /// <summary>Advances the scrolling lines. Driven from Core's tick.</summary>
        internal void Tick(float deltaTime)
        {
            if (!marquee || marquees.Count == 0)
            {
                return;
            }
            if (deltaTime <= 0f || deltaTime > 0.5f)
            {
                deltaTime = 0.016f;   // ignore load-spike frames
            }

            for (int i = 0; i < marquees.Count; i++)
            {
                Marquee m = marquees[i];
                if (m.Rect == null)
                {
                    continue;
                }

                if (m.PauseLeft > 0f)
                {
                    m.PauseLeft -= deltaTime;
                    if (m.PauseLeft <= 0f && m.HoldingTail)
                    {
                        m.HoldingTail = false;
                        m.Offset = m.Start;
                        m.PauseLeft = MarqueePause;
                    }
                }
                else
                {
                    m.Offset -= MarqueeSpeed * deltaTime;
                    if (m.Offset <= 0f)
                    {
                        m.Offset = 0f;
                        m.HoldingTail = true;
                        m.PauseLeft = MarqueePause;
                    }
                }

                m.Rect.anchoredPosition = new Vector2(m.Offset, 0f);
                marquees[i] = m;
            }
        }

        /// <summary>
        /// Lazily builds the canvas + pool. Returns false while the game is not ready -- the
        /// prefab comes from Addressables and is null early in the boot sequence.
        /// </summary>
        internal bool TryBuild(out string blocker)
        {
            blocker = "";
            if (IsBuilt)
            {
                return true;
            }
            if (DolocAPI.uiSystem == null)
            {
                blocker = "DolocAPI.uiSystem is null";
                return false;
            }

            GameObject prefab;
            try
            {
                prefab = LocPfbs.UI_ELEMENT_MISSIONTIPEX;
            }
            catch (System.Exception e)
            {
                blocker = "prefab load threw " + e.GetType().Name + ": " + e.Message;
                return false;
            }
            if (prefab == null)
            {
                blocker = "UI_ELEMENT_MISSIONTIPEX returned null (Addressables not ready?)";
                return false;
            }

            if (prefabWidth <= 0f)
            {
                RectTransform prefabRect = prefab.transform as RectTransform;
                prefabWidth = prefabRect != null && prefabRect.sizeDelta.x >= MinTipWidth
                    ? prefabRect.sizeDelta.x
                    : FallbackTipWidth;
                Diag.Info("Tip prefab width = " + prefabWidth);
            }

            if (canvas == null)
            {
                GameObject go = new GameObject(CanvasName);
                canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1900;
                Object.DontDestroyOnLoad(go);

                CanvasScaler scaler = go.AddComponent<CanvasScaler>();
                CanvasScaler source = FindGameScaler();
                if (source != null)
                {
                    scaler.uiScaleMode = source.uiScaleMode;
                    scaler.referenceResolution = source.referenceResolution;
                    scaler.screenMatchMode = source.screenMatchMode;
                    scaler.matchWidthOrHeight = source.matchWidthOrHeight;
                }
                else
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                }
                Canvas.ForceUpdateCanvases();
            }

            if (root == null)
            {
                GameObject rootGo = new GameObject("TipRoot", typeof(RectTransform));
                root = rootGo.GetComponent<RectTransform>();
                root.SetParent(canvas.transform, false);
                DolocUtils.SetAnchorAndPivot(root, UIAlignmentType.RightTop);
                root.sizeDelta = Vector2.zero;
                offsetApplied = false;   // the caller's SetOffset places it
                rootGo.SetActive(visible);
            }

            if (pool == null)
            {
                pool = new ObjectPool<MissionTip>(prefab, root);
            }
            return true;
        }

        private static CanvasScaler FindGameScaler()
        {
            DolocUiSystem ui = DolocAPI.uiSystem;
            if (ui != null)
            {
                CanvasScaler inParent = ui.GetComponentInParent<CanvasScaler>();
                if (inParent != null)
                {
                    return inParent;
                }
                CanvasScaler inChildren = ui.GetComponentInChildren<CanvasScaler>(true);
                if (inChildren != null)
                {
                    return inChildren;
                }
            }
            return Object.FindObjectOfType<CanvasScaler>();
        }

        /// <summary>Rebuilds the whole stack. Cheap at a handful of entries.</summary>
        internal void Render(List<TipLine> lines)
        {
            if (!IsBuilt)
            {
                return;
            }
            marquees.Clear();
            pool.RecycleAll();

            float y = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                MissionTip tip = pool.Next;
                if (tip == null)
                {
                    continue;
                }
                RectTransform tipRect = tip.transform as RectTransform;
                if (tipRect == null)
                {
                    continue;   // not a UI object; nothing sensible to place
                }

                TipLine line = lines[i];
                tip.Render(line.Title, "", "", line.AccentHex);
                ApplyLayout(tip, tipRect, line);

                tipRect.localPosition = new Vector3(0f, y, 0f);
                y -= tipRect.sizeDelta.y + RowSpacing;
                tip.SetVisible(true);
            }
        }

        private void ApplyLayout(MissionTip tip, RectTransform tipRect, TipLine line)
        {
            // Located structurally: root > row(HorizontalLayoutGroup) > [guide, progress].
            // Matching on text content broke as soon as the text became something computed
            // here rather than passed in.
            HorizontalLayoutGroup rowGroup = tip.GetComponentInChildren<HorizontalLayoutGroup>(true);
            TMP_Text guide = null;
            TMP_Text progress = null;
            if (rowGroup != null)
            {
                TMP_Text[] rowTexts = rowGroup.GetComponentsInChildren<TMP_Text>(true);
                if (rowTexts.Length > 0)
                {
                    guide = rowTexts[0];
                }
                if (rowTexts.Length > 1)
                {
                    progress = rowTexts[1];
                }
            }

            // Everything goes into the guide. The progress field is laid out to its right and
            // its ContentSizeFitter pushed it past the title's right edge, breaking alignment.
            if (progress != null && progress.gameObject.activeSelf)
            {
                progress.gameObject.SetActive(false);
            }

            ContentSizeFitter fitter =
                DolocUtils.GetOrCreateComponent<ContentSizeFitter>(tip.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // TipWidth is validated at the measurement site, so the budget is always usable.
            float width = TipWidth;
            float budget = width - PrefabPadding;

            SetWidth(tipRect, width);
            SetWidth(tip.transform.Find("title") as RectTransform, budget);
            if (rowGroup != null)
            {
                SetWidth(rowGroup.transform as RectTransform, budget);
            }

            if (guide == null)
            {
                return;
            }
            guide.text = string.Join(Separator, line.Items.ToArray());

            float rowHeight = marquee
                ? LayoutScrolling(guide, rowGroup, budget)
                : LayoutWrapped(guide, budget);

            // The root's VerticalLayoutGroup runs with childControlHeight = false, so it reads
            // each child's own rect height and ignores LayoutElement.preferredHeight.
            SetHeight(guide.rectTransform.parent as RectTransform, rowHeight);
            tip.RebuildLayout();
        }

        /// <summary>One unwrapped line inside a mask, queued for scrolling if it overflows.</summary>
        private float LayoutScrolling(TMP_Text guide, HorizontalLayoutGroup rowGroup, float budget)
        {
            // The layout group has to go: it would reset the position we animate.
            if (rowGroup != null)
            {
                if (rowGroup.enabled)
                {
                    rowGroup.enabled = false;
                }
                DolocUtils.GetOrCreateComponent<RectMask2D>(rowGroup.gameObject);
            }

            guide.enableWordWrapping = false;
            guide.overflowMode = TextOverflowModes.Overflow;
            guide.maxVisibleLines = 1;

            float textWidth = guide.GetPreferredValues().x;
            float rectWidth = textWidth > budget ? textWidth : budget;

            RectTransform guideRect = guide.rectTransform;
            DolocUtils.SetAnchorAndPivot(guideRect, UIAlignmentType.RightMiddle);
            guideRect.sizeDelta = new Vector2(rectWidth, LineHeight);

            Marquee m = new Marquee();
            m.Rect = guideRect;
            m.Start = rectWidth - budget;
            m.Offset = m.Start;
            m.PauseLeft = MarqueePause;
            guideRect.anchoredPosition = new Vector2(m.Offset, 0f);

            // Text that fits gets its one and only position above; only movers need ticking.
            if (m.Start > 0.5f)
            {
                marquees.Add(m);
            }
            return LineHeight;
        }

        /// <summary>TMP wrapping over at most WrapMaxLines lines, then ellipsised.</summary>
        private float LayoutWrapped(TMP_Text guide, float budget)
        {
            // TMP only wraps if the width is constrained: the HorizontalLayoutGroup otherwise
            // grows the rect to the full text width, leaving nothing to wrap against.
            guide.enableWordWrapping = true;
            guide.overflowMode = TextOverflowModes.Ellipsis;

            LayoutElement le = DolocUtils.GetOrCreateComponent<LayoutElement>(guide.gameObject);
            le.preferredWidth = budget;
            le.flexibleWidth = 0f;

            RectTransform guideRect = guide.rectTransform;
            guideRect.sizeDelta = new Vector2(budget, LineHeight * WrapMaxLines);
            guide.maxVisibleLines = WrapMaxLines;
            guide.ForceMeshUpdate();

            int used = guide.textInfo != null && guide.textInfo.lineCount > 0
                ? guide.textInfo.lineCount
                : 1;
            if (used > WrapMaxLines)
            {
                used = WrapMaxLines;
            }
            float rowHeight = LineHeight * used;
            guideRect.sizeDelta = new Vector2(budget, rowHeight);
            return rowHeight;
        }

        private static void SetWidth(RectTransform rect, float width)
        {
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            }
        }

        private static void SetHeight(RectTransform rect, float height)
        {
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            }
        }
    }

    /// <summary>
    /// One rendered tip. Items stay separate so the layout can join or measure them without
    /// re-parsing a combined string.
    /// </summary>
    internal struct TipLine
    {
        public string Title;
        public List<string> Items;
        public string AccentHex;

        public TipLine(string title, List<string> items, string accentHex)
        {
            Title = title;
            Items = items;
            AccentHex = accentHex;
        }
    }
}
