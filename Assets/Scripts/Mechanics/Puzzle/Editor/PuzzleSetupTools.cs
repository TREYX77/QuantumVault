using UnityEditor;
using UnityEngine;

// One-click setup for pressure plates, portal gates and mirrors, so a puzzle is built from menus
// rather than by hand-wiring components:
//   GameObject > Puzzle > Pressure Plate            a new plate in front of the Scene view
//   GameObject > Puzzle > Mirror                    a new wall mirror in front of the Scene view
//   Tools > Puzzle > Make Selected Portal a Gate    turns any portal into a working gate
//   Tools > Puzzle > Make Selected a Mirror         glass on the face of a wall toward the Scene view
//   Portal Gate header > Add Pressure Plate         a new plate already linked to that gate
public static class PuzzleSetupTools
{
    public const string PlatePrefabPath = "Assets/Prefabs/Puzzle/Pressure Plate.prefab";
    public const string MirrorPrefabPath = "Assets/Prefabs/Puzzle/Mirror.prefab";
    public const string MirrorMaterialPath = "Assets/Materials/Puzzle/M_Mirror.mat";

    // How far in front of a portal a new plate is put.
    private const float PlateDistance = 4f;

    // Wall left showing round glass laid on a wall, like a frame.
    private const float MirrorFrame = 0.05f;

    // Glass stands this far proud of its wall, so the two never z-fight.
    private const float GlassGap = 0.001f;

    // Depth of the collider given to a bare quad, so a beam from behind is stopped: raycasts pass
    // through the back of a mesh collider.
    private const float QuadBacking = 0.02f;

    [MenuItem("GameObject/Puzzle/Pressure Plate", false, 10)]
    private static void CreatePlateFromMenu(MenuCommand command)
    {
        // The hierarchy menu runs once per selected object; one plate is enough.
        if (command.context != null && command.context != Selection.activeObject) return;

        SceneView view = SceneView.lastActiveSceneView;
        Vector3 point = view != null ? view.pivot : Vector3.zero;

        GameObject plate = InstantiatePlate(GroundBelow(point), null);

        if (plate != null) Selection.activeGameObject = plate;
    }

    [MenuItem("Tools/Puzzle/Make Selected Portal a Gate")]
    private static void MakeSelectedGates()
    {
        foreach (GameObject selected in Selection.gameObjects)
        {
            PortalGate gate = MakeGate(selected);

            if (gate == null)
            {
                Debug.LogWarning($"'{selected.name}' has no vortex to fit a gate to. Select a portal's root object.", selected);
            }
        }
    }

    [MenuItem("Tools/Puzzle/Make Selected Portal a Gate", true)]
    private static bool CanMakeSelectedGates()
    {
        return Selection.gameObjects.Length > 0;
    }

    [MenuItem("GameObject/Puzzle/Mirror", false, 11)]
    private static void CreateMirrorFromMenu(MenuCommand command)
    {
        // The hierarchy menu runs once per selected object; one mirror is enough.
        if (command.context != null && command.context != Selection.activeObject) return;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MirrorPrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"Mirror prefab not found at {MirrorPrefabPath}.");

            return;
        }

        SceneView view = SceneView.lastActiveSceneView;
        Vector3 point = GroundBelow(view != null ? view.pivot : Vector3.zero);

        GameObject mirror = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(mirror, "Create Mirror");

        // Standing on the floor, turned so its glass, on its -Z side, faces the Scene view.
        Vector3 away = view != null ? Vector3.ProjectOnPlane(point - view.camera.transform.position, Vector3.up) : Vector3.forward;

        mirror.transform.SetPositionAndRotation(point, Quaternion.LookRotation(away.sqrMagnitude > 0.0001f ? away : Vector3.forward));

        Selection.activeGameObject = mirror;
    }

    [MenuItem("Tools/Puzzle/Make Selected a Mirror")]
    private static void MakeSelectedMirrors()
    {
        SceneView view = SceneView.lastActiveSceneView;
        Vector3 eye = view != null ? view.camera.transform.position : Vector3.zero;

        foreach (GameObject selected in Selection.gameObjects)
        {
            if (MakeMirror(selected, eye) == null)
            {
                Debug.LogWarning($"'{selected.name}' has no mesh to turn into a mirror. Select a wall or a quad.", selected);
            }
        }
    }

    [MenuItem("Tools/Puzzle/Make Selected a Mirror", true)]
    private static bool CanMakeSelectedMirrors()
    {
        return Selection.gameObjects.Length > 0;
    }

    // A flat quad becomes the glass itself. Anything else is a wall: glass is laid on whichever
    // face looks most toward the eye, and the wall keeps its own material and collider, which is
    // what a beam meets. Run again from another side to move the glass to that face.
    public static MirrorSurface MakeMirror(GameObject target, Vector3 eye)
    {
        MeshFilter filter = target.GetComponent<MeshFilter>();
        Renderer renderer = target.GetComponent<Renderer>();

        if (filter == null || filter.sharedMesh == null || renderer == null) return null;

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MirrorMaterialPath);

        if (material == null)
        {
            Debug.LogError($"Mirror material not found at {MirrorMaterialPath}.");

            return null;
        }

        Bounds bounds = filter.sharedMesh.bounds;
        Renderer glass;

        if (bounds.size.z < 0.0001f)
        {
            Undo.RecordObject(renderer, "Make Mirror");
            renderer.sharedMaterial = material;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);

            BackQuad(target, bounds);
            glass = null;
        }
        else
        {
            glass = LayGlass(target, bounds, eye, material);
        }

        MirrorSurface mirror = target.GetComponent<MirrorSurface>();

        if (mirror == null) mirror = Undo.AddComponent<MirrorSurface>(target);

        // Left empty for a quad, which means "this object's own renderer".
        SerializedObject serialized = new SerializedObject(mirror);
        serialized.FindProperty("glass").objectReferenceValue = glass;
        serialized.ApplyModifiedProperties();

        return mirror;
    }

    // A bare quad's mesh collider lets beams through from behind, so it gets a thin box set just
    // behind its face instead, with the front of the box exactly on the glass.
    private static void BackQuad(GameObject quad, Bounds bounds)
    {
        MeshCollider meshCollider = quad.GetComponent<MeshCollider>();

        if (meshCollider != null) Undo.DestroyObjectImmediate(meshCollider);

        BoxCollider box = quad.GetComponent<BoxCollider>();

        if (box == null) box = Undo.AddComponent<BoxCollider>(quad);
        else Undo.RecordObject(box, "Make Mirror");

        // In the quad's own units, which its scale stretches to the glass; only the depth is fixed in metres.
        float depth = QuadBacking / Mathf.Max(0.0001f, Mathf.Abs(quad.transform.lossyScale.z));

        box.center = new Vector3(bounds.center.x, bounds.center.y, depth * 0.5f);
        box.size = new Vector3(bounds.size.x, bounds.size.y, depth);

        PrefabUtility.RecordPrefabInstancePropertyModifications(box);
    }

    // A quad on the face of the mesh's box that looks most toward the eye, a frame's width in from
    // its edges and just proud of it, with no collider of its own.
    private static Renderer LayGlass(GameObject wall, Bounds bounds, Vector3 eye, Material material)
    {
        const string glassName = "Mirror Glass";

        Transform t = wall.transform;
        Transform old = t.Find(glassName);

        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        int bestAxis = 2;
        float bestSign = -1f;
        float bestScore = float.NegativeInfinity;

        for (int axis = 0; axis < 3; axis++)
        {
            foreach (float sign in new[] { 1f, -1f })
            {
                Vector3 normal = t.TransformDirection(Axis(axis) * sign);
                Vector3 face = t.TransformPoint(bounds.center + Axis(axis) * (sign * bounds.extents[axis]));
                float score = Vector3.Dot(normal, (eye - face).normalized);

                if (score <= bestScore) continue;

                bestScore = score;
                bestAxis = axis;
                bestSign = sign;
            }
        }

        // Across the face and up it: up is the wall's own up, except on a top or bottom face.
        int across = bestAxis == 0 ? 2 : 0;
        int along = bestAxis == 1 ? 2 : 1;

        Vector3 outward = t.TransformDirection(Axis(bestAxis) * bestSign);
        Vector3 centre = t.TransformPoint(bounds.center + Axis(bestAxis) * (bestSign * bounds.extents[bestAxis]));
        float width = 2f * t.TransformVector(Axis(across) * bounds.extents[across]).magnitude - 2f * MirrorFrame;
        float height = 2f * t.TransformVector(Axis(along) * bounds.extents[along]).magnitude - 2f * MirrorFrame;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = glassName;
        quad.layer = wall.layer;
        Object.DestroyImmediate(quad.GetComponent<Collider>());

        // Sized in the world, then parented keeping that size, so a scaled wall gets glass of the
        // right size rather than a stretched one. A Quad shows its -Z side, so -Z faces outward.
        quad.transform.SetPositionAndRotation(centre + outward * GlassGap,
                                              Quaternion.LookRotation(-outward, t.TransformDirection(Axis(along))));
        quad.transform.localScale = new Vector3(Mathf.Max(0.05f, width), Mathf.Max(0.05f, height), 1f);

        Undo.RegisterCreatedObjectUndo(quad, "Make Mirror");
        Undo.SetTransformParent(quad.transform, t, true, "Make Mirror");

        // The wall already casts the shadow, and the glass is unlit.
        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return renderer;
    }

    private static Vector3 Axis(int index)
    {
        Vector3 axis = Vector3.zero;
        axis[index] = 1f;

        return axis;
    }

    [MenuItem("CONTEXT/PortalGate/Add Pressure Plate")]
    private static void AddPlateFromComponent(MenuCommand command)
    {
        PressurePlate plate = AddPlate((PortalGate)command.context);

        if (plate != null) Selection.activeGameObject = plate.gameObject;
    }

    [MenuItem("CONTEXT/PortalGate/Fit Barrier To Vortex")]
    private static void FitFromComponent(MenuCommand command)
    {
        PortalGate gate = (PortalGate)command.context;

        Undo.RecordObject(gate.GetComponent<BoxCollider>(), "Fit Barrier To Vortex");

        if (!gate.FitBarrierToVortex()) Debug.LogWarning("No vortex renderer found to fit to.", gate);

        PrefabUtility.RecordPrefabInstancePropertyModifications(gate.GetComponent<BoxCollider>());
    }

    // Adds the gate and sizes its wall to the vortex disc. Returns null when the object has no
    // vortex, so nothing half set up is left behind.
    public static PortalGate MakeGate(GameObject portal)
    {
        PortalGate gate = portal.GetComponent<PortalGate>();

        if (gate == null)
        {
            gate = Undo.AddComponent<PortalGate>(portal);
        }

        BoxCollider box = portal.GetComponent<BoxCollider>();
        Undo.RecordObject(box, "Make Portal Gate");

        if (!gate.FitBarrierToVortex())
        {
            Undo.DestroyObjectImmediate(gate);
            Undo.DestroyObjectImmediate(box);

            return null;
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(box);

        return gate;
    }

    // A new plate on the ground in front of the portal, on whichever side has floor, already in
    // the gate's list.
    public static PressurePlate AddPlate(PortalGate gate)
    {
        Vector3 centre = gate.BarrierCentre;
        Vector3 facing = Vector3.ProjectOnPlane(gate.Facing, Vector3.up).normalized;

        if (facing.sqrMagnitude < 0.01f) facing = Vector3.ProjectOnPlane(gate.transform.forward, Vector3.up).normalized;

        // The portal's foot, so the side whose floor is level with it wins over a pit.
        float footHeight = gate.GetComponentInChildren<Renderer>() != null
            ? gate.GetComponentInChildren<Renderer>().bounds.min.y
            : gate.transform.position.y;

        Vector3 best = centre + facing * PlateDistance;
        float bestScore = float.MaxValue;

        foreach (float side in new[] { 1f, -1f })
        {
            Vector3 probe = centre + facing * (PlateDistance * side);

            if (!Physics.Raycast(probe + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 20f,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;

            float score = Mathf.Abs(hit.point.y - footHeight);

            if (score < bestScore)
            {
                bestScore = score;
                best = hit.point;
            }
        }

        GameObject plateObject = InstantiatePlate(best, gate.transform.parent);

        if (plateObject == null) return null;

        plateObject.name = $"Pressure Plate ({gate.name})";

        PressurePlate plate = plateObject.GetComponent<PressurePlate>();

        Undo.RecordObject(gate, "Add Pressure Plate");
        gate.AddPlate(plate);
        PrefabUtility.RecordPrefabInstancePropertyModifications(gate);

        return plate;
    }

    private static GameObject InstantiatePlate(Vector3 position, Transform parent)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlatePrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"Pressure Plate prefab not found at {PlatePrefabPath}.");

            return null;
        }

        GameObject plate = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(plate, "Create Pressure Plate");

        if (parent != null) Undo.SetTransformParent(plate.transform, parent, "Create Pressure Plate");

        plate.transform.position = position;
        plate.transform.rotation = Quaternion.identity;

        // Real size whatever group it lands in, or a scaled level folder makes a giant plate.
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
        plate.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);

        return plate;
    }

    private static Vector3 GroundBelow(Vector3 point)
    {
        Physics.SyncTransforms();

        return Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 50f,
                               Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point
            : point;
    }
}
