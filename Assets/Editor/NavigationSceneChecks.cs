using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class NavigationSceneChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Navigation/Preview Downtown Display")]
    public static void Preview()
    {
        car_navigation controller = UnityEngine.Object.FindAnyObjectByType<car_navigation>();
        if (controller == null)
            throw new InvalidOperationException("Open Assets/Scenes/SampleScene.unity first.");
        controller.BuildDisplay();
        EditorApplication.QueuePlayerLoopUpdate();
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }

    public static void Run()
    {
        SessionState.SetBool("NavigationGoogleLiveCheck", false);
        SessionState.SetBool("NavigationChecksPending", true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        car_navigation controller = UnityEngine.Object.FindAnyObjectByType<car_navigation>();
        Require(Field<car_navigation.MapSource>(controller, "mapSource") == car_navigation.MapSource.GooglePhotorealistic3DTiles,
            "Google 3D Tiles is the saved scene default.");
        typeof(car_navigation).GetField("googleApiKeyFile", PrivateInstance).SetValue(controller, "NavigationChecksMissingKey.txt");
        EditorApplication.update -= CheckWhenReady;
        EditorApplication.update += CheckWhenReady;
        EditorApplication.EnterPlaymode();
    }

    public static void RunGoogleLive()
    {
        SessionState.SetBool("NavigationGoogleLiveCheck", true);
        SessionState.SetBool("NavigationChecksPending", true);
        SessionState.SetFloat("NavigationGoogleCheckDeadline", (float)EditorApplication.timeSinceStartup + 120f);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.update -= CheckWhenReady;
        EditorApplication.update += CheckWhenReady;
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void ResumeChecks()
    {
        if (SessionState.GetBool("NavigationChecksPending", false))
            EditorApplication.update += CheckWhenReady;
    }

    private static void CheckWhenReady()
    {
        if (!EditorApplication.isPlaying || Time.frameCount < 8)
            return;
        try
        {
            if (SessionState.GetBool("NavigationGoogleLiveCheck", false))
            {
                if (!CheckGoogleLive())
                    return;
            }
            else
                Check();
            EditorApplication.update -= CheckWhenReady;
            SessionState.EraseBool("NavigationChecksPending");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            EditorApplication.update -= CheckWhenReady;
            SessionState.EraseBool("NavigationChecksPending");
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static bool CheckGoogleLive()
    {
        car_navigation controller = UnityEngine.Object.FindAnyObjectByType<car_navigation>();
        Require(controller != null, "Live Google check requires the navigation controller.");
        string fallback = Field<string>(controller, "googleFallbackReason");
        Require(string.IsNullOrEmpty(fallback), "LIVE GOOGLE CHECK: " + fallback);
        Cesium3DTileset tileset = Field<Cesium3DTileset>(controller, "googleTileset");
        Require(tileset != null, "LIVE GOOGLE CHECK: No Google tileset; configure the local key file.");
        Require(EditorApplication.timeSinceStartup < SessionState.GetFloat("NavigationGoogleCheckDeadline", 0), "LIVE GOOGLE CHECK: Loading timed out.");
        if (Field<bool>(controller, "googleMapLoading") || tileset.GetComponentsInChildren<MeshRenderer>().Length == 0)
            return false;

        Vector3[] route = Field<Vector3[]>(controller, "route");
        Require(route.All(point => float.IsFinite(point.y)), "Live terrain heights are finite.");
        Transform vehicle = Field<Transform>(controller, "vehicle");
        Vector3 before = vehicle.position;
        for (int tick = 0; tick < 90; tick++)
            controller.Simulate(1f / 30f);
        Require(Vector3.Distance(before, vehicle.position) > 3f, "Car moves after Google terrain loads.");
        Invoke(controller, "UpdateCamera", true);
        string output = Environment.GetEnvironmentVariable("NAVIGATION_CHECK_OUTPUT") ?? "Temp/NavigationChecks";
        Directory.CreateDirectory(output);
        Capture(controller, 1440, 900, Path.Combine(output, "google-desktop.png"));
        Capture(controller, 720, 1280, Path.Combine(output, "google-portrait.png"));
        CheckArrival(controller);
        Debug.Log($"LIVE GOOGLE CHECK PASSED: {route.Length} road heights, {tileset.GetComponentsInChildren<MeshRenderer>().Length} rendered tile meshes; one-way arrival and desktop/portrait captures verified.");
        return true;
    }

    private static void Check()
    {
        car_navigation controller = UnityEngine.Object.FindAnyObjectByType<car_navigation>();
        Require(controller != null, "Scene has a navigation controller.");
        controller.BuildDisplay();
        Require(Field<Cesium3DTileset>(controller, "googleTileset") == null, "Missing key uses the offline map.");
        Vector3[] route = Field<Vector3[]>(controller, "route");
        float length = Field<float>(controller, "routeLength");
        Require(route.Length > 30 && length > 800 && length < 1200, "One-way downtown route loaded.");
        Require(Vector3.Distance(route[0], route[route.Length - 1]) > 400, "Destination is distinct from the starting point.");
        Require(GameObject.Find("Building Roofs").GetComponent<MeshFilter>().sharedMesh.vertexCount > 1000, "Real building geometry is present.");
        Require(GameObject.Find("Streets").GetComponent<MeshFilter>().sharedMesh.vertexCount > 1000, "Street geometry is present.");

        Transform vehicle = Field<Transform>(controller, "vehicle");
        Vector3 start = vehicle.position;
        for (int tick = 0; tick < 300; tick++)
            controller.Simulate(1f / 30f);
        Require(Vector3.Distance(start, vehicle.position) > 20, "Car cruises along the route.");
        controller.TogglePause();
        Vector3 pausedPosition = vehicle.position;
        controller.Simulate(10);
        Require(Vector3.Distance(pausedPosition, vehicle.position) < 0.001f, "Pause stops the car.");
        controller.TogglePause();

        CheckArrival(controller);
        string output = Environment.GetEnvironmentVariable("NAVIGATION_CHECK_OUTPUT") ?? "Temp/NavigationChecks";
        Directory.CreateDirectory(output);
        Invoke(controller, "UpdateCamera", true);
        Capture(controller, 1440, 900, Path.Combine(output, "arrival-desktop.png"));
        Capture(controller, 720, 1280, Path.Combine(output, "arrival-portrait.png"));
        FieldInfo segment = typeof(car_navigation).GetField("segment", PrivateInstance);
        segment.SetValue(controller, 0);
        typeof(car_navigation).GetField("segmentDistance", PrivateInstance).SetValue(controller, 35f);
        typeof(car_navigation).GetField("arrived", PrivateInstance).SetValue(controller, false);
        Invoke(controller, "Advance", length * 3f);
        Require(Field<bool>(controller, "arrived") && Field<int>(controller, "segment") == route.Length - 2,
            "A frame advancing past the destination clamps there instead of wrapping.");
        segment.SetValue(controller, 0);
        typeof(car_navigation).GetField("segmentDistance", PrivateInstance).SetValue(controller, 35f);
        typeof(car_navigation).GetField("arrived", PrivateInstance).SetValue(controller, false);
        controller.Simulate(3);
        controller.Simulate(0);
        Invoke(controller, "UpdateCamera", true);

        Capture(controller, 1440, 900, Path.Combine(output, "desktop.png"));
        Capture(controller, 720, 1280, Path.Combine(output, "portrait.png"));

        Button[] buttons = UnityEngine.Object.FindObjectsByType<Button>();
        Require(buttons.Length == 4 && buttons.All(button => button.targetGraphic.raycastTarget), "All four map controls can receive pointer input.");
        Button pause = buttons.Single(button => button.name == "Pause / resume drive");
        bool previousPause = Field<bool>(controller, "paused");
        pause.onClick.Invoke();
        Require(Field<bool>(controller, "paused") != previousPause, "Pause button is wired.");
        buttons.Single(button => button.name == "North-up / heading-up").onClick.Invoke();
        Require(Field<bool>(controller, "northUp"), "Orientation button is wired.");
        Button zoom = buttons.Single(button => button.name == "Zoom in");
        for (int click = 0; click < 20; click++)
            zoom.onClick.Invoke();
        Require(Mathf.Approximately(Field<float>(controller, "cameraHeight"), 90), "Zoom is clamped.");

        CheckGoogleMapSource(controller);

        Debug.Log($"NAVIGATION CHECKS PASSED: {route.Length} points, {length:0} meters, non-looping arrival; Google configuration, key loading, open-route terrain alignment, and fallback checked without network requests; screenshots: {output}");
    }

    private static void CheckArrival(car_navigation controller)
    {
        Vector3[] route = Field<Vector3[]>(controller, "route");
        Transform vehicle = Field<Transform>(controller, "vehicle");
        int previousSegment = Field<int>(controller, "segment");
        for (int tick = 0; tick < 42000 && !Field<bool>(controller, "arrived"); tick++)
        {
            controller.Simulate(1f / 30f);
            int currentSegment = Field<int>(controller, "segment");
            Require(currentSegment >= previousSegment, "Route progress never wraps to an earlier segment.");
            previousSegment = currentSegment;
            Require(float.IsFinite(vehicle.position.x) && float.IsFinite(vehicle.position.z), "Position stays finite.");
            Require(Field<float>(controller, "segmentDistance") <= Vector3.Distance(route[currentSegment], route[currentSegment + 1]) + 0.01f,
                "Position stays on the current road segment.");
        }
        Require(Field<bool>(controller, "arrived"), "The vehicle arrives instead of continuing to cruise.");
        Vector3 destination = route[route.Length - 1] + Vector3.up * 3;
        Require(Vector3.Distance(vehicle.position, destination) < 0.01f && Field<float>(controller, "speed") == 0,
            "The vehicle stops exactly at the destination.");
        controller.Simulate(600);
        controller.TogglePause();
        controller.Simulate(600);
        Require(Vector3.Distance(vehicle.position, destination) < 0.01f && Field<float>(controller, "speed") == 0,
            "Waiting or pressing resume after arrival cannot restart a lap.");
        object next = typeof(car_navigation).GetMethod("NextTurn", PrivateInstance)
            .Invoke(controller, new object[] { Field<float>(controller, "routeLength") });
        Require(ReferenceEquals(next, Field<object>(controller, "destinationStep")), "The final instruction is arrival, not the first turn again.");
        Invoke(controller, "RefreshHud");
        object hud = Field<object>(controller, "hud");
        Require(Field<Text>(hud, "turnDistance").text == "Arrived" && Field<Text>(hud, "time").text == "0 min" &&
            Field<Text>(hud, "distance").text == "0.0 mi" && Field<Text>(hud, "speed").text == "0",
            "Arrival readouts show completed guidance and zero time, distance, and speed.");
        Require(Mathf.Approximately(Field<RectTransform>(hud, "progress").anchorMax.x, 1), "Trip progress reaches 100 percent.");
    }

    private static void CheckGoogleMapSource(car_navigation controller)
    {
        string directory = Path.Combine(Application.temporaryCachePath, "NavigationKeyChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            MethodInfo readKey = typeof(car_navigation).GetMethod("TryReadGoogleApiKey", BindingFlags.Static | BindingFlags.NonPublic);
            object[] arguments = { directory, "key.txt", null };
            Require(!(bool)readKey.Invoke(null, arguments), "Missing key is rejected.");
            foreach (string content in new[] { "", " \n", "YOUR_GOOGLE_MAP_TILES_API_KEY", "two\nkeys", "two keys" })
            {
                File.WriteAllText(Path.Combine(directory, "key.txt"), content);
                Require(!(bool)readKey.Invoke(null, arguments), "Empty, placeholder, or multiline keys are rejected.");
                Require((string)arguments[2] == "", "Rejected credentials are not returned.");
            }
            File.WriteAllText(Path.Combine(directory, "key.txt"), "  synthetic-test-key\r\n");
            Require((bool)readKey.Invoke(null, arguments) && (string)arguments[2] == "synthetic-test-key", "Key whitespace is trimmed.");
            arguments[1] = "../key.txt";
            Require(!(bool)readKey.Invoke(null, arguments), "Key filename cannot traverse directories.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        Cesium3DTileset tileset = (Cesium3DTileset)typeof(car_navigation).GetMethod("CreateGoogleTileset", PrivateInstance)
            .Invoke(controller, new object[] { "synthetic+test&key" });
        Require(!tileset.gameObject.activeSelf, "Tileset can be configured without making requests.");
        Require(tileset.tilesetSource == CesiumDataSource.FromUrl &&
            tileset.url == "https://tile.googleapis.com/v1/3dtiles/root.json?key=synthetic%2Btest%26key", "Google endpoint and key escaping are correct.");
        Require(tileset.showCreditsOnScreen && !tileset.updateInEditor, "Attribution is enabled and editor streaming is disabled.");
        Require((tileset.gameObject.hideFlags & HideFlags.DontSave) == HideFlags.DontSave, "The key-bearing tileset cannot be saved to a scene.");
        Invoke(Field<object>(controller, "hud"), "PositionGoogleCredits");
        var document = CesiumCreditSystem.GetDefaultCreditSystem().GetComponent<UnityEngine.UIElements.UIDocument>();
        var credits = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(document.rootVisualElement, "OnScreenCredits");
        Require(credits != null && credits.style.backgroundColor.value.r < 0.2f && credits.style.color.value.r > 0.9f,
            "Google attribution has a high-contrast background.");
        Require(credits.style.width.keyword == UnityEngine.UIElements.StyleKeyword.Auto &&
            credits.style.right.keyword == UnityEngine.UIElements.StyleKeyword.Auto && document.sortingOrder > 100,
            "Attribution sizes to its content instead of stretching into a full-width strip and stays above the HUD.");

        Vector3[] route = Field<Vector3[]>(controller, "route");
        double3[] coordinates = Field<double3[]>(controller, "routeCoordinates");
        var sampled = new CesiumSampleHeightResult
        {
            longitudeLatitudeHeightPositions = coordinates.Select(position => new double3(position.x, position.y, 25)).ToArray(),
            sampleSuccess = Enumerable.Repeat(true, route.Length).ToArray()
        };
        MethodInfo applyHeights = typeof(car_navigation).GetMethod("TryApplyRouteHeights", PrivateInstance);
        sampled.sampleSuccess = new bool[route.Length];
        Require(!(bool)applyHeights.Invoke(controller, new object[] { sampled }) && route.All(point => point.y == 0),
            "An entirely missing height query does not corrupt the route.");
        sampled.sampleSuccess[0] = true;
        Require(!(bool)applyHeights.Invoke(controller, new object[] { sampled }), "One valid height is insufficient for interpolation.");
        sampled.sampleSuccess[route.Length / 2] = true;
        Require(!(bool)applyHeights.Invoke(controller, new object[] { sampled }) && route.All(point => point.y == 0),
            "Long unsampled sections are rejected without partially modifying the route.");
        float[] cumulative = Field<float[]>(controller, "cumulativeDistances");
        int shortGap = Enumerable.Range(1, route.Length - 2).First(index => cumulative[index + 1] - cumulative[index - 1] < 100);
        sampled.sampleSuccess = Enumerable.Repeat(true, route.Length).ToArray();
        sampled.sampleSuccess[shortGap] = false;
        Require((bool)applyHeights.Invoke(controller, new object[] { sampled }) && float.IsFinite(route[shortGap].y),
            "A short missing sample is interpolated rather than disabling Google Maps.");
        sampled.sampleSuccess[shortGap] = true;
        sampled.longitudeLatitudeHeightPositions[shortGap].z = double.NaN;
        Require((bool)applyHeights.Invoke(controller, new object[] { sampled }) && float.IsFinite(route[shortGap].y),
            "Non-finite samples are treated as gaps and never applied to the route.");
        sampled.longitudeLatitudeHeightPositions[shortGap].z = 25;
        sampled.sampleSuccess[0] = false;
        Require((bool)applyHeights.Invoke(controller, new object[] { sampled }) == (cumulative[1] <= 25f),
            "A missing starting height is filled only from a sample within 25 meters, never from the destination.");
        sampled.sampleSuccess[0] = true;
        sampled.sampleSuccess[route.Length - 1] = false;
        Require((bool)applyHeights.Invoke(controller, new object[] { sampled }) == (cumulative[route.Length - 1] - cumulative[route.Length - 2] <= 25f),
            "A missing destination height is filled only from a nearby sample, never from the starting point.");
        sampled.sampleSuccess[0] = sampled.sampleSuccess[route.Length - 1] = true;
        Require((bool)applyHeights.Invoke(controller, new object[] { sampled }), "Valid sampled heights are applied.");
        Require(route.All(point => point.y > 24 && point.y < 26), "Route uses terrain height in the georeference frame.");
        sampled.longitudeLatitudeHeightPositions[route.Length - 1].z = 45;
        Require((bool)applyHeights.Invoke(controller, new object[] { sampled }) && route[route.Length - 1].y > 44 && route[0].y < 26,
            "The two endpoint heights remain independent.");
        Require(Vector3.Distance(route[0], route[route.Length - 1]) > 400, "Terrain sampling does not close the open route.");
        Invoke(controller, "DrawRoute");
        Invoke(controller, "PlaceVehicle", 1f);
        Require(Field<Transform>(controller, "vehicle").position.y > 27, "Car follows the raised road surface.");
        Mesh ribbon = Field<GameObject>(controller, "routeRibbon").GetComponent<MeshFilter>().sharedMesh;
        Require(ribbon.vertices.All(point => point.y > 24), "Route mesh preserves terrain elevation.");

        typeof(car_navigation).GetField("googleTileset", PrivateInstance).SetValue(controller, tileset);
        typeof(car_navigation).GetField("googleMapLoading", PrivateInstance).SetValue(controller, true);
        typeof(car_navigation).GetField("googleLoadDeadline", PrivateInstance).SetValue(controller, Time.realtimeSinceStartup + 90f);
        Invoke(controller, "UpdateGoogleMap");
        Require(Field<object>(controller, "routeHeightRequest") == null && Field<bool>(controller, "googleMapLoading"),
            "Height sampling waits until Cesium creates its first tile.");
        Invoke(controller, "OnGoogleTilesetLoadFailure", new Cesium3DTilesetLoadFailureDetails(tileset, Cesium3DTilesetLoadType.TilesetJson, 403, "Synthetic failure"));
        Invoke(controller, "UpdateGoogleMap");
        Require(Field<Cesium3DTileset>(controller, "googleTileset") == null && !Field<bool>(controller, "googleMapLoading"), "Rejected Google requests leave loading state and use the offline map.");
        Require(route.All(point => point.y == 0) && Field<Transform>(controller, "vehicle").gameObject.activeSelf, "Offline fallback restores a visible, flat route and car.");
    }

    private static void Capture(car_navigation controller, int width, int height, string path)
    {
        Camera camera = Camera.main;
        var target = new RenderTexture(width, height, 24);
        target.Create();
        camera.targetTexture = target;
        Canvas.ForceUpdateCanvases();
        Invoke(controller, "RefreshHud");
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        Color32[] pixels = image.GetPixels32();
        int routePixels = pixels.Count(pixel => pixel.b > 200 && pixel.g > 100 && pixel.g < 170 && pixel.r < 80);
        Vector3 markerScreen = camera.WorldToScreenPoint(Field<Transform>(controller, "vehicle").position);
        int arrowPixels = 0;
        for (int row = Mathf.Max(0, (int)markerScreen.y - 55); row < Mathf.Min(height, (int)markerScreen.y + 55); row++)
            for (int column = Mathf.Max(0, (int)markerScreen.x - 55); column < Mathf.Min(width, (int)markerScreen.x + 55); column++)
            {
                Color32 pixel = pixels[row * width + column];
                if (pixel.b > 170 && pixel.r < 45 && pixel.g < 100)
                    arrowPixels++;
            }
        Require(routePixels > 100, $"Blue route highlight is visible at {width}x{height}.");
        Require(arrowPixels > 15, $"Blue navigation arrow is visible at its current position at {width}x{height}.");
        Require(pixels.Select(pixel => (pixel.r << 16) | (pixel.g << 8) | pixel.b).Distinct().Count() > 100, "Render is not blank.");

        Button button = UnityEngine.Object.FindObjectsByType<Button>().First();
        RectTransform buttonRect = (RectTransform)button.transform;
        Vector2 buttonCenter = RectTransformUtility.WorldToScreenPoint(camera, buttonRect.TransformPoint(buttonRect.rect.center));
        var pointer = new PointerEventData(EventSystem.current) { position = buttonCenter };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Require(hits.Any(hit => hit.gameObject == button.gameObject), $"Button hit testing works at {width}x{height}.");

        RenderTexture.active = previous;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
    private static void Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, arguments);
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}