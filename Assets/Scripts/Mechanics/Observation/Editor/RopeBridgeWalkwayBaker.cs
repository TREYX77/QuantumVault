using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Builds a cheap walkable collider for a rope bridge out of its render mesh. The art mesh is far
// too heavy to collide with, and a box sits on the sag's highest point, so the player floats.
// This feels the real deck with rays once, then saves a strip of a few hundred triangles that
// follows the planks, plus a rail either side so a player walking backwards cannot slide off.
// Select the bridge's visual (the object with the MeshFilter) and run Tools/Puzzle.
public static class RopeBridgeWalkwayBaker
{
    private const string MenuPath = "Tools/Puzzle/Bake Bridge Walkway";
    private const string MeshFolder = "Assets/Meshes/Generated";

    private const string WalkwayName = "WalkwayCollider";
    private const string RailsName = "RailsCollider";

    // Along the bridge. Enough to follow the sag without the strip going faceted underfoot.
    private const int Stations = 48;

    // Across the bridge, only used to find where the planks end.
    private const int LateralScan = 41;

    // Rays per station, spread along it, so a gap between planks does not read as a hole.
    private const int SubSamples = 5;

    private const float DeckThickness = 0.15f;
    private const float RailHeight = 1.2f;
    private const float RailThickness = 0.1f;

    // A hit this close to the centre plank's height is still deck, not a rope or a post.
    private const float DeckTolerance = 0.25f;

    [MenuItem(MenuPath)]
    private static void BakeSelected()
    {
        GameObject bridge = Selection.activeGameObject;

        if (Bake(bridge, out string report)) Debug.Log(report, bridge);
        else Debug.LogError(report, bridge);
    }

    [MenuItem(MenuPath, true)]
    private static bool CanBakeSelected()
    {
        GameObject selected = Selection.activeGameObject;

        return selected != null && selected.GetComponent<MeshFilter>() != null;
    }

    // Right click the Observed Vanish component itself, so a bridge is one component and one
    // click: the biggest mesh under it is taken to be the bridge.
    [MenuItem("CONTEXT/ObservedVanish/Bake Bridge Walkway")]
    private static void BakeFromComponent(MenuCommand command)
    {
        ObservedVanish vanish = (ObservedVanish)command.context;
        MeshFilter biggest = null;
        float biggestSize = 0f;

        foreach (MeshFilter filter in vanish.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;

            float size = Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.lossyScale).sqrMagnitude;

            if (size > biggestSize)
            {
                biggestSize = size;
                biggest = filter;
            }
        }

        if (biggest == null)
        {
            Debug.LogError("Bake Bridge Walkway: no mesh found on or under this object.", vanish);

            return;
        }

        if (Bake(biggest.gameObject, out string report)) Debug.Log(report, biggest);
        else Debug.LogError(report, biggest);
    }

    public static bool Bake(GameObject bridge, out string report)
    {
        MeshFilter filter = bridge != null ? bridge.GetComponent<MeshFilter>() : null;

        if (filter == null || filter.sharedMesh == null)
        {
            report = "Bake Bridge Walkway: select the bridge object that has the MeshFilter.";

            return false;
        }

        // The saved meshes are named after the object's scene id, which a new object only gets
        // once the scene is saved. Without one, two new bridges would overwrite each other.
        if (GlobalObjectId.GetGlobalObjectIdSlow(bridge).targetObjectId == 0)
        {
            report = $"Bake Bridge Walkway: save the scene first, so '{bridge.name}' has a stable id to name its meshes after.";

            return false;
        }

        Transform visual = bridge.transform;
        Mesh mesh = filter.sharedMesh;
        Bounds local = mesh.bounds;
        Vector3 scale = visual.lossyScale;

        // The long side of the mesh, measured after scale, is the walking direction.
        bool alongX = Mathf.Abs(local.extents.x * scale.x) >= Mathf.Abs(local.extents.z * scale.z);
        int along = alongX ? 0 : 2;
        int across = alongX ? 2 : 0;

        // A hidden copy carries the collider, so the bridge itself, often a prefab instance,
        // never picks up an override. Collider.Raycast only tests this one collider, so the
        // rest of the level cannot get in the way.
        GameObject probe = new GameObject("Walkway Probe") { hideFlags = HideFlags.HideAndDontSave };
        probe.transform.SetPositionAndRotation(visual.position, visual.rotation);
        probe.transform.localScale = scale;

        try
        {
            MeshCollider deckProbe = probe.AddComponent<MeshCollider>();
            deckProbe.sharedMesh = mesh;
            Physics.SyncTransforms();

            float castHeight = local.max.y + 1f;
            float castLength = (local.size.y + 2f) * Mathf.Abs(scale.y) + 1f;

            bool CastDown(float a, float c, out Vector3 point)
            {
                Vector3 origin = Vector3.zero;
                origin[along] = a;
                origin[across] = c;
                origin.y = castHeight;

                Ray ray = new Ray(visual.TransformPoint(origin), -visual.up);
                bool hit = deckProbe.Raycast(ray, out RaycastHit info, castLength);

                point = hit ? info.point : Vector3.zero;

                return hit;
            }

            float start = local.min[along];
            float length = local.size[along];
            float centreLine = local.center[across];
            float step = length / Stations;

            // 1. Deck height down the middle, where there are no ropes overhead.
            Vector3?[] centre = new Vector3?[Stations + 1];

            for (int i = 0; i <= Stations; i++)
            {
                // Pulled in a little at the ends, where the anchor posts are.
                float a = start + Mathf.Lerp(0.01f, 0.99f, i / (float)Stations) * length;

                Vector3? best = null;

                for (int s = 0; s < SubSamples; s++)
                {
                    float offset = (s / (float)(SubSamples - 1) - 0.5f) * step * 0.8f;

                    if (!CastDown(a + offset, centreLine, out Vector3 point)) continue;

                    if (best == null || Vector3.Dot(point - best.Value, visual.up) > 0f) best = point;
                }

                centre[i] = best;
            }

            int found = 0;

            foreach (Vector3? point in centre)
            {
                if (point != null) found++;
            }

            if (found < Stations / 2)
            {
                report = $"Bake Bridge Walkway: only {found} of {Stations + 1} rays found a deck on '{bridge.name}'. " +
                         "Is the mesh walkable along its longest side?";

                return false;
            }

            FillGaps(centre);
            SmoothOutliers(centre, visual.up);

            // 2. How wide the planks are, from the middle stretch where the deck is cleanest.
            float halfWidth = MeasureHalfWidth(centre, visual, along, across, local, CastDown);

            // 3. Everything is built in the visual's frame without its scale, so the collider
            // objects can sit beside it at unit scale.
            Matrix4x4 toFrame = Matrix4x4.TRS(visual.position, visual.rotation, Vector3.one).inverse;
            Vector3 sideways = Vector3.zero;
            sideways[across] = 1f;

            Vector3 worldSide = visual.rotation * sideways;
            float worldHalfWidth = halfWidth * Mathf.Abs(scale[across]);

            Vector3[] left = new Vector3[Stations + 1];
            Vector3[] right = new Vector3[Stations + 1];

            for (int i = 0; i <= Stations; i++)
            {
                Vector3 deck = centre[i].Value;

                left[i] = toFrame.MultiplyPoint3x4(deck - worldSide * worldHalfWidth);
                right[i] = toFrame.MultiplyPoint3x4(deck + worldSide * worldHalfWidth);
            }

            Vector3 up = Vector3.up;
            Vector3 outward = sideways;

            Mesh walkway = BuildSlab(left, right, -up * DeckThickness, up);
            Mesh rails = BuildRails(left, right, outward, up);

            string key = AssetKey(bridge);
            walkway = SaveMesh(walkway, $"{key}_Walkway");
            rails = SaveMesh(rails, $"{key}_Rails");

            GameObject walkwayObject = PlaceCollider(WalkwayName, visual, walkway, bridge.layer);
            GameObject railsObject = PlaceCollider(RailsName, visual, rails, bridge.layer);

            Selection.objects = new Object[] { bridge, walkwayObject, railsObject };

            float lowest = float.MaxValue;
            float highest = float.MinValue;

            foreach (Vector3? point in centre)
            {
                lowest = Mathf.Min(lowest, point.Value.y);
                highest = Mathf.Max(highest, point.Value.y);
            }

            report = $"Baked walkway for '{bridge.name}': {walkway.triangles.Length / 3} deck triangles, " +
                     $"{rails.triangles.Length / 3} rail triangles, deck {worldHalfWidth * 2f:0.00} m wide, " +
                     $"sags {highest - lowest:0.00} m. Saved to {MeshFolder}.";

            return true;
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }

    // A station whose rays all fell through gaps borrows from its neighbours.
    private static void FillGaps(Vector3?[] points)
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] != null) continue;

            int before = i - 1;
            while (before >= 0 && points[before] == null) before--;

            int after = i + 1;
            while (after < points.Length && points[after] == null) after++;

            if (before >= 0 && after < points.Length)
            {
                float t = (i - before) / (float)(after - before);
                points[i] = Vector3.Lerp(points[before].Value, points[after].Value, t);
            }
            else if (before >= 0)
            {
                points[i] = points[before];
            }
            else if (after < points.Length)
            {
                points[i] = points[after];
            }
        }
    }

    // A ray that clipped a post or a knot sits well off the curve its neighbours describe.
    private static void SmoothOutliers(Vector3?[] points, Vector3 up)
    {
        for (int i = 1; i < points.Length - 1; i++)
        {
            Vector3 expected = (points[i - 1].Value + points[i + 1].Value) * 0.5f;
            float off = Vector3.Dot(points[i].Value - expected, up);

            if (Mathf.Abs(off) > DeckTolerance) points[i] = expected;
        }
    }

    private delegate bool DownCast(float along, float across, out Vector3 point);

    private static float MeasureHalfWidth(Vector3?[] centre, Transform visual, int along, int across,
                                          Bounds local, DownCast cast)
    {
        List<float> widths = new List<float>();
        float halfSpan = local.extents[across];
        float centreLine = local.center[across];
        float start = local.min[along];
        float length = local.size[along];

        for (int i = Stations / 4; i <= Stations * 3 / 4; i += 3)
        {
            float a = start + Mathf.Lerp(0.01f, 0.99f, i / (float)Stations) * length;
            float deckHeight = Vector3.Dot(centre[i].Value, visual.up);

            float leftEdge = 0f;
            float rightEdge = 0f;

            for (int side = -1; side <= 1; side += 2)
            {
                float edge = 0f;

                for (int k = 1; k <= LateralScan / 2; k++)
                {
                    float c = centreLine + side * halfSpan * k / (LateralScan / 2f);

                    if (!cast(a, c, out Vector3 point)) break;

                    float height = Vector3.Dot(point, visual.up);

                    if (Mathf.Abs(height - deckHeight) > DeckTolerance) break;

                    edge = Mathf.Abs(c - centreLine);
                }

                if (side < 0) leftEdge = edge;
                else rightEdge = edge;
            }

            widths.Add(Mathf.Min(leftEdge, rightEdge));
        }

        widths.Sort();

        float median = widths.Count > 0 ? widths[widths.Count / 2] : halfSpan * 0.5f;

        // Never narrower than a sliver, never past the mesh.
        return Mathf.Clamp(median, halfSpan * 0.15f, halfSpan);
    }

    // A closed slab from two edge lines: top, bottom, both sides and both ends.
    private static Mesh BuildSlab(Vector3[] left, Vector3[] right, Vector3 down, Vector3 up)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        int n = left.Length;

        for (int i = 0; i < n; i++)
        {
            vertices.Add(left[i]);
            vertices.Add(right[i]);
            vertices.Add(left[i] + down);
            vertices.Add(right[i] + down);
        }

        for (int i = 0; i < n - 1; i++)
        {
            int a = i * 4;
            int b = (i + 1) * 4;
            Vector3 forward = (left[i + 1] - left[i]).normalized;
            Vector3 side = (right[i] - left[i]).normalized;

            AddQuad(vertices, triangles, a + 0, a + 1, b + 1, b + 0, up);
            AddQuad(vertices, triangles, a + 2, a + 3, b + 3, b + 2, -up);
            AddQuad(vertices, triangles, a + 0, b + 0, b + 2, a + 2, -side);
            AddQuad(vertices, triangles, a + 1, b + 1, b + 3, a + 3, side);

            if (i == 0) AddQuad(vertices, triangles, a + 0, a + 1, a + 3, a + 2, -forward);
            if (i == n - 2) AddQuad(vertices, triangles, b + 0, b + 1, b + 3, b + 2, forward);
        }

        return Finish(vertices, triangles, "Walkway");
    }

    // Two thin walls along the deck edges, open at both ends so the player can step on and off.
    private static Mesh BuildRails(Vector3[] left, Vector3[] right, Vector3 outward, Vector3 up)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        AddWall(vertices, triangles, left, -outward, up);
        AddWall(vertices, triangles, right, outward, up);

        return Finish(vertices, triangles, "Rails");
    }

    private static void AddWall(List<Vector3> vertices, List<int> triangles, Vector3[] edge, Vector3 outward, Vector3 up)
    {
        int first = vertices.Count;
        int n = edge.Length;

        for (int i = 0; i < n; i++)
        {
            Vector3 bottom = edge[i] - up * DeckThickness;

            vertices.Add(bottom);
            vertices.Add(bottom + up * (RailHeight + DeckThickness));
            vertices.Add(bottom + outward * RailThickness);
            vertices.Add(bottom + outward * RailThickness + up * (RailHeight + DeckThickness));
        }

        for (int i = 0; i < n - 1; i++)
        {
            int a = first + i * 4;
            int b = first + (i + 1) * 4;
            Vector3 forward = (edge[i + 1] - edge[i]).normalized;

            AddQuad(vertices, triangles, a + 0, a + 1, b + 1, b + 0, -outward);
            AddQuad(vertices, triangles, a + 2, a + 3, b + 3, b + 2, outward);
            AddQuad(vertices, triangles, a + 1, a + 3, b + 3, b + 1, up);
            AddQuad(vertices, triangles, a + 0, a + 2, b + 2, b + 0, -up);

            if (i == 0) AddQuad(vertices, triangles, a + 0, a + 1, a + 3, a + 2, -forward);
            if (i == n - 2) AddQuad(vertices, triangles, b + 0, b + 1, b + 3, b + 2, forward);
        }
    }

    // Winds the quad so its face points the way asked, so the caller never has to think about
    // vertex order and a mirrored bridge cannot end up with an inside out collider.
    private static void AddQuad(List<Vector3> vertices, List<int> triangles, int a, int b, int c, int d, Vector3 facing)
    {
        Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);

        if (Vector3.Dot(normal, facing) < 0f)
        {
            (b, d) = (d, b);
        }

        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
        triangles.Add(a);
        triangles.Add(c);
        triangles.Add(d);
    }

    private static Mesh Finish(List<Vector3> vertices, List<int> triangles, string name)
    {
        Mesh mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    // Named after the scene object, not just its name, so two bridges both called rope_bridge
    // never overwrite each other's colliders.
    private static string AssetKey(GameObject bridge)
    {
        GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(bridge);
        string scene = Path.GetFileNameWithoutExtension(bridge.scene.path).Replace(' ', '_');

        return $"{scene}_{bridge.name}_{id.targetObjectId:X}";
    }

    // Rebaking writes into the existing asset, so the collider keeps pointing at it.
    private static Mesh SaveMesh(Mesh mesh, string name)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Meshes")) AssetDatabase.CreateFolder("Assets", "Meshes");
        if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder("Assets/Meshes", "Generated");

        string path = $"{MeshFolder}/{name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

        mesh.name = name;

        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);

            return mesh;
        }

        existing.Clear();
        existing.SetVertices(mesh.vertices);
        existing.SetTriangles(mesh.triangles, 0);
        existing.RecalculateNormals();
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(mesh);
        AssetDatabase.SaveAssetIfDirty(existing);

        return existing;
    }

    // Inside the bridge, so the colliders vanish with it whether Observed Vanish sits on the
    // bridge itself or on a parent above it. Older bakes put them beside the bridge; those are
    // picked up and moved in rather than left behind as a second, never vanishing copy.
    private static GameObject PlaceCollider(string name, Transform visual, Mesh mesh, int layer)
    {
        Transform existing = visual.Find(name);

        if (existing == null && visual.parent != null) existing = visual.parent.Find(name);

        GameObject target;

        if (existing != null)
        {
            target = existing.gameObject;
            Undo.RecordObject(target.transform, "Bake Bridge Walkway");

            if (target.transform.parent != visual) Undo.SetTransformParent(target.transform, visual, "Bake Bridge Walkway");
        }
        else
        {
            target = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(target, "Bake Bridge Walkway");
            Undo.SetTransformParent(target.transform, visual, "Bake Bridge Walkway");
        }

        target.layer = layer;

        // Unrotated against the bridge, so dividing out its scale leaves exactly one metre per
        // unit in the world, which is what the mesh is built in, with no skew.
        Vector3 scale = visual.lossyScale;
        target.transform.localPosition = Vector3.zero;
        target.transform.localRotation = Quaternion.identity;
        target.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);

        MeshCollider collider = target.GetComponent<MeshCollider>();

        if (collider == null) collider = Undo.AddComponent<MeshCollider>(target);
        else Undo.RecordObject(collider, "Bake Bridge Walkway");

        collider.sharedMesh = null;
        collider.sharedMesh = mesh;
        collider.convex = false;

        return target;
    }
}
