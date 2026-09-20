using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using CesiumForUnity;
using UIDocument = UnityEngine.UIElements.UIDocument;

internal sealed class NavigationHud
{
    private static readonly Color Ink = new Color32(27, 48, 48, 255);
    private static readonly Color Teal = new Color32(0, 147, 134, 255);
    private readonly Font font;
    private readonly Camera camera;
    private readonly RectTransform safeArea;
    private readonly RectTransform maneuver;
    private readonly RectTransform progress;
    private readonly Text turnDistance;
    private readonly Text turnStreet;
    private readonly Text turnAction;
    private readonly Text currentStreet;
    private readonly Text speed;
    private readonly Text time;
    private readonly Text arrival;
    private readonly Text distance;
    private readonly Text state;
    private readonly NavigationIconGraphic turnIcon;
    private readonly NavigationIconGraphic pauseIcon;
    private readonly NavigationIconGraphic orientationIcon;
    private readonly List<MapLabel> labels = new List<MapLabel>();
    private readonly Text attribution;
    private UIDocument creditsDocument;

    private sealed class MapLabel
    {
        public Vector3 Position;
        public RectTransform Rect;
    }

    public NavigationHud(Transform parent, Camera camera, Font font, car_navigation controller, Func<double, double, Vector3> project)
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
        scaler.referenceResolution = new Vector2(1280, 800);
        scaler.matchWidthOrHeight = 0.5f;
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

        RectTransform header = Panel("Location Header", safeArea, new Vector2(0, 1), Vector2.one, Vector2.zero, new Vector2(0, 62), Color.white);
        header.pivot = new Vector2(0.5f, 1);
        Text city = Label(header, "SAN FRANCISCO", 24, Ink, TextAnchor.MiddleLeft);
        Place(city.rectTransform, new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(300, 40), new Vector2(0, 0.5f));
        state = Label(header, "DOWNTOWN ROUTE", 14, Teal, TextAnchor.MiddleRight);
        Place(state.rectTransform, new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(220, 36), new Vector2(1, 0.5f));

        maneuver = Panel("Next Maneuver", safeArea, Vector2.up, Vector2.up, new Vector2(20, -82), new Vector2(430, 124), Ink);
        maneuver.pivot = Vector2.up;
        turnIcon = Icon(maneuver, "right", Color.white);
        Place(turnIcon.rectTransform, new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(66, 66), new Vector2(0, 0.5f));
        turnDistance = Label(maneuver, "", 36, Color.white, TextAnchor.MiddleLeft);
        Place(turnDistance.rectTransform, Vector2.up, new Vector2(110, -12), new Vector2(290, 44), Vector2.up);
        turnAction = Label(maneuver, "", 14, new Color32(143, 224, 208, 255), TextAnchor.MiddleLeft);
        Place(turnAction.rectTransform, Vector2.up, new Vector2(110, -55), new Vector2(290, 24), Vector2.up);
        turnStreet = Label(maneuver, "", 23, Color.white, TextAnchor.MiddleLeft);
        Place(turnStreet.rectTransform, Vector2.up, new Vector2(110, -81), new Vector2(290, 32), Vector2.up);

        RectTransform controls = Rect("Map Controls", safeArea, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, -12), new Vector2(54, 246));
        controls.pivot = new Vector2(1, 0.5f);
        Control(controls, "plus", "Zoom in", 0, () => controller.Zoom(-30));
        Control(controls, "minus", "Zoom out", 64, () => controller.Zoom(30));
        orientationIcon = Control(controls, "follow", "North-up / heading-up", 128, controller.ToggleOrientation);
        pauseIcon = Control(controls, "pause", "Pause / resume drive", 192, controller.TogglePause);

        RectTransform footer = Panel("Trip Readout", safeArea, Vector2.zero, Vector2.right, Vector2.zero, new Vector2(0, 114), Ink);
        footer.pivot = new Vector2(0.5f, 0);
        progress = Panel("Trip Progress", footer, Vector2.up, Vector2.up, Vector2.zero, new Vector2(0, 4), Teal);
        progress.pivot = Vector2.up;
        RectTransform speedColumn = Rect("Speed", footer, Vector2.zero, new Vector2(0.25f, 1), Vector2.zero, Vector2.zero);
        RectTransform timeColumn = Rect("Time", footer, new Vector2(0.25f, 0), new Vector2(0.65f, 1), Vector2.zero, Vector2.zero);
        RectTransform distanceColumn = Rect("Distance", footer, new Vector2(0.65f, 0), Vector2.one, Vector2.zero, Vector2.zero);
        speed = Readout(speedColumn, "0", "MPH", out _);
        time = Readout(timeColumn, "", "", out arrival);
        distance = Readout(distanceColumn, "", "DISTANCE REMAINING", out _);

        RectTransform streetStrip = Panel("Current Street", safeArea, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 133), new Vector2(320, 40), Color.white);
        currentStreet = Label(streetStrip, "", 20, Ink, TextAnchor.MiddleCenter);
        attribution = Label(safeArea, "Map data (c) OpenStreetMap contributors / ODbL", 11, Ink, TextAnchor.MiddleLeft);
        Place(attribution.rectTransform, Vector2.zero, new Vector2(14, 184), new Vector2(420, 18), Vector2.zero);

        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var eventSystem = new GameObject("Navigation Input", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(parent, false);
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }

    public void Update(string street, string nextStreet, string modifier, float metersToTurn, float metersRemaining,
        float speedMph, float minutesRemaining, float completion, bool paused, bool northUp, bool googleMap, bool mapLoading, bool arrived)
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
        state.gameObject.SetActive(width > 620);
        currentStreet.text = street;
        turnStreet.text = nextStreet;
        turnDistance.text = metersToTurn < 260 ? $"{Mathf.Max(25, Mathf.RoundToInt(metersToTurn * 3.28084f / 25f) * 25)} ft" : $"{metersToTurn / 1609.344f:0.0} mi";
        turnAction.text = modifier == "uturn" ? "Make a U-turn" : modifier.Contains("left") ? "Turn left" : modifier.Contains("right") ? "Turn right" : "Continue straight";
        if (modifier == "arrive")
            turnAction.text = "Destination ahead";
        if (mapLoading)
            turnAction.text = "Loading Google 3D map...";
        if (arrived)
        {
            turnDistance.text = "Arrived";
            turnAction.text = "Destination reached";
        }
        turnIcon.Symbol = modifier;
        turnIcon.gameObject.SetActive(!arrived);
        speed.text = Mathf.RoundToInt(speedMph).ToString();
        time.text = arrived ? "0 min" : $"{Mathf.Max(1, Mathf.CeilToInt(minutesRemaining))} min";
        arrival.text = arrived ? "TRIP COMPLETE" : paused ? "DRIVE PAUSED" : $"ARRIVAL {DateTime.Now.AddMinutes(minutesRemaining):h:mm tt}";
        distance.text = $"{metersRemaining / 1609.344f:0.0} mi";
        progress.anchorMax = new Vector2(Mathf.Clamp01(completion), 1);
        pauseIcon.Symbol = paused ? "play" : "pause";
        pauseIcon.GetComponentInParent<Button>().interactable = !arrived;
        orientationIcon.Symbol = northUp ? "compass" : "follow";
        state.text = arrived ? "ARRIVED" : paused ? "DRIVE PAUSED" : googleMap ? "GOOGLE 3D" : "OFFLINE MAP";
        attribution.text = googleMap ? "Route data (c) OpenStreetMap contributors / ODbL" : "Map data (c) OpenStreetMap contributors / ODbL";
        if (googleMap)
        {
            foreach (MapLabel label in labels)
                label.Rect.gameObject.SetActive(false);
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
        creditsDocument.sortingOrder = 110;
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
        credits.style.bottom = (Screen.safeArea.yMin + 210f * canvasScale) * scale;
        credits.style.left = (Screen.safeArea.xMin + 14f * canvasScale) * scale;
        credits.style.maxWidth = Mathf.Min(520f * canvasScale, Screen.safeArea.width - 28f * canvasScale) * scale;
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
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safeArea, screen, camera, out Vector2 point);
            label.Rect.anchoredPosition = point;
            occupied.Add(screen);
        }
    }

    private Text Readout(Transform parent, string value, string caption, out Text subtitle)
    {
        Text text = Label(parent, value, 36, Color.white, TextAnchor.MiddleCenter);
        text.rectTransform.anchorMin = new Vector2(0.05f, 0.4f);
        text.rectTransform.anchorMax = new Vector2(0.95f, 0.93f);
        subtitle = Label(parent, caption, 12, new Color32(160, 189, 183, 255), TextAnchor.MiddleCenter);
        subtitle.rectTransform.anchorMin = new Vector2(0.05f, 0.08f);
        subtitle.rectTransform.anchorMax = new Vector2(0.95f, 0.4f);
        return text;
    }

    private NavigationIconGraphic Control(Transform parent, string symbol, string tooltip, float offset, UnityAction action)
    {
        RectTransform rect = Panel(tooltip, parent, Vector2.up, Vector2.up, new Vector2(0, -offset), new Vector2(54, 54), Color.white);
        rect.pivot = Vector2.up;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        button.targetGraphic.raycastTarget = true;
        button.onClick.AddListener(action);
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color32(212, 238, 229, 255);
        colors.pressedColor = new Color32(165, 214, 203, 255);
        button.colors = colors;
        NavigationIconGraphic icon = Icon(rect, symbol, Ink);
        icon.rectTransform.offsetMin = Vector2.one * 15;
        icon.rectTransform.offsetMax = Vector2.one * -15;
        RectTransform tooltipRect = Panel("Tooltip", rect, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(-12, 0), new Vector2(190, 36), Ink);
        tooltipRect.pivot = new Vector2(1, 0.5f);
        Label(tooltipRect, tooltip, 14, Color.white, TextAnchor.MiddleCenter);
        tooltipRect.gameObject.SetActive(false);
        EventTrigger trigger = rect.gameObject.AddComponent<EventTrigger>();
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => tooltipRect.gameObject.SetActive(true));
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => tooltipRect.gameObject.SetActive(false));
        trigger.triggers.Add(enter);
        trigger.triggers.Add(exit);
        return icon;
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