using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class SanFranciscoMap : IDisposable
{
    private readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
    private readonly Transform parent;
    private readonly Shader mapShader;
    private readonly Shader vehicleShader;

    public SanFranciscoMap(Transform parent, Shader mapShader, Shader vehicleShader)
    {
        this.parent = parent;
        this.mapShader = mapShader;
        this.vehicleShader = vehicleShader;
    }

    public Material Material(string name, Color color, bool lit = false)
    {
        Shader shader = lit ? vehicleShader : mapShader;
        if (shader == null)
            shader = Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
        var material = new Material(shader) { name = name, color = color };
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.15f);
        material.SetFloat("_Cull", 0f);
        resources.Add(material);
        return material;
    }

    public void Build(TextAsset source, Func<double, double, Vector3> project)
    {
        XElement document = XDocument.Parse(source.text).Root;
        var nodes = new Dictionary<long, Vector3>();
        foreach (XElement node in document.Elements("node"))
            nodes[(long)node.Attribute("id")] = project((double)node.Attribute("lon"), (double)node.Attribute("lat"));

        var ground = new Geometry();
        var water = new Geometry();
        var parks = new Geometry();
        var roadEdges = new Geometry();
        var roads = new Geometry();
        var paths = new Geometry();
        var roofs = new Geometry();
        var walls = new Geometry();
        var roofEdges = new Geometry();
        var triangulatorObject = new GameObject("Map Polygon Triangulator");
        triangulatorObject.transform.SetParent(parent, false);
        PolygonCollider2D triangulator = triangulatorObject.AddComponent<PolygonCollider2D>();
        triangulator.isTrigger = true;

        water.Quad(new Vector3(-5000, -1, -5000), new Vector3(-5000, -1, 5000),
            new Vector3(5000, -1, 5000), new Vector3(5000, -1, -5000));

        foreach (XElement way in document.Elements("way"))
        {
            Dictionary<string, string> tags = way.Elements("tag")
                .ToDictionary(tag => (string)tag.Attribute("k"), tag => (string)tag.Attribute("v"));
            Vector3[] points = way.Elements("nd").Select(reference => (long)reference.Attribute("ref"))
                .Where(nodes.ContainsKey).Select(reference => nodes[reference]).ToArray();
            if (points.Length < 2)
                continue;

            if (tags.TryGetValue("natural", out string natural) && natural == "coastline")
            {
                for (int index = 0; index < points.Length - 1; index++)
                {
                    Vector3 first = points[index];
                    Vector3 second = points[index + 1];
                    if (Mathf.Abs(first.z) > 4000f || Mathf.Abs(second.z) > 4000f)
                        continue;
                    ground.Quad(new Vector3(-5000, 0, first.z), first, second, new Vector3(-5000, 0, second.z));
                }
                continue;
            }

            if (!points.Any(point => Mathf.Abs(point.x) < 1350 && Mathf.Abs(point.z) < 1100))
                continue;

            if (tags.TryGetValue("highway", out string highway))
            {
                if (highway == "construction" || highway == "proposed" || highway == "steps")
                    continue;
                bool pedestrian = highway == "footway" || highway == "path" || highway == "cycleway";
                float width = pedestrian ? 2.4f : highway == "service" ? 5f : highway == "primary" ? 15f : 10f;
                roadEdges.Stroke(points, width + 2.5f, 0.15f);
                (pedestrian ? paths : roads).Stroke(points, width, pedestrian ? 0.22f : 0.3f);
            }
            else if (tags.ContainsKey("building") && tags["building"] != "no")
            {
                float height = 12f;
                if (tags.TryGetValue("building:levels", out string levels) && float.TryParse(levels, NumberStyles.Float, CultureInfo.InvariantCulture, out float floors))
                    height = floors * 3f;
                if (tags.TryGetValue("height", out string heightText) && float.TryParse(heightText.Replace("m", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float meters))
                    height = meters;
                height = Mathf.Clamp(height * 0.4f, 3.5f, 38f);
                if (tags.TryGetValue("name", out string name) && name.Contains("Transamerica"))
                    height = 75f;
                roofs.Polygon(points, height, triangulator);
                roofEdges.Stroke(points, 0.6f, height + 0.04f);
                for (int index = 0; index < points.Length - 1; index++)
                {
                    Vector3 first = points[index];
                    Vector3 second = points[index + 1];
                    walls.Quad(first, first + Vector3.up * height, second + Vector3.up * height, second);
                }
            }
            else if (tags.TryGetValue("leisure", out string leisure) && leisure == "park")
            {
                parks.Polygon(points, 0.12f, triangulator);
            }
        }

        if (ground.VertexCount == 0)
            ground.Quad(new Vector3(-5000, 0, -5000), new Vector3(-5000, 0, 5000), new Vector3(5000, 0, 5000), new Vector3(5000, 0, -5000));

        Emit("San Francisco Bay", water, Material("Bay", new Color32(157, 207, 219, 255)));
        Emit("Downtown Blocks", ground, Material("Land", new Color32(225, 230, 225, 255)));
        Emit("Public Gardens", parks, Material("Parks", new Color32(168, 202, 157, 255)));
        Emit("Street Edges", roadEdges, Material("Street Edges", new Color32(204, 211, 207, 255)));
        Emit("Streets", roads, Material("Streets", new Color32(253, 253, 247, 255)));
        Emit("Walkways", paths, Material("Walkways", new Color32(240, 241, 232, 255)));
        Emit("Building Walls", walls, Material("Facades", new Color32(176, 189, 185, 255), true));
        Emit("Building Roofs", roofs, Material("Roofs", new Color32(212, 220, 217, 255)));
        Emit("Building Outlines", roofEdges, Material("Roof Edges", new Color32(190, 202, 197, 255)));
        Release(triangulatorObject);
    }

    public GameObject Line(string name, Vector3[] points, float width, float height, Material material)
    {
        var geometry = new Geometry();
        geometry.Stroke(points, width, height);
        return Emit(name, geometry, material);
    }

    public void NavigationArrow(Transform marker, Material outline, Material blue, Material highlight)
    {
        var border = new Geometry();
        border.Quad(new Vector3(0, 0, 14), new Vector3(-10, 0, -10), new Vector3(0, 0, -5), new Vector3(10, 0, -10));
        Emit("Arrow Outline", border, outline).transform.SetParent(marker, false);

        var left = new Geometry();
        left.Triangle(new Vector3(0, 0.1f, 11), new Vector3(-7.5f, 0.1f, -7.5f), new Vector3(0, 0.1f, -3.5f));
        Emit("Arrow Left", left, highlight).transform.SetParent(marker, false);

        var right = new Geometry();
        right.Triangle(new Vector3(0, 0.1f, 11), new Vector3(0, 0.1f, -3.5f), new Vector3(7.5f, 0.1f, -7.5f));
        Emit("Arrow Right", right, blue).transform.SetParent(marker, false);
    }

    private GameObject Emit(string name, Geometry geometry, Material material)
    {
        if (geometry.VertexCount == 0)
            return null;
        Mesh mesh = geometry.ToMesh(name);
        resources.Add(mesh);
        var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        item.transform.SetParent(parent, false);
        item.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = item.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return item;
    }

    public void Dispose()
    {
        foreach (UnityEngine.Object resource in resources)
            Release(resource);
        resources.Clear();
    }

    public static void Release(UnityEngine.Object resource)
    {
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(resource);
        else
            UnityEngine.Object.DestroyImmediate(resource);
    }

    private sealed class Geometry
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        public int VertexCount => vertices.Count;

        public void Triangle(Vector3 first, Vector3 second, Vector3 third)
        {
            int offset = vertices.Count;
            vertices.AddRange(new[] { first, second, third });
            triangles.AddRange(new[] { offset, offset + 1, offset + 2 });
        }

        public void Quad(Vector3 first, Vector3 second, Vector3 third, Vector3 fourth)
        {
            int offset = vertices.Count;
            vertices.AddRange(new[] { first, second, third, fourth });
            triangles.AddRange(new[] { offset, offset + 1, offset + 2, offset, offset + 2, offset + 3 });
        }

        public void Stroke(Vector3[] points, float width, float height)
        {
            for (int index = 0; index < points.Length - 1; index++)
            {
                Vector3 first = points[index];
                Vector3 second = points[index + 1];
                first.y += height;
                second.y += height;
                Vector3 forward = (second - first).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, forward) * width * 0.5f;
                first -= forward * width * 0.18f;
                second += forward * width * 0.18f;
                Quad(first - side, second - side, second + side, first + side);
            }
        }

        public void Polygon(Vector3[] points, float height, PolygonCollider2D triangulator)
        {
            int count = points.Length;
            if ((points[0] - points[count - 1]).sqrMagnitude < 0.01f)
                count--;
            if (count < 3)
                return;
            var outline = new Vector2[count];
            for (int index = 0; index < count; index++)
                outline[index] = new Vector2(points[index].x, points[index].z);
            triangulator.SetPath(0, outline);
            Mesh polygon = triangulator.CreateMesh(false, false);
            if (polygon == null)
                return;
            int offset = vertices.Count;
            foreach (Vector3 point in polygon.vertices)
                vertices.Add(new Vector3(point.x, height, point.y));
            foreach (int index in polygon.triangles)
                triangles.Add(offset + index);
            Release(polygon);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}