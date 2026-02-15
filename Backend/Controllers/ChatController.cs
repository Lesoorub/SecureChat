using System.Net.WebSockets;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("chat")]
public class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;
    private readonly ChatsManager _chatManager;

    public ChatController(ILogger<ChatController> logger, ChatsManager ChatManager)
    {
        _logger = logger;
        _chatManager = ChatManager;
    }

    [HttpPost]
    [Route("create")]
    public IActionResult Create([FromBody] CreateRoomRequest request)
    {
        if (_chatManager.TryCreateRoom(request.RoomName, request.Salt, request.Verifier))
        {
            return Ok();
        }
        else
        {
            return BadRequest();
        }
    }

    [HttpGet]
    [Route("join")]
    public async Task Join(string roomname)
    {
        try
        {
            HttpContext context = ControllerContext.HttpContext;
            bool isSocketRequest = context.WebSockets.IsWebSocketRequest;

            if (!isSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            await _chatManager.TryJoinRoom(roomname, webSocket);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"{nameof(Join)} throws error");
            ControllerContext.HttpContext.Response.StatusCode = 500;
        }
    }

    public class CreateRoomRequest
    {
        [JsonPropertyName("room")]
        public string RoomName { get; set; } = string.Empty;
        [JsonPropertyName("salt")]
        public string Salt { get; set; } = string.Empty;
        [JsonPropertyName("verifier")]
        public string Verifier { get; set; } = string.Empty;
    }
}
