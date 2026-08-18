
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using IRIS.Node;
using MessagePack;

namespace IRIS.SceneLoader
{

    [MessagePackObject(keyAsPropertyName: true)]
    public class VideoFrame
    {
        public int width { get; set; }
        public int height { get; set; }
        public byte[] image { get; set; }
        public double timestamp { get; set; }
    }


    public class VideoStreamReceiver : MonoBehaviour
    {
        private const float VideoSurfaceZOffset = -1f;
        private const string LeftStereoLayerName = "IRISStereoLeft";
        private const string RightStereoLayerName = "IRISStereoRight";
        private const string UniversalRenderPipelineUnlitShader = "Universal Render Pipeline/Unlit";
        private const string LegacyUnlitTextureShader = "Unlit/Texture";

        private sealed class StereoQuadSurface
        {
            public GameObject root;
            public MeshRenderer[] renderers;
            public Texture2D[] textures;
            public int activeRendererIndex;
        }

        public RawImage rawImage;
        private RawImage[] monoRawImages;
        private int activeMonoRawImageIndex;
        private Texture2D[] monoTextures;
        private StereoQuadSurface leftStereoSurface;
        private StereoQuadSurface rightStereoSurface;
        private byte[] latestPrimaryImageBytes;
        private byte[] latestSecondaryImageBytes;
        private readonly object primaryFrameLock = new object();
        private readonly object secondaryFrameLock = new object();
        private string primaryTopic;
        private string secondaryTopic;
        private bool stereoMode;

        private void Start()
        {
            EnsureMonoBuffers();
        }

        public void StartSubscription(VideoStreamConfig config)
        {
            CleanupSubscriptions();
            stereoMode = false;
            primaryTopic = config.name;
            EnsureMonoBuffers();
            ConfigureMonoBuffers(config.width, config.height);
            SetActiveMonoBuffer(0);
            SetStereoSurfaceVisible(leftStereoSurface, false);
            SetStereoSurfaceVisible(rightStereoSurface, false);
            IRISXRNode.Instance.SubscriberManager.RegisterSubscriptionCallback<VideoFrame>(config.name, OnPrimaryFrameReceived, config.url);
        }

        public void StartStereoSubscription(VideoStreamConfig leftConfig, VideoStreamConfig rightConfig)
        {
            CleanupSubscriptions();
            stereoMode = true;
            primaryTopic = leftConfig.name;
            secondaryTopic = rightConfig.name;
            EnsureMonoBuffers();
            SetMonoBuffersVisible(false);
            bool leftSurfaceReady = EnsureStereoSurface(ref leftStereoSurface, LeftStereoLayerName, "Left");
            bool rightSurfaceReady = EnsureStereoSurface(ref rightStereoSurface, RightStereoLayerName, "Right");
            if (!leftSurfaceReady || !rightSurfaceReady)
            {
                stereoMode = false;
                SetMonoBuffersVisible(true);
                return;
            }

            ConfigureStereoSurface(leftStereoSurface, leftConfig.width, leftConfig.height);
            ConfigureStereoSurface(rightStereoSurface, rightConfig.width, rightConfig.height);
            SetStereoSurfaceVisible(leftStereoSurface, true);
            SetStereoSurfaceVisible(rightStereoSurface, true);

            IRISXRNode.Instance.SubscriberManager.RegisterSubscriptionCallback<VideoFrame>(
                leftConfig.name,
                OnPrimaryFrameReceived,
                leftConfig.url
            );
            IRISXRNode.Instance.SubscriberManager.RegisterSubscriptionCallback<VideoFrame>(
                rightConfig.name,
                OnSecondaryFrameReceived,
                rightConfig.url
            );
        }

        private void Update()
        {
            if (stereoMode)
            {
                UpdateStereoSurface(leftStereoSurface, ref latestPrimaryImageBytes, primaryFrameLock);
                UpdateStereoSurface(rightStereoSurface, ref latestSecondaryImageBytes, secondaryFrameLock);
                return;
            }

            UpdateMonoBuffers(ref latestPrimaryImageBytes, primaryFrameLock);
        }

        public void CloseWindow()
        {
            // VideoStreamSpawner lives on a scene object unrelated to VideoRender's hierarchy,
            // so GetComponentInParent returns null. Use FindObjectOfType to locate it.
            VideoStreamSpawner spawner = FindObjectOfType<VideoStreamSpawner>();
            if (spawner != null)
            {
                spawner.DismissVideoReceiver(gameObject.name);
            }
            Destroy(gameObject);
        }

        private void OnPrimaryFrameReceived(VideoFrame videoFrame)
        {
            lock (primaryFrameLock)
            {
                latestPrimaryImageBytes = videoFrame.image;
            }
        }

        private void OnSecondaryFrameReceived(VideoFrame videoFrame)
        {
            lock (secondaryFrameLock)
            {
                latestSecondaryImageBytes = videoFrame.image;
            }
        }

        private void OnDestroy()
        {
            CleanupSubscriptions();
        }

        private void CleanupSubscriptions()
        {
            if (!string.IsNullOrEmpty(primaryTopic) && IRISXRNode.Instance != null)
            {
                IRISXRNode.Instance.SubscriberManager.Unsubscribe(primaryTopic);
            }
            if (!string.IsNullOrEmpty(secondaryTopic) && IRISXRNode.Instance != null)
            {
                IRISXRNode.Instance.SubscriberManager.Unsubscribe(secondaryTopic);
            }
            primaryTopic = null;
            secondaryTopic = null;
            latestPrimaryImageBytes = null;
            latestSecondaryImageBytes = null;
            stereoMode = false;
        }

        private void EnsureMonoBuffers()
        {
            if (monoRawImages == null)
            {
                monoRawImages = new RawImage[2];
            }
            if (monoTextures == null)
            {
                monoTextures = new Texture2D[2];
            }

            if (monoRawImages[0] == null)
            {
                monoRawImages[0] = rawImage;
            }

            for (int index = 0; index < monoRawImages.Length; index++)
            {
                if (monoTextures[index] == null)
                {
                    monoTextures[index] = CreateVideoTexture();
                }

                if (monoRawImages[index] == null)
                {
                    GameObject eyeObject = Instantiate(rawImage.gameObject, rawImage.transform.parent);
                    eyeObject.name = rawImage.gameObject.name + "_Buffer" + index;
                    monoRawImages[index] = eyeObject.GetComponent<RawImage>();
                }

                ConfigureMonoRawImage(monoRawImages[index], monoTextures[index], index);
            }
        }

        private void ConfigureMonoRawImage(RawImage image, Texture2D texture, int index)
        {
            CopyRectTransform(rawImage.rectTransform, image.rectTransform);
            SetVideoSurfaceDepth(image.rectTransform);
            image.canvasRenderer.cullTransparentMesh = false;
            image.texture = texture;
            image.material = null;
            image.transform.SetSiblingIndex(rawImage.transform.GetSiblingIndex() + index);
        }

        private void ConfigureMonoBuffers(int width, int height)
        {
            if (monoRawImages == null)
            {
                return;
            }

            for (int index = 0; index < monoRawImages.Length; index++)
            {
                if (monoRawImages[index] != null)
                {
                    monoRawImages[index].rectTransform.sizeDelta = new Vector2(width, height);
                }
            }
        }

        private void UpdateMonoBuffers(ref byte[] latestImageBytes, object frameLock)
        {
            byte[] imageBytes = null;
            lock (frameLock)
            {
                if (latestImageBytes != null)
                {
                    imageBytes = latestImageBytes;
                    latestImageBytes = null;
                }
            }

            if (imageBytes == null)
            {
                return;
            }

            int inactiveMonoRawImageIndex = 1 - activeMonoRawImageIndex;
            if (monoTextures[inactiveMonoRawImageIndex].LoadImage(imageBytes, false))
            {
                monoRawImages[inactiveMonoRawImageIndex].transform.SetAsLastSibling();
                SetRawImageAlpha(monoRawImages[inactiveMonoRawImageIndex], 1f);
                SetRawImageAlpha(monoRawImages[activeMonoRawImageIndex], 0f);
                activeMonoRawImageIndex = inactiveMonoRawImageIndex;
            }
        }

        private void SetActiveMonoBuffer(int newActiveIndex)
        {
            activeMonoRawImageIndex = newActiveIndex;
            if (monoRawImages == null)
            {
                return;
            }

            for (int index = 0; index < monoRawImages.Length; index++)
            {
                if (monoRawImages[index] == null)
                {
                    continue;
                }

                bool isActive = index == activeMonoRawImageIndex;
                SetRawImageAlpha(monoRawImages[index], isActive ? 1f : 0f);
                if (isActive)
                {
                    monoRawImages[index].transform.SetAsLastSibling();
                }
            }
        }

        private void SetMonoBuffersVisible(bool visible)
        {
            if (monoRawImages == null)
            {
                return;
            }

            for (int index = 0; index < monoRawImages.Length; index++)
            {
                if (monoRawImages[index] != null)
                {
                    bool shouldShow = visible && index == activeMonoRawImageIndex;
                    SetRawImageAlpha(monoRawImages[index], shouldShow ? 1f : 0f);
                }
            }
        }

        private bool EnsureStereoSurface(ref StereoQuadSurface surface, string layerName, string eyeName)
        {
            if (surface == null)
            {
                surface = new StereoQuadSurface
                {
                    root = new GameObject(rawImage.gameObject.name + "_" + eyeName + "StereoSurface"),
                    renderers = new MeshRenderer[2],
                    textures = new Texture2D[2],
                    activeRendererIndex = 0,
                };
                surface.root.transform.SetParent(rawImage.transform, false);
            }

            int layerIndex = LayerMask.NameToLayer(layerName);
            if (layerIndex < 0)
            {
                Debug.LogError($"Missing stereo layer '{layerName}'. Quest scene must define it before stereo video can render correctly.");
                SetStereoSurfaceVisible(surface, false);
                return false;
            }

            SetLayerRecursively(surface.root, layerIndex);
            ConfigureStereoRootTransform(surface.root.transform);

            for (int index = 0; index < surface.renderers.Length; index++)
            {
                if (surface.textures[index] == null)
                {
                    surface.textures[index] = CreateVideoTexture();
                }

                if (surface.renderers[index] == null)
                {
                    GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = surface.root.name + "_Buffer" + index;
                    quad.transform.SetParent(surface.root.transform, false);
                    Collider quadCollider = quad.GetComponent<Collider>();
                    if (quadCollider != null)
                    {
                        Destroy(quadCollider);
                    }
                    surface.renderers[index] = quad.GetComponent<MeshRenderer>();
                }

                ConfigureStereoRenderer(surface.renderers[index], surface.textures[index], layerIndex);
            }

            return true;
        }

        private void ConfigureStereoRootTransform(Transform transformRoot)
        {
            transformRoot.localPosition = new Vector3(0f, 0f, VideoSurfaceZOffset);
            transformRoot.localRotation = Quaternion.identity;
            transformRoot.localScale = Vector3.one;
        }

        private void ConfigureStereoSurface(StereoQuadSurface surface, int width, int height)
        {
            if (surface == null)
            {
                return;
            }

            for (int index = 0; index < surface.renderers.Length; index++)
            {
                if (surface.renderers[index] != null)
                {
                    Transform rendererTransform = surface.renderers[index].transform;
                    rendererTransform.localPosition = Vector3.zero;
                    rendererTransform.localRotation = Quaternion.identity;
                    rendererTransform.localScale = new Vector3(width, height, 1f);
                }
            }

            SetActiveStereoRenderer(surface, 0);
        }

        private void ConfigureStereoRenderer(MeshRenderer renderer, Texture2D texture, int layerIndex)
        {
            GameObject rendererObject = renderer.gameObject;
            rendererObject.layer = layerIndex;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            Material material = renderer.sharedMaterial;
            if (!IsStereoSurfaceMaterial(material))
            {
                material = CreateStereoMaterial(texture);
                renderer.sharedMaterial = material;
            }
            else
            {
                AssignStereoTexture(material, texture);
                if (material.HasProperty("_Cull"))
                {
                    material.SetInt("_Cull", (int)CullMode.Off);
                }
            }
        }

        private bool IsStereoSurfaceMaterial(Material material)
        {
            if (material == null || material.shader == null)
            {
                return false;
            }

            return material.shader.name == UniversalRenderPipelineUnlitShader
                || material.shader.name == LegacyUnlitTextureShader;
        }

        private Material CreateStereoMaterial(Texture2D texture)
        {
            Shader shader = Shader.Find(UniversalRenderPipelineUnlitShader);
            if (shader == null)
            {
                shader = Shader.Find(LegacyUnlitTextureShader);
            }
            if (shader == null)
            {
                Debug.LogError("Could not find a supported unlit shader for stereo video surfaces.");
                return null;
            }

            Material material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }
            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", (int)CullMode.Off);
            }
            AssignStereoTexture(material, texture);
            return material;
        }

        private void AssignStereoTexture(Material material, Texture2D texture)
        {
            if (material == null)
            {
                return;
            }

            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
        }

        private void SetStereoSurfaceVisible(StereoQuadSurface surface, bool visible)
        {
            if (surface == null || surface.renderers == null)
            {
                return;
            }

            for (int index = 0; index < surface.renderers.Length; index++)
            {
                if (surface.renderers[index] != null)
                {
                    surface.renderers[index].enabled = visible && index == surface.activeRendererIndex;
                }
            }
        }

        private void SetActiveStereoRenderer(StereoQuadSurface surface, int newActiveIndex)
        {
            if (surface == null || surface.renderers == null)
            {
                return;
            }

            surface.activeRendererIndex = newActiveIndex;
            for (int index = 0; index < surface.renderers.Length; index++)
            {
                if (surface.renderers[index] != null)
                {
                    surface.renderers[index].enabled = index == surface.activeRendererIndex;
                }
            }
        }

        private void UpdateStereoSurface(StereoQuadSurface surface, ref byte[] latestImageBytes, object frameLock)
        {
            if (surface == null || surface.textures == null || surface.renderers == null)
            {
                return;
            }

            byte[] imageBytes = null;
            lock (frameLock)
            {
                if (latestImageBytes != null)
                {
                    imageBytes = latestImageBytes;
                    latestImageBytes = null;
                }
            }

            if (imageBytes == null)
            {
                return;
            }

            int inactiveRendererIndex = 1 - surface.activeRendererIndex;
            if (surface.textures[inactiveRendererIndex].LoadImage(imageBytes, false))
            {
                if (surface.renderers[inactiveRendererIndex].sharedMaterial != null)
                {
                    AssignStereoTexture(surface.renderers[inactiveRendererIndex].sharedMaterial, surface.textures[inactiveRendererIndex]);
                }
                surface.renderers[inactiveRendererIndex].enabled = true;
                surface.renderers[surface.activeRendererIndex].enabled = false;
                surface.activeRendererIndex = inactiveRendererIndex;
            }
        }

        private void SetVideoSurfaceDepth(RectTransform rectTransform)
        {
            Vector3 localPosition = rectTransform.localPosition;
            localPosition.z = rawImage.rectTransform.localPosition.z + VideoSurfaceZOffset;
            rectTransform.localPosition = localPosition;
        }

        private void CopyRectTransform(RectTransform source, RectTransform destination)
        {
            destination.anchorMin = source.anchorMin;
            destination.anchorMax = source.anchorMax;
            destination.pivot = source.pivot;
            destination.anchoredPosition = source.anchoredPosition;
            destination.sizeDelta = source.sizeDelta;
            destination.localRotation = source.localRotation;
            destination.localScale = source.localScale;
        }

        private void SetRawImageAlpha(RawImage image, float alpha)
        {
            if (image == null)
            {
                return;
            }
            Color color = image.color;
            color.a = alpha;
            image.color = color;
            image.raycastTarget = alpha > 0f;
        }

        private Texture2D CreateVideoTexture()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.SetPixels32(new Color32[] {
                new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255),
                new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255)
            });
            tex.Apply();
            return tex;
        }

        private void SetLayerRecursively(GameObject target, int layerIndex)
        {
            if (target == null)
            {
                return;
            }

            target.layer = layerIndex;
            foreach (Transform child in target.transform)
            {
                SetLayerRecursively(child.gameObject, layerIndex);
            }
        }
    }
}
