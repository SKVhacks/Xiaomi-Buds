using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace XiaomiBuds.Core;

public sealed class RfcommException(string message) : Exception(message);

/// <summary>Bluetooth Classic RFCOMM client that connects to a service by UUID.</summary>
public sealed class RfcommConnection(ulong address, Guid serviceUuid) : IDisposable
{
    BluetoothDevice? _device;
    RfcommDeviceService? _service;
    StreamSocket? _socket;
    DataReader? _reader;
    DataWriter? _writer;
    readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task ConnectAsync(CancellationToken ct)
    {
        _device = await BluetoothDevice.FromBluetoothAddressAsync(address).AsTask(ct)
                  ?? throw new RfcommException("Device not found. Is it paired?");
        var id = RfcommServiceId.FromUuid(serviceUuid);
        var res = await _device.GetRfcommServicesForIdAsync(id, BluetoothCacheMode.Uncached).AsTask(ct);
        _service = res?.Services.FirstOrDefault();
        if (_service is null)
        {
            res = await _device.GetRfcommServicesForIdAsync(id, BluetoothCacheMode.Cached).AsTask(ct);
            _service = res?.Services.FirstOrDefault();
        }
        if (_service is null)
            throw new RfcommException("Control channel not available. Take the buds out of the case.");

        _socket = new StreamSocket();
        await _socket.ConnectAsync(_service.ConnectionHostName, _service.ConnectionServiceName,
            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(ct);
        _reader = new DataReader(_socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        _writer = new DataWriter(_socket.OutputStream);
    }

    public async Task<byte[]> ReadAsync(CancellationToken ct)
    {
        var r = _reader ?? throw new RfcommException("Not connected");
        uint n = await r.LoadAsync(1024).AsTask(ct);
        if (n == 0) throw new RfcommException("Connection closed by device");
        var buf = new byte[n];
        r.ReadBytes(buf);
        return buf;
    }

    public async Task WriteAsync(byte[] data)
    {
        await _writeLock.WaitAsync();
        try
        {
            var w = _writer ?? throw new RfcommException("Not connected");
            w.WriteBytes(data);
            await w.StoreAsync();
        }
        finally { _writeLock.Release(); }
    }

    public void Dispose()
    {
        foreach (var d in new IDisposable?[] { _reader, _writer, _socket, _service, _device })
            try { d?.Dispose(); } catch { }
        _reader = null; _writer = null; _socket = null; _service = null; _device = null;
    }
}
