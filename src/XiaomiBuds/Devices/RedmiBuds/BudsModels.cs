using System.Text.RegularExpressions;

namespace XiaomiBuds.Devices.RedmiBuds;

/// <summary>
/// What one earbuds model supports and how it looks. Feature lists come from Gadgetbridge's
/// per-model coordinators (or, for Redmi Buds 5, from testing on real hardware).
/// Models marked <see cref="Experimental"/> speak the same protocol family but aren't documented;
/// their features are detected at runtime from what the buds actually report.
/// </summary>
public sealed record BudsModel
{
    public required string Name { get; init; }
    /// <summary>Case-insensitive regex for the Bluetooth device name.</summary>
    public required string Pattern { get; init; }
    /// <summary>Folder under Assets/Devices.</summary>
    public required string Art { get; init; }
    /// <summary>Official product photo of the case with the buds (Xiaomi CDN; downloaded and cached on first use).</summary>
    public string? HeroUrl { get; init; }
    /// <summary>Official photo of a single (right) bud; cut out at runtime and mirrored for the left bud.</summary>
    public string? BudUrl { get; init; }
    /// <summary>Colors with separate case/left/right images (local, optional). Otherwise the hero photo is used.</summary>
    public string[] Colors { get; init; } = [];
    public bool Experimental { get; init; }

    public SoundMode[] Modes { get; init; } = [];
    public AncStrength[] AncLevels { get; init; } = [];
    public TransparencyStrength[] TransparencyLevels { get; init; } = [];
    public bool AdaptiveAnc { get; init; }
    public EqPreset[] EqPresets { get; init; } = [];
    public bool AdaptiveSound { get; init; }
    public bool EarDetection { get; init; }
    public bool AutoAnswer { get; init; }
    public bool DualConnection { get; init; }
    public bool Find { get; init; }
    public bool FindPerBud { get; init; }

    /// <summary>True when separate per-color case/bud images are present locally.</summary>
    public bool SplitArt => Colors.Length > 0 &&
        File.Exists(Core.ImageStore.BundledPath($"{Art}/case_{Colors[0].ToLowerInvariant()}.png"));
    /// <summary>Separate left/right bud images are available (local per-color set, or a downloadable bud photo).</summary>
    public bool HasBudArt => SplitArt || BudUrl != null;
    public bool HasSettings => EarDetection || AutoAnswer || DualConnection;

    Regex? _regex;
    public bool Matches(string deviceName) =>
        (_regex ??= new Regex(Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).IsMatch(deviceName);

    /// <summary>The features to show: the model's own list, or what the buds reported for experimental models.</summary>
    public BudsModel Resolve(BudsState s)
    {
        if (!Experimental) return this;
        bool strength = s.Reported.Contains(Config.EffectStrength);
        return this with
        {
            Modes = s.Mode is null ? [] : BudsCatalog.AllModes,
            AncLevels = strength ? [AncStrength.Light, AncStrength.Balanced, AncStrength.Deep] : [],
            TransparencyLevels = strength ? [TransparencyStrength.Regular, TransparencyStrength.Voice] : [],
            AdaptiveAnc = s.Reported.Contains(Config.AdaptiveAnc),
            EqPresets = s.Reported.Contains(Config.EqPreset) ? [EqPreset.Standard, EqPreset.Voice, EqPreset.Bass, EqPreset.Treble] : [],
            AdaptiveSound = s.Reported.Contains(Config.AdaptiveSound),
            EarDetection = s.EarDetection is not null,
            AutoAnswer = s.Reported.Contains(Config.AutoAnswer),
            DualConnection = s.Reported.Contains(Config.DoubleConnection),
            Find = s.Connected,
            FindPerBud = false,
        };
    }

    /// <summary>A key that changes whenever the visible feature set changes.</summary>
    public string FeatureKey() => string.Join("|",
        string.Join(",", Modes), string.Join(",", AncLevels), string.Join(",", TransparencyLevels), AdaptiveAnc,
        string.Join(",", EqPresets), AdaptiveSound, EarDetection, AutoAnswer, DualConnection, Find, FindPerBud);
}

public static class BudsCatalog
{
    // display order for the three big buttons
    public static readonly SoundMode[] AllModes = [SoundMode.NoiseCancelling, SoundMode.Off, SoundMode.Transparency];

    static string P(string brand, string model) =>
        $@"\b{brand}\s*buds\s*{model}\b(?!\s*(pro|lite|active|play|se)\b)";

    static readonly BudsModel ProFamily = new()
    {
        Name = "", Pattern = "", Art = "",
        Modes = AllModes,
        AncLevels = [AncStrength.Light, AncStrength.Balanced, AncStrength.Deep],
        TransparencyLevels = [TransparencyStrength.Regular, TransparencyStrength.Voice, TransparencyStrength.Ambient],
        AdaptiveAnc = true,
        EqPresets = [EqPreset.Standard, EqPreset.Voice, EqPreset.Bass, EqPreset.Treble],
        AdaptiveSound = true,
        EarDetection = true, AutoAnswer = true, DualConnection = true,
    };

    public static readonly BudsModel[] Models =
    [
        // ---- documented by Gadgetbridge / tested
        new()
        {
            Name = "Redmi Buds 3 Pro", Pattern = P("redmi", @"3\s*pro"), Art = "RedmiBuds3Pro", HeroUrl = "https://i01.appmifile.com/v1/MI_18455B3E4DA706226CF7535A58E875F0267/pms_1666842127.11379990.png",
            Modes = AllModes,
            AncLevels = [AncStrength.Light, AncStrength.Balanced, AncStrength.Deep, AncStrength.Adaptive],
            TransparencyLevels = [TransparencyStrength.Regular, TransparencyStrength.Voice],
            EqPresets = [EqPreset.Standard, EqPreset.Voice, EqPreset.Bass, EqPreset.Treble],
            EarDetection = true, AutoAnswer = true, DualConnection = true,
        },
        new() { Name = "Redmi Buds 4 Active", Pattern = P("redmi", @"4\s*active"), Art = "RedmiBuds4Active" },
        new()
        {
            Name = "Redmi Buds 5", Pattern = P("redmi", "5"), Art = "RedmiBuds5", HeroUrl = "https://i02.appmifile.com/735_operatorx_operatorx_opx/11/01/2024/34bf6081e4d7666038873f17cd02eb84.png",
            BudUrl = "https://i02.appmifile.com/mi-com-product/fly-birds/redmi-buds-5/PC/03c883aadcd8f0066b344c48db1c0b47.png", Colors = ["Black", "White", "Blue"],
            Modes = AllModes,
            AncLevels = [AncStrength.Light, AncStrength.Balanced, AncStrength.Deep],
            TransparencyLevels = [TransparencyStrength.Regular, TransparencyStrength.Voice],
            EqPresets = [EqPreset.Standard, EqPreset.Voice, EqPreset.Bass, EqPreset.Treble, EqPreset.Volume],
            EarDetection = true, AutoAnswer = true, DualConnection = true, Find = true, FindPerBud = true,
        },
        ProFamily with { Name = "Redmi Buds 5 Pro", Pattern = P("redmi", @"5\s*pro"), Art = "RedmiBuds5Pro", HeroUrl = "https://i02.appmifile.com/413_operatorx_operatorx_opx/11/01/2024/c4dc8c66faa8d0a2f98de1d8657dd8c3.png",
        BudUrl = "https://i02.appmifile.com/mi-com-product/fly-birds/redmi-buds-5-pro/PC/963987dbaafb072c271b4c38b4ed913b.png" },
        ProFamily with { Name = "Redmi Buds 6", Pattern = P("redmi", "6"), Art = "RedmiBuds6", HeroUrl = "https://i02.appmifile.com/679_operatorx_operatorx_opx/10/01/2025/85b0cae1d16fe746900b64c81cd732f1.png" },
        ProFamily with { Name = "Redmi Buds 6 Pro", Pattern = P("redmi", @"6\s*pro"), Art = "RedmiBuds6Pro", HeroUrl = "https://i02.appmifile.com/964_operatorx_operatorx_opx/10/01/2025/659aea3cbf8efea894d759e3a36cb8b1.png" },
        new()
        {
            Name = "Redmi Buds 6 Active", Pattern = P("redmi", @"6\s*active"), Art = "RedmiBuds6Active", HeroUrl = "https://i02.appmifile.com/42_operatorx_operatorx_opx/12/09/2024/7fe8ec3ca79020887596d5776b4efcc6.png",
            EqPresets = [EqPreset.Standard, EqPreset.Voice, EqPreset.Bass, EqPreset.Treble, EqPreset.Volume],
            Find = true, FindPerBud = true,
        },
        new()
        {
            Name = "Redmi Buds 6 Lite", Pattern = P("redmi", @"6\s*lite"), Art = "RedmiBuds6Lite", HeroUrl = "https://i02.appmifile.com/103_operatorx_operatorx_opx/15/08/2024/dc0880f52cfb9fa67d1d000b54c9582e.png",
            Modes = AllModes, AutoAnswer = true, Find = true, FindPerBud = true,
        },
        new()
        {
            Name = "Redmi Buds 8 Active", Pattern = P("redmi", @"8\s*active"), Art = "RedmiBuds8Active", HeroUrl = "https://i02.appmifile.com/710_operatorx_operatorx_xm/25/02/2026/7e7ec0a10c2d5347100893d5959dbcc0.png",
            EqPresets = [EqPreset.Balanced, EqPreset.Voice, EqPreset.Bass, EqPreset.Treble, EqPreset.Volume],
            AdaptiveSound = true, DualConnection = true, Find = true, FindPerBud = true,
        },

        // ---- same protocol family, not documented yet: features detected at runtime
        new() { Name = "Redmi Buds 6 Play", Pattern = P("redmi", @"6\s*play"), Art = "RedmiBuds6Play", HeroUrl = "https://i02.appmifile.com/640_operatorx_operatorx_opx/02/09/2024/2e17e2f72632d75847bdb47d5cee20bc.png", Experimental = true },
        new() { Name = "Redmi Buds 8", Pattern = P("redmi", "8"), Art = "RedmiBuds8", HeroUrl = "https://i02.appmifile.com/452_operatorx_operatorx_opx/15/05/2026/0e8cca8fe9ab0e6c48cb991312f69337.png", Experimental = true },
        new() { Name = "Redmi Buds 8 Pro", Pattern = P("redmi", @"8\s*pro"), Art = "RedmiBuds8Pro", HeroUrl = "https://i02.appmifile.com/254_operatorx_operatorx_opx/04/02/2026/1fbb1560d48118d7dbe5621d6ce39e51.png", Experimental = true },
        new() { Name = "Redmi Buds 8 Lite", Pattern = P("redmi", @"8\s*lite"), Art = "RedmiBuds8Lite", HeroUrl = "https://i02.appmifile.com/249_operatorx_operatorx_xm/05/01/2026/0decc61ecd6fefe2b02f1bc5caa341d7.png", Experimental = true },
        new() { Name = "Xiaomi Buds 3", Pattern = P("xiaomi", "3"), Art = "XiaomiBuds3", HeroUrl = "https://i01.appmifile.com/v1/MI_18455B3E4DA706226CF7535A58E875F0267/pms_1666840980.90454343.png", Experimental = true },
        new() { Name = "Xiaomi Buds 3T Pro", Pattern = P("xiaomi", @"3t\s*pro"), Art = "XiaomiBuds3TPro", HeroUrl = "https://i01.appmifile.com/v1/MI_18455B3E4DA706226CF7535A58E875F0267/pms_1666842327.69247949.png", Experimental = true },
        new() { Name = "Xiaomi Buds 4 Pro", Pattern = P("xiaomi", @"4\s*pro"), Art = "XiaomiBuds4Pro", HeroUrl = "https://i02.appmifile.com/904_operatorx_operatorx_opx/30/11/2023/925c21d5ac010447ff918b61d0d37cbf.png", Experimental = true },
        new() { Name = "Xiaomi Buds 5", Pattern = P("xiaomi", "5"), Art = "XiaomiBuds5", HeroUrl = "https://i02.appmifile.com/468_operatorx_operatorx_opx/26/09/2024/fa1b4874bfa9f80b7c2a2d8bbdd46947.png", Experimental = true },
        new() { Name = "Xiaomi Buds 5 Pro", Pattern = P("xiaomi", @"5\s*pro"), Art = "XiaomiBuds5Pro", HeroUrl = "https://i02.appmifile.com/240_operatorx_operatorx_opx/02/03/2025/535cc294bdd08b430b3fe9f0c52d3875.png", Experimental = true },
    ];

    /// <summary>Any other Redmi/Xiaomi earbuds: try the same protocol, detect features.</summary>
    public static readonly BudsModel Generic = new()
    {
        Name = "Xiaomi earbuds", Pattern = @"\b(redmi|xiaomi)\s*(buds|earbuds)\b", Art = "Generic", Experimental = true,
    };

    public static BudsModel? Match(string deviceName) =>
        Models.FirstOrDefault(m => m.Matches(deviceName)) ?? (Generic.Matches(deviceName) ? Generic : null);
}
