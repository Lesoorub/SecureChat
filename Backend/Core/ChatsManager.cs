using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Backend.Controllers;

public class ChatsManager
{
    readonly ConcurrentDictionary<string/*roomname*/, Room> _rooms = new ConcurrentDictionary<string, Room>();

    public ChatsManager()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5));
                    foreach (var (roomKey, room) in _rooms)
                    {
                        if (room.MembersCount == 0)
                        {
                            _rooms.TryRemove(roomKey, out _);
                        }
                    }
                }
                catch
                {

                }
            }
        });
    }

    public bool TryCreateRoom(string roomname, string salt, string verifier)
    {
        var room = new Room(salt, verifier);
        return _rooms.TryAdd(roomname, room);
    }

    public async Task TryJoinRoom(string roomname, WebSocket ws)
    {
        if (_rooms.TryGetValue(roomname, out var room))
        {
            await room.TryAuthSRC(ws, roomname);
            await room.AddMember(ws);
        }
    }
}
