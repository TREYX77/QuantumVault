using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The look of a MirrorSurface: a hidden camera standing where the viewer's reflection would, drawing
// into a texture the glass shows. An ordinary camera that URP schedules itself, just before the
// player's in the same frame, rather than a nested render, which URP 17 has made obsolete.
// A plain class rather than a component, so a mirror stays one thing to add.
public class MirrorReflection
{
    private static readonly int MirrorPlanarId = Shader.PropertyToID("_MirrorPlanar");

    // The PC asset's second renderer: the PC renderer without SSAO, which would misread depth
    // under the oblique projection and cost a full extra pass per mirror. Quality levels without
    // one, such as Mobile, use their default renderer, which has no SSAO anyway.
    private const int ReflectionRendererIndex = 1;

    // The clip plane sits this far in front of the glass, so neither the glass nor the wall it is
    // laid on can show in its own reflection.
    private const float ClipOffset = 0.02f;

    // The cameras reflections are made for. Every other camera, reflection cameras included, gets
    // the probe fallback, because a reflection only lines up from the eye it was made for.
    private static readonly HashSet<Camera> viewers = new HashSet<Camera>();
    private static readonly Plane[] frustum = new Plane[6];
    private static CommandBuffer flagCommands;
    private static bool hooked;

    private Camera camera;
    private RenderTexture texture;

    public RenderTexture Texture => texture;

    // Statics survive play sessions when the domain reload is skipped, and so would the hook.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        viewers.Clear();

        if (hooked) RenderPipelineManager.beginCameraRendering -= FlagCamera;

        hooked = false;
    }

    // Draws this frame's reflection, or rests the camera when the viewer cannot see the glass.
    // Returns whether there is a reflection to show.
    public bool Render(MirrorSurface mirror, Camera view, Bounds glassBounds, float resolutionScale, bool shadows)
    {
        if (view == null || !view.isActiveAndEnabled || !Sees(mirror, view, glassBounds))
        {
            Pause();

            return false;
        }

        Hook();
        viewers.Add(view);

        if (camera == null) Build();

        FitTexture(view, resolutionScale);

        Transform eye = view.transform;

        // Turned, not mirrored: a reflection matrix would flip every triangle's winding and need
        // GL.invertCulling around the render. A camera facing the reflected way the right way up
        // sees the same picture left-right reversed instead, which the shader flips back for free.
        camera.transform.SetPositionAndRotation(
            mirror.ReflectPoint(eye.position),
            Quaternion.LookRotation(mirror.ReflectVector(eye.forward), mirror.ReflectVector(eye.up)));

        camera.fieldOfView = view.fieldOfView;
        camera.nearClipPlane = view.nearClipPlane;
        camera.farClipPlane = view.farClipPlane;
        camera.aspect = view.aspect;
        camera.clearFlags = view.clearFlags;
        camera.backgroundColor = view.backgroundColor;
        camera.cullingMask = view.cullingMask;

        // Lower depth renders first, so the texture is ready before the player's camera reads it.
        camera.depth = view.depth - 1f;
        camera.GetUniversalAdditionalCameraData().renderShadows = shadows;

        // The near plane swapped for the glass, so nothing behind the mirror can show in it. The
        // camera stands behind the wall, so this also keeps the wall itself out of the picture.
        camera.ResetProjectionMatrix();
        camera.projectionMatrix = camera.CalculateObliqueMatrix(GlassPlane(mirror));

        camera.enabled = true;

        return true;
    }

    // Off, keeping the camera and texture for when the mirror comes back into view.
    public void Pause()
    {
        if (camera != null) camera.enabled = false;
    }

    public void Dispose()
    {
        if (camera != null)
        {
            camera.targetTexture = null;
            Object.Destroy(camera.gameObject);
            camera = null;
        }

        ReleaseTexture();
    }

    // In front of the glass and on screen. A mirror seen from behind is only the back of a wall.
    private static bool Sees(MirrorSurface mirror, Camera view, Bounds glassBounds)
    {
        if (Vector3.Dot(view.transform.position - mirror.Center, mirror.Normal) <= 0.01f) return false;

        GeometryUtility.CalculateFrustumPlanes(view, frustum);

        return GeometryUtility.TestPlanesAABB(frustum, glassBounds);
    }

    private void Build()
    {
        GameObject go = new GameObject("Mirror Reflection Camera") { hideFlags = HideFlags.HideAndDontSave };

        camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.allowHDR = true;
        camera.allowMSAA = false;

        // Baked occlusion is for eyes inside the level, and this one stands behind a wall, where
        // the occlusion data would hide everything it should see.
        camera.useOcclusionCulling = false;

        // The player's camera applies post-processing to the whole screen, mirror included, so
        // doing it here as well would grade and bloom the reflection twice.
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.stopNaN = false;
        data.dithering = false;

        if (HasReflectionRenderer()) data.SetRenderer(ReflectionRendererIndex);
    }

    private static bool HasReflectionRenderer()
    {
        UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;

        return asset != null
               && ReflectionRendererIndex < asset.rendererDataList.Length
               && asset.rendererDataList[ReflectionRendererIndex] != null;
    }

    // The player's screen size, scaled, and made again when the window is resized. HDR, so a
    // laser in the mirror stays brighter than white and blooms with the rest of the screen.
    private void FitTexture(Camera view, float resolutionScale)
    {
        int width = Mathf.Max(16, Mathf.RoundToInt(view.pixelWidth * resolutionScale));
        int height = Mathf.Max(16, Mathf.RoundToInt(view.pixelHeight * resolutionScale));

        if (texture != null && texture.width == width && texture.height == height) return;

        ReleaseTexture();

        texture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
        {
            name = "Mirror Reflection",
            hideFlags = HideFlags.HideAndDontSave
        };

        texture.Create();
        camera.targetTexture = texture;
    }

    private void ReleaseTexture()
    {
        if (texture == null) return;

        if (camera != null) camera.targetTexture = null;

        texture.Release();
        Object.Destroy(texture);
        texture = null;
    }

    // The glass as a plane in the reflection camera's own space, facing the side it reflects.
    private Vector4 GlassPlane(MirrorSurface mirror)
    {
        Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
        Vector3 normal = mirror.Normal;
        Vector3 point = worldToCamera.MultiplyPoint(mirror.Center + normal * ClipOffset);
        Vector3 facing = worldToCamera.MultiplyVector(normal).normalized;

        return new Vector4(facing.x, facing.y, facing.z, -Vector3.Dot(point, facing));
    }

    private static void Hook()
    {
        if (hooked) return;

        hooked = true;

        if (flagCommands == null) flagCommands = new CommandBuffer { name = "Mirror Planar Flag" };

        RenderPipelineManager.beginCameraRendering += FlagCamera;
    }

    // Queued on the camera's own context, ahead of its drawing, so each camera reads its own value.
    private static void FlagCamera(ScriptableRenderContext context, Camera camera)
    {
        flagCommands.Clear();
        flagCommands.SetGlobalFloat(MirrorPlanarId, viewers.Contains(camera) ? 1f : 0f);
        context.ExecuteCommandBuffer(flagCommands);
        flagCommands.Clear();
    }
}
