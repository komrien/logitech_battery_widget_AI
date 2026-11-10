using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LogitechBatteryWidget;

public record BatteryInfo(string Name, int Percentage, bool Charging, bool FullyCharged, double? HoursLeft);

/// <summary>
/// G HUB 로컬 API(ws://localhost:9010)에서 배터리 상태를 받는다.
/// 배터리 변경 이벤트를 구독하고, 놓치는 경우를 대비해 3초마다 직접 조회한다.
/// </summary>
public sealed class GHubClient : IDisposable
{
    static readonly Uri Url = new("ws://localhost:9010");
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    readonly CancellationTokenSource _cts = new();
    readonly SemaphoreSlim _kick = new(0);
    readonly SemaphoreSlim _sendLock = new(1, 1);
    Dictionary<string, string> _names = new();               // deviceId -> 표시 이름
    readonly Dictionary<string, BatteryInfo> _state = new();  // deviceId -> 상태
    bool _needList;

    public event Action<IReadOnlyList<BatteryInfo>>? Updated;
    public event Action<string>? Offline;

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    public void RequestRefresh() => _kick.Release();

    async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SessionAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Offline?.Invoke($"{e.GetType().Name}: {e.Message}");
            }
            try { await Task.Delay(ReconnectDelay, ct); } catch (OperationCanceledException) { return; }
        }
    }

    async Task SessionAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol("json");
        ws.Options.SetRequestHeader("Origin", "file://");
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await ws.ConnectAsync(Url, timeout.Token);
        }

        async Task Send(string verb, string path, string msgId = "")
        {
            var json = JsonSerializer.Serialize(new { msgId, verb, path });
            await _sendLock.WaitAsync(ct);
            try { await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct); }
            finally { _sendLock.Release(); }
        }

        await Send("SUBSCRIBE", "/battery/state/changed");
        await Send("SUBSCRIBE", "/devices/state/changed");
        await Send("GET", "/devices/list", "list");

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var poller = Task.Run(async () =>
        {
            while (!sessionCts.IsCancellationRequested)
            {
                await _kick.WaitAsync(PollInterval, sessionCts.Token);
                foreach (var id in _names.Keys.ToList())
                    await Send("GET", $"/battery/{id}/state", "bat");
            }
        }, sessionCts.Token);

        try
        {
            var buffer = new byte[64 * 1024];
            using var ms = new MemoryStream();
            while (ws.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buffer, ct);
                    if (r.MessageType == WebSocketMessageType.Close)
                        throw new WebSocketException("G HUB가 연결을 닫았어요");
                    ms.Write(buffer, 0, r.Count);
                } while (!r.EndOfMessage);

                Handle(ms.ToArray());
                if (_needList)
                {
                    _needList = false;
                    await Send("GET", "/devices/list", "list");
                }
            }
        }
        finally
        {
            sessionCts.Cancel();
            try { await poller; } catch { /* 종료 중 */ }
        }
    }

    void Handle(byte[] raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var path = root.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            if (path == "/devices/state/changed") _needList = true;
            return;
        }

        if (path == "/devices/list")
        {
            var names = new Dictionary<string, string>();
            if (payload.TryGetProperty("deviceInfos", out var infos))
            {
                foreach (var d in infos.EnumerateArray())
                {
                    if (!Bool(d, "hasWirelessInterface") || Str(d, "state") != "ACTIVE") continue;
                    var id = Str(d, "id");
                    if (id is null) continue;
                    names[id] = Str(d, "displayName") ?? Str(d, "deviceBaseModel") ?? id;
                }
            }
            _names = names;
            foreach (var gone in _state.Keys.Where(k => !names.ContainsKey(k)).ToList())
                _state.Remove(gone);
            RequestRefresh();
            return;
        }

        if (path == "/devices/state/changed")
        {
            _needList = true;
            return;
        }

        if ((Str(payload, "@type") ?? "").EndsWith("wireless.Battery"))
        {
            var id = Str(payload, "deviceId");
            if (id is null || !_names.TryGetValue(id, out var name))
            {
                _needList = true;
                return;
            }
            if (Str(payload, "batteryStateOfChargeSupport") == "STATE_OF_CHARGE_UNSUPPORTED") return;

            double? hours = payload.TryGetProperty("mileage", out var m) && m.ValueKind == JsonValueKind.Number
                ? m.GetDouble() : null;
            var pct = payload.TryGetProperty("percentage", out var pc) && pc.ValueKind == JsonValueKind.Number
                ? pc.GetInt32() : 0;
            _state[id] = new BatteryInfo(name, pct, Bool(payload, "charging"), Bool(payload, "fullyCharged"), hours);
            Updated?.Invoke(_state.Values.ToList());
        }
    }

    static string? Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool Bool(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    public void Dispose() => _cts.Cancel();
}
