using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace XiaomiBuds.Core;

/// <summary>
/// Product photos are Xiaomi's, so they are not shipped in the repository. Each image is looked up in:
///   1. Assets/Devices/&lt;key&gt; next to the exe (optional local copies, git-ignored)
///   2. %LOCALAPPDATA%\XiaomiBuds\images\&lt;key&gt; (download cache)
///   3. Xiaomi's own product-page CDN: downloaded once, trimmed to its visible pixels, cached.
/// </summary>
public static class ImageStore
{
    static readonly string BundledDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Devices");
    static readonly string CacheDir = Path.Combine(Log.Dir, "images");
    static readonly HttpClient Http = CreateClient();
    static readonly ConcurrentDictionary<string, Task<string?>> Pending = new();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("XiaomiBuds/2.0 (Windows)");
        return c;
    }

    public static string BundledPath(string key) => Path.Combine(BundledDir, key);

    /// <summary>Local path of the image, downloading it first if needed. Null if unavailable.</summary>
    public static Task<string?> GetAsync(string key, string? url, ImageProcess process = ImageProcess.Trim)
    {
        var bundled = BundledPath(key);
        if (File.Exists(bundled)) return Task.FromResult<string?>(bundled);
        var cached = Path.Combine(CacheDir, key);
        if (File.Exists(cached)) return Task.FromResult<string?>(cached);
        if (url is null) return Task.FromResult<string?>(null);
        return Pending.GetOrAdd(key, _ => DownloadAsync(url, cached, process));
    }

    static async Task<string?> DownloadAsync(string url, string target, ImageProcess process)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await TrimAndSaveAsync(bytes, target, maxHeight: 600, process);
            Log.Info($"image cached: {target}");
            return target;
        }
        catch (Exception e)
        {
            Log.Warn($"image download failed ({url}): {e.Message}");
            Pending.TryRemove(Path.GetRelativePath(CacheDir, target), out _); // allow a retry later
            return null;
        }
    }

    /// <summary>Crop transparent margins and scale down, so every model's photo sits the same way.</summary>
    static async Task TrimAndSaveAsync(byte[] data, string target, int maxHeight, ImageProcess process)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(data.AsBuffer());
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);
        var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        int w = (int)decoder.OrientedPixelWidth, h = (int)decoder.OrientedPixelHeight;
        if (process is ImageProcess.IsolateBud or ImageProcess.IsolateBudMirrored) IsolateBud(pixels, w, h);
        if (process == ImageProcess.IsolateBudMirrored) Mirror(pixels, w, h);

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (pixels[(y * w + x) * 4 + 3] > 12)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) { minX = 0; minY = 0; maxX = w - 1; maxY = h - 1; }
        int cw = maxX - minX + 1, ch = maxY - minY + 1;
        var crop = new byte[cw * ch * 4];
        for (int y = 0; y < ch; y++)
            System.Buffer.BlockCopy(pixels, ((minY + y) * w + minX) * 4, crop, y * cw * 4, cw * 4);

        double scale = Math.Min(1.0, (double)maxHeight / ch);
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)cw, (uint)ch, 96, 96, crop);
        encoder.BitmapTransform.ScaledWidth = (uint)Math.Max(1, Math.Round(cw * scale));
        encoder.BitmapTransform.ScaledHeight = (uint)Math.Max(1, Math.Round(ch * scale));
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        await encoder.FlushAsync();

        var result = new byte[output.Size];
        output.Seek(0);
        await output.ReadAsync(result.AsBuffer(), (uint)output.Size, InputStreamOptions.None);
        var tmp = target + ".tmp";
        await File.WriteAllBytesAsync(tmp, result);
        File.Move(tmp, target, overwrite: true);
    }

    /// <summary>
    /// Xiaomi's single-bud photos have colorful sound-wave graphics around a white/grey bud.
    /// Keep only low-saturation pixels, cut thin strands (morphological opening), keep the largest blob.
    /// </summary>
    static void IsolateBud(byte[] px, int w, int h)
    {
        int n = w * h;
        var keep = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2], a = px[i * 4 + 3];
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double sat = max == 0 ? 0 : (max - min) / (double)max;
            keep[i] = a > 8 && sat < 0.18;
        }
        keep = Morph(Morph(keep, w, h, 4, erode: true), w, h, 4, erode: false);

        // largest 4-connected component
        var label = new int[n];
        int best = 0, bestSize = 0, cur = 0;
        var stack = new Stack<int>();
        for (int s = 0; s < n; s++)
        {
            if (!keep[s] || label[s] != 0) continue;
            cur++;
            int size = 0;
            stack.Push(s);
            label[s] = cur;
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                size++;
                int x = p % w, y = p / w;
                if (x > 0 && keep[p - 1] && label[p - 1] == 0) { label[p - 1] = cur; stack.Push(p - 1); }
                if (x < w - 1 && keep[p + 1] && label[p + 1] == 0) { label[p + 1] = cur; stack.Push(p + 1); }
                if (y > 0 && keep[p - w] && label[p - w] == 0) { label[p - w] = cur; stack.Push(p - w); }
                if (y < h - 1 && keep[p + w] && label[p + w] == 0) { label[p + w] = cur; stack.Push(p + w); }
            }
            if (size > bestSize) { bestSize = size; best = cur; }
        }
        if (best == 0) return;

        // soft edge: 3x3 average of the mask, never more opaque than the source
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sum = 0, cnt = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        cnt++;
                        if (label[yy * w + xx] == best) sum++;
                    }
                int i = (y * w + x) * 4 + 3;
                px[i] = (byte)Math.Min(px[i], 255 * sum / cnt);
            }
    }

    /// <summary>Square-kernel erosion or dilation, done separably.</summary>
    static bool[] Morph(bool[] m, int w, int h, int r, bool erode)
    {
        var tmp = new bool[m.Length];
        var outp = new bool[m.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool v = erode;
                for (int k = -r; k <= r && v == erode; k++)
                {
                    int xx = Math.Clamp(x + k, 0, w - 1);
                    if (m[y * w + xx] != erode) v = !erode;
                }
                tmp[y * w + x] = v;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool v = erode;
                for (int k = -r; k <= r && v == erode; k++)
                {
                    int yy = Math.Clamp(y + k, 0, h - 1);
                    if (tmp[yy * w + x] != erode) v = !erode;
                }
                outp[y * w + x] = v;
            }
        return outp;
    }

    static void Mirror(byte[] px, int w, int h)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w / 2; x++)
            {
                int a = (y * w + x) * 4, b = (y * w + (w - 1 - x)) * 4;
                for (int c = 0; c < 4; c++) (px[a + c], px[b + c]) = (px[b + c], px[a + c]);
            }
    }
}

public enum ImageProcess { Trim, IsolateBud, IsolateBudMirrored }
