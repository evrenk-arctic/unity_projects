using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class NavigationSceneChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int liveMapStage;
    private static double liveMapReadySince = -1;
    private static float liveRoadmapDayBrightness;
    private static int liveMapSegment;
    private static float liveMapDistance;
    private static Vector3 liveMapPosition;

    [MenuItem("Navigation/Set Game View to 1920x720")]
    public static void SetGameViewSize()
    {
        Assembly editorAssembly = typeof(Editor).Assembly;
        Type sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes", true);
        object sizes = sizesType.BaseType.GetProperty("instance").GetValue(null);
        object group = sizesType.GetProperty("currentGroup").GetValue(sizes);
        Type groupType = group.GetType();
        Type sizeType = editorAssembly.GetType("UnityEditor.GameViewSize", true);
        int count = (int)groupType.GetMethod("GetBuiltinCount").Invoke(group, null)
            + (int)groupType.GetMethod("GetCustomCount").Invoke(group, null);
        int selected = count;
        for (int index = 0; index < count; index++)
        {
            object size = groupType.GetMethod("GetGameViewSize").Invoke(group, new object[] { index });
            if ((int)sizeType.GetProperty("width").GetValue(size) == 1920 &&
                (int)sizeType.GetProperty("height").GetValue(size) == 720 &&
                sizeType.GetProperty("sizeType").GetValue(size).ToString() == "FixedResolution")
            {
                selected = index;
                break;
            }
        }
        if (selected == count)
        {
            Type sizeMode = editorAssembly.GetType("UnityEditor.GameViewSizeType", true);
            object size = Activator.CreateInstance(sizeType, Enum.Parse(sizeMode, "FixedResolution"), 1920, 720, "Navigation 1920x720");
            groupType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
            if (!Application.isBatchMode)
                sizesType.GetMethod("SaveToHDD").Invoke(sizes, null);
        }
        Type gameViewType = editorAssembly.GetType("UnityEditor.GameView", true);
        EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, "Game", false);
        PropertyInfo selectedSize = gameViewType.GetProperty("selectedSizeIndex", PrivateInstance | BindingFlags.Public);
        selectedSize.SetValue(gameView, selected);
        Require((int)selectedSize.GetValue(gameView) == selected, "Game view uses the 1920x720 fixed-resolution preset.");
        gameView.Repaint();
    }

    private static void ConfigureDefaultGameView()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool("Navigation1920x720Configured", false))
            return;
        SetGameViewSize();
        SessionState.SetBool("Navigation1920x720Configured", true);
    }

    [MenuItem("Navigation/Preview Downtown Display")]
    public static void Preview()
    {
        car_navigation controller = UnityEngine.Object.FindAnyObjectByType<car_navigation>();
        if (controller == null)
            throw new InvalidOperationException("Open Assets/Scenes/SampleScene.unity first.");
        controller.BuildDisplay();
        SetGameViewSize();
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
        BeginGoogleLive(false);
    }

    public static void RunGoogleRoadmapLive()
    {
        BeginGoogleLive(true);
    }

    private static void BeginGoogleLive(bool startInRoadmap)
    {
        liveMapStage = 0;
        liveMapReadySince = -1;
        SessionState.SetBool("NavigationGoogleLiveCheck", true);
        SessionState.SetBool("NavigationStartsInRoadmap", startInRoadmap);
        SessionState.SetBool("NavigationChecksPending", true);
        SessionState.SetFloat("NavigationGoogleCheckDeadline", (float)EditorApplication.timeSinceStartup + 300f);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SetGameViewSize();
        typeof(car_navigation).GetField("topDownView", PrivateInstance).SetValue(UnityEngine.Object.FindAnyObjectByType<car_navigation>(), startInRoadmap);
        EditorApplication.update -= CheckWhenReady;
        EditorApplication.update += CheckWhenReady;
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void ResumeChecks()
    {
        if (SessionState.GetBool("NavigationChecksPending", false))
            EditorApplication.update += CheckWhenReady;
        if (!Application.isBatchMode)
            EditorApplication.delayCall += ConfigureDefaultGameView;
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
        bool roadmap = Field<bool>(controller, "topDownView");
        Cesium3DTileset tileset = Field<Cesium3DTileset>(controller, roadmap ? "googleRoadmapTileset" : "googleTileset");
        Require(tileset != null, "LIVE GOOGLE CHECK: No Google tileset; configure the local key file.");
        Require(EditorApplication.timeSinceStartup < SessionState.GetFloat("NavigationGoogleCheckDeadline", 0), "LIVE GOOGLE CHECK: Loading timed out.");
        if (Field<bool>(controller, "googleMapLoading") || tileset.GetComponentsInChildren<MeshRenderer>().Length == 0 || tileset.ComputeLoadProgress() < 99f)
        {
            liveMapReadySince = -1;
            return false;
        }
        if (liveMapReadySince < 0)
            liveMapReadySince = EditorApplication.timeSinceStartup;
        if (EditorApplication.timeSinceStartup - liveMapReadySince < 2)
            return false;

        Vector3[] route = Field<Vector3[]>(controller, "route");
        Require(route.All(point => float.IsFinite(point.y)), "Live terrain heights are finite.");
        Transform vehicle = Field<Transform>(controller, "vehicle");
        string output = Environment.GetEnvironmentVariable("NAVIGATION_CHECK_OUTPUT") ?? "Temp/NavigationChecks";
        Directory.CreateDirectory(output);
        if (liveMapStage == 0)
        {
            if (!Field<bool>(controller, "navigationBackground"))
                controller.ToggleNavigationBackground();
            Field<InstrumentCluster>(controller, "cluster").Tick(1);
            Vector3 before = vehicle.position;
            for (int tick = 0; tick < 90; tick++)
                controller.Simulate(1f / 30f);
            Require(Vector3.Distance(before, vehicle.position) > 3f, "Car moves after Google map loads.");
            controller.TogglePause();
            liveMapSegment = Field<int>(controller, "segment");
            liveMapDistance = Field<float>(controller, "segmentDistance");
            liveMapPosition = vehicle.position;
            if (!roadmap)
            {
                Invoke(controller, "UpdateCamera", true);
                liveMapStage = -1;
                liveMapReadySince = -1;
                return false;
            }
            Require(Field<Cesium3DTileset>(controller, "googleTileset") == null && Field<object>(controller, "routeHeightRequest") == null,
                "Direct 2D startup does not require photorealistic tiles or terrain sampling.");
            liveMapStage = 1;
            liveMapReadySince = -1;
            return false;
        }
        Require(Field<int>(controller, "segment") == liveMapSegment && Mathf.Approximately(Field<float>(controller, "segmentDistance"), liveMapDistance),
            "Asynchronous source and theme switching preserve route progress.");
        Require(Mathf.Abs(vehicle.position.x - liveMapPosition.x) < 0.01f && Mathf.Abs(vehicle.position.z - liveMapPosition.z) < 0.01f,
            "Source switching preserves the geographic vehicle position.");
        Invoke(controller, "UpdateCamera", true);
        if (liveMapStage == -1)
        {
            Capture(controller, 1920, 720, Path.Combine(output, "google-day-3d.png"));
            controller.ToggleViewMode();
            liveMapStage = 1;
            liveMapReadySince = -1;
            return false;
        }
        if (liveMapStage == 1 || liveMapStage == 2 || liveMapStage == 3)
        {
            Require(roadmap && tileset.tilesetSource == CesiumDataSource.FromEllipsoid, "2D renders a separate Google Roadmap layer.");
            Cesium3DTileset photo = Field<Cesium3DTileset>(controller, "googleTileset");
            Require(photo == null || !photo.gameObject.activeSelf, "Photorealistic geometry is not rendered behind the road map.");
            Require(Mathf.Approximately(vehicle.position.y, 3), "Navigation sits on the flat Roadmap surface.");
            var overlay = Field<CesiumGoogleMapTilesRasterOverlay>(controller, "googleRoadmapOverlay");
            Require(overlay.mapType == GoogleMapTilesMapType.Roadmap && overlay.showCreditsOnScreen, "Official Roadmap tiles retain on-screen attribution.");
            CheckLiveRoadmapCredits(controller);
            if (liveMapStage == 1)
            {
                Require(overlay.styles.Count == 0, "Day mode requests Google's default map style.");
                liveRoadmapDayBrightness = Capture(controller, 1920, 720, Path.Combine(output, "google-roadmap-day.png"));
                Capture(controller, 720, 1280, Path.Combine(output, "google-roadmap-day-portrait.png"));
                Require(liveRoadmapDayBrightness > 0.45f, "Default Google Roadmap tiles render a light daytime map.");
                controller.ToggleDayNight();
            }
            else if (liveMapStage == 2)
            {
                Require(overlay.styles.Count > 0, "Night mode requests server-side Google Roadmap styles.");
                float brightness = Capture(controller, 1920, 720, Path.Combine(output, "google-roadmap-night.png"));
                Capture(controller, 720, 1280, Path.Combine(output, "google-roadmap-night-portrait.png"));
                Require(brightness < liveRoadmapDayBrightness * 0.75f, "Google returns visibly different night-styled tile imagery.");
                controller.ToggleDayNight();
            }
            else
            {
                Require(overlay.styles.Count == 0, "Returning to day clears all night style rules.");
                float restored = Capture(controller, 1920, 720, Path.Combine(output, "google-roadmap-day-restored.png"));
                Require(Mathf.Abs(restored - liveRoadmapDayBrightness) < 0.12f, "Daytime tile colors are restored without cumulative tinting.");
                controller.ToggleViewMode();
            }
            liveMapStage++;
            liveMapReadySince = -1;
            return false;
        }
        Require(!roadmap && !Field<Cesium3DTileset>(controller, "googleRoadmapTileset").gameObject.activeSelf,
            "Returning to 3D deactivates Roadmap tiles.");
        float traveled = Field<float>(controller, "segmentDistance");
        float[] cumulative = Field<float[]>(controller, "cumulativeDistances");
        float fraction = traveled / (cumulative[liveMapSegment + 1] - cumulative[liveMapSegment]);
        Require(Mathf.Abs(vehicle.position.y - (Mathf.Lerp(route[liveMapSegment].y, route[liveMapSegment + 1].y, fraction) + 3)) < 0.01f,
            "Returning to 3D restores sampled street elevation.");
        controller.TogglePause();
        CheckArrival(controller);
        Debug.Log($"LIVE GOOGLE CHECK PASSED: Google Roadmap day/night tile images, 2D/3D switching, {route.Length} route points, preserved progress and arrival verified.");
        return true;
    }

    private static void Check()
    {
        Require(PlayerSettings.defaultScreenWidth == 1920 && PlayerSettings.defaultScreenHeight == 720,
            "Player window defaults to 1920x720.");
        Require(PlayerSettings.fullScreenMode == FullScreenMode.Windowed && !PlayerSettings.defaultIsNativeResolution,
            "Player uses the configured window size instead of the display's native fullscreen resolution.");
        SetGameViewSize();
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

        string output = Environment.GetEnvironmentVariable("NAVIGATION_CHECK_OUTPUT") ?? "Temp/NavigationChecks";
        Directory.CreateDirectory(output);
        CheckInstrumentCluster(controller, output);

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
        Invoke(controller, "UpdateCamera", true);
        Capture(controller, 1440, 900, Path.Combine(output, "arrival-desktop.png"));
        Capture(controller, 1920, 720, Path.Combine(output, "arrival-1920x720.png"));
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
        Capture(controller, 1920, 720, Path.Combine(output, "navigation-1920x720.png"));
        Capture(controller, 720, 1280, Path.Combine(output, "portrait.png"));

        CheckKeyboardControls(controller);
        CheckDisplayModes(controller, output, "offline");
        CheckNewTileTheme(controller);

        CheckGoogleMapSource(controller);
        CheckGoogleRoadmapConfiguration(controller);

        Debug.Log($"NAVIGATION CHECKS PASSED: {route.Length} points, {length:0} meters, non-looping arrival; dynamic cluster, media controls, map reveal and keyboard controls verified; Google configuration, key loading, open-route terrain alignment, and fallback checked without network requests; screenshots: {output}");
    }

    private static void CheckInstrumentCluster(car_navigation controller, string output)
    {
        InstrumentCluster cluster = Field<InstrumentCluster>(controller, "cluster");
        Require(cluster != null && !cluster.IsVisible && cluster.NavigationVisible &&
            !Field<bool>(controller, "instrumentClusterVisible") && Field<bool>(controller, "navigationBackground"),
            "The scene starts with navigation only and the instrument cluster disabled.");
        RectTransform canvas = Field<RectTransform>(cluster, "canvasRect");
        RectTransform navigationHud = Field<RectTransform>(Field<object>(controller, "hud"), "safeArea");
        Require(navigationHud.gameObject.activeInHierarchy, "Navigation is visible on startup.");
        Require(canvas.GetComponentsInChildren<Button>(true).All(button => !button.isActiveAndEnabled),
            "Hidden cluster controls are inactive and cannot receive input on startup.");
        Capture(controller, 1920, 720, Path.Combine(output, "navigation-only-default.png"));
        controller.ToggleInstrumentCluster();
        cluster.Tick(1);
        Require(cluster.IsVisible && cluster.NavigationVisible && navigationHud.gameObject.activeInHierarchy,
            "Enabling the cluster overlays the instruments on navigation by default.");
        controller.ToggleNavigationBackground();
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        Require(scaler.referenceResolution == new Vector2(1920, 720), "The cluster targets 1920x720.");
        RectTransform speedometer = (RectTransform)canvas.Find("Speedometer");
        RectTransform media = (RectTransform)canvas.Find("Media Player");
        Require(speedometer != null && media != null && speedometer.anchoredPosition.x == 330 && media.anchoredPosition.x == -330,
            "Instruments have stable left and right tracks with an open center.");
        AudioSource source = Field<AudioSource>(cluster, "audioSource");
        Require(source != null && source.clip != null && cluster.TrackTitle == "Coastline", "The media player loads its playable demo track.");
        float[] samples = new float[22050];
        source.clip.GetData(samples, 22050);
        Require(samples.Any(value => Mathf.Abs(value) > 0.01f), "Demo media contains audio, not just a simulated progress label.");
        Capture(controller, 1920, 720, Path.Combine(output, "cluster-only.png"), false);

        Vector3 before = Field<Transform>(controller, "vehicle").position;
        controller.Simulate(3);
        Invoke(controller, "RefreshHud");
        cluster.Tick(1);
        Require(controller.SpeedMph > 0 && Mathf.Abs(cluster.DisplayedSpeed - controller.SpeedMph) < 0.1f,
            "The speedometer follows real simulated vehicle speed.");
        Require(Field<Transform>(controller, "vehicle").position != before, "The drive advances while the map is hidden.");
        Capture(controller, 1920, 720, Path.Combine(output, "cluster-driving.png"), false);

        int segment = Field<int>(controller, "segment");
        float distance = Field<float>(controller, "segmentDistance");
        int track = cluster.TrackIndex;
        cluster.Seek(0.25f);
        float playbackTime = source.time;
        controller.ToggleNavigationBackground();
        cluster.Tick(1);
        Require(cluster.NavigationVisible && Field<RectTransform>(Field<object>(controller, "hud"), "safeArea").gameObject.activeSelf,
            "Map reveal activates navigation behind the cluster.");
        Require(canvas.gameObject.activeInHierarchy && speedometer.gameObject.activeInHierarchy && media.gameObject.activeInHierarchy &&
            Field<RawImage>(cluster, "background").color.a == 0, "Both instruments stay in the foreground while the map fills the background.");
        Require(Field<int>(controller, "segment") == segment && Field<float>(controller, "segmentDistance") == distance &&
            cluster.TrackIndex == track && Mathf.Abs(source.time - playbackTime) < 0.5f,
            "Map reveal does not restart navigation or media playback.");
        Capture(controller, 1920, 720, Path.Combine(output, "cluster-navigation.png"));

        Canvas.ForceUpdateCanvases();
        Button next = canvas.GetComponentsInChildren<Button>().Single(button => button.name == "Next track");
        RectTransform buttonRect = (RectTransform)next.transform;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(Camera.main, buttonRect.TransformPoint(buttonRect.rect.center));
        var pointer = new PointerEventData(EventSystem.current) { position = screen };
        var hits = new System.Collections.Generic.List<RaycastResult>();
        int hiddenSegment = Field<int>(controller, "segment");
        float hiddenDistance = Field<float>(controller, "segmentDistance");
        AudioClip playingClip = source.clip;
        float playingTime = source.time;
        bool playback = Field<bool>(cluster, "playbackRequested");
        controller.ToggleInstrumentCluster();
        Canvas.ForceUpdateCanvases();
        EventSystem.current.RaycastAll(pointer, hits);
        Require(!cluster.IsVisible && navigationHud.gameObject.activeInHierarchy &&
            !hits.Any(hit => hit.gameObject.transform.IsChildOf(canvas)),
            "Hiding the cluster removes both instruments, shading and all media hit targets while keeping navigation visible.");
        Require(Field<int>(controller, "segment") == hiddenSegment && Field<float>(controller, "segmentDistance") == hiddenDistance &&
            source.clip == playingClip && Mathf.Abs(source.time - playingTime) < 0.5f && Field<bool>(cluster, "playbackRequested") == playback,
            "Toggling the cluster does not reset navigation or change media playback.");
        Capture(controller, 1920, 720, Path.Combine(output, "navigation-only-restored.png"));
        controller.ToggleInstrumentCluster();
        cluster.Tick(1);
        Capture(controller, 1920, 720, Path.Combine(output, "cluster-restored.png"));
        next.onClick.Invoke();
        Require(cluster.TrackTitle == "Blue Hour" && cluster.TrackIndex == 1, "Next track changes audio, title and artwork.");
        cluster.PreviousTrack();
        Require(cluster.TrackIndex == 0, "Previous track returns to the earlier track.");
        cluster.TogglePlayback();
        Require(!Field<bool>(cluster, "playbackRequested") && !source.isPlaying, "Music can be paused independently of the drive.");
        cluster.NextTrack();
        Require(!Field<bool>(cluster, "playbackRequested") && !source.isPlaying, "Changing tracks preserves paused media state.");
        cluster.TogglePlayback();
        Require(Field<bool>(cluster, "playbackRequested"), "Music resumes after being paused.");
        cluster.Seek(0.5f);
        Require(Mathf.Abs(source.time - source.clip.length * 0.5f) < 0.1f, "The playback slider seeks in the clip.");
        cluster.SetVolume(0.4f);
        Require(Mathf.Approximately(source.volume, 0.4f), "The volume slider changes output volume.");
        cluster.ToggleMute();
        Require(source.mute, "Mute silences audio without resetting the track.");
        cluster.ToggleMute();
        cluster.SetVolume(0.22f);
        int finishedTrack = cluster.TrackIndex;
        typeof(InstrumentCluster).GetField("lastPlaybackTime", PrivateInstance).SetValue(cluster, source.clip.length - 0.1f);
        source.Stop();
        cluster.Tick(0);
        Require(cluster.TrackIndex != finishedTrack && Field<bool>(cluster, "playbackRequested"),
            "Playback advances to the next track when a song ends.");
        controller.TogglePause();
        Invoke(controller, "RefreshHud");
        cluster.Tick(1);
        Require(cluster.DisplayedSpeed == 0 && Field<bool>(cluster, "playbackRequested"), "Pausing the drive zeros speed without pausing music.");
        controller.TogglePause();
        controller.ToggleNavigationBackground();
        cluster.Tick(1);
        Require(!Field<RectTransform>(Field<object>(controller, "hud"), "safeArea").gameObject.activeSelf && source.clip != null,
            "Hiding navigation restores the empty center while keeping media alive.");
        controller.ToggleInstrumentCluster();
        Require(!cluster.IsVisible && navigationHud.gameObject.activeInHierarchy && cluster.NavigationVisible,
            "Disabling the cluster from cluster-only mode always restores navigation.");
        controller.ToggleNavigationBackground();
        Require(cluster.NavigationVisible && !Field<bool>(controller, "navigationBackground"),
            "The background shortcut cannot hide navigation when the cluster is disabled.");
        controller.ToggleInstrumentCluster();
        Require(cluster.IsVisible && !cluster.NavigationVisible, "Re-enabling the cluster restores its previous background preference.");
        controller.ToggleNavigationBackground();
        controller.ToggleInstrumentCluster();
    }

    private static void CheckKeyboardControls(car_navigation controller)
    {
        Keyboard originalKeyboard = Keyboard.current;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        float originalHeight = Field<float>(controller, "cameraHeight");
        try
        {
            Invoke(controller, "HandleKeyboardInput", (object)null);
            InstrumentCluster cluster = Field<InstrumentCluster>(controller, "cluster");
            Require(!cluster.IsVisible && cluster.NavigationVisible, "Keyboard checks start in navigation-only mode.");
            PressKeys(controller, keyboard, Key.M);
            Require(!cluster.IsVisible && cluster.NavigationVisible, "M cannot blank the navigation-only view.");
            PressKeys(controller, keyboard, Key.C);
            Require(cluster.IsVisible && cluster.NavigationVisible, "C enables the cluster over navigation.");
            ApplyKeyboardState(controller, keyboard, Key.C);
            Require(cluster.IsVisible, "Holding C toggles the cluster only once.");
            bool navigation = cluster.NavigationVisible;
            PressKeys(controller, keyboard, Key.M);
            Require(cluster.NavigationVisible != navigation, "M toggles the navigation background.");
            ApplyKeyboardState(controller, keyboard, Key.M);
            Require(cluster.NavigationVisible != navigation, "Holding M toggles only once.");
            PressKeys(controller, keyboard, Key.M);
            Require(cluster.NavigationVisible == navigation, "M restores the previous background.");
            PressKeys(controller, keyboard, Key.C);
            Require(!cluster.IsVisible && cluster.NavigationVisible, "C returns to navigation-only mode.");
            bool playing = Field<bool>(cluster, "playbackRequested");
            PressKeys(controller, keyboard, Key.P);
            Require(Field<bool>(cluster, "playbackRequested") != playing, "P toggles media playback.");
            PressKeys(controller, keyboard, Key.P);
            int track = cluster.TrackIndex;
            cluster.Seek(0);
            PressKeys(controller, keyboard, Key.RightBracket);
            Require(cluster.TrackIndex != track, "Right bracket selects the next track.");
            PressKeys(controller, keyboard, Key.LeftBracket);
            Require(cluster.TrackIndex == track, "Left bracket selects the previous track.");
            PressKeys(controller, keyboard, Key.Space);
            Require(Field<bool>(controller, "paused") && Field<float>(controller, "speed") == 0, "Space pauses the drive.");
            Transform marker = Field<Transform>(controller, "vehicle");
            Vector3 position = marker.position;
            controller.Simulate(2);
            Require(Vector3.Distance(position, marker.position) < 0.001f, "Keyboard pause stops movement.");
            ApplyKeyboardState(controller, keyboard, Key.Space);
            Require(Field<bool>(controller, "paused"), "Holding Space does not toggle repeatedly.");
            Invoke(controller, "RefreshHud");
            Require(Field<Text>(Field<object>(controller, "hud"), "turnAction").text == "Drive paused",
                "Pause status is visible without a button.");
            PressKeys(controller, keyboard, Key.Space);
            controller.Simulate(2);
            Require(!Field<bool>(controller, "paused") && Vector3.Distance(position, marker.position) > 0.1f,
                "Pressing Space again resumes movement.");

            bool originalOrientation = Field<bool>(controller, "northUp");
            PressKeys(controller, keyboard, Key.N);
            Require(Field<bool>(controller, "northUp") != originalOrientation, "N switches map orientation.");
            ApplyKeyboardState(controller, keyboard, Key.N);
            Require(Field<bool>(controller, "northUp") != originalOrientation, "Holding N toggles only once.");
            PressKeys(controller, keyboard, Key.N);
            Require(Field<bool>(controller, "northUp") == originalOrientation, "N switches the orientation back.");

            PressKeys(controller, keyboard, Key.T);
            Require(Field<bool>(controller, "nightMode"), "T enables night mode.");
            ApplyKeyboardState(controller, keyboard, Key.T);
            Require(Field<bool>(controller, "nightMode"), "Holding T does not repeatedly switch the theme.");
            PressKeys(controller, keyboard, Key.T);
            Require(!Field<bool>(controller, "nightMode"), "T returns to daytime.");
            PressKeys(controller, keyboard, Key.V);
            Require(Field<bool>(controller, "topDownView"), "V enables 2D mode.");
            ApplyKeyboardState(controller, keyboard, Key.V);
            Require(Field<bool>(controller, "topDownView"), "Holding V does not repeatedly switch the view.");
            PressKeys(controller, keyboard, Key.V);
            Require(!Field<bool>(controller, "topDownView"), "V restores the angled 3D view.");

            PressKeys(controller, keyboard, Key.Equals);
            Require(Mathf.Approximately(Field<float>(controller, "cameraHeight"), originalHeight - 30), "Equals zooms in.");
            PressKeys(controller, keyboard, Key.Minus);
            Require(Mathf.Approximately(Field<float>(controller, "cameraHeight"), originalHeight), "Minus zooms out.");
            PressKeys(controller, keyboard, Key.LeftShift, Key.Equals);
            Require(Mathf.Approximately(Field<float>(controller, "cameraHeight"), originalHeight - 30), "Shift+Equals (plus) zooms in.");
            PressKeys(controller, keyboard, Key.NumpadMinus);
            Require(Mathf.Approximately(Field<float>(controller, "cameraHeight"), originalHeight), "Numpad minus zooms out.");
            PressKeys(controller, keyboard, Key.NumpadPlus);
            Require(Mathf.Approximately(Field<float>(controller, "cameraHeight"), originalHeight - 30), "Numpad plus zooms in.");
            ApplyKeyboardState(controller, keyboard, Key.NumpadPlus);
            Require(Mathf.Approximately(Field<float>(controller, "cameraHeight"), originalHeight - 30), "Held zoom keys do not repeat.");
            for (int press = 0; press < 12; press++)
                PressKeys(controller, keyboard, Key.Equals);
            Require(Mathf.Approximately(Field<float>(controller, "cameraHeight"), 90), "Keyboard zoom-in is clamped.");
            for (int press = 0; press < 12; press++)
                PressKeys(controller, keyboard, Key.Minus);
            Require(Mathf.Approximately(Field<float>(controller, "cameraHeight"), 300), "Keyboard zoom-out is clamped.");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            if (originalKeyboard != null)
                originalKeyboard.MakeCurrent();
            controller.Zoom(originalHeight - Field<float>(controller, "cameraHeight"));
        }
    }

    private static void PressKeys(car_navigation controller, Keyboard keyboard, params Key[] keys)
    {
        ApplyKeyboardState(controller, keyboard);
        ApplyKeyboardState(controller, keyboard, keys);
    }

    private static void ApplyKeyboardState(car_navigation controller, Keyboard keyboard, params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
        Invoke(controller, "HandleKeyboardInput", keyboard);
    }

    private static void CheckDisplayModes(car_navigation controller, string output, string source)
    {
        Require(!Field<bool>(controller, "nightMode") && !Field<bool>(controller, "topDownView"), "Display starts in daytime 3D mode.");
        Camera camera = Field<Camera>(controller, "navigationCamera");
        Transform marker = Field<Transform>(controller, "vehicle");
        Vector3 position = marker.position;
        int segment = Field<int>(controller, "segment");
        float distance = Field<float>(controller, "segmentDistance");
        float zoom = Field<float>(controller, "cameraHeight");
        bool paused = Field<bool>(controller, "paused");
        Invoke(controller, "UpdateCamera", true);
        Quaternion dayRotation = camera.transform.rotation;
        float dayBrightness = Capture(controller, 1920, 720, Path.Combine(output, source + "-day-3d.png"));

        controller.ToggleDayNight();
        float nightBrightness = Capture(controller, 1920, 720, Path.Combine(output, source + "-night-3d.png"));
        Require(nightBrightness < dayBrightness * 0.75f, "Night mode visibly darkens the map, not just the HUD.");
        Text street = Field<Text>(Field<object>(controller, "hud"), "currentStreet");
        Require(street.color.grayscale > 0.7f && street.transform.parent.GetComponent<Image>().color.grayscale < 0.2f,
            "Night street labels remain readable against a dark background.");

        controller.ToggleViewMode();
        Invoke(controller, "UpdateCamera", true);
        Require(camera.orthographic && Vector3.Dot(camera.transform.forward, Vector3.down) > 0.999f,
            "2D mode uses a true top-down orthographic camera without a tilted view.");
        Require(Vector3.Dot(camera.transform.up, marker.forward) > 0.999f, "2D heading-up respects the marker heading.");
        Capture(controller, 1920, 720, Path.Combine(output, source + "-night-2d.png"));
        Capture(controller, 720, 1280, Path.Combine(output, source + "-night-2d-portrait.png"));
        controller.ToggleOrientation();
        Invoke(controller, "UpdateCamera", true);
        Require(Vector3.Dot(camera.transform.up, Vector3.forward) > 0.999f, "North-up works in 2D mode.");
        controller.ToggleOrientation();
        controller.Zoom(-30);
        Invoke(controller, "UpdateCamera", true);
        Require(Mathf.Approximately(camera.orthographicSize, zoom - 30), "Zoom works in 2D mode.");
        controller.Zoom(30);
        controller.ToggleDayNight();
        Invoke(controller, "UpdateCamera", true);
        Capture(controller, 1920, 720, Path.Combine(output, source + "-day-2d.png"));
        Capture(controller, 720, 1280, Path.Combine(output, source + "-day-2d-portrait.png"));
        controller.ToggleViewMode();
        Invoke(controller, "UpdateCamera", true);

        Require(Quaternion.Angle(camera.transform.rotation, dayRotation) < 0.01f && Mathf.Approximately(camera.orthographicSize, zoom),
            "Returning to 3D restores the camera angle and preserves zoom.");
        Require(marker.position == position && Field<int>(controller, "segment") == segment &&
            Field<float>(controller, "segmentDistance") == distance && Field<bool>(controller, "paused") == paused,
            "Mode switching preserves route progress, vehicle position, and pause state.");
        Require(!Field<bool>(controller, "nightMode") && !Field<bool>(controller, "topDownView"), "Theme and view toggles are independent and reversible.");
    }

    private static void CheckNewTileTheme(car_navigation controller)
    {
        var tile = new GameObject("Synthetic Google Tile", typeof(MeshRenderer));
        MeshRenderer renderer = tile.GetComponent<MeshRenderer>();
        Material material = Resources.Load<Material>("CesiumUnlitTilesetMaterial");
        Require(material != null, "Cesium tile material is available for theme verification.");
        renderer.sharedMaterial = material;
        int colorProperty = Shader.PropertyToID("_baseColorFactor");
        int unrelatedProperty = Shader.PropertyToID("NavigationTestValue");
        Vector4 original = material.GetVector(colorProperty);
        var properties = new MaterialPropertyBlock();
        properties.SetFloat(unrelatedProperty, 42);
        renderer.SetPropertyBlock(properties, 0);
        bool ready = Field<bool>(controller, "googleTilesReady");
        try
        {
            controller.ToggleDayNight();
            Invoke(controller, "OnGoogleTileCreated", tile);
            renderer.GetPropertyBlock(properties, 0);
            Vector4 tinted = properties.GetVector(colorProperty);
            Require(tinted.x < original.x * 0.3f && Mathf.Approximately(tinted.w, original.w),
                "Newly streamed tiles inherit night mode without changing opacity.");
            Require(properties.GetFloat(unrelatedProperty) == 42 && material.GetVector(colorProperty) == original,
                "Tile theming preserves other properties and does not modify shared assets.");
            controller.ToggleDayNight();
            Invoke(controller, "ApplyGoogleTileTheme", tile);
            renderer.GetPropertyBlock(properties, 0);
            Require(properties.GetVector(colorProperty) == original, "Daytime restores the tile's original color.");
        }
        finally
        {
            if (Field<bool>(controller, "nightMode"))
                controller.ToggleDayNight();
            typeof(car_navigation).GetField("googleTilesReady", PrivateInstance).SetValue(controller, ready);
            UnityEngine.Object.DestroyImmediate(tile);
        }
    }

    private static void CheckGoogleRoadmapConfiguration(car_navigation controller)
    {
        Cesium3DTileset tileset = (Cesium3DTileset)typeof(car_navigation).GetMethod("CreateGoogleRoadmap", PrivateInstance)
            .Invoke(controller, new object[] { "synthetic-roadmap-key" });
        try
        {
            CesiumGoogleMapTilesRasterOverlay overlay = tileset.GetComponent<CesiumGoogleMapTilesRasterOverlay>();
            Require(!tileset.gameObject.activeSelf && tileset.tilesetSource == CesiumDataSource.FromEllipsoid && tileset.opaqueMaterial != null,
                "Roadmap is configured on an unlit ellipsoid without making network requests.");
            Require(overlay != null && overlay.mapType == GoogleMapTilesMapType.Roadmap && overlay.apiKey == "synthetic-roadmap-key",
                "2D mode uses Google's Roadmap tile API, not the photorealistic tileset.");
            Require(overlay.showCreditsOnScreen && tileset.showCreditsOnScreen && (tileset.hideFlags | tileset.gameObject.hideFlags).HasFlag(HideFlags.DontSave),
                "Roadmap retains provider credits and cannot save credentials to the scene.");
            Require(overlay.styles.Count == 0, "Google Roadmap day mode uses default server styling.");
            controller.ToggleDayNight();
            Cesium3DTileset nightTileset = (Cesium3DTileset)typeof(car_navigation).GetMethod("CreateGoogleRoadmap", PrivateInstance)
                .Invoke(controller, new object[] { "synthetic-roadmap-key" });
            Require(nightTileset.GetComponent<CesiumGoogleMapTilesRasterOverlay>().styles.Count > 0,
                "Google Roadmap night mode sends explicit server styling.");
            UnityEngine.Object.DestroyImmediate(nightTileset.gameObject);
            controller.ToggleDayNight();

            Vector3[] route = Field<Vector3[]>(controller, "route");
            route[0].y = 15;
            route[route.Length - 1].y = 35;
            typeof(car_navigation).GetField("googleRoadmapTileset", PrivateInstance).SetValue(controller, tileset);
            typeof(car_navigation).GetField("googleRoadmapOverlay", PrivateInstance).SetValue(controller, overlay);
            typeof(car_navigation).GetField("topDownView", PrivateInstance).SetValue(controller, true);
            Invoke(controller, "DrawRoute");
            Invoke(controller, "PlaceVehicle", 1f);
            Mesh ribbon = Field<GameObject>(controller, "routeRibbon").GetComponent<MeshFilter>().sharedMesh;
            Require(ribbon.vertices.All(point => Mathf.Approximately(point.y, 0.85f)) && Mathf.Approximately(Field<Transform>(controller, "vehicle").position.y, 3),
                "The 2D route and arrow are flat while preserving original terrain heights.");
            Require(route[0].y == 15 && route[route.Length - 1].y == 35, "2D rendering does not overwrite the 3D terrain samples.");
            Invoke(controller, "OnGoogleRoadmapLoadFailure", new CesiumRasterOverlayLoadFailureDetails(overlay, CesiumRasterOverlayLoadType.TileProvider, 403, "Synthetic failure"));
            Invoke(controller, "UpdateGoogleMap");
            Require(Field<Cesium3DTileset>(controller, "googleRoadmapTileset") == null && !Field<bool>(controller, "googleMapLoading"),
                "A rejected Roadmap session falls back locally without leaving a blank active layer.");
        }
        finally
        {
            typeof(car_navigation).GetField("topDownView", PrivateInstance).SetValue(controller, false);
            if (tileset != null)
                UnityEngine.Object.DestroyImmediate(tileset.gameObject);
        }
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
        Require(Field<Text>(hud, "turnDistance").text == "Arrived" && Field<Text>(hud, "turnAction").text == "Destination reached",
            "Arrival guidance remains visible without the trip footer.");
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
        CheckCreditImageSizing(Field<object>(controller, "hud"));

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

    private static void CheckCreditImageSizing(object hud)
    {
        var container = new UnityEngine.UIElements.VisualElement();
        var wrapper = new UnityEngine.UIElements.VisualElement();
        var image = new Texture2D(800, 160);
        var logo = new UnityEngine.UIElements.VisualElement();
        logo.style.backgroundImage = new UnityEngine.UIElements.StyleBackground(image);
        logo.style.width = image.width;
        logo.style.height = image.height;
        var label = new UnityEngine.UIElements.Label("Google Maps | Map data provider attribution");
        label.tooltip = "Provider link";
        var link = new UnityEngine.UIElements.Clickable(() => { }) { target = label };
        wrapper.Add(logo);
        wrapper.Add(label);
        container.Add(wrapper);
        try
        {
            MethodInfo resize = hud.GetType().GetMethod("SizeCreditContent", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (float scale in new[] { 1f, 1.25f, 1f })
            {
                resize.Invoke(null, new object[] { container, scale });
                Require(Mathf.Approximately(logo.style.height.value.value, 18 * scale) &&
                    Mathf.Approximately(logo.style.width.value.value, 90 * scale), "Large credit logos shrink proportionally without cumulative resizing.");
                Require(logo.style.marginLeft.value.value >= 10 * scale && logo.style.marginRight.value.value >= 10 * scale &&
                    logo.style.marginTop.value.value >= 10 * scale && logo.style.marginBottom.value.value >= 5 * scale,
                    "Compact attribution keeps the required logo clear space.");
                Require(label.text == "Google Maps | Map data provider attribution" && label.tooltip == "Provider link" &&
                    label.parent == wrapper && link.target == label && label.pickingMode == UnityEngine.UIElements.PickingMode.Position,
                    "Credit resizing does not replace, hide, or detach provider text or links.");
                Require(label.style.fontSize.value.value >= 12 * scale && label.style.whiteSpace.value == UnityEngine.UIElements.WhiteSpace.Normal,
                    "Attribution remains readable and wraps instead of clipping.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    private static void CheckLiveRoadmapCredits(car_navigation controller)
    {
        object hud = Field<object>(controller, "hud");
        var document = Field<UnityEngine.UIElements.UIDocument>(hud, "creditsDocument");
        Require(document != null, "Roadmap credits have a visible UI document.");
        var root = document.rootVisualElement;
        var credits = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(root, "OnScreenCredits");
        var popup = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(root, "PopupCredits");
        float units = credits.style.fontSize.value.value / 12f;
        Require(credits.childCount > 0 && credits.resolvedStyle.height > 0 && credits.resolvedStyle.height <= 70 * units,
            $"The real Google 2D info box stays compact: {credits.resolvedStyle.width / units:0}x{credits.resolvedStyle.height / units:0} logical pixels.");
        Require(credits.resolvedStyle.width <= credits.style.maxWidth.value.value + 1 && credits.style.flexWrap.value == UnityEngine.UIElements.Wrap.NoWrap,
            "The real 2D info box fits its compact width limit.");
        Require(popup.style.width.keyword == UnityEngine.UIElements.StyleKeyword.Auto &&
            popup.style.height.keyword == UnityEngine.UIElements.StyleKeyword.Auto && popup.style.top.keyword == UnityEngine.UIElements.StyleKeyword.Auto,
            "Expanded attribution uses natural content size instead of most of the screen.");
        Debug.Log($"ROADMAP CREDIT LAYOUT VERIFIED: {credits.resolvedStyle.width / units:0}x{credits.resolvedStyle.height / units:0} logical pixels.");
    }

    private static float Capture(car_navigation controller, int width, int height, string path, bool navigation = true)
    {
        InstrumentCluster cluster = Field<InstrumentCluster>(controller, "cluster");
        Require(navigation || cluster.IsVisible, "A cluster-only capture requires the cluster to be enabled explicitly.");
        if (cluster.NavigationVisible != navigation)
            controller.ToggleNavigationBackground();
        cluster.Tick(1);
        Camera camera = Camera.main;
        var target = new RenderTexture(width, height, 24);
        target.Create();
        camera.targetTexture = target;
        Canvas.ForceUpdateCanvases();
        Invoke(controller, "RefreshHud");
        cluster.Tick(1);
        foreach (Text text in Field<RectTransform>(cluster, "canvasRect").GetComponentsInChildren<Text>())
            text.SetAllDirty();
        Canvas.ForceUpdateCanvases();
        RectTransform safeArea = Field<RectTransform>(Field<object>(controller, "hud"), "safeArea");
        Require(!safeArea.GetComponentsInChildren<RectTransform>(true).Any(rect =>
            rect.name == "Location Header" || rect.name == "Trip Readout" || rect.name == "Trip Progress"),
            "The top and bottom horizontal bars are not created.");
        Require(!safeArea.GetComponentsInChildren<Image>().Any(image => image.rectTransform.rect.width >= safeArea.rect.width - 1f),
            $"No full-width HUD panel covers the map at {width}x{height}.");
        Require(safeArea.GetComponentsInChildren<Button>(true).Length == 0 && safeArea.Find("Map Controls") == null,
            $"No on-screen navigation buttons or control panel exist at {width}x{height}.");
        RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        Color32[] pixels = image.GetPixels32();
        if (navigation && Field<bool>(controller, "topDownView") && Field<Cesium3DTileset>(controller, "googleRoadmapTileset") != null)
        {
            Color32 background = camera.backgroundColor;
            int uncovered = pixels.Count(pixel => Mathf.Abs(pixel.r - background.r) <= 2 &&
                Mathf.Abs(pixel.g - background.g) <= 2 && Mathf.Abs(pixel.b - background.b) <= 2);
            Require(uncovered < pixels.Length * 0.01f,
                $"Google Roadmap covers the viewport at {width}x{height}; uncovered pixels: {100f * uncovered / pixels.Length:0.0}% (camera {camera.pixelWidth}x{camera.pixelHeight}, screen {Screen.width}x{Screen.height}).");
        }
        float brightness = (float)pixels.Where((pixel, index) => index % width > width * 0.36f && index % width < width * 0.64f &&
            index / width > height * 0.35f && index / width < height * 0.7f)
            .Average(pixel => (pixel.r * 0.2126 + pixel.g * 0.7152 + pixel.b * 0.0722) / 255.0);
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
        if (navigation)
        {
            Require(routePixels > 100, $"Blue route highlight is visible at {width}x{height}.");
            Require(arrowPixels > 15, $"Blue navigation arrow is visible at its current position at {width}x{height}.");
        }
        else
            Require(brightness < 0.12f, "The center is empty graphite rather than a map or placeholder when navigation is hidden.");
        Require(Field<RectTransform>(cluster, "canvasRect").Find("Speedometer").gameObject.activeInHierarchy == cluster.IsVisible &&
            Field<RectTransform>(cluster, "canvasRect").Find("Media Player").gameObject.activeInHierarchy == cluster.IsVisible,
            "Both instruments follow the cluster visibility setting without changing the navigation background.");
        Require(pixels.Select(pixel => (pixel.r << 16) | (pixel.g << 8) | pixel.b).Distinct().Count() > 100, "Render is not blank.");

        RectTransform clusterCanvas = Field<RectTransform>(cluster, "canvasRect");
        Button mediaButton = clusterCanvas.GetComponentsInChildren<Button>(true).Single(button => button.name == "Next track");
        RectTransform buttonRect = (RectTransform)mediaButton.transform;
        var pointer = new PointerEventData(EventSystem.current)
        {
            position = RectTransformUtility.WorldToScreenPoint(camera, buttonRect.TransformPoint(buttonRect.rect.center))
        };
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Require(hits.Any(hit => hit.gameObject == mediaButton.gameObject) == cluster.IsVisible,
            $"Media controls receive input only when the cluster is enabled at {width}x{height}.");

        RenderTexture.active = previous;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        return brightness;
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
    private static void Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, arguments);
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}