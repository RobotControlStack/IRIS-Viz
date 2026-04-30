

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
        private RawImage secondaryRawImage;
        private Texture2D primaryDisplayTexture;
        private Texture2D primaryUploadTexture;
        private Texture2D secondaryDisplayTexture;
        private Texture2D secondaryUploadTexture;
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
            EnsurePrimaryTexture();
        }

        public void StartSubscription(VideoStreamConfig config)
        {
            CleanupSubscriptions();
            stereoMode = false;
            primaryTopic = config.name;
            EnsurePrimaryTexture();
            if (secondaryRawImage != null)
            {
                secondaryRawImage.gameObject.SetActive(false);
            }
            rawImage.material = null;
            IRISXRNode.Instance.SubscriberManager.RegisterSubscriptionCallback<VideoFrame>(config.name, OnPrimaryFrameReceived, config.url);
            rawImage.rectTransform.sizeDelta = new Vector2(config.width, config.height);
        }

        public void StartStereoSubscription(VideoStreamConfig leftConfig, VideoStreamConfig rightConfig)
        {
            CleanupSubscriptions();
            stereoMode = true;
            primaryTopic = leftConfig.name;
            secondaryTopic = rightConfig.name;
            EnsurePrimaryTexture();
            EnsureSecondaryRawImage();
            EnsureStereoMaterials();

            rawImage.rectTransform.sizeDelta = new Vector2(leftConfig.width, leftConfig.height);
            secondaryRawImage.rectTransform.sizeDelta = new Vector2(rightConfig.width, rightConfig.height);
            rawImage.material = leftEyeMaterial;
            secondaryRawImage.material = rightEyeMaterial;
            secondaryRawImage.gameObject.SetActive(true);

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
                ref primaryDisplayTexture,
                ref primaryUploadTexture,
                rawImage,
                ref latestPrimaryImageBytes,
                primaryFrameLock
            );
            if (stereoMode && secondaryRawImage != null && secondaryDisplayTexture != null && secondaryUploadTexture != null)
            {
                UpdateTexture(
                    ref secondaryDisplayTexture,
                    ref secondaryUploadTexture,
                    secondaryRawImage,
                    ref latestSecondaryImageBytes,
                    secondaryFrameLock
                );
            }
        }

        public void CloseWindow()
        {
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

        private void EnsurePrimaryTexture()
        {
            if (primaryDisplayTexture == null)
            {
                primaryDisplayTexture = CreateVideoTexture();
            }
            if (primaryUploadTexture == null)
            {
                primaryUploadTexture = CreateVideoTexture();
            }
            rawImage.texture = primaryDisplayTexture;
        }

        private void EnsureSecondaryRawImage()
        {
            if (secondaryRawImage == null)
            {
                GameObject secondaryRawImageObject = Instantiate(rawImage.gameObject, rawImage.transform.parent);
                secondaryRawImageObject.name = rawImage.gameObject.name + "_RightEye";
                secondaryRawImage = secondaryRawImageObject.GetComponent<RawImage>();

                TMP_Text[] labels = secondaryRawImageObject.GetComponentsInChildren<TMP_Text>(true);
                for (int index = 0; index < labels.Length; index++)
                {
                    labels[index].gameObject.SetActive(false);
                }
            }

            secondaryRawImage.transform.SetSiblingIndex(rawImage.transform.GetSiblingIndex() + 1);
            secondaryRawImage.rectTransform.anchorMin = rawImage.rectTransform.anchorMin;
            secondaryRawImage.rectTransform.anchorMax = rawImage.rectTransform.anchorMax;
            secondaryRawImage.rectTransform.pivot = rawImage.rectTransform.pivot;
            secondaryRawImage.rectTransform.anchoredPosition = rawImage.rectTransform.anchoredPosition;
            secondaryRawImage.rectTransform.localRotation = rawImage.rectTransform.localRotation;
            secondaryRawImage.rectTransform.localScale = rawImage.rectTransform.localScale;

            if (secondaryDisplayTexture == null)
            {
                secondaryDisplayTexture = CreateVideoTexture();
            }
            if (secondaryUploadTexture == null)
            {
                secondaryUploadTexture = CreateVideoTexture();
            }
            secondaryRawImage.texture = secondaryDisplayTexture;
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

        private Texture2D CreateVideoTexture()
        {
            return new Texture2D(2, 2, TextureFormat.RGBA32, false);
        }

        private void UpdateTexture(
            ref Texture2D displayTexture,
            ref Texture2D uploadTexture,
            RawImage image,
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

            if (uploadTexture.LoadImage(imageBytes, false))
            {
                Texture2D previousDisplayTexture = displayTexture;
                displayTexture = uploadTexture;
                uploadTexture = previousDisplayTexture;
                image.texture = displayTexture;
            }
        }
    }
}
