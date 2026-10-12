using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Makes placed AR content actually look grounded in the real room instead of rendering
/// under flat, fixed lighting. ARCameraManager on ARRig already requests light estimation
/// (m_LightEstimationMode is set to request everything the platform supports), but nothing
/// in the project was consuming that data — this is the missing consumer: it listens for
/// each camera frame's estimate and applies brightness/color/direction to the scene's
/// Directional Light and ambient lighting, the same way Unity's own AR Foundation samples
/// (e.g. BasicLightEstimation) do it. Attach to the same GameObject as ARCameraManager
/// (ARRig) and wire directionalLight to the scene's Directional Light.
/// </summary>
[RequireComponent(typeof(ARCameraManager))]
public class ARLightEstimationController : MonoBehaviour
{
    [Tooltip("The scene's main Directional Light. Its intensity/color/rotation get driven by the camera's light estimate whenever the platform reports one.")]
    [SerializeField] private Light directionalLight;

    [Tooltip("Directional Light intensity to use when no estimate is available yet (first few frames, or a platform that doesn't report brightness).")]
    [SerializeField] private float fallbackIntensity = 1.8f;

    [Tooltip("Never let the real-time light estimate darken the scene below this floor -- placed objects should stay clearly visible even in a dim room.")]
    [SerializeField] private float minIntensity = 0.8f;

    private ARCameraManager cameraManager;

    private void Awake()
    {
        cameraManager = GetComponent<ARCameraManager>();

        // directionalLight can't be wired as a prefab field (it lives in whichever scene
        // this rig is placed in, not the ARRig prefab itself) -- fall back to whatever the
        // scene's Lighting settings call the sun, then to any Directional light present.
        if (directionalLight == null)
        {
            if (RenderSettings.sun != null)
            {
                directionalLight = RenderSettings.sun;
            }
            else
            {
                foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light.type == LightType.Directional)
                    {
                        directionalLight = light;
                        break;
                    }
                }
            }
        }
    }

    private void OnEnable()
    {
        if (cameraManager != null)
        {
            cameraManager.frameReceived += OnCameraFrameReceived;
        }
    }

    private void OnDisable()
    {
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnCameraFrameReceived;
        }
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        if (directionalLight == null)
        {
            return;
        }

        if (args.lightEstimation.averageBrightness.HasValue)
        {
            directionalLight.intensity = Mathf.Max(minIntensity, args.lightEstimation.averageBrightness.Value * 2f);
        }
        else if (args.lightEstimation.averageMainLightBrightness.HasValue)
        {
            directionalLight.intensity = Mathf.Max(minIntensity, args.lightEstimation.averageMainLightBrightness.Value);
        }
        else
        {
            directionalLight.intensity = fallbackIntensity;
        }

        if (args.lightEstimation.colorCorrection.HasValue)
        {
            directionalLight.color = args.lightEstimation.colorCorrection.Value;
        }
        else if (args.lightEstimation.averageColorTemperature.HasValue)
        {
            directionalLight.colorTemperature = args.lightEstimation.averageColorTemperature.Value;
        }

        if (args.lightEstimation.mainLightDirection.HasValue)
        {
            directionalLight.transform.rotation = Quaternion.LookRotation(args.lightEstimation.mainLightDirection.Value);
        }

        if (args.lightEstimation.mainLightColor.HasValue)
        {
            directionalLight.color = args.lightEstimation.mainLightColor.Value;
        }

        if (args.lightEstimation.ambientSphericalHarmonics.HasValue)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientProbe = args.lightEstimation.ambientSphericalHarmonics.Value;
        }
    }
}
