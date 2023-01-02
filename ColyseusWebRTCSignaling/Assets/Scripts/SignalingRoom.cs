using Newtonsoft.Json;
using System.Collections;
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
    private MediaStream sendStream;

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

    AudioClip m_clipInput;
    int m_samplingFrequency = 48000;
    int m_lengthSeconds = 1;
    string m_deviceName = null;
    AudioStreamTrack m_audioTrack;

    private void SetupRoom()
    {
        Room.OnMessage<OnAddPeerMsg>("addPeer", OnAddPeer);
        Room.OnMessage<OnCandidateMsg>("candidate", OnCandidate);
        Room.OnMessage<OnDescMsg>("desc", OnDesc);

        m_clipInput = Microphone.Start(m_deviceName, true, m_lengthSeconds, m_samplingFrequency);
        // set the latency to “0” samples before the audio starts to play.
        while (!(Microphone.GetPosition(m_deviceName) > 0)) { }

        ClientInstance.Instance.inputAudioSource.loop = true;
        ClientInstance.Instance.inputAudioSource.clip = m_clipInput;
        ClientInstance.Instance.inputAudioSource.Play();

        m_audioTrack = new AudioStreamTrack(ClientInstance.Instance.inputAudioSource);
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

        var config = CreateRTCConfiguration();

        // Create new peer connection
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
        peerConnection.AddTrack(m_audioTrack, sendStream);
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
