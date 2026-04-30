

using UnityEngine;
using UnityEngine.UI;
using IRIS.Node;
using MessagePack;
using TMPro;

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
        public RawImage rawImage;
        private RawImage[] primaryRawImages;
        private RawImage[] secondaryRawImages;
        private int activePrimaryRawImageIndex;
        private int activeSecondaryRawImageIndex;
        private Texture2D[] primaryTextures;
        private Texture2D[] secondaryTextures;
        private byte[] latestPrimaryImageBytes;
        private byte[] latestSecondaryImageBytes;
        private readonly object primaryFrameLock = new object();
        private readonly object secondaryFrameLock = new object();
        private string primaryTopic;
        private string secondaryTopic;
        private bool stereoMode;
        private static Material leftEyeMaterial;
        private static Material rightEyeMaterial;

        private void Start()
        {
            EnsurePrimaryRawImages();
        }

        public void StartSubscription(VideoStreamConfig config)
        {
            CleanupSubscriptions();
            stereoMode = false;
            primaryTopic = config.name;
            EnsurePrimaryRawImages();
            SetEyeVisible(secondaryRawImages, false);
            SetEyeMaterial(primaryRawImages, null);
            IRISXRNode.Instance.SubscriberManager.RegisterSubscriptionCallback<VideoFrame>(config.name, OnPrimaryFrameReceived, config.url);
            SetEyeSize(primaryRawImages, config.width, config.height);
        }

        public void StartStereoSubscription(VideoStreamConfig leftConfig, VideoStreamConfig rightConfig)
        {
            CleanupSubscriptions();
            stereoMode = true;
            primaryTopic = leftConfig.name;
            secondaryTopic = rightConfig.name;
            EnsurePrimaryRawImages();
            EnsureSecondaryRawImages();
            EnsureStereoMaterials();

            SetEyeSize(primaryRawImages, leftConfig.width, leftConfig.height);
            SetEyeSize(secondaryRawImages, rightConfig.width, rightConfig.height);
            SetEyeMaterial(primaryRawImages, leftEyeMaterial);
            SetEyeMaterial(secondaryRawImages, rightEyeMaterial);
            SetEyeVisible(secondaryRawImages, true);

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
            UpdateTexture(
                primaryRawImages,
                primaryTextures,
                ref activePrimaryRawImageIndex,
                ref latestPrimaryImageBytes,
                primaryFrameLock
            );
            if (stereoMode && secondaryRawImages != null && secondaryTextures != null)
            {
                UpdateTexture(
                    secondaryRawImages,
                    secondaryTextures,
                    ref activeSecondaryRawImageIndex,
                    ref latestSecondaryImageBytes,
                    secondaryFrameLock
                );
            }
        }

        public void CloseWindow()
        {
            VideoStreamSpawner spawner = GetComponentInParent<VideoStreamSpawner>();
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
            stereoMode = false;
        }

        private void EnsurePrimaryRawImages()
        {
            EnsureEyeBuffers(ref primaryRawImages, ref primaryTextures, rawImage);
            SetActiveEyeBuffer(primaryRawImages, ref activePrimaryRawImageIndex, 0);
        }

        private void EnsureSecondaryRawImages()
        {
            RawImage secondaryTemplate = GetSecondaryTemplateRawImage();
            EnsureEyeBuffers(ref secondaryRawImages, ref secondaryTextures, secondaryTemplate);
            SetActiveEyeBuffer(secondaryRawImages, ref activeSecondaryRawImageIndex, 0);
        }

        private void EnsureStereoMaterials()
        {
            Shader stereoEyeShader = Shader.Find("IRIS/UI/StereoEyeMask");
            if (stereoEyeShader == null)
            {
                Debug.LogError("Could not find IRIS/UI/StereoEyeMask shader.");
                return;
            }

            if (leftEyeMaterial == null)
            {
                leftEyeMaterial = new Material(stereoEyeShader);
                leftEyeMaterial.SetFloat("_EyeIndex", 0f);
            }
            if (rightEyeMaterial == null)
            {
                rightEyeMaterial = new Material(stereoEyeShader);
                rightEyeMaterial.SetFloat("_EyeIndex", 1f);
            }
        }

        private RawImage GetSecondaryTemplateRawImage()
        {
            if (secondaryRawImages != null && secondaryRawImages.Length > 0 && secondaryRawImages[0] != null)
            {
                return secondaryRawImages[0];
            }

            EnsurePrimaryRawImages();
            GameObject secondaryRawImageObject = Instantiate(primaryRawImages[0].gameObject, primaryRawImages[0].transform.parent);
            secondaryRawImageObject.name = rawImage.gameObject.name + "_RightEye";
            RawImage secondaryTemplate = secondaryRawImageObject.GetComponent<RawImage>();
            TMP_Text[] labels = secondaryRawImageObject.GetComponentsInChildren<TMP_Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                labels[index].gameObject.SetActive(false);
            }
            CopyRectTransform(rawImage.rectTransform, secondaryTemplate.rectTransform);
            return secondaryTemplate;
        }

        private void EnsureEyeBuffers(ref RawImage[] eyeRawImages, ref Texture2D[] eyeTextures, RawImage template)
        {
            if (eyeRawImages == null)
            {
                eyeRawImages = new RawImage[2];
            }
            if (eyeTextures == null)
            {
                eyeTextures = new Texture2D[2];
            }

            if (eyeRawImages[0] == null)
            {
                eyeRawImages[0] = template;
            }
            ConfigureRawImage(eyeRawImages[0], 0);

            for (int index = 0; index < 2; index++)
            {
                if (eyeTextures[index] == null)
                {
                    eyeTextures[index] = CreateVideoTexture();
                }

                if (eyeRawImages[index] == null)
                {
                    GameObject eyeObject = Instantiate(template.gameObject, template.transform.parent);
                    eyeObject.name = template.gameObject.name + "_Buffer" + index;
                    eyeRawImages[index] = eyeObject.GetComponent<RawImage>();
                }

                ConfigureRawImage(eyeRawImages[index], index);
                eyeRawImages[index].texture = eyeTextures[index];
            }
        }

        private void ConfigureRawImage(RawImage image, int index)
        {
            CopyRectTransform(rawImage.rectTransform, image.rectTransform);
            SetRawImageAlpha(image, index == 0 ? 1f : 0f);
            image.transform.SetSiblingIndex(rawImage.transform.GetSiblingIndex() + index);
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

        private void SetEyeSize(RawImage[] eyeRawImages, int width, int height)
        {
            if (eyeRawImages == null)
            {
                return;
            }
            for (int index = 0; index < eyeRawImages.Length; index++)
            {
                if (eyeRawImages[index] != null)
                {
                    eyeRawImages[index].rectTransform.sizeDelta = new Vector2(width, height);
                }
            }
        }

        private void SetEyeMaterial(RawImage[] eyeRawImages, Material material)
        {
            if (eyeRawImages == null)
            {
                return;
            }
            for (int index = 0; index < eyeRawImages.Length; index++)
            {
                if (eyeRawImages[index] != null)
                {
                    eyeRawImages[index].material = material;
                }
            }
        }

        private void SetEyeVisible(RawImage[] eyeRawImages, bool visible)
        {
            if (eyeRawImages == null)
            {
                return;
            }
            for (int index = 0; index < eyeRawImages.Length; index++)
            {
                if (eyeRawImages[index] != null)
                {
                    SetRawImageAlpha(eyeRawImages[index], visible && index == 0 ? 1f : 0f);
                }
            }
        }

        private void SetActiveEyeBuffer(RawImage[] eyeRawImages, ref int activeIndex, int newActiveIndex)
        {
            if (eyeRawImages == null)
            {
                return;
            }
            activeIndex = newActiveIndex;
            for (int index = 0; index < eyeRawImages.Length; index++)
            {
                if (eyeRawImages[index] != null)
                {
                    bool isActive = index == activeIndex;
                    SetRawImageAlpha(eyeRawImages[index], isActive ? 1f : 0f);
                    if (isActive)
                    {
                        eyeRawImages[index].transform.SetAsLastSibling();
                    }
                }
            }
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
            return new Texture2D(2, 2, TextureFormat.RGBA32, false);
        }

        private void UpdateTexture(
            RawImage[] eyeRawImages,
            Texture2D[] eyeTextures,
            ref int activeEyeRawImageIndex,
            ref byte[] latestImageBytes,
            object frameLock
        )
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

            int inactiveEyeRawImageIndex = 1 - activeEyeRawImageIndex;
            if (eyeTextures[inactiveEyeRawImageIndex].LoadImage(imageBytes, false))
            {
                SetRawImageAlpha(eyeRawImages[inactiveEyeRawImageIndex], 1f);
                eyeRawImages[inactiveEyeRawImageIndex].transform.SetAsLastSibling();
                SetRawImageAlpha(eyeRawImages[activeEyeRawImageIndex], 0f);
                activeEyeRawImageIndex = inactiveEyeRawImageIndex;
            }
        }
    }
}
