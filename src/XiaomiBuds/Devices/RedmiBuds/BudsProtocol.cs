namespace XiaomiBuds.Devices.RedmiBuds;

// Redmi Buds wire protocol (ported from Gadgetbridge, AGPL-3.0-or-later).
// request : FE DC BA | type | opcode | len(BE16) | seq | payload | EF        (len = 1 + payload)
// response: FE DC BA | type | opcode | len(BE16) | 00 | seq | payload | EF   (len = 2 + payload)

public static class MsgType
{
    public const byte PhoneRequest = 0xC4;
    public const byte Response = 0x04;
    public static bool IsRequest(byte t) => (t & 0x40) != 0;
}

public enum Op : byte
{
    GetDeviceInfo = 0x02, Anc = 0x08, GetDeviceRunInfo = 0x09, ReportStatus = 0x0E,
    AuthChallenge = 0x50, AuthConfirm = 0x51, SetConfig = 0xF2, GetConfig = 0xF3, NotifyConfig = 0xF4,
}

public enum Config : byte
{
    AutoAnswer = 0x03, DoubleConnection = 0x04, EarDetection = 0x06, EqPreset = 0x07,
    FindEarbuds = 0x09, EffectStrength = 0x0B, AdaptiveAnc = 0x25, AdaptiveSound = 0x29, EqCurve = 0x37,
}

public enum SoundMode : byte { Off = 0, NoiseCancelling = 1, Transparency = 2 }
public enum StrengthTarget : byte { Anc = 1, Transparency = 2 }
public enum AncStrength : byte { Balanced = 0, Light = 1, Deep = 2, Adaptive = 3 }
public enum TransparencyStrength : byte { Regular = 0, Voice = 1, Ambient = 2 }
public enum EqPreset : byte { Standard = 0, Voice = 1, Bass = 5, Treble = 6, Volume = 7, Balanced = 0x15 }
public enum FindTarget : byte { Left = 1, Right = 2, Both = 3 }

public sealed record Message(byte Type, byte Opcode, byte Seq, byte[] Payload)
{
    public bool IsRequest => MsgType.IsRequest(Type);

    public byte[] Encode()
    {
        int extra = IsRequest ? 1 : 2;
        int len = Payload.Length + extra;
        var o = new List<byte>(Payload.Length + 10) { 0xFE, 0xDC, 0xBA, Type, Opcode, (byte)(len >> 8), (byte)len };
        if (!IsRequest) o.Add(0x00);
        o.Add(Seq);
        o.AddRange(Payload);
        o.Add(0xEF);
        return o.ToArray();
    }

    public override string ToString()
    {
        string op = Enum.IsDefined(typeof(Op), Opcode) ? ((Op)Opcode).ToString() : $"0x{Opcode:X2}";
        return $"<{Type:X2} {op} seq={Seq} {Convert.ToHexString(Payload)}>";
    }
}

/// <summary>Reassembles frames from the RFCOMM byte stream using the length field.</summary>
public sealed class FrameParser
{
    readonly List<byte> _buf = new();

    public List<Message> Feed(byte[] data)
    {
        _buf.AddRange(data);
        var msgs = new List<Message>();
        while (true)
        {
            int start = FindHeader();
            if (start < 0)
            {
                if (_buf.Count > 2) _buf.RemoveRange(0, _buf.Count - 2);
                return msgs;
            }
            if (start > 0) _buf.RemoveRange(0, start);
            if (_buf.Count < 7) return msgs;
            int length = (_buf[5] << 8) | _buf[6];
            int total = 7 + length + 1;
            if (_buf.Count < total) return msgs;
            var f = _buf.GetRange(0, total).ToArray();
            _buf.RemoveRange(0, total);
            if (f[^1] != 0xEF) continue;
            byte t = f[3], op = f[4];
            if (MsgType.IsRequest(t))
            {
                if (total < 9) continue;
                msgs.Add(new Message(t, op, f[7], f[8..^1]));
            }
            else
            {
                if (total < 10) continue;
                msgs.Add(new Message(t, op, f[8], f[9..^1]));
            }
        }
    }

    int FindHeader()
    {
        for (int i = 0; i + 2 < _buf.Count; i++)
            if (_buf[i] == 0xFE && _buf[i + 1] == 0xDC && _buf[i + 2] == 0xBA) return i;
        return -1;
    }
}

public readonly record struct Battery(int? Level, bool Charging)
{
    public static Battery From(byte b) => b == 0xFF ? new(null, false) : new(b & 0x7F, (b & 0x80) != 0);
    public bool Known => Level.HasValue;
    public override string ToString() => Level is int l ? $"{l}%" : "—";
}

public sealed class BudsState
{
    public bool Connected;
    public bool Authenticated;
    public string Firmware = "";
    public string Hardware = "";
    public Battery Left, Right, Case;
    public SoundMode? Mode;
    public AncStrength? AncLevel;
    public TransparencyStrength? TransparencyLevel;
    public int? EqPreset;
    public bool? EarDetection, AutoAnswer, DoubleConnection, AdaptiveAnc, AdaptiveSound;
    /// <summary>Configs the buds answered with data (unsupported ones come back empty).</summary>
    public HashSet<Config> Reported = new();

    public BudsState Clone()
    {
        var c = (BudsState)MemberwiseClone();
        c.Reported = new HashSet<Config>(Reported);
        return c;
    }
}

public static class BudsCodec
{
    public static IEnumerable<(byte Index, byte[] Data)> Tlv(byte[] p, int indexOffset = 1)
    {
        int i = 0;
        while (i < p.Length)
        {
            int ln = p[i];
            if (ln == 0 || i + 1 + ln > p.Length) yield break;
            var block = p[(i + 1)..(i + 1 + ln)];
            if (block.Length >= indexOffset)
                yield return (block[indexOffset - 1], block[indexOffset..]);
            i += ln + 1;
        }
    }

    static void Batteries(BudsState s, byte[] d)
    {
        if (d.Length < 3) return;
        s.Left = Battery.From(d[0]);
        s.Right = Battery.From(d[1]);
        s.Case = Battery.From(d[2]);
    }

    static T? E<T>(byte v) where T : struct, Enum => Enum.IsDefined(typeof(T), v) ? (T)Enum.ToObject(typeof(T), v) : null;

    public static void ApplyDeviceInfo(BudsState s, byte[] p)
    {
        foreach (var (idx, d) in Tlv(p))
        {
            if (idx == 0x01 && d.Length >= 4) s.Firmware = $"{d[0] >> 4}.{d[0] & 0xF}.{d[1] >> 4}.{d[1] & 0xF}";
            else if (idx == 0x03 && d.Length >= 4) s.Hardware = $"VID {d[0]:X2}{d[1]:X2} · PID {d[2]:X2}{d[3]:X2}";
            else if (idx == 0x07) Batteries(s, d);
        }
    }

    public static void ApplyRunInfo(BudsState s, byte[] p)
    {
        foreach (var (idx, d) in Tlv(p))
        {
            if (d.Length == 0) continue;
            if (idx == 0x09) s.Mode = E<SoundMode>(d[0]);
            else if (idx == 0x0A) s.EarDetection = d[0] == 0x00;
        }
    }

    public static void ApplyReportStatus(BudsState s, byte[] p)
    {
        foreach (var (idx, d) in Tlv(p))
        {
            if (idx == 0x00) Batteries(s, d);
            else if (idx == 0x04 && d.Length > 0) s.Mode = E<SoundMode>(d[0]);
        }
    }

    public static void ApplyNotifyConfig(BudsState s, byte[] p)
    {
        foreach (var (idx, d) in Tlv(p, 2))
        {
            if (idx == 0x0B && d.Length >= 2)
            {
                s.Mode = E<SoundMode>(d[0]);
                if (s.Mode == SoundMode.NoiseCancelling) s.AncLevel = E<AncStrength>(d[1]);
                else if (s.Mode == SoundMode.Transparency) s.TransparencyLevel = E<TransparencyStrength>(d[1]);
            }
        }
    }

    public static void ApplyGetConfig(BudsState s, byte[] p)
    {
        if (p.Length < 4) return;
        var cfg = (Config)p[2];
        byte v = p[3];
        s.Reported.Add(cfg);
        switch (cfg)
        {
            case Config.EqPreset: s.EqPreset = v; break;
            case Config.EffectStrength when p.Length >= 5:
                if (v == (byte)StrengthTarget.Anc) s.AncLevel = E<AncStrength>(p[4]);
                else if (v == (byte)StrengthTarget.Transparency) s.TransparencyLevel = E<TransparencyStrength>(p[4]);
                break;
            case Config.AutoAnswer: s.AutoAnswer = v == 1; break;
            case Config.DoubleConnection: s.DoubleConnection = v == 1; break;
            case Config.AdaptiveAnc: s.AdaptiveAnc = v == 1; break;
            case Config.AdaptiveSound: s.AdaptiveSound = v == 1; break;
        }
    }

    // ------------------------------------------------------------ payload builders
    public static byte[] AuthChallenge(byte[] challenge) => [0x01, .. challenge];
    public static byte[] SoundModePayload(SoundMode m) => [0x02, 0x04, (byte)m];
    public static byte[] Strength(StrengthTarget t, byte v) => [0x04, 0x00, (byte)Config.EffectStrength, (byte)t, v];
    public static byte[] EqPresetPayload(EqPreset p) => [0x03, 0x00, (byte)Config.EqPreset, (byte)p];
    public static byte[] BoolConfig(Config c, bool on) => [0x03, 0x00, (byte)c, (byte)(on ? 1 : 0)];
    public static byte[] EarDetection(bool on) => [0x02, 0x06, (byte)(on ? 0 : 1)];
    public static byte[] Find(bool start, FindTarget t) => [0x04, 0x00, (byte)Config.FindEarbuds, (byte)(start ? 1 : 0), (byte)t];
    public static byte[] GetConfig(Config c) => [0x00, (byte)c];

    public static readonly Config[] InitialConfigs =
    [
        Config.EffectStrength, Config.AdaptiveAnc, Config.EarDetection, Config.DoubleConnection,
        Config.AutoAnswer, Config.AdaptiveSound, Config.EqPreset, Config.EqCurve,
    ];
}
