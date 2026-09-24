using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class car_navigation : MonoBehaviour
{
	public enum MapSource
	{
		GooglePhotorealistic3DTiles,
		OfflineOpenStreetMap
	}

	[Header("Map Source")]
	[SerializeField] private MapSource mapSource = MapSource.GooglePhotorealistic3DTiles;
	[SerializeField] private string googleApiKeyFile = "GoogleMapsApiKey.txt";

	[Header("Display Modes")]
	[SerializeField] private bool topDownView;
	[SerializeField] private bool nightMode;
	[SerializeField] private bool navigationBackground = true;
	[SerializeField] private bool instrumentClusterVisible;

	[Header("Cluster Media")]
	[SerializeField] private AudioClip[] mediaTracks = Array.Empty<AudioClip>();

	[Header("Downtown San Francisco")]
	[SerializeField] private CesiumGeoreference georeference;
	[SerializeField] private Camera navigationCamera;
	[SerializeField] private TextAsset mapData;
	[SerializeField] private TextAsset routeData;
	[SerializeField] private Font displayFont;
	[SerializeField] private Shader mapShader;
	[SerializeField] private Shader vehicleShader;
	[SerializeField] private Shader navigationOverlayShader;
	[SerializeField, Range(5f, 35f)] private float cruiseSpeedMph = 22f;
	[SerializeField, Range(90f, 300f)] private float cameraHeight = 170f;

	private const double OriginLongitude = -122.4006;
	private const double OriginLatitude = 37.7944;
	private const float MetersPerMile = 1609.344f;
	private Vector3[] route;
	private Transform vehicle;
	private int segment;
	private float segmentDistance;
	private float speed;
	private bool paused;
	private bool arrived;
	private bool northUp;
	private float routeLength;
	private float[] cumulativeDistances;
	private RouteStep[] steps;
	private RouteStep destinationStep;
	private Transform generated;
	private SanFranciscoMap map;
	private NavigationHud hud;
	private InstrumentCluster cluster;
	private Cesium3DTileset googleTileset;
	private Cesium3DTileset googleRoadmapTileset;
	private CesiumGoogleMapTilesRasterOverlay googleRoadmapOverlay;
	private string googleApiKey;
	private bool googleRoadmapNight;
	private readonly HashSet<Texture> previousRoadmapTextures = new HashSet<Texture>();
	private double3[] routeCoordinates;
	private Task<CesiumSampleHeightResult> routeHeightRequest;
	private bool googleMapLoading;
	private bool googleLoadFailed;
	private long googleLoadHttpStatus;
	private string googleFallbackReason;
	private bool googleTilesReady;
	private int googleHeightAttempts;
	private float googleNextHeightAttempt;
	private float googleLoadDeadline;
	private GameObject routeCasing;
	private GameObject routeRibbon;
	private Material routeCasingMaterial;
	private Material routeMaterial;
	private MaterialPropertyBlock tileAppearance;
	private static readonly int CesiumBaseColor = Shader.PropertyToID("_baseColorFactor");
	private static readonly int StandardBaseColor = Shader.PropertyToID("_BaseColor");
	private static readonly int RoadmapTexture = Shader.PropertyToID("_overlayTexture_0");
	private bool IsGoogleRoadmap => topDownView && googleRoadmapTileset != null;
	public float SpeedMph => speed / MetersPerMile * 3600f;

	public static string GoogleApiKeyDirectory => Application.isEditor
		? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings"))
		: Application.persistentDataPath;

	[Serializable] private sealed class RouteData
	{
		public string destination = "Destination";
		public RoutePoint[] points = Array.Empty<RoutePoint>();
		public RouteStep[] steps = Array.Empty<RouteStep>();
	}

	[Serializable] private sealed class RoutePoint
	{
		public double longitude = 0;
		public double latitude = 0;
	}

	[Serializable] private sealed class RouteStep
	{
		public string street = "";
		public string type = "";
		public string modifier = "straight";
		public double longitude = 0;
		public double latitude = 0;
		[NonSerialized] public float distance;
	}

	private void Start()
	{
		BuildDisplay();
	}

	public void BuildDisplay()
	{
		if (generated != null)
			return;
		if (routeData == null)
		{
			Debug.LogError("Assign the bundled San Francisco route asset to the navigation controller.", this);
			enabled = false;
			return;
		}
		if (georeference == null)
			georeference = FindAnyObjectByType<CesiumGeoreference>();
		if (georeference == null)
			georeference = new GameObject("San Francisco Georeference").AddComponent<CesiumGeoreference>();

		georeference.SetOriginLongitudeLatitudeHeight(OriginLongitude, OriginLatitude, 0.0);
		navigationCamera = navigationCamera != null ? navigationCamera : Camera.main;
		if (navigationCamera == null)
		{
			navigationCamera = new GameObject("Navigation Camera").AddComponent<Camera>();
			navigationCamera.tag = "MainCamera";
		}

		navigationCamera.orthographic = true;
		navigationCamera.orthographicSize = cameraHeight;
		navigationCamera.nearClipPlane = 0.5f;
		navigationCamera.farClipPlane = 3000f;
		navigationCamera.clearFlags = CameraClearFlags.SolidColor;
		navigationCamera.backgroundColor = new Color32(222, 233, 232, 255);
		UniversalAdditionalCameraData cameraData = navigationCamera.GetUniversalAdditionalCameraData();
		cameraData.renderPostProcessing = false;
		cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
		cameraData.antialiasingQuality = AntialiasingQuality.High;

		generated = new GameObject("San Francisco Navigation Display").transform;
		if (!Application.isPlaying)
			generated.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
		map = new SanFranciscoMap(generated, mapShader, vehicleShader);
		LoadRoute();
		if (!TryCreateGoogleTileset())
		{
			if (mapData == null)
			{
				Debug.LogError("Assign the bundled San Francisco map for offline navigation.", this);
				enabled = false;
				return;
			}
			map.Build(mapData, MapPoint);
		}
		routeCasingMaterial = CreateOverlayMaterial("Route Outline", Color.white, 3010);
		routeMaterial = CreateOverlayMaterial("Active Route Blue", new Color32(44, 132, 245, 255), 3011);
		DrawRoute();
		CreateVehicle();
		vehicle.rotation = Quaternion.LookRotation(route[1] - route[0]);
		PlaceVehicle();
		UpdateCamera(true);
		hud = new NavigationHud(generated, navigationCamera, displayFont, MapPoint);
		cluster = generated.gameObject.AddComponent<InstrumentCluster>();
		cluster.Initialize(navigationCamera, displayFont, mediaTracks);
		ApplyNavigationBackground();
		ApplyDisplayTheme();
		if (googleMapLoading)
		{
			SetNavigationVisible(false);
		}
		UnityEngine.Canvas.ForceUpdateCanvases();
		RefreshHud();
	}

	private bool TryCreateGoogleTileset()
	{
		if (mapSource != MapSource.GooglePhotorealistic3DTiles || !Application.isPlaying)
			return false;
		if (!TryReadGoogleApiKey(GoogleApiKeyDirectory, googleApiKeyFile, out string key))
		{
			Debug.LogWarning("Google Map Tiles key file is missing, empty, or unreadable. Using the offline map. " +
				"Put the key in GoogleMapsApiKey.txt under " + GoogleApiKeyDirectory + " and restart Play mode.", this);
			return false;
		}

		googleApiKey = key;
		Cesium3DTileset.OnCesium3DTilesetLoadFailure += OnGoogleTilesetLoadFailure;
		CesiumRasterOverlay.OnCesiumRasterOverlayLoadFailure += OnGoogleRoadmapLoadFailure;
		ActivateGoogleView();
		return true;
	}

	private void ActivateGoogleView()
	{
		googleLoadFailed = false;
		googleLoadHttpStatus = 0;
		googleTilesReady = false;
		googleHeightAttempts = 0;
		googleNextHeightAttempt = 0;
		routeHeightRequest = null;
		previousRoadmapTextures.Clear();
		if (topDownView)
		{
			if (googleTileset != null)
				googleTileset.gameObject.SetActive(false);
			if (googleRoadmapTileset == null)
			{
				googleRoadmapTileset = CreateGoogleRoadmap(googleApiKey);
				googleRoadmapOverlay = googleRoadmapTileset.GetComponent<CesiumGoogleMapTilesRasterOverlay>();
			}
			googleRoadmapNight = nightMode;
			googleRoadmapOverlay.styles = GoogleRoadmapStyles(nightMode);
			googleRoadmapTileset.gameObject.SetActive(true);
		}
		else
		{
			if (googleRoadmapTileset != null)
				googleRoadmapTileset.gameObject.SetActive(false);
			if (googleTileset == null)
			{
				googleTileset = CreateGoogleTileset(googleApiKey);
				googleTileset.OnTileGameObjectCreated += OnGoogleTileCreated;
			}
			googleTileset.gameObject.SetActive(true);
		}
		BeginGoogleMapLoading();
		if (vehicle != null)
		{
			DrawRoute();
			PlaceVehicle();
			UpdateCamera(true);
			SetNavigationVisible(false);
		}
	}

	private void BeginGoogleMapLoading()
	{
		googleMapLoading = true;
		googleLoadDeadline = Time.realtimeSinceStartup + 90f;
	}

	private Cesium3DTileset CreateGoogleTileset(string key)
	{
		var tiles = new GameObject("Google Photorealistic 3D Tiles");
		tiles.SetActive(false);
		tiles.hideFlags = HideFlags.DontSave;
		tiles.transform.SetParent(georeference.transform, false);
		Cesium3DTileset tileset = tiles.AddComponent<Cesium3DTileset>();
		tileset.tilesetSource = CesiumDataSource.FromUrl;
		tileset.url = "https://tile.googleapis.com/v1/3dtiles/root.json?key=" + Uri.EscapeDataString(key);
		tileset.showCreditsOnScreen = true;
		tileset.maximumScreenSpaceError = 8;
		tileset.createPhysicsMeshes = false;
		tileset.updateInEditor = false;
		return tileset;
	}

	private Cesium3DTileset CreateGoogleRoadmap(string key)
	{
		var tiles = new GameObject("Google 2D Roadmap");
		tiles.SetActive(false);
		tiles.hideFlags = HideFlags.DontSave;
		tiles.transform.SetParent(georeference.transform, false);
		Cesium3DTileset tileset = tiles.AddComponent<Cesium3DTileset>();
		tileset.tilesetSource = CesiumDataSource.FromEllipsoid;
		tileset.opaqueMaterial = Resources.Load<Material>("CesiumUnlitTilesetMaterial");
		tileset.showCreditsOnScreen = true;
		tileset.maximumScreenSpaceError = 4;
		tileset.createPhysicsMeshes = false;
		tileset.updateInEditor = false;
		CesiumGoogleMapTilesRasterOverlay overlay = tiles.AddComponent<CesiumGoogleMapTilesRasterOverlay>();
		overlay.apiKey = key;
		overlay.mapType = GoogleMapTilesMapType.Roadmap;
		overlay.language = "en-US";
		overlay.region = "US";
		overlay.layerTypes = new List<GoogleMapTilesLayerType>();
		overlay.styles = GoogleRoadmapStyles(nightMode);
		overlay.scale = GoogleMapTilesScale.ScaleFactor2x;
		overlay.highDpi = true;
		overlay.maximumScreenSpaceError = 1;
		overlay.showCreditsOnScreen = true;
		return tileset;
	}

	private static List<string> GoogleRoadmapStyles(bool night)
	{
		if (!night)
			return new List<string>();
		return new List<string>
		{
			"{\"elementType\":\"geometry\",\"stylers\":[{\"color\":\"#202124\"}]}",
			"{\"elementType\":\"labels.text.fill\",\"stylers\":[{\"color\":\"#e8eaed\"}]}",
			"{\"elementType\":\"labels.text.stroke\",\"stylers\":[{\"color\":\"#202124\"}]}",
			"{\"featureType\":\"poi\",\"elementType\":\"labels.text.fill\",\"stylers\":[{\"color\":\"#bdc1c6\"}]}",
			"{\"featureType\":\"poi.park\",\"elementType\":\"geometry\",\"stylers\":[{\"color\":\"#263c32\"}]}",
			"{\"featureType\":\"road\",\"elementType\":\"geometry.fill\",\"stylers\":[{\"color\":\"#484b50\"}]}",
			"{\"featureType\":\"road\",\"elementType\":\"geometry.stroke\",\"stylers\":[{\"color\":\"#303236\"}]}",
			"{\"featureType\":\"road.highway\",\"elementType\":\"geometry.fill\",\"stylers\":[{\"color\":\"#74684c\"}]}",
			"{\"featureType\":\"water\",\"elementType\":\"geometry\",\"stylers\":[{\"color\":\"#172c3a\"}]}",
			"{\"featureType\":\"water\",\"elementType\":\"labels.text.fill\",\"stylers\":[{\"color\":\"#8ab4d0\"}]}",
			"{\"featureType\":\"water\",\"elementType\":\"labels.text.stroke\",\"stylers\":[{\"color\":\"#172c3a\"}]}"
		};
	}

	private void OnGoogleTilesetLoadFailure(Cesium3DTilesetLoadFailureDetails details)
	{
		if (details.tileset == (IsGoogleRoadmap ? googleRoadmapTileset : googleTileset))
		{
			googleLoadFailed = true;
			googleLoadHttpStatus = details.httpStatusCode;
		}
	}

	private void OnGoogleRoadmapLoadFailure(CesiumRasterOverlayLoadFailureDetails details)
	{
		if (IsGoogleRoadmap && details.overlay == googleRoadmapOverlay)
		{
			googleLoadFailed = true;
			googleLoadHttpStatus = details.httpStatusCode;
		}
	}

	private HashSet<Texture> LoadedRoadmapTextures()
	{
		var textures = new HashSet<Texture>();
		if (googleRoadmapTileset == null)
			return textures;
		var properties = new MaterialPropertyBlock();
		foreach (MeshRenderer renderer in googleRoadmapTileset.GetComponentsInChildren<MeshRenderer>())
		{
			if (!renderer.enabled)
				continue;
			Material[] materials = renderer.sharedMaterials;
			for (int index = 0; index < materials.Length; index++)
			{
				renderer.GetPropertyBlock(properties, index);
				Texture texture = properties.GetTexture(RoadmapTexture);
				if (texture == null && materials[index] != null && materials[index].HasProperty(RoadmapTexture))
					texture = materials[index].GetTexture(RoadmapTexture);
				if (texture != null && texture.width > 4 && texture.height > 4)
					textures.Add(texture);
			}
		}
		return textures;
	}

	private void OnGoogleTileCreated(GameObject tile)
	{
		googleTilesReady = true;
		ApplyGoogleTileTheme(tile);
	}

	private void ApplyGoogleTileTheme(GameObject tile)
	{
		if (tileAppearance == null)
			tileAppearance = new MaterialPropertyBlock();
		Vector4 tint = nightMode ? new Vector4(0.18f, 0.23f, 0.28f, 1f) : Vector4.one;
		foreach (MeshRenderer renderer in tile.GetComponentsInChildren<MeshRenderer>(true))
		{
			Material[] materials = renderer.sharedMaterials;
			for (int index = 0; index < materials.Length; index++)
			{
				Material material = materials[index];
				if (material == null)
					continue;
				int property = material.HasProperty(CesiumBaseColor) ? CesiumBaseColor : StandardBaseColor;
				if (!material.HasProperty(property))
					continue;
				renderer.GetPropertyBlock(tileAppearance, index);
				tileAppearance.SetVector(property, Vector4.Scale(material.GetVector(property), tint));
				renderer.SetPropertyBlock(tileAppearance, index);
			}
		}
	}

	private void ApplyDisplayTheme()
	{
		if (navigationCamera != null)
			navigationCamera.backgroundColor = nightMode ? new Color32(22, 29, 33, 255) : new Color32(222, 233, 232, 255);
		map?.SetNightMode(nightMode);
		hud?.SetNightMode(nightMode);
		if (routeCasingMaterial != null)
			routeCasingMaterial.SetColor("_BaseColor", nightMode ? new Color32(144, 181, 217, 255) : Color.white);
		if (googleTileset != null)
			ApplyGoogleTileTheme(googleTileset.gameObject);
		if (IsGoogleRoadmap && googleRoadmapNight != nightMode)
		{
			previousRoadmapTextures.Clear();
			previousRoadmapTextures.UnionWith(LoadedRoadmapTextures());
			googleRoadmapNight = nightMode;
			googleRoadmapOverlay.styles = GoogleRoadmapStyles(nightMode);
			BeginGoogleMapLoading();
		}
	}

	private void UpdateGoogleMap()
	{
		if (googleTileset == null && googleRoadmapTileset == null)
			return;
		if (googleLoadFailed)
		{
			UseOfflineFallback((IsGoogleRoadmap ? "Google Roadmap session" : "Photorealistic tileset") + " request failed (HTTP " + googleLoadHttpStatus + ").");
			return;
		}
		if (googleMapLoading && Time.realtimeSinceStartup >= googleLoadDeadline)
		{
			UseOfflineFallback(IsGoogleRoadmap ? "Google Roadmap tiles exceeded the 90-second loading limit." : "Route height sampling exceeded the 90-second loading limit.");
			return;
		}
		if (!googleMapLoading)
			return;
		if (IsGoogleRoadmap)
		{
			HashSet<Texture> textures = LoadedRoadmapTextures();
			textures.ExceptWith(previousRoadmapTextures);
			if (textures.Count == 0)
				return;
			FinishGoogleMapLoading();
			return;
		}
		if (routeHeightRequest == null)
		{
			if (!googleTilesReady || Time.realtimeSinceStartup < googleNextHeightAttempt)
				return;
			try
			{
				googleHeightAttempts++;
				routeHeightRequest = googleTileset.SampleHeightMostDetailed(routeCoordinates);
				routeHeightRequest.ContinueWith(failed => { var ignored = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
			}
			catch (Exception exception)
			{
				UseOfflineFallback("Height sampling could not start (" + exception.GetType().Name + ").");
			}
			return;
		}
		if (!routeHeightRequest.IsCompleted)
			return;
		if (routeHeightRequest.IsFaulted || routeHeightRequest.IsCanceled)
		{
			string failure = routeHeightRequest.IsCanceled ? "Canceled" : routeHeightRequest.Exception.GetBaseException().GetType().Name;
			UseOfflineFallback("Height sampling failed (" + failure + ").");
			return;
		}
		if (!TryApplyRouteHeights(routeHeightRequest.Result))
		{
			if (googleHeightAttempts < 3)
			{
				routeHeightRequest = null;
				googleNextHeightAttempt = Time.realtimeSinceStartup + 1f;
				return;
			}
			int successful = 0;
			bool[] samples = routeHeightRequest.Result?.sampleSuccess;
			if (samples != null)
				foreach (bool sample in samples)
					if (sample)
						successful++;
			UseOfflineFallback("Insufficient route terrain after three attempts (" + successful + "/" + route.Length + " samples succeeded).");
			return;
		}
		FinishGoogleMapLoading();
	}

	private void FinishGoogleMapLoading()
	{
		googleMapLoading = false;
		routeHeightRequest = null;
		previousRoadmapTextures.Clear();
		DrawRoute();
		PlaceVehicle();
		UpdateCamera(true);
		SetNavigationVisible(true);
	}

	private bool TryApplyRouteHeights(CesiumSampleHeightResult result)
	{
		if (result?.sampleSuccess == null || result.longitudeLatitudeHeightPositions == null ||
			result.sampleSuccess.Length != route.Length || result.longitudeLatitudeHeightPositions.Length != route.Length)
			return false;
		var heights = new float[route.Length];
		int firstValid = -1;
		int lastValid = -1;
		for (int index = 0; index < route.Length; index++)
		{
			heights[index] = float.NaN;
			if (!result.sampleSuccess[index] || !math.all(math.isfinite(result.longitudeLatitudeHeightPositions[index])))
				continue;
			double3 earthPosition = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(result.longitudeLatitudeHeightPositions[index]);
			double3 localPosition = georeference.TransformEarthCenteredEarthFixedPositionToUnity(earthPosition);
			heights[index] = georeference.transform.TransformPoint((Vector3)(float3)localPosition).y;
			if (!float.IsFinite(heights[index]))
				continue;
			lastValid = index;
			if (firstValid < 0)
				firstValid = index;
		}
		if (firstValid < 0 || lastValid == firstValid || cumulativeDistances[firstValid] > 25f || routeLength - cumulativeDistances[lastValid] > 25f)
			return false;
		for (int index = 0; index < firstValid; index++)
			heights[index] = heights[firstValid];
		for (int index = lastValid + 1; index < route.Length; index++)
			heights[index] = heights[lastValid];
		int previous = firstValid;
		for (int next = firstValid + 1; next <= lastValid; next++)
		{
			if (!float.IsFinite(heights[next]))
				continue;
			if (previous + 1 != next)
			{
				float gap = cumulativeDistances[next] - cumulativeDistances[previous];
				if (gap > 100f || gap <= 0)
					return false;
				for (int missing = previous + 1; missing < next; missing++)
				{
					float distance = cumulativeDistances[missing] - cumulativeDistances[previous];
					heights[missing] = Mathf.Lerp(heights[previous], heights[next], distance / gap);
				}
			}
			previous = next;
		}
		for (int index = 0; index < route.Length; index++)
			route[index].y = heights[index];
		return true;
	}

	private void UseOfflineFallback(string reason)
	{
		googleFallbackReason = reason;
		Cesium3DTileset.OnCesium3DTilesetLoadFailure -= OnGoogleTilesetLoadFailure;
		CesiumRasterOverlay.OnCesiumRasterOverlayLoadFailure -= OnGoogleRoadmapLoadFailure;
		ReleaseGoogleMaps();
		googleMapLoading = false;
		routeHeightRequest = null;
		Debug.LogWarning("Google Maps: " + reason + " Using the offline map. Check Map Tiles API permissions for the selected map type.", this);
		if (mapData == null)
		{
			SetNavigationVisible(false);
			enabled = false;
			return;
		}
		for (int index = 0; index < route.Length; index++)
			route[index].y = 0;
		map.Build(mapData, MapPoint);
		DrawRoute();
		PlaceVehicle();
		UpdateCamera(true);
		SetNavigationVisible(true);
	}

	private void DrawRoute()
	{
		if (routeCasing != null)
		{
			routeCasing.SetActive(false);
			routeRibbon.SetActive(false);
			SanFranciscoMap.Release(routeCasing);
			SanFranciscoMap.Release(routeRibbon);
		}
		Vector3[] displayRoute = route;
		if (IsGoogleRoadmap)
		{
			displayRoute = (Vector3[])route.Clone();
			for (int index = 0; index < displayRoute.Length; index++)
				displayRoute[index].y = 0;
		}
		routeCasing = map.Line("Route Casing", displayRoute, 12f, 0.75f, routeCasingMaterial);
		routeRibbon = map.Line("Downtown Route", displayRoute, 8f, 0.85f, routeMaterial);
	}

	private void SetNavigationVisible(bool visible)
	{
		routeCasing.SetActive(visible);
		routeRibbon.SetActive(visible);
		vehicle.gameObject.SetActive(visible);
	}

	private static bool TryReadGoogleApiKey(string directory, string fileName, out string key)
	{
		key = "";
		if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
			return false;
		try
		{
			string path = Path.Combine(directory, fileName);
			if (!File.Exists(path))
				return false;
			string value = File.ReadAllText(path).Trim();
			if (value.Length == 0 || value == "YOUR_GOOGLE_MAP_TILES_API_KEY" || value.IndexOfAny(new[] { '\r', '\n', ' ', '\t' }) >= 0)
				return false;
			key = value;
			return true;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private void LoadRoute()
	{
		RouteData data = JsonUtility.FromJson<RouteData>(routeData.text);
		var points = new List<Vector3>();
		var coordinates = new List<double3>();
		foreach (RoutePoint point in data.points)
		{
			Vector3 position = MapPoint(point.longitude, point.latitude);
			if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], position) > 0.1f)
			{
				points.Add(position);
				coordinates.Add(new double3(point.longitude, point.latitude, 0));
			}
		}
		if (points.Count < 2 || Vector3.Distance(points[0], points[points.Count - 1]) < 2f)
			throw new InvalidOperationException("The downtown route needs distinct start and destination points.");
		route = points.ToArray();
		routeCoordinates = coordinates.ToArray();
		cumulativeDistances = new float[route.Length];
		for (int index = 1; index < route.Length; index++)
			cumulativeDistances[index] = cumulativeDistances[index - 1] + Vector3.Distance(route[index - 1], route[index]);
		routeLength = cumulativeDistances[route.Length - 1];
		steps = data.steps;
		destinationStep = new RouteStep
		{
			street = data.destination,
			type = "arrive",
			modifier = "arrive",
			distance = routeLength
		};
		int previousIndex = 0;
		foreach (RouteStep step in steps)
		{
			Vector3 location = MapPoint(step.longitude, step.latitude);
			float closest = float.MaxValue;
			int closestIndex = previousIndex;
			for (int index = previousIndex; index < route.Length; index++)
			{
				float distance = (route[index] - location).sqrMagnitude;
				if (distance < closest)
				{
					closest = distance;
					closestIndex = index;
				}
				if (distance < 0.1f)
					break;
			}
			step.distance = cumulativeDistances[closestIndex];
			previousIndex = closestIndex;
		}
	}

	private Vector3 MapPoint(double longitude, double latitude)
	{
		double3 earthPosition = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
			new double3(longitude, latitude, 0.0));
		double3 localPosition = georeference.TransformEarthCenteredEarthFixedPositionToUnity(earthPosition);
		return georeference.transform.TransformPoint(new Vector3((float)localPosition.x, 0f, (float)localPosition.z));
	}

	private void CreateVehicle()
	{
		vehicle = new GameObject("Navigation Arrow").transform;
		vehicle.SetParent(generated, false);
		map.NavigationArrow(vehicle,
			CreateOverlayMaterial("Arrow Outline", Color.white, 3020),
			CreateOverlayMaterial("Arrow Blue", new Color32(16, 76, 211, 255), 3021),
			CreateOverlayMaterial("Arrow Highlight", new Color32(75, 160, 255, 255), 3021));
	}

	private Material CreateOverlayMaterial(string name, Color color, int renderQueue)
	{
		Material material = map.Material(name, color);
		material.shader = navigationOverlayShader != null ? navigationOverlayShader : Shader.Find("Navigation/Map Overlay");
		material.SetColor("_BaseColor", color);
		material.renderQueue = renderQueue;
		return material;
	}

	private void Update()
	{
		UpdateGoogleMap();
		HandleKeyboardInput(Keyboard.current);
		Simulate(Time.deltaTime);
	}

	private void HandleKeyboardInput(Keyboard keyboard)
	{
		if (keyboard != null)
		{
			if (keyboard.spaceKey.wasPressedThisFrame)
				TogglePause();
			if (keyboard.cKey.wasPressedThisFrame)
				ToggleInstrumentCluster();
			if (keyboard.mKey.wasPressedThisFrame)
				ToggleNavigationBackground();
			if (keyboard.pKey.wasPressedThisFrame)
				cluster?.TogglePlayback();
			if (keyboard.rightBracketKey.wasPressedThisFrame)
				cluster?.NextTrack();
			if (keyboard.leftBracketKey.wasPressedThisFrame)
				cluster?.PreviousTrack();
			if (keyboard.nKey.wasPressedThisFrame)
				ToggleOrientation();
			if (keyboard.vKey.wasPressedThisFrame)
				ToggleViewMode();
			if (keyboard.tKey.wasPressedThisFrame)
				ToggleDayNight();
			if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
				Zoom(-30);
			if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
				Zoom(30);
		}
	}

	public void Simulate(float deltaTime)
	{
		if (route == null || route.Length < 2 || googleMapLoading || arrived)
			return;

		float traveled = cumulativeDistances[segment] + segmentDistance;
		RouteStep next = NextTurn(traveled);
		float turnDistance = Mathf.Max(0, next.distance - traveled);
		float cornerFactor = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01((turnDistance - 5f) / 30f));
		float targetSpeed = Mathf.Min(cruiseSpeedMph * MetersPerMile / 3600f * cornerFactor, Mathf.Sqrt(6f * Mathf.Max(0, routeLength - traveled)));
		speed = Mathf.MoveTowards(speed, paused ? 0f : targetSpeed, deltaTime * 3f);
		Advance(speed * deltaTime);
		PlaceVehicle(deltaTime);
	}

	private RouteStep NextTurn(float traveled)
	{
		foreach (RouteStep step in steps)
			if (step.type != "depart" && step.type != "arrive" && step.distance > traveled + 3f)
				return step;
		return destinationStep;
	}

	private void Advance(float distance)
	{
		if (arrived)
			return;
		segmentDistance += Mathf.Max(0, distance);
		while (segment < route.Length - 1)
		{
			float length = cumulativeDistances[segment + 1] - cumulativeDistances[segment];
			if (segmentDistance < length && length > 0.001f)
				break;
			if (segment == route.Length - 2)
			{
				segmentDistance = length;
				arrived = true;
				speed = 0;
				break;
			}
			segmentDistance -= length;
			segment++;
		}
	}

	private void PlaceVehicle(float deltaTime = 1f)
	{
		Vector3 direction = route[segment + 1] - route[segment];
		float length = cumulativeDistances[segment + 1] - cumulativeDistances[segment];
		vehicle.position = Vector3.Lerp(route[segment], route[segment + 1], segmentDistance / length) + Vector3.up * 3f;
		if (IsGoogleRoadmap)
			vehicle.position = new Vector3(vehicle.position.x, 3f, vehicle.position.z);
		vehicle.localScale = Vector3.one * navigationCamera.orthographicSize / 170f;
		direction.y = 0;
		if (direction.sqrMagnitude > 0.001f)
			vehicle.rotation = Quaternion.Slerp(vehicle.rotation, Quaternion.LookRotation(direction), 1f - Mathf.Exp(-deltaTime * 5f));
	}

	private void LateUpdate()
	{
		if (vehicle != null)
		{
			UpdateCamera(false);
			RefreshHud();
		}
	}

	private void RefreshHud()
	{
		float traveled = cumulativeDistances[segment] + segmentDistance;
		RouteStep next = NextTurn(traveled);
		float turnDistance = Mathf.Max(0, next.distance - traveled);
		string street = steps[0].street;
		foreach (RouteStep step in steps)
			if (step.distance <= traveled + 1f)
				street = step.street;
		hud?.Update(street, next.street, next.modifier, turnDistance,
			paused, googleTileset != null || googleRoadmapTileset != null, googleMapLoading, arrived, IsGoogleRoadmap);
		cluster?.SetDrivingState(SpeedMph, paused, arrived, googleMapLoading);
	}

	private void UpdateCamera(bool immediate)
	{
		Vector3 forward = northUp ? Vector3.forward : vehicle.forward;
		Vector3 target = vehicle.position + forward * 65f;
		Vector3 position = target + Vector3.up * 360f - (topDownView ? Vector3.zero : forward * 230f);
		Quaternion rotation = topDownView ? Quaternion.LookRotation(Vector3.down, forward) : Quaternion.LookRotation(target - position);
		float blend = immediate ? 1f : 1f - Mathf.Exp(-Time.deltaTime * 3f);
		navigationCamera.transform.position = Vector3.Lerp(navigationCamera.transform.position, position, blend);
		navigationCamera.transform.rotation = Quaternion.Slerp(navigationCamera.transform.rotation, rotation, blend);
		navigationCamera.orthographicSize = Mathf.Lerp(navigationCamera.orthographicSize, cameraHeight, blend);
	}

	public void TogglePause()
	{
		if (arrived)
			return;
		paused = !paused;
		if (paused)
			speed = 0;
	}
	public void ToggleOrientation() => northUp = !northUp;
	public void ToggleInstrumentCluster()
	{
		instrumentClusterVisible = !instrumentClusterVisible;
		ApplyNavigationBackground();
	}

	public void ToggleNavigationBackground()
	{
		if (!instrumentClusterVisible)
			return;
		navigationBackground = !navigationBackground;
		ApplyNavigationBackground();
	}

	private void ApplyNavigationBackground()
	{
		bool showNavigation = !instrumentClusterVisible || navigationBackground;
		hud?.SetVisible(showNavigation);
		cluster?.SetVisible(instrumentClusterVisible);
		cluster?.SetNavigationVisible(showNavigation);
	}
	public void ToggleViewMode()
	{
		topDownView = !topDownView;
		if (!string.IsNullOrEmpty(googleApiKey))
			ActivateGoogleView();
	}
	public void ToggleDayNight()
	{
		nightMode = !nightMode;
		ApplyDisplayTheme();
	}
	public void Zoom(float amount) => cameraHeight = Mathf.Clamp(cameraHeight + amount, 90f, 300f);

	private void OnDestroy()
	{
		Cesium3DTileset.OnCesium3DTilesetLoadFailure -= OnGoogleTilesetLoadFailure;
		CesiumRasterOverlay.OnCesiumRasterOverlayLoadFailure -= OnGoogleRoadmapLoadFailure;
		ReleaseGoogleMaps();
		map?.Dispose();
		if (generated != null)
			SanFranciscoMap.Release(generated.gameObject);
	}

	private void ReleaseGoogleMaps()
	{
		if (googleTileset != null)
		{
			googleTileset.OnTileGameObjectCreated -= OnGoogleTileCreated;
			googleTileset.gameObject.SetActive(false);
			SanFranciscoMap.Release(googleTileset.gameObject);
		}
		if (googleRoadmapTileset != null)
		{
			googleRoadmapTileset.gameObject.SetActive(false);
			SanFranciscoMap.Release(googleRoadmapTileset.gameObject);
		}
		googleTileset = null;
		googleRoadmapTileset = null;
		googleRoadmapOverlay = null;
		googleApiKey = null;
	}
}
