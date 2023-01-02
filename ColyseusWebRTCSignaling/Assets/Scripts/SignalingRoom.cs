using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

public class SignalingRoom : BaseRoomManager<object>
{
    public SignalingRoom() : base("signaling", new Dictionary<string, object>())
    {
    }

    public override async Task<bool> Join()
    {
        if (await base.Join())
        {
            SetupRoom();
            return true;
        }
        return false;
    }

    public override async Task<bool> JoinById(string id)
    {
        if (await base.JoinById(id))
        {
            SetupRoom();
            return true;
        }
        return false;
    }

    private void SetupRoom()
    {
        Room.OnMessage<Dictionary<string, object>>("addPeer", OnAddPeer);
        Room.OnMessage<Dictionary<string, object>>("offer", OnOffer);
        Room.OnMessage<Dictionary<string, object>>("candidate", OnCandidate);
    }

    private void OnAddPeer(Dictionary<string, object> data)
    {

    }

    private void OnOffer(Dictionary<string, object> data)
    {

    }

    private void OnCandidate(Dictionary<string, object> data)
    {

    }

    public async Task SendOffer(string sessionDescription)
    {
        var data = new Dictionary<string, object>();
        data["sessionDescription"] = sessionDescription;
        await Room.Send("offer", data);
    }

    public async Task SendCandidate(string candidate)
    {
        var data = new Dictionary<string, object>();
        data["candidate"] = candidate;
        await Room.Send("candidate", data);
    }
}
