using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

public class SignalingRoom : BaseRoomManager<object>
{
    [System.Serializable]
    public class OnAddPeerMsg
    {
        public string sessionId;
        public bool shouldCreateOffer;

        public OnAddPeerMsg()
        {
            sessionId = string.Empty;
            shouldCreateOffer = false;
        }
    }

    [System.Serializable]
    public class OnCandidateMsg
    {
        public string sessionId;
        public string candidate;
        public string sdpMid;
        public int? sdpMLineIndex;

        public OnCandidateMsg()
        {
            sessionId = string.Empty;
            candidate = string.Empty;
            sdpMid = string.Empty;
            sdpMLineIndex = null;
        }
    }

    [System.Serializable]
    public class OnDescMsg
    {
        public string sessionId;
        public int type;
        public string sdp;

        public OnDescMsg()
        {
            sessionId = string.Empty;
            type = 0;
            sdp = string.Empty;
        }
    }

    private Dictionary<string, RTCPeerConnection> peers = new Dictionary<string, RTCPeerConnection>();
    private Dictionary<string, MediaStream> peerReceiveStreams = new Dictionary<string, MediaStream>();
    private Dictionary<string, AudioSource> peerAudioOutputSources = new Dictionary<string, AudioSource>();
    private AudioClip audioInputClip;
    private AudioStreamTrack audioInputTrack;
    private MediaStream sendStream;
    private string micDeviceName;
    private int samplingFrequency = 48000;
    private int lengthSeconds = 1;

    public SignalingRoom() : base("signalingRoom", new Dictionary<string, object>())
    {

    }

    public override async Task<bool> Join()
    {
        peers.Clear();
        if (await base.Join())
        {
            SetupRoom();
            return true;
        }
        return false;
    }

    public override async Task<bool> JoinById(string id)
    {
        peers.Clear();
        if (await base.JoinById(id))
        {
            SetupRoom();
            return true;
        }
        return false;
    }

    private void SetupRoom()
    {
        Room.OnMessage<OnAddPeerMsg>("addPeer", OnAddPeer);
        Room.OnMessage<string>("removePeer", OnRemovePeer);
        Room.OnMessage<OnCandidateMsg>("candidate", OnCandidate);
        Room.OnMessage<OnDescMsg>("desc", OnDesc);

        micDeviceName = Microphone.devices[0];
        audioInputClip = Microphone.Start(micDeviceName, true, lengthSeconds, samplingFrequency);
        // set the latency to “0” samples before the audio starts to play.
        while (!(Microphone.GetPosition(micDeviceName) > 0)) { }

        ClientInstance.Instance.inputAudioSource.loop = true;
        ClientInstance.Instance.inputAudioSource.clip = audioInputClip;
        ClientInstance.Instance.inputAudioSource.Play();

        audioInputTrack = new AudioStreamTrack(ClientInstance.Instance.inputAudioSource);
        sendStream = new MediaStream();
    }

    private static RTCConfiguration CreateRTCConfiguration()
    {
        RTCConfiguration config = default;
        config.iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } };
        return config;
    }

    private async void OnAddPeer(OnAddPeerMsg data)
    {
        var sessionId = data.sessionId;
        var shouldCreateOffer = data.shouldCreateOffer;
        if (peers.ContainsKey(sessionId))
        {
            Debug.LogWarning($"Already connected to peer: {sessionId}");
            return;
        }

        // Create new peer connection
        var config = CreateRTCConfiguration();
        var peerConnection = new RTCPeerConnection(ref config);
        peerConnection.OnIceCandidate = async (RTCIceCandidate candidate) =>
        {
            var info = new RTCIceCandidateInit();
            info.candidate = candidate.Candidate;
            info.sdpMid = candidate.SdpMid;
            info.sdpMLineIndex = candidate.SdpMLineIndex;
            await SendCandidate(sessionId, info);
        };
        peerConnection.OnTrack = (RTCTrackEvent trackEvent) =>
        {
            peerReceiveStreams[sessionId].AddTrack(trackEvent.Track);
        };
        peerConnection.AddTrack(audioInputTrack, sendStream);
        peers[sessionId] = peerConnection;

        // Create media receive stream
        var peerMediaStream = new MediaStream();
        peerMediaStream.OnAddTrack = (trackEvent) =>
        {
            if (trackEvent.Track is VideoStreamTrack videoTrack)
            {
                videoTrack.OnVideoReceived += (Texture texture) =>
                {
                    // Render the video
                };
            }
            if (trackEvent.Track is AudioStreamTrack audioTrack)
            {
                // Play audio
                if (peerAudioOutputSources.ContainsKey(sessionId))
                {
                    Debug.LogWarning($"Adding audio track for {sessionId} again, it actually should has only one?");
                    Object.Destroy(peerAudioOutputSources[sessionId].gameObject);
                    peerAudioOutputSources.Remove(sessionId);
                }

                var audioOutputSource = new GameObject($"{sessionId}_AudioOutputSource").AddComponent<AudioSource>();
                audioOutputSource.SetTrack(audioTrack);
                audioOutputSource.loop = true;
                audioOutputSource.Play();
                peerAudioOutputSources[sessionId] = audioOutputSource;
            }
        };
        peerReceiveStreams[sessionId] = peerMediaStream;

        if (shouldCreateOffer)
        {
            Debug.Log($"Creating RTC offer to {sessionId}");
            var createOfferAsyncOp = peerConnection.CreateOffer();

            while (!createOfferAsyncOp.IsDone)
            {
                await Task.Yield();
            }

            if (createOfferAsyncOp.IsError)
            {
                Debug.LogError($"Error when create offer: {createOfferAsyncOp.Error}");
                return;
            }

            var desc = createOfferAsyncOp.Desc;
            var setLocalDescAsyncOp = peerConnection.SetLocalDescription(ref desc);

            while (!setLocalDescAsyncOp.IsDone)
            {
                await Task.Yield();
            }

            if (setLocalDescAsyncOp.IsError)
            {
                Debug.LogError($"Error when set local desc, after create offer: {setLocalDescAsyncOp.Error}");
                return;
            }

            await SendDesc(sessionId, desc);
        }
    }

    private void OnRemovePeer(string sessionId)
    {
        if (peerReceiveStreams.TryGetValue(sessionId, out var peerReceiveStream))
        {
            peerReceiveStream.Dispose();
            peerReceiveStreams.Remove(sessionId);
        }

        if (peerAudioOutputSources.TryGetValue(sessionId, out var peerAudioOutputSource))
        {
            Object.Destroy(peerAudioOutputSource.gameObject);
            peerAudioOutputSources.Remove(sessionId);
        }

        if (peers.TryGetValue(sessionId, out var peer))
        {
            peer.Close();
            peer.Dispose();
            peers.Remove(sessionId);
        }
    }

    private void OnCandidate(OnCandidateMsg data)
    {
        var sessionId = data.sessionId;
        var peer = peers[sessionId];
        var info = new RTCIceCandidateInit();
        info.candidate = data.candidate;
        info.sdpMid = data.sdpMid;
        if (data.sdpMLineIndex.HasValue)
            info.sdpMLineIndex = data.sdpMLineIndex;
        peer.AddIceCandidate(new RTCIceCandidate(info));
    }

    private async void OnDesc(OnDescMsg data)
    {
        var sessionId = data.sessionId;
        var peerConnection = peers[sessionId];
        var desc = new RTCSessionDescription();
        desc.type = (RTCSdpType)data.type;
        desc.sdp = data.sdp;
        var setRemoteDescAsyncOp = peerConnection.SetRemoteDescription(ref desc);

        while (!setRemoteDescAsyncOp.IsDone)
        {
            await Task.Yield();
        }

        if (desc.type != RTCSdpType.Offer)
            return;

        Debug.Log($"Creating RTC answer to {sessionId}");
        var createAnswerAsyncOp = peerConnection.CreateAnswer();

        while (!createAnswerAsyncOp.IsDone)
        {
            await Task.Yield();
        }

        if (createAnswerAsyncOp.IsError)
        {
            Debug.LogError($"Error when create answer: {createAnswerAsyncOp.Error}");
            return;
        }

        desc = createAnswerAsyncOp.Desc;
        var setLocalDescAsyncOp = peerConnection.SetLocalDescription(ref desc);

        while (!setLocalDescAsyncOp.IsDone)
        {
            await Task.Yield();
        }

        if (setLocalDescAsyncOp.IsError)
        {
            Debug.LogError($"Error when set local desc, after create answer: {setLocalDescAsyncOp.Error}");
            return;
        }

        await SendDesc(sessionId, desc);
    }

    public async Task SendCandidate(string sessionId, RTCIceCandidateInit candidateInit)
    {
        var data = new OnCandidateMsg();
        data.sessionId = sessionId;
        data.candidate = candidateInit.candidate;
        data.sdpMid = candidateInit.sdpMid;
        if (candidateInit.sdpMLineIndex.HasValue)
            data.sdpMLineIndex = candidateInit.sdpMLineIndex.Value;
        await Room.Send("candidate", data);
    }

    public async Task SendDesc(string sessionId, RTCSessionDescription desc)
    {
        var data = new OnDescMsg();
        data.sessionId = sessionId;
        data.type = (int)desc.type;
        data.sdp = desc.sdp;
        await Room.Send("desc", data);
    }
}
