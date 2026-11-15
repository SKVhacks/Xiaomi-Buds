using XiaomiBuds.Core;

namespace XiaomiBuds.Devices.RedmiBuds;

/// <summary>Async client for the Redmi Buds control channel: connect, authenticate, track state, send commands.</summary>
public sealed class RedmiBudsClient(ulong address)
{
    static readonly Guid ServiceUuid = Guid.Parse("0000fd2d-0000-1000-8000-00805f9b34fb");

    RfcommConnection? _conn;
    FrameParser _parser = new();
    int _seq;
    TaskCompletionSource _authed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationTokenSource? _readCts;

    public BudsState State { get; private set; } = new();
    public event Action<BudsState>? Changed;
    public event Action? Disconnected;

    void Emit() => Changed?.Invoke(State.Clone());

    public async Task ConnectAsync(CancellationToken ct)
    {
        _parser = new FrameParser();
        _authed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        State = new BudsState();
        var conn = new RfcommConnection(address, ServiceUuid);
        try { await conn.ConnectAsync(ct); }
        catch { conn.Dispose(); throw; }
        _conn = conn;
        State.Connected = true;
        Emit();
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;
        _ = Task.Run(() => ReadLoop(conn, token));

        await Request(Op.AuthChallenge, BudsCodec.AuthChallenge(BudsAuth.RandomChallenge()));
        var done = await Task.WhenAny(_authed.Task, Task.Delay(10_000, ct));
        if (done != _authed.Task) Log.Warn("auth not confirmed within 10 s, continuing anyway");
        await RefreshAsync();
    }

    public void Disconnect()
    {
        _readCts?.Cancel();
        _conn?.Dispose();
        _conn = null;
    }

    public async Task RefreshAsync()
    {
        await Request(Op.GetDeviceInfo, [0xFF, 0xFF, 0xFF, 0xFF]);
        await Request(Op.GetDeviceRunInfo, [0xFF, 0xFF, 0xFF, 0xFF]);
        foreach (var c in BudsCodec.InitialConfigs)
            await Request(Op.GetConfig, BudsCodec.GetConfig(c));
    }

    // ------------------------------------------------------------ commands
    public async Task SetModeAsync(SoundMode m)
    {
        await Request(Op.Anc, BudsCodec.SoundModePayload(m));
        State.Mode = m; Emit();
    }

    public async Task SetAncLevelAsync(AncStrength s)
    {
        await Request(Op.SetConfig, BudsCodec.Strength(StrengthTarget.Anc, (byte)s));
        State.AncLevel = s; Emit();
    }

    public async Task SetTransparencyLevelAsync(TransparencyStrength s)
    {
        await Request(Op.SetConfig, BudsCodec.Strength(StrengthTarget.Transparency, (byte)s));
        State.TransparencyLevel = s; Emit();
    }

    public async Task SetEqAsync(EqPreset p)
    {
        await Request(Op.SetConfig, BudsCodec.EqPresetPayload(p));
        State.EqPreset = (int)p; Emit();
    }

    public async Task SetEarDetectionAsync(bool on)
    {
        await Request(Op.Anc, BudsCodec.EarDetection(on));
        State.EarDetection = on; Emit();
    }

    public async Task SetAutoAnswerAsync(bool on)
    {
        await Request(Op.SetConfig, BudsCodec.BoolConfig(Config.AutoAnswer, on));
        State.AutoAnswer = on; Emit();
    }

    public async Task SetDoubleConnectionAsync(bool on)
    {
        await Request(Op.SetConfig, BudsCodec.BoolConfig(Config.DoubleConnection, on));
        State.DoubleConnection = on; Emit();
    }

    public async Task SetAdaptiveAncAsync(bool on)
    {
        await Request(Op.SetConfig, BudsCodec.BoolConfig(Config.AdaptiveAnc, on));
        State.AdaptiveAnc = on; Emit();
    }

    public async Task SetAdaptiveSoundAsync(bool on)
    {
        await Request(Op.SetConfig, BudsCodec.BoolConfig(Config.AdaptiveSound, on));
        State.AdaptiveSound = on; Emit();
    }

    public async Task FindAsync(bool start, FindTarget target)
    {
        await Request(Op.SetConfig, BudsCodec.Find(false, FindTarget.Both));
        if (start) await Request(Op.SetConfig, BudsCodec.Find(true, target));
    }

    // ------------------------------------------------------------ plumbing
    async Task Send(Message m)
    {
        var c = _conn ?? throw new RfcommException("Not connected");
        await c.WriteAsync(m.Encode());
    }

    Task Request(Op op, byte[] payload)
    {
        byte seq = (byte)Interlocked.Increment(ref _seq);
        return Send(new Message(MsgType.PhoneRequest, (byte)op, seq, payload));
    }

    Task Reply(Message req, byte[]? payload = null) =>
        Send(new Message(MsgType.Response, req.Opcode, req.Seq, payload ?? []));

    async Task ReadLoop(RfcommConnection conn, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var data = await conn.ReadAsync(ct);
                foreach (var m in _parser.Feed(data))
                {
                    try { await Handle(m); }
                    catch (Exception e) { Log.Error($"failed handling {m}", e); }
                }
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested) { Log.Warn("read loop ended: " + e.Message); }
        catch { }
        finally
        {
            State.Connected = false;
            State.Authenticated = false;
            Emit();
            Disconnected?.Invoke();
        }
    }

    async Task Handle(Message m)
    {
        var s = State;
        switch ((Op)m.Opcode)
        {
            case Op.AuthChallenge:
                if (!m.IsRequest) await Request(Op.AuthConfirm, [0x01, 0x00]);
                else if (m.Payload.Length >= 17)
                    await Reply(m, [0x01, .. BudsAuth.ComputeResponse(m.Payload[1..17])]);
                break;
            case Op.AuthConfirm:
                if (m.IsRequest)
                {
                    await Reply(m, [0x01]);
                    s.Authenticated = true;
                    _authed.TrySetResult();
                    Emit();
                }
                break;
            case Op.GetDeviceInfo when !m.IsRequest:
                BudsCodec.ApplyDeviceInfo(s, m.Payload); Emit(); break;
            case Op.GetDeviceRunInfo when !m.IsRequest:
                BudsCodec.ApplyRunInfo(s, m.Payload); Emit(); break;
            case Op.ReportStatus:
                BudsCodec.ApplyReportStatus(s, m.Payload);
                if (m.IsRequest) await Reply(m);
                Emit(); break;
            case Op.NotifyConfig:
                BudsCodec.ApplyNotifyConfig(s, m.Payload);
                if (m.IsRequest) await Reply(m);
                Emit(); break;
            case Op.GetConfig when !m.IsRequest:
                Log.Info($"config reply {Convert.ToHexString(m.Payload)}");
                BudsCodec.ApplyGetConfig(s, m.Payload); Emit(); break;
            default:
                if (m.IsRequest) await Reply(m); // ACK unknown requests so the buds don't stall
                break;
        }
    }
}
