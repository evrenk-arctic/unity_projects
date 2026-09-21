# San Francisco Instrument Cluster

Open `Assets/Scenes/SampleScene.unity` in Unity 6000.6 and press **Play**.
The scene starts as a **1920x720 instrument cluster**: an animated speedometer on
the left, a media player on the right, and an empty center. Press **M** to reveal
the moving navigation map across the entire background; both instruments stay in
the foreground. Press **M** again to return to the clean cluster. The drive and
music continue independently of which background is visible.

The blue arrow follows a one-way 0.9 km drive from Drumm Street to Washington
Street beside the Transamerica Pyramid, then stops and displays **Arrived**.
The route does not loop or restart automatically; restart Play mode to drive again. **Google
Photorealistic 3D Tiles is the default map source.** Without a configured key, the
display automatically uses the bundled offline OpenStreetMap map. The speedometer
uses that drive's actual simulated speed, including acceleration, slowing at turns,
pausing, and stopping on arrival. It is not connected to real vehicle telemetry.

The standalone player defaults to a **1920x720 window**. The editor selects a
matching fixed-resolution Game view preset once per session; select
**Navigation > Set Game View to 1920x720** to restore it manually. Unity may scale
the preview to fit the Game tab, but its render resolution remains 1920x720.

## Google Map Tiles Setup

1. Enable **Map Tiles API** and billing on your Google Cloud project. The key must
	allow both Photorealistic 3D Tiles and 2D Roadmap tile sessions from this Unity application.
2. Replace the placeholder in `UserSettings/GoogleMapsApiKey.txt` with just your API
	key on one line, without quotes, JSON, or `key=`. The file is Git-ignored and
	outside `Assets`; it is not imported or included in builds.
3. Press **Play**. The car waits while Cesium streams the map and samples the route
	elevation for 3D, then starts cruising. In 2D it waits for Google Roadmap imagery
	instead; no 3D terrain sampling is required. Google/server attribution stays on
	screen whenever the map is revealed. Map streaming starts with the scene, even
	while the cluster-only background is visible, and may incur API charges.

To select the original styled map, change **Map Source** on the `car_navigation`
component under `CesiumGeoreference` to **Offline Open Street Map**, then restart
Play mode. Key changes also take effect on the next Play session. Sampling starts
after Cesium creates its first tile and retries incomplete queries up to three
times. Missing heights between valid samples at most 100 meters apart are
interpolated along the route. Missing endpoint heights use the nearest valid
sample only when it is within 25 meters; start and destination stay independent. Larger gaps,
missing or invalid key files, request failures, and a 90-second loading timeout
still fall back to the offline map. Console warnings identify the specific cause
without including the API key or request URL.

For a built player, supply the same key filename under
`Application.persistentDataPath` (`car_navigation.GoogleApiKeyDirectory` gives the
runtime directory). The editor's key file is deliberately not copied to builds.
This file-based integration targets native players, not WebGL browser storage.
Restrict the key to Map Tiles API and use appropriate application restrictions and
quotas. A key used by a client application cannot be kept secret from its users.
The application does not log the key, but Cesium/network diagnostics may contain
request URLs; redact those before sharing logs. Google streaming requires internet
access and is subject to Google's billing, coverage, and Map Tiles API terms.

The display includes an angled follow camera, real street and building footprints,
a highlighted route, upcoming turns, a compact current-street label, and arrival
guidance. The map fills the background without top or bottom bars or on-screen
navigation buttons; provider credits remain visible between the foreground
instruments. Media controls are clickable icons and sliders. In the editor, focus
the **Game** view for keyboard shortcuts:

| Shortcut | Action |
| --- | --- |
| **M** | Reveal / hide the full-background navigation map |
| **P** | Play / pause music |
| **[** / **]** | Previous / next media track |
| **+** or **=** (also numpad **+**) | Zoom in |
| **-** (also numpad **-**) | Zoom out |
| **N** | Toggle north-up / heading-up |
| **T** | Toggle day/night (server-rendered styles on Google Roadmap) |
| **V** | Switch Google 2D Roadmap / photorealistic 3D |
| **Space** | Pause / resume driving |

Each press performs one action. Pause/resume does not restart a completed trip.

## Media Player

The player supports play/pause, previous/next track, seek, volume, mute, elapsed
time, automatic track advance, track title, and artwork. It starts at low volume
with three original synthesized demo tracks and generated artwork, so the controls
work immediately without downloads or an account. These are demonstration music
clips, not commercial songs or a connection to Spotify/Apple Music.

To play your own music, add Unity-supported audio assets to the project and assign
them to **Cluster Media > Media Tracks** on the `car_navigation` component. Null
entries are ignored; an empty list uses the demo playlist. Local track titles use
the clip names and generated artwork. **Space** pauses only the drive; **P** pauses
only music. The previous-track control restarts a track after its first three
seconds, otherwise selects the preceding track.

The map starts in daytime with the angled 3D view, hidden behind the cluster until
**M** is pressed. The two map mode switches are
independent and preserve route progress, pause state, zoom, and heading preference.

**Google 2D mode uses real [Roadmap tiles](https://developers.google.com/maps/documentation/tile/roadmap)**,
including Google's roads, buildings, place labels, and icons. It is a separate
`CesiumGoogleMapTilesRasterOverlay` on an unlit ellipsoid, not a rotated view of the
photorealistic model. The blue route and marker are flattened for this view without
overwriting their sampled 3D heights. **N** selects north-up when you prefer raster
map labels to stay upright; heading-up rotates the map and its labels together.

Google Roadmap day mode uses the default Google styling. Night mode requests new
tiles with dark geometry and readable road, water, and label colors through Google's
[server-side style API](https://developers.google.com/maps/documentation/tile/style-reference).
The returned tile imagery is styled by Google, not tinted by Unity. The API takes
explicit JSON style rules, not a built-in Navigation SDK night-mode flag; this scene
does not embed Google's consumer navigation app or Navigation SDK.

The photorealistic 3D API does **not** supply nighttime imagery. In 3D, night mode
remains a local dark treatment of daytime photos. Offline mode retains its local
day/night palette and switches between angled and top-down views. Required provider
credits remain visible in every Google mode.

In 2D, provider credits use a compact, content-sized box. Logos are scaled
proportionally with clear space, and attribution text remains readable and
clickable. The expanded attribution panel also sizes to its content rather than
reserving most of the screen height.

Switching Google sources or Roadmap styles may take a moment and pauses simulated
travel while new tiles arrive. The existing pause preference is preserved. Failed
requests or a 90-second loading timeout fall back to the offline map with a specific
Console warning. Cesium manages tile streaming, session requests, and attribution;
the API key is never serialized into the scene, and this code does not save Google
tiles to project assets.

The `car_navigation` component on `CesiumGeoreference` exposes initial **Top Down
View** (Google Roadmap in Google mode) and **Night Mode** settings, cruise speed, and camera height.
**Navigation Background** controls whether the scene starts with the map revealed.
**Navigation > Preview Downtown Display** previews the generated
offline map in the editor without Google requests; Play mode is the normal animated
experience. The Google map retains real building heights and uses sampled road
surface elevation; the offline map keeps its simplified heights.

## Data and Scope

- Street/building geometry: [OpenStreetMap contributors](https://www.openstreetmap.org/copyright), licensed under [ODbL 1.0](https://opendatacommons.org/licenses/odbl/1-0/). The bundled XML is a filtered downtown extract downloaded from the OSM API. Building heights are compressed for legibility; ground elevation is flattened.
- Driving route: the outbound leg of a route generated with the [OSRM routing service](https://project-osrm.org/) using OpenStreetMap roads. It is a bundled demonstration route, not live traffic, GPS positioning, or a production navigation service.
- Typeface: Barlow Medium, under the SIL Open Font License bundled in `Assets/NavigationDisplay-License.txt`.
- Demo media: original synthesized music and procedurally generated bitmap artwork created at runtime; no third-party recordings are bundled.
- Cesium provides geographic coordinate conversion and streams [Google Photorealistic 3D Tiles](https://developers.google.com/maps/documentation/tile/3d-tiles) or Google 2D Roadmap tiles when selected and configured. The unused original tileset remains disabled. Google tiles and sampled heights are not saved as local map assets.

## Verification

`NavigationSceneChecks.Run` is an editor batch entry point that checks geometry,
cluster-only and navigation-background rendering, speed binding, media button hit
testing, playback/track/seek/volume/mute/auto-advance behavior, the empty center, and
persistent foreground instruments. It also checks
one-way travel, destination arrival without wraparound, simulated keyboard presses,
pause/resume, orientation toggling, zoom bounds, held-key behavior, absence of
on-screen navigation controls and full-width HUD bars, and desktop/portrait rendering. Mode
checks cover all day/night and 2D/3D combinations at 1920x720, portrait 2D views,
rendered night brightness, camera direction, unchanged trip progress, and new-tile
theme inheritance without modifying shared materials. It also checks the Google default,
key-file handling, URL configuration, terrain alignment, bounded interpolation of
missing heights, startup readiness, Roadmap configuration, server-style selection,
flat overlays that preserve 3D heights, and failure fallback with synthetic inputs,
without contacting Google. It writes PNGs to `Temp/NavigationChecks`
(or the directory supplied through `NAVIGATION_CHECK_OUTPUT`). Run it against a
separate copy if this project is already open in Unity:

```sh
Unity -batchmode -projectPath <project-copy> -executeMethod NavigationSceneChecks.Run -logFile <log-path>
```

Do not pass `-nographics` or `-quit`: the checks enter Play mode, render the actual
Unity cameras and UI, and exit with a success or failure code automatically.

For a bounded live check, use `-executeMethod NavigationSceneChecks.RunGoogleLive`
instead and provide the key file in the test project's `UserSettings` directory.
This check makes billable Google requests and verifies 3D -> Roadmap -> night ->
day -> 3D transitions, flat/3D route alignment, unchanged trip progress, completed
arrival, actual day/night tile brightness, and at least 99% map coverage in 1920x720
and portrait captures. `NavigationSceneChecks.RunGoogleRoadmapLive` starts directly
in 2D and also verifies that Roadmap startup does not require photorealistic tiles.
It was verified against the downtown route on 2026-09-20. Camera captures do not
include Cesium's separate UI Toolkit attribution overlay; the normal Game view does.