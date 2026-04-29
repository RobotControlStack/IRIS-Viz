

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
        private Texture2D primaryTexture;
        private Texture2D secondaryTexture;
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
            UpdateTexture(primaryTexture, rawImage, ref latestPrimaryImageBytes, primaryFrameLock);
            if (stereoMode && secondaryRawImage != null && secondaryTexture != null)
            {
                UpdateTexture(secondaryTexture, secondaryRawImage, ref latestSecondaryImageBytes, secondaryFrameLock);
            }
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
            if (primaryTexture == null)
            {
                primaryTexture = new Texture2D(2, 2);
            }
            rawImage.texture = primaryTexture;
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

            if (secondaryTexture == null)
            {
                secondaryTexture = new Texture2D(2, 2);
            }
            secondaryRawImage.texture = secondaryTexture;
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

        private void UpdateTexture(Texture2D texture, RawImage image, ref byte[] latestImageBytes, object frameLock)
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

            if (texture.LoadImage(imageBytes, false))
            {
                image.texture = texture;
            }
        }
    }
}
