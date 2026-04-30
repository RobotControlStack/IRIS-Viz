using UnityEngine;
using MessagePack;
using IRIS.Node;
using IRIS.Utilities;
using System.Collections.Generic;



namespace IRIS.SceneLoader
{

    [MessagePackObject(keyAsPropertyName: true)]
    public class VideoStreamConfig
    {
        public string name { get; set; }
        public string url { get; set; }
        public int width { get; set; }
        public int height { get; set; }
    }

    public class VideoStreamSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject videoReceiverPrefab;
        private readonly Dictionary<string, VideoStreamConfig> pendingLeftConfigs = new Dictionary<string, VideoStreamConfig>();
        private readonly Dictionary<string, VideoStreamConfig> pendingRightConfigs = new Dictionary<string, VideoStreamConfig>();
        private readonly HashSet<string> dismissedReceivers = new HashSet<string>();

        void Start()
        {
            if (videoReceiverPrefab == null)
            {
                Debug.LogError("VideoReceiverPrefab is not assigned in VideoStreamSpawner.");
                return;
            }
            IRISXRNode.Instance.ServiceManager.RegisterServiceCallback<VideoStreamConfig, string>("SpawnVideoReceiver", SpawnVideoReceiver);
            IRISXRNode.Instance.ServiceManager.RegisterServiceCallback<string, string>("DeleteVideoReceiver", DeleteVideoReceiver);
        }

        private string SpawnVideoReceiver(VideoStreamConfig config)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                string stereoBaseName;
                bool isLeft;
                if (TryGetStereoBaseName(config.name, out stereoBaseName, out isLeft))
                {
                    if (dismissedReceivers.Contains(stereoBaseName))
                    {
                        return;
                    }
                    RegisterStereoConfig(stereoBaseName, config, isLeft);
                    TrySpawnStereoReceiver(stereoBaseName);
                    return;
                }

                if (dismissedReceivers.Contains(config.name))
                {
                    return;
                }

                Transform existingReceiver = gameObject.transform.Find(config.name);
                if (existingReceiver != null)
                {
                    return;
                }

                GameObject videoStreamObj = Instantiate(videoReceiverPrefab, gameObject.transform);
                videoStreamObj.name = config.name;
                VideoStreamReceiver receiver = videoStreamObj.GetComponent<VideoStreamReceiver>();
                if (receiver != null)
                {
                    receiver.StartSubscription(config);
                }
            });
            return ResponseStatus.SUCCESS;
        }

        private string DeleteVideoReceiver(string videoStreamId)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                string stereoBaseName;
                bool isLeft;
                if (TryGetStereoBaseName(videoStreamId, out stereoBaseName, out isLeft))
                {
                    pendingLeftConfigs.Remove(stereoBaseName);
                    pendingRightConfigs.Remove(stereoBaseName);
                    dismissedReceivers.Remove(stereoBaseName);
                    videoStreamId = stereoBaseName;
                }
                else
                {
                    dismissedReceivers.Remove(videoStreamId);
                }

                Transform videoStreamTrans = gameObject.transform.Find(videoStreamId);
                if (videoStreamTrans != null)
                {
                    Destroy(videoStreamTrans.gameObject);
                }
            });
            return ResponseStatus.SUCCESS;
        }

        public void DismissVideoReceiver(string videoStreamId)
        {
            string stereoBaseName;
            bool isLeft;
            if (TryGetStereoBaseName(videoStreamId, out stereoBaseName, out isLeft))
            {
                dismissedReceivers.Add(stereoBaseName);
                pendingLeftConfigs.Remove(stereoBaseName);
                pendingRightConfigs.Remove(stereoBaseName);
                return;
            }

            dismissedReceivers.Add(videoStreamId);
        }

        private void RegisterStereoConfig(string stereoBaseName, VideoStreamConfig config, bool isLeft)
        {
            if (isLeft)
            {
                pendingLeftConfigs[stereoBaseName] = config;
            }
            else
            {
                pendingRightConfigs[stereoBaseName] = config;
            }
        }

        private void TrySpawnStereoReceiver(string stereoBaseName)
        {
            VideoStreamConfig leftConfig;
            VideoStreamConfig rightConfig;
            if (!pendingLeftConfigs.TryGetValue(stereoBaseName, out leftConfig) ||
                !pendingRightConfigs.TryGetValue(stereoBaseName, out rightConfig))
            {
                return;
            }

            Transform existingReceiver = gameObject.transform.Find(stereoBaseName);
            if (existingReceiver != null)
            {
                return;
            }

            GameObject videoStreamObj = Instantiate(videoReceiverPrefab, gameObject.transform);
            videoStreamObj.name = stereoBaseName;
            VideoStreamReceiver receiver = videoStreamObj.GetComponent<VideoStreamReceiver>();
            if (receiver != null)
            {
                receiver.StartStereoSubscription(leftConfig, rightConfig);
            }
        }

        private bool TryGetStereoBaseName(string streamName, out string stereoBaseName, out bool isLeft)
        {
            if (streamName.EndsWith("_left"))
            {
                stereoBaseName = streamName.Substring(0, streamName.Length - "_left".Length);
                isLeft = true;
                return true;
            }
            if (streamName.EndsWith("_right"))
            {
                stereoBaseName = streamName.Substring(0, streamName.Length - "_right".Length);
                isLeft = false;
                return true;
            }

            stereoBaseName = streamName;
            isLeft = false;
            return false;
        }
    }
}
