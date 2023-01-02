using Colyseus;
using System.Threading.Tasks;
using UnityEngine;

public class ClientInstance : MonoBehaviour
{
    public static ClientInstance Instance { get; private set; }

    public string address = "localhost:2567";
    public bool secured = false;
    public AudioSource inputAudioSource;

    public SignalingRoom SignalingRoom { get; private set; } = null;



    private ColyseusClient _client;
    public ColyseusClient Client
    {
        get
        {
            if (_client == null)
                _client = new ColyseusClient(GetWsAddress());
            _client.Settings.useSecureProtocol = secured;
            return _client;
        }
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public string GetWsAddress()
    {
        return (secured ? "wss://" : "ws://") + address;
    }

    public string GetApiAddress()
    {
        return (secured ? "https://" : "http://") + address;
    }

    public async Task<SignalingRoom> JoinSignalingRoom()
    {
        if (SignalingRoom != null)
            await SignalingRoom.Leave();
        SignalingRoom = new SignalingRoom();
        if (await SignalingRoom.Join())
        {
            return SignalingRoom;
        }
        return null;
    }

    public async Task LeaveSignalingRoom()
    {
        if (SignalingRoom != null)
        {
            await SignalingRoom.Leave();
            SignalingRoom = null;
        }
    }

    private async void OnApplicationQuit()
    {
        await LeaveSignalingRoom();
    }

    private void Start()
    {
        // Test codes
        JoinSignalingRoom();
    }
}