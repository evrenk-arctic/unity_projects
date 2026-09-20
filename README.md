# San Francisco Navigation Display

Open `Assets/Scenes/SampleScene.unity` in Unity 6000.6 and press **Play**.
The blue arrow follows a one-way 0.9 km drive from Drumm Street to Washington
Street beside the Transamerica Pyramid, then stops and displays **Arrived**.
The route does not loop or restart automatically; restart Play mode to drive again. **Google
Photorealistic 3D Tiles is the default map source.** Without a configured key, the
display automatically uses the bundled offline OpenStreetMap map.

## Google Map Tiles Setup

1. Enable **Map Tiles API** and billing on your Google Cloud project. The key must
	allow Photorealistic 3D Tiles requests from this Unity application.
2. Replace the placeholder in `UserSettings/GoogleMapsApiKey.txt` with just your API
	key on one line, without quotes, JSON, or `key=`. The file is Git-ignored and
	outside `Assets`; it is not imported or included in builds.
3. Press **Play**. The car waits while Cesium streams the map and samples the route
	elevation, then starts cruising. Google/server attribution stays on screen.

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
a highlighted route, upcoming turns, current street, mph, estimated arrival, and
trip progress. Use the on-screen controls to zoom, switch north-up/heading-up,
or pause. Keyboard alternatives: **Space**, **N**, and **+ / -**.

The `car_navigation` component on `CesiumGeoreference` exposes cruise speed and
camera height. **Navigation > Preview Downtown Display** previews the generated
offline map in the editor without Google requests; Play mode is the normal animated
experience. The Google map retains real building heights and uses sampled road
surface elevation; the offline map keeps its simplified heights.

## Data and Scope

- Street/building geometry: [OpenStreetMap contributors](https://www.openstreetmap.org/copyright), licensed under [ODbL 1.0](https://opendatacommons.org/licenses/odbl/1-0/). The bundled XML is a filtered downtown extract downloaded from the OSM API. Building heights are compressed for legibility; ground elevation is flattened.
- Driving route: the outbound leg of a route generated with the [OSRM routing service](https://project-osrm.org/) using OpenStreetMap roads. It is a bundled demonstration route, not live traffic, GPS positioning, or a production navigation service. ETA is a simulation estimate.
- Typeface: Barlow Medium, under the SIL Open Font License bundled in `Assets/NavigationDisplay-License.txt`.
- Cesium provides geographic coordinate conversion and streams [Google Photorealistic 3D Tiles](https://developers.google.com/maps/documentation/tile/3d-tiles) when selected and configured. The unused original tileset remains disabled. Google tiles and sampled heights are not saved as local map assets.

## Verification

`NavigationSceneChecks.Run` is an editor batch entry point that checks geometry,
one-way travel, destination arrival without wraparound, pause/resume, control wiring, zoom bounds, pointer hit
testing, and desktop/portrait rendering. It also checks the Google default,
key-file handling, URL configuration, terrain alignment, bounded interpolation of
missing heights, startup readiness, and failure fallback with synthetic inputs,
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
This check makes billable Google requests and verifies tile rendering, route
heights, car motion, button hit testing, and desktop/portrait camera captures.
It was verified against the downtown route on 2026-09-20. Camera captures do not
include Cesium's separate UI Toolkit attribution overlay; the normal Game view does.