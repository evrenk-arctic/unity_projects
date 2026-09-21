using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using CesiumForUnity;
using UIDocument = UnityEngine.UIElements.UIDocument;
using VisualElement = UnityEngine.UIElements.VisualElement;
using StyleKeyword = UnityEngine.UIElements.StyleKeyword;

internal sealed class NavigationHud
{
    private static readonly Color Ink = new Color32(27, 48, 48, 255);
    private readonly Font font;
    private readonly Camera camera;
    private readonly RectTransform safeArea;
    private readonly RectTransform maneuver;
    private readonly Text turnDistance;
    private readonly Text turnStreet;
    private readonly Text turnAction;
    private readonly Text currentStreet;
    private readonly NavigationIconGraphic turnIcon;
    private readonly List<MapLabel> labels = new List<MapLabel>();
    private readonly Text attribution;
    private UIDocument creditsDocument;
    private bool roadmapCredits;
    private bool visible = true;

    public void SetVisible(bool enabled)
    {
        visible = enabled;
        safeArea.gameObject.SetActive(enabled);
        if (creditsDocument != null)
            creditsDocument.rootVisualElement.style.display = enabled ? UnityEngine.UIElements.DisplayStyle.Flex : UnityEngine.UIElements.DisplayStyle.None;
    }

    private sealed class MapLabel
    {
        public Vector3 Position;
        public RectTransform Rect;
    }

    public NavigationHud(Transform parent, Camera camera, Font font, Func<double, double, Vector3> project)
    {
        this.camera = camera;
        this.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasObject = new GameObject("Navigation HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(parent, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        safeArea = Rect("Safe Area", canvasObject.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        AddLabel("Sacramento St", -122.3997, 37.7942, project);
        AddLabel("California St", -122.3994, 37.7933, project);
        AddLabel("Washington St", -122.4002, 37.7959, project);
        AddLabel("Clay St", -122.4023, 37.7948, project);
        AddLabel("Battery St", -122.4000, 37.7970, project);
        AddLabel("Sansome St", -122.4017, 37.7968, project);
        AddLabel("Montgomery St", -122.4029, 37.7936, project);
        AddLabel("Kearny St", -122.4045, 37.7934, project);
        AddLabel("Pine St", -122.3999, 37.7923, project);
        AddLabel("Drumm St", -122.3969, 37.7957, project);
        AddLabel("FERRY BUILDING", -122.3937, 37.7955, project);
        AddLabel("TRANSAMERICA", -122.4036, 37.7952, project);
        AddLabel("CHINATOWN", -122.4067, 37.7958, project);
        AddLabel("EMBARCADERO", -122.3955, 37.7972, project);

        maneuver = Panel("Next Maneuver", safeArea, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(430, 124), Ink);
        maneuver.pivot = new Vector2(0.5f, 1);
        turnIcon = Icon(maneuver, "right", Color.white);
        Place(turnIcon.rectTransform, new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(66, 66), new Vector2(0, 0.5f));
        turnDistance = Label(maneuver, "", 36, Color.white, TextAnchor.MiddleLeft);
        Place(turnDistance.rectTransform, Vector2.up, new Vector2(110, -12), new Vector2(290, 44), Vector2.up);
        turnAction = Label(maneuver, "", 14, new Color32(143, 224, 208, 255), TextAnchor.MiddleLeft);
        Place(turnAction.rectTransform, Vector2.up, new Vector2(110, -55), new Vector2(290, 24), Vector2.up);
        turnStreet = Label(maneuver, "", 23, Color.white, TextAnchor.MiddleLeft);
        Place(turnStreet.rectTransform, Vector2.up, new Vector2(110, -81), new Vector2(290, 32), Vector2.up);

        RectTransform streetStrip = Panel("Current Street", safeArea, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(320, 40), Color.white);
        currentStreet = Label(streetStrip, "", 20, Ink, TextAnchor.MiddleCenter);
        attribution = Label(safeArea, "Map data (c) OpenStreetMap contributors / ODbL", 11, Ink, TextAnchor.MiddleLeft);
        Place(attribution.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(420, 18), new Vector2(0.5f, 0));
        attribution.alignment = TextAnchor.MiddleCenter;

        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var eventSystem = new GameObject("Navigation Input", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(parent, false);
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }

    public void SetNightMode(bool enabled)
    {
        Color foreground = enabled ? new Color32(222, 233, 236, 255) : Ink;
        maneuver.GetComponent<Image>().color = enabled ? new Color32(16, 23, 28, 255) : Ink;
        currentStreet.rectTransform.parent.GetComponent<Image>().color = enabled ? new Color32(25, 35, 42, 255) : Color.white;
        currentStreet.color = foreground;
        turnDistance.color = turnStreet.color = turnIcon.color = enabled ? new Color32(222, 233, 236, 255) : Color.white;
        turnAction.color = enabled ? new Color32(125, 198, 185, 255) : new Color32(143, 224, 208, 255);
        attribution.color = foreground;
        foreach (MapLabel label in labels)
        {
            label.Rect.GetComponent<Text>().color = enabled ? new Color32(185, 203, 199, 255) : new Color32(75, 94, 89, 255);
            label.Rect.GetComponent<Outline>().effectColor = enabled ? new Color(0, 0, 0, 0.85f) : new Color(1, 1, 1, 0.85f);
        }
    }

    public void Update(string street, string nextStreet, string modifier, float metersToTurn,
        bool paused, bool googleMap, bool mapLoading, bool arrived, bool roadmapView)
    {
        UnityEngine.Rect area = Screen.safeArea;
        safeArea.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
        safeArea.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        float width = safeArea.rect.width;
        maneuver.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(430, width - 40));
        float textWidth = Mathf.Max(100, maneuver.rect.width - 130);
        turnStreet.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        turnDistance.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        turnAction.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        currentStreet.text = street;
        turnStreet.text = nextStreet;
        turnDistance.text = metersToTurn < 260 ? $"{Mathf.Max(25, Mathf.RoundToInt(metersToTurn * 3.28084f / 25f) * 25)} ft" : $"{metersToTurn / 1609.344f:0.0} mi";
        turnAction.text = modifier == "uturn" ? "Make a U-turn" : modifier.Contains("left") ? "Turn left" : modifier.Contains("right") ? "Turn right" : "Continue straight";
        if (modifier == "arrive")
            turnAction.text = "Destination ahead";
        if (mapLoading)
            turnAction.text = roadmapView ? "Loading Google road map..." : "Loading Google 3D map...";
        else if (paused)
            turnAction.text = "Drive paused";
        if (arrived)
        {
            turnDistance.text = "Arrived";
            turnAction.text = "Destination reached";
        }
        turnIcon.Symbol = modifier;
        turnIcon.gameObject.SetActive(!arrived);
        attribution.text = googleMap ? "Route data (c) OpenStreetMap contributors / ODbL" : "Map data (c) OpenStreetMap contributors / ODbL";
        if (googleMap)
        {
            foreach (MapLabel label in labels)
                label.Rect.gameObject.SetActive(false);
            roadmapCredits = roadmapView;
            PositionGoogleCredits();
        }
        else
            UpdateLabels();
    }

    private void PositionGoogleCredits()
    {
        if (creditsDocument == null)
            creditsDocument = CesiumCreditSystem.GetDefaultCreditSystem().GetComponent<UIDocument>();
        if (creditsDocument == null || creditsDocument.rootVisualElement == null)
            return;
        creditsDocument.rootVisualElement.style.display = visible ? UnityEngine.UIElements.DisplayStyle.Flex : UnityEngine.UIElements.DisplayStyle.None;
        creditsDocument.sortingOrder = 160;
        var credits = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(creditsDocument.rootVisualElement, "OnScreenCredits");
        if (credits == null)
            return;
        credits.style.width = UnityEngine.UIElements.StyleKeyword.Auto;
        credits.style.right = UnityEngine.UIElements.StyleKeyword.Auto;
        credits.style.backgroundColor = new Color(0.08f, 0.12f, 0.12f, 0.92f);
        credits.style.color = Color.white;
        float panelHeight = creditsDocument.rootVisualElement.resolvedStyle.height;
        if (!float.IsFinite(panelHeight) || panelHeight <= 0)
            return;
        float scale = panelHeight / Screen.height;
        float canvasScale = safeArea.GetComponentInParent<Canvas>().scaleFactor;
        float creditScale = Mathf.Max(1f, canvasScale) * scale;
        credits.style.bottom = (Screen.safeArea.yMin + 96f * canvasScale) * scale;
        float creditWidth = Mathf.Min((roadmapCredits ? 360f : 520f) * canvasScale, Screen.safeArea.width - 28f * canvasScale);
        credits.style.left = (Screen.safeArea.xMin + (Screen.safeArea.width - creditWidth) * 0.5f) * scale;
        credits.style.maxWidth = creditWidth * scale;
        credits.style.height = StyleKeyword.Auto;
        credits.style.flexWrap = roadmapCredits ? UnityEngine.UIElements.Wrap.NoWrap : UnityEngine.UIElements.Wrap.Wrap;
        credits.style.paddingLeft = credits.style.paddingRight = 4f * creditScale;
        credits.style.paddingTop = credits.style.paddingBottom = 3f * creditScale;
        credits.style.fontSize = 12f * creditScale;
        SizeCreditContent(credits, creditScale);

        var popup = UnityEngine.UIElements.UQueryExtensions.Q<VisualElement>(creditsDocument.rootVisualElement, "PopupCredits");
        if (popup == null)
            return;
        popup.style.width = StyleKeyword.Auto;
        popup.style.height = StyleKeyword.Auto;
        popup.style.top = StyleKeyword.Auto;
        popup.style.right = StyleKeyword.Auto;
        popup.style.left = credits.style.left;
        float creditHeight = float.IsFinite(credits.resolvedStyle.height) ? credits.resolvedStyle.height : 40f * creditScale;
        popup.style.bottom = credits.style.bottom.value.value + creditHeight + 8f * creditScale;
        popup.style.maxWidth = credits.style.maxWidth;
        popup.style.paddingLeft = popup.style.paddingRight = 8f * creditScale;
        popup.style.paddingTop = popup.style.paddingBottom = 4f * creditScale;
        popup.style.fontSize = 12f * creditScale;
        popup.style.borderTopLeftRadius = popup.style.borderTopRightRadius = 4f;
        popup.style.borderBottomLeftRadius = popup.style.borderBottomRightRadius = 4f;
        SizeCreditContent(popup, creditScale);
    }

    private static void SizeCreditContent(VisualElement container, float scale)
    {
        foreach (VisualElement child in container.Children())
        {
            Texture2D image = child.style.backgroundImage.value.texture;
            if (image != null && image.height > 0)
            {
                float height = 18f * scale;
                child.style.height = height;
                child.style.width = height * image.width / image.height;
                child.style.flexShrink = 0;
                child.style.marginLeft = child.style.marginRight = 10f * scale;
                child.style.marginTop = 10f * scale;
                child.style.marginBottom = 5f * scale;
            }
            else if (child is UnityEngine.UIElements.Label)
            {
                child.style.fontSize = 12f * scale;
                child.style.whiteSpace = UnityEngine.UIElements.WhiteSpace.Normal;
                child.style.flexShrink = 1;
                child.style.marginTop = child.style.marginBottom = 0;
                child.style.paddingTop = child.style.paddingBottom = 0;
                child.style.minWidth = 0;
                child.style.maxWidth = new UnityEngine.UIElements.Length(100, UnityEngine.UIElements.LengthUnit.Percent);
            }
            SizeCreditContent(child, scale);
        }
    }

    private void AddLabel(string name, double longitude, double latitude, Func<double, double, Vector3> project)
    {
        Text label = Label(safeArea, name, 14, new Color32(75, 94, 89, 255), TextAnchor.MiddleCenter);
        Place(label.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180, 25), new Vector2(0.5f, 0.5f));
        Outline outline = label.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1, 1, 1, 0.85f);
        outline.effectDistance = new Vector2(1, -1);
        labels.Add(new MapLabel { Position = project(longitude, latitude) + Vector3.up * 3, Rect = label.rectTransform });
    }

    private void UpdateLabels()
    {
        var occupied = new List<Vector2>();
        foreach (MapLabel label in labels)
        {
            Vector3 screen = camera.WorldToScreenPoint(label.Position);
            bool visible = screen.z > 0 && screen.x > 100 && screen.x < camera.pixelWidth - 100
                && screen.y > 225 && screen.y < camera.pixelHeight - 225;
            foreach (Vector2 other in occupied)
                if (Mathf.Abs(other.x - screen.x) < 130 && Mathf.Abs(other.y - screen.y) < 26)
                    visible = false;
            label.Rect.gameObject.SetActive(visible);
            if (!visible)
                continue;
            Vector2 viewport = new Vector2(screen.x / camera.pixelWidth, screen.y / camera.pixelHeight);
            Vector2 areaSize = safeArea.anchorMax - safeArea.anchorMin;
            Vector2 point = new Vector2((viewport.x - safeArea.anchorMin.x) / areaSize.x - 0.5f,
                (viewport.y - safeArea.anchorMin.y) / areaSize.y - 0.5f);
            label.Rect.anchoredPosition = Vector2.Scale(point, safeArea.rect.size);
            occupied.Add(screen);
        }
    }

    private Text Label(Transform parent, string content, int size, Color color, TextAnchor alignment)
    {
        RectTransform rect = Rect("Label", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(10, size - 6);
        text.resizeTextMaxSize = size;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static NavigationIconGraphic Icon(Transform parent, string symbol, Color color)
    {
        RectTransform rect = Rect("Icon", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        NavigationIconGraphic icon = rect.gameObject.AddComponent<NavigationIconGraphic>();
        icon.Symbol = symbol;
        icon.color = color;
        icon.raycastTarget = false;
        return icon;
    }

    private static RectTransform Panel(string name, Transform parent, Vector2 minimum, Vector2 maximum, Vector2 position, Vector2 size, Color color)
    {
        RectTransform rect = Rect(name, parent, minimum, maximum, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 minimum, Vector2 maximum, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = minimum;
        rect.anchorMax = maximum;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}

[RequireComponent(typeof(CanvasRenderer))]
internal sealed class NavigationIconGraphic : MaskableGraphic
{
    private string symbol;
    public string Symbol
    {
        get => symbol;
        set
        {
            if (symbol == value)
                return;
            symbol = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        if (symbol == "plus" || symbol == "minus")
        {
            Stroke(helper, new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.5f));
            if (symbol == "plus")
                Stroke(helper, new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.9f));
        }
        else if (symbol == "pause")
        {
            Stroke(helper, new Vector2(0.3f, 0.1f), new Vector2(0.3f, 0.9f), 0.18f);
            Stroke(helper, new Vector2(0.7f, 0.1f), new Vector2(0.7f, 0.9f), 0.18f);
        }
        else if (symbol == "play")
            Triangle(helper, new Vector2(0.15f, 0.05f), new Vector2(0.95f, 0.5f), new Vector2(0.15f, 0.95f));
        else if (symbol == "next" || symbol == "previous")
        {
            bool next = symbol == "next";
            float start = next ? 0.08f : 0.92f;
            float end = next ? 0.8f : 0.2f;
            Triangle(helper, new Vector2(start, 0.1f), new Vector2(end, 0.5f), new Vector2(start, 0.9f));
            float bar = next ? 0.93f : 0.07f;
            Stroke(helper, new Vector2(bar, 0.1f), new Vector2(bar, 0.9f), 0.12f);
        }
        else if (symbol == "speaker" || symbol == "mute")
        {
            Stroke(helper, new Vector2(0.12f, 0.35f), new Vector2(0.12f, 0.65f), 0.2f);
            Triangle(helper, new Vector2(0.15f, 0.5f), new Vector2(0.52f, 0.12f), new Vector2(0.52f, 0.88f));
            Stroke(helper, new Vector2(0.7f, 0.28f), new Vector2(0.7f, 0.72f), 0.08f);
            if (symbol == "mute")
                Stroke(helper, new Vector2(0.68f, 0.28f), new Vector2(0.95f, 0.72f), 0.1f);
            else
                Stroke(helper, new Vector2(0.9f, 0.16f), new Vector2(0.9f, 0.84f), 0.08f);
        }
        else if (symbol == "follow" || symbol == "compass")
        {
            Triangle(helper, new Vector2(0.5f, 1), new Vector2(0.12f, 0.05f), new Vector2(0.5f, 0.25f));
            Triangle(helper, new Vector2(0.5f, 1), new Vector2(0.5f, 0.25f), new Vector2(0.88f, 0.05f));
            if (symbol == "compass")
                Stroke(helper, new Vector2(0.15f, 0), new Vector2(0.85f, 0), 0.08f);
        }
        else if (symbol == "uturn")
        {
            Stroke(helper, new Vector2(0.2f, 0.1f), new Vector2(0.2f, 0.75f));
            Stroke(helper, new Vector2(0.2f, 0.75f), new Vector2(0.8f, 0.75f));
            Stroke(helper, new Vector2(0.8f, 0.75f), new Vector2(0.8f, 0.25f));
            Triangle(helper, new Vector2(0.58f, 0.3f), new Vector2(0.8f, 0.02f), new Vector2(1, 0.3f));
        }
        else if (symbol != null && (symbol.Contains("left") || symbol.Contains("right")))
        {
            bool left = symbol.Contains("left");
            float start = left ? 0.72f : 0.28f;
            float end = left ? 0.1f : 0.9f;
            Stroke(helper, new Vector2(start, 0.05f), new Vector2(start, 0.7f));
            Stroke(helper, new Vector2(start, 0.7f), new Vector2(end, 0.7f));
            Triangle(helper, new Vector2(end, 0.7f), new Vector2(left ? 0.38f : 0.62f, 0.98f), new Vector2(left ? 0.38f : 0.62f, 0.42f));
        }
        else
        {
            Stroke(helper, new Vector2(0.5f, 0.02f), new Vector2(0.5f, 0.75f));
            Triangle(helper, new Vector2(0.5f, 1), new Vector2(0.2f, 0.65f), new Vector2(0.8f, 0.65f));
        }
    }

    private Vector3 Point(Vector2 point) => new Vector3(rectTransform.rect.xMin + point.x * rectTransform.rect.width, rectTransform.rect.yMin + point.y * rectTransform.rect.height);

    private void Stroke(VertexHelper helper, Vector2 start, Vector2 end, float width = 0.13f)
    {
        Vector2 direction = (end - start).normalized;
        Vector2 normal = new Vector2(-direction.y, direction.x) * width * 0.5f;
        int offset = helper.currentVertCount;
        foreach (Vector2 point in new[] { start - normal, start + normal, end + normal, end - normal })
            helper.AddVert(Point(point), color, Vector2.zero);
        helper.AddTriangle(offset, offset + 1, offset + 2);
        helper.AddTriangle(offset, offset + 2, offset + 3);
    }

    private void Triangle(VertexHelper helper, Vector2 first, Vector2 second, Vector2 third)
    {
        int offset = helper.currentVertCount;
        helper.AddVert(Point(first), color, Vector2.zero);
        helper.AddVert(Point(second), color, Vector2.zero);
        helper.AddVert(Point(third), color, Vector2.zero);
        helper.AddTriangle(offset, offset + 1, offset + 2);
    }
}