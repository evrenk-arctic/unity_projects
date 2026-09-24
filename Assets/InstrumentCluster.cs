using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class InstrumentCluster : MonoBehaviour
{
    private static readonly Color White = new Color32(239, 245, 243, 255);
    private static readonly Color Muted = new Color32(146, 162, 165, 255);
    private static readonly Color Accent = new Color32(110, 226, 199, 255);
    private readonly List<UnityEngine.Object> ownedAssets = new List<UnityEngine.Object>();
    private readonly string[] demoTitles = { "Coastline", "Blue Hour", "Open Road" };
    private RectTransform canvasRect;
    private RawImage background;
    private CanvasGroup navigationShade;
    private Text speedText;
    private Text driveState;
    private Text gearText;
    private Text titleText;
    private Text artistText;
    private Text elapsedText;
    private Text durationText;
    private Text mediaState;
    private Text clock;
    private Slider progress;
    private Slider volume;
    private RawImage artwork;
    private NavigationIconGraphic playbackIcon;
    private NavigationIconGraphic muteIcon;
    private ClusterDialGraphic dial;
    private Font displayFont;
    private AudioSource audioSource;
    private AudioClip[] playlist;
    private Texture2D[] covers;
    private bool demoPlaylist;
    private bool playbackRequested;
    private bool navigationVisible;
    private float navigationBlend;
    private float targetSpeed;
    private float lastPlaybackTime;
    private int trackIndex;

    public float DisplayedSpeed { get; private set; }
    public string TrackTitle => titleText.text;
    public int TrackIndex => trackIndex;
    public bool IsPlaying => playbackRequested && audioSource.isPlaying;
    public bool NavigationVisible => navigationVisible;
    public bool IsVisible => canvasRect != null && canvasRect.gameObject.activeSelf;

    public void Initialize(Camera camera, Font font, AudioClip[] tracks)
    {
        displayFont = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        canvasRect = Rect("Instrument Cluster", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Canvas canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 0.8f;
        canvas.sortingOrder = 150;
        canvasRect.gameObject.AddComponent<GraphicRaycaster>();
        CanvasScaler scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        background = Rect("Cluster Background", canvasRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
        background.texture = MakeBackdrop(false);
        background.raycastTarget = false;
        RectTransform shade = Rect("Navigation Instrument Shade", canvasRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        RawImage shadeImage = shade.gameObject.AddComponent<RawImage>();
        shadeImage.texture = MakeBackdrop(true);
        shadeImage.raycastTarget = false;
        navigationShade = shade.gameObject.AddComponent<CanvasGroup>();
        navigationShade.alpha = 0;
        navigationShade.blocksRaycasts = false;

        RectTransform left = Rect("Speedometer", canvasRect, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(330, 0), new Vector2(500, 620));
        Label(left, "DRIVE", 17, Accent, new Vector2(0, 270), new Vector2(250, 28));
        RectTransform gauge = Rect("Speed Dial", left, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0, 14), new Vector2(440, 440));
        dial = gauge.gameObject.AddComponent<ClusterDialGraphic>();
        dial.raycastTarget = false;
        for (int value = 0; value <= 120; value += 20)
        {
            float angle = (225f - value / 120f * 270f) * Mathf.Deg2Rad;
            Label(gauge, value.ToString(), 20, Muted, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 151, new Vector2(54, 30));
        }
        speedText = Label(gauge, "0", 112, White, new Vector2(0, 7), new Vector2(250, 136));
        Label(gauge, "MPH", 18, Muted, new Vector2(0, -62), new Vector2(100, 28));
        gearText = Label(left, "D", 30, Accent, new Vector2(0, -186), new Vector2(80, 44));
        driveState = Label(left, "READY", 14, Muted, new Vector2(0, -236), new Vector2(380, 26));
        Label(left, "SAN FRANCISCO", 13, Muted, new Vector2(0, -290), new Vector2(350, 22));

        RectTransform right = Rect("Media Player", canvasRect, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-330, 0), new Vector2(460, 620));
        mediaState = Label(right, "NOW PLAYING", 17, Accent, new Vector2(0, 270), new Vector2(360, 28));
        artwork = Rect("Album Artwork", right, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0, 135), new Vector2(182, 182)).gameObject.AddComponent<RawImage>();
        artwork.raycastTarget = false;
        titleText = Label(right, "", 32, White, new Vector2(0, 6), new Vector2(420, 52));
        artistText = Label(right, "", 16, Muted, new Vector2(0, -34), new Vector2(420, 28));
        progress = MakeSlider(right, "Playback Position", new Vector2(0, -85), new Vector2(348, 22), 0);
        progress.onValueChanged.AddListener(Seek);
        elapsedText = Label(right, "0:00", 13, Muted, new Vector2(-152, -110), new Vector2(70, 22));
        durationText = Label(right, "0:00", 13, Muted, new Vector2(152, -110), new Vector2(70, 22));
        MediaButton(right, "previous", "Previous track", new Vector2(-94, -171), PreviousTrack);
        playbackIcon = MediaButton(right, "pause", "Play / pause music", new Vector2(0, -171), TogglePlayback);
        MediaButton(right, "next", "Next track", new Vector2(94, -171), NextTrack);
        muteIcon = MediaButton(right, "speaker", "Mute / unmute", new Vector2(-139, -245), ToggleMute);
        volume = MakeSlider(right, "Volume", new Vector2(26, -245), new Vector2(230, 22), 0.22f);
        volume.onValueChanged.AddListener(SetVolume);
        clock = Label(right, "", 14, Muted, new Vector2(0, -290), new Vector2(350, 22));

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0;
        audioSource.volume = volume.value;
        var validTracks = new List<AudioClip>();
        if (tracks != null)
            foreach (AudioClip clip in tracks)
                if (clip != null)
                    validTracks.Add(clip);
        demoPlaylist = validTracks.Count == 0;
        if (demoPlaylist)
            for (int index = 0; index < demoTitles.Length; index++)
                validTracks.Add(MakeDemoTrack(index));
        playlist = validTracks.ToArray();
        covers = new Texture2D[playlist.Length];
        for (int index = 0; index < covers.Length; index++)
            covers[index] = MakeCover(index);
        LoadTrack(0, true);
        Tick(0);
    }

    private void Update() => Tick(Time.unscaledDeltaTime);

    public void Tick(float deltaTime)
    {
        if (audioSource == null)
            return;
        DisplayedSpeed = Mathf.Lerp(DisplayedSpeed, targetSpeed, 1 - Mathf.Exp(-deltaTime * 9));
        if (Mathf.Abs(DisplayedSpeed - targetSpeed) < 0.05f)
            DisplayedSpeed = targetSpeed;
        speedText.text = Mathf.RoundToInt(DisplayedSpeed).ToString();
        dial.Speed = DisplayedSpeed;
        navigationBlend = Mathf.MoveTowards(navigationBlend, navigationVisible ? 1 : 0, deltaTime * 3);
        background.color = new Color(1, 1, 1, 1 - navigationBlend);
        navigationShade.alpha = navigationBlend;
        if (playbackRequested && !audioSource.isPlaying && lastPlaybackTime >= audioSource.clip.length - 0.3f)
            NextTrack();
        float position = audioSource.time;
        if (audioSource.isPlaying)
            lastPlaybackTime = position;
        progress.SetValueWithoutNotify(position / Mathf.Max(0.01f, audioSource.clip.length));
        elapsedText.text = FormatTime(position);
        playbackIcon.Symbol = playbackRequested ? "pause" : "play";
        mediaState.text = playbackRequested ? "NOW PLAYING" : "PLAYBACK PAUSED";
        muteIcon.Symbol = audioSource.mute ? "mute" : "speaker";
        clock.text = DateTime.Now.ToString("HH:mm");
    }

    public void SetDrivingState(float speedMph, bool paused, bool arrived, bool loading)
    {
        targetSpeed = loading ? 0 : Mathf.Max(0, speedMph);
        gearText.text = arrived ? "P" : "D";
        driveState.text = loading ? "CONNECTING" : arrived ? "PARKED" : paused ? "DRIVE PAUSED" : "CRUISING";
    }

    public void SetVisible(bool visible)
    {
        canvasRect.gameObject.SetActive(visible);
    }

    public void SetNavigationVisible(bool visible)
    {
        navigationVisible = visible;
        if (!visible)
        {
            navigationBlend = 0;
            background.color = Color.white;
            navigationShade.alpha = 0;
        }
    }

    public void TogglePlayback()
    {
        if (audioSource == null)
            return;
        playbackRequested = !playbackRequested;
        if (playbackRequested)
        {
            audioSource.UnPause();
            if (!audioSource.isPlaying)
                audioSource.Play();
        }
        else
            audioSource.Pause();
        Tick(0);
    }

    public void NextTrack() => LoadTrack((trackIndex + 1) % playlist.Length, playbackRequested);
    public void PreviousTrack()
    {
        if (audioSource.time > 3)
            Seek(0);
        else
            LoadTrack((trackIndex + playlist.Length - 1) % playlist.Length, playbackRequested);
    }

    public void ToggleMute()
    {
        audioSource.mute = !audioSource.mute;
        Tick(0);
    }

    public void SetVolume(float value)
    {
        audioSource.volume = Mathf.Clamp01(value);
        volume.SetValueWithoutNotify(audioSource.volume);
    }

    public void Seek(float fraction)
    {
        audioSource.time = Mathf.Clamp01(fraction) * Mathf.Max(0, audioSource.clip.length - 0.05f);
        lastPlaybackTime = audioSource.time;
    }

    private void LoadTrack(int index, bool play)
    {
        trackIndex = index;
        audioSource.Stop();
        audioSource.clip = playlist[index];
        audioSource.Play();
        if (!play)
            audioSource.Pause();
        playbackRequested = play;
        lastPlaybackTime = 0;
        titleText.text = demoPlaylist ? demoTitles[index] : playlist[index].name;
        artistText.text = demoPlaylist ? "Navigation Sessions" : "Local collection";
        artwork.texture = covers[index];
        durationText.text = FormatTime(audioSource.clip.length);
    }

    private Text Label(Transform parent, string value, int size, Color color, Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = Rect(value.Length == 0 ? "Readout" : value, parent, Vector2.one * 0.5f, Vector2.one * 0.5f, position, dimensions);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = displayFont;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.resizeTextForBestFit = size == 32;
        text.resizeTextMinSize = Mathf.Max(12, size - 8);
        text.resizeTextMaxSize = size;
        return text;
    }

    private NavigationIconGraphic MediaButton(Transform parent, string symbol, string tooltip, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        RectTransform rect = Rect(tooltip, parent, Vector2.one * 0.5f, Vector2.one * 0.5f, position, new Vector2(58, 58));
        Image hit = rect.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.onClick.AddListener(action);
        RectTransform iconRect = Rect("Media Icon", rect, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, new Vector2(22, 22));
        NavigationIconGraphic icon = iconRect.gameObject.AddComponent<NavigationIconGraphic>();
        icon.Symbol = symbol;
        icon.color = symbol == "pause" ? Accent : White;
        icon.raycastTarget = false;
        Text hint = Label(rect, tooltip, 12, White, new Vector2(0, 49), new Vector2(175, 24));
        hint.gameObject.SetActive(false);
        EventTrigger trigger = rect.gameObject.AddComponent<EventTrigger>();
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => hint.gameObject.SetActive(true));
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => hint.gameObject.SetActive(false));
        trigger.triggers.Add(enter);
        trigger.triggers.Add(exit);
        return icon;
    }

    private Slider MakeSlider(Transform parent, string name, Vector2 position, Vector2 size, float value)
    {
        RectTransform rect = Rect(name, parent, Vector2.one * 0.5f, Vector2.one * 0.5f, position, size);
        Image hit = rect.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        RectTransform rail = Rect("Track", rect, new Vector2(0, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(0, 3));
        rail.gameObject.AddComponent<Image>().color = new Color32(70, 83, 85, 255);
        RectTransform fill = Rect("Fill", rail, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fill.gameObject.AddComponent<Image>().color = Accent;
        Slider slider = rect.gameObject.AddComponent<Slider>();
        slider.targetGraphic = hit;
        slider.fillRect = fill;
        slider.value = value;
        return slider;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private Texture2D MakeBackdrop(bool shade)
    {
        const int width = 512;
        const int height = 128;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = shade ? "Instrument Contrast" : "Cluster Graphite", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[width * height];
        for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
            {
                float horizontal = column / (width - 1f);
                float vertical = row / (height - 1f);
                float side = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.13f, 0.29f, Mathf.Abs(horizontal - 0.5f)));
                float light = 0.012f + 0.019f * Mathf.Sin(vertical * Mathf.PI) + 0.014f * side;
                pixels[row * width + column] = shade ? new Color(0.015f, 0.022f, 0.026f, 0.97f * side)
                    : new Color(light * 0.83f, light, light * 1.08f, 1);
            }
        texture.SetPixels(pixels);
        texture.Apply();
        ownedAssets.Add(texture);
        return texture;
    }

    private Texture2D MakeCover(int index)
    {
        const int size = 192;
        var texture = new Texture2D(size, size, TextureFormat.RGB24, false) { name = "Original Album Artwork", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        Color sky = index % 3 == 0 ? new Color32(91, 160, 160, 255) : index % 3 == 1 ? new Color32(86, 104, 153, 255) : new Color32(209, 140, 99, 255);
        for (int row = 0; row < size; row++)
            for (int column = 0; column < size; column++)
            {
                float horizontal = column / (size - 1f);
                float vertical = row / (size - 1f);
                Color color = Color.Lerp(new Color32(20, 52, 67, 255), sky, vertical);
                if (Vector2.Distance(new Vector2(horizontal, vertical), new Vector2(0.67f, 0.72f)) < 0.15f)
                    color = new Color32(235, 227, 196, 255);
                float ridge = 0.31f + 0.19f * Mathf.Sin(horizontal * 4 + index) + 0.06f * Mathf.Sin(horizontal * 13);
                if (vertical < ridge)
                    color = new Color32(20, 58, 63, 255);
                if (vertical < 0.22f + 0.10f * Mathf.Cos(horizontal * 7))
                    color = new Color32(15, 35, 46, 255);
                if (row < 36 && row % 5 == 0)
                    color = Color.Lerp(color, sky, 0.18f);
                pixels[row * size + column] = color;
            }
        texture.SetPixels(pixels);
        texture.Apply();
        ownedAssets.Add(texture);
        return texture;
    }

    private AudioClip MakeDemoTrack(int index)
    {
        const int sampleRate = 22050;
        const int duration = 32;
        var samples = new float[sampleRate * duration];
        int[] notes = { 0, 7, 12, 4, 9, 7, 4, 2 };
        for (int sample = 0; sample < samples.Length; sample++)
        {
            double time = sample / (double)sampleRate;
            double beat = time * 2;
            double phase = beat - Math.Floor(beat);
            int note = notes[((int)beat + index * 2) % notes.Length] + index * 2;
            double frequency = 220 * Math.Pow(2, note / 12.0);
            double melody = Math.Sin(2 * Math.PI * frequency * time) * Math.Exp(-phase * 6) * Math.Min(phase * 60, 1);
            double bass = Math.Sin(2 * Math.PI * 55 * Math.Pow(2, index / 12.0) * time) * 0.18;
            double pad = (Math.Sin(2 * Math.PI * 110 * time) + Math.Sin(2 * Math.PI * 164.81 * time)) * 0.07;
            double fade = Math.Min(1, Math.Min(time, duration - time) / 0.25);
            samples[sample] = (float)((melody * 0.24 + bass + pad) * fade);
        }
        AudioClip clip = AudioClip.Create(demoTitles[index], samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        ownedAssets.Add(clip);
        return clip;
    }

    private static string FormatTime(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    private void OnDestroy()
    {
        if (audioSource != null)
            audioSource.Stop();
        foreach (UnityEngine.Object resource in ownedAssets)
            SanFranciscoMap.Release(resource);
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class ClusterDialGraphic : MaskableGraphic
{
    private float speed;
    public float Speed
    {
        get => speed;
        set
        {
            if (Mathf.Abs(speed - value) < 0.01f)
                return;
            speed = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.47f;
        Color track = new Color32(63, 79, 83, 255);
        Color accent = new Color32(110, 226, 199, 255);
        for (int index = 0; index < 120; index++)
        {
            float start = 225 - index / 120f * 270;
            float end = 225 - (index + 1) / 120f * 270;
            Arc(helper, radius, 3, start, end, track);
            if (index < Mathf.Clamp(speed, 0, 120))
                Arc(helper, radius, 6, start, end, accent);
            if (index % 2 == 0)
            {
                float inner = radius - (index % 10 == 0 ? 23 : 14);
                Segment(helper, Polar(inner, start), Polar(radius - 6, start), index % 10 == 0 ? 2f : 1f,
                    index % 10 == 0 ? new Color32(179, 194, 197, 255) : track);
            }
        }
        float needle = 225 - Mathf.Clamp(speed / 120, 0, 1) * 270;
        Segment(helper, Polar(radius - 30, needle), Polar(radius + 5, needle), 4, accent);
    }

    private static Vector2 Polar(float radius, float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * radius;

    private static void Arc(VertexHelper helper, float radius, float width, float start, float end, Color color)
    {
        Quad(helper, Polar(radius, start), Polar(radius, end), Polar(radius - width, end), Polar(radius - width, start), color);
    }

    private static void Segment(VertexHelper helper, Vector2 start, Vector2 end, float width, Color color)
    {
        Vector2 direction = (end - start).normalized;
        Vector2 normal = new Vector2(-direction.y, direction.x) * width * 0.5f;
        Quad(helper, start - normal, start + normal, end + normal, end - normal, color);
    }

    private static void Quad(VertexHelper helper, Vector2 first, Vector2 second, Vector2 third, Vector2 fourth, Color color)
    {
        int offset = helper.currentVertCount;
        foreach (Vector2 point in new[] { first, second, third, fourth })
            helper.AddVert(point, color, Vector2.zero);
        helper.AddTriangle(offset, offset + 1, offset + 2);
        helper.AddTriangle(offset, offset + 2, offset + 3);
    }
}