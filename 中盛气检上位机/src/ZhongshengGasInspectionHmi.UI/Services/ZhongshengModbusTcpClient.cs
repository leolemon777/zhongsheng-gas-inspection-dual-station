using System.Buffers.Binary;
using System.Net.Sockets;

namespace ZhongshengGasInspectionHmi.UI.Services;

public sealed class ZhongshengModbusTcpClient
{
    private readonly IModbusCommunicationLog _communicationLog;
    private ushort _transactionId;

    public ZhongshengModbusTcpClient()
        : this(NullModbusCommunicationLog.Instance)
    {
    }

    public ZhongshengModbusTcpClient(IModbusCommunicationLog communicationLog)
    {
        _communicationLog = communicationLog;
    }

    public async Task<ushort[]> ReadInputRegistersAsync(
        string host,
        int port,
        byte unitId,
        ushort startAddress,
        ushort quantity,
        bool useRtuOverTcp,
        CancellationToken cancellationToken)
    {
        if (quantity == 0)
        {
            return [];
        }

        var payload = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), quantity);
        var response = await SendAsync(host, port, unitId, 0x04, payload, useRtuOverTcp, cancellationToken);
        if (response.Length < 2 || response[0] != 0x04)
        {
            throw new InvalidOperationException("读取输入寄存器返回数据格式错误。");
        }

        var byteCount = response[1];
        if (byteCount != quantity * 2 || response.Length < 2 + byteCount)
        {
            throw new InvalidOperationException("读取输入寄存器返回长度错误。");
        }

        var values = new ushort[quantity];
        for (var index = 0; index < quantity; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2 + index * 2, 2));
        }

        return values;
    }

    public Task<bool[]> ReadCoilsAsync(
        string host,
        int port,
        byte unitId,
        ushort startAddress,
        ushort quantity,
        bool useRtuOverTcp,
        CancellationToken cancellationToken)
    {
        return ReadBitsAsync(host, port, unitId, 0x01, startAddress, quantity, useRtuOverTcp, cancellationToken);
    }

    public Task<bool[]> ReadDiscreteInputsAsync(
        string host,
        int port,
        byte unitId,
        ushort startAddress,
        ushort quantity,
        bool useRtuOverTcp,
        CancellationToken cancellationToken)
    {
        return ReadBitsAsync(host, port, unitId, 0x02, startAddress, quantity, useRtuOverTcp, cancellationToken);
    }

    public async Task WriteSingleCoilAsync(
        string host,
        int port,
        byte unitId,
        ushort address,
        bool isOn,
        bool useRtuOverTcp,
        CancellationToken cancellationToken)
    {
        var payload = BuildWriteSingleCoilPayload(address, isOn);
        var response = await SendAsync(host, port, unitId, 0x05, payload, useRtuOverTcp, cancellationToken);
        ValidateWriteSingleCoilResponse(response, address, isOn);
    }

    private static byte[] BuildWriteSingleCoilPayload(ushort address, bool isOn)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), address);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), isOn ? (ushort)0xFF00 : (ushort)0x0000);
        return payload;
    }

    private static void ValidateWriteSingleCoilResponse(byte[] response, ushort address, bool isOn)
    {
        if (response.Length != 5 || response[0] != 0x05)
        {
            throw new InvalidOperationException("写单个线圈返回数据格式错误。");
        }

        var returnedAddress = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(1, 2));
        var returnedValue = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(3, 2));
        var expectedValue = isOn ? (ushort)0xFF00 : (ushort)0x0000;
        if (returnedAddress != address || returnedValue != expectedValue)
        {
            throw new InvalidOperationException("写单个线圈返回地址或状态与请求不一致。");
        }
    }

    public async Task WriteSingleCoilForDurationAsync(
        string host,
        int port,
        byte unitId,
        ushort address,
        TimeSpan duration,
        bool useRtuOverTcp,
        CancellationToken cancellationToken)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "线圈保持时间不能小于 0。");
        }

        // 写 ON 后立即断开连接，通电保持期间不再占用 TCP 连接；
        // 两台上位机共用同一 IO 模块时，避免脉冲期间阻塞另一台通信。
        await WriteSingleCoilAsync(host, port, unitId, address, true, useRtuOverTcp, cancellationToken);

        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryWriteCoilOffAsync(host, port, unitId, address, useRtuOverTcp);
            throw;
        }

        await WriteSingleCoilAsync(host, port, unitId, address, false, useRtuOverTcp, cancellationToken);
    }

    private async Task TryWriteCoilOffAsync(
        string host,
        int port,
        byte unitId,
        ushort address,
        bool useRtuOverTcp)
    {
        try
        {
            await WriteSingleCoilAsync(host, port, unitId, address, false, useRtuOverTcp, CancellationToken.None);
        }
        catch
        {
            // 取消场景下尽力关闭线圈，失败忽略（线圈可能保持 ON，由现场复位）。
        }
    }

    private async Task<bool[]> ReadBitsAsync(
        string host,
        int port,
        byte unitId,
        byte functionCode,
        ushort startAddress,
        ushort quantity,
        bool useRtuOverTcp,
        CancellationToken cancellationToken)
    {
        if (quantity == 0)
        {
            return [];
        }

        var payload = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), quantity);
        var response = await SendAsync(host, port, unitId, functionCode, payload, useRtuOverTcp, cancellationToken);
        if (response.Length < 2 || response[0] != functionCode)
        {
            throw new InvalidOperationException($"读位状态返回数据格式错误，功能码 0x{functionCode:X2}。");
        }

        var byteCount = response[1];
        var expectedBytes = (quantity + 7) / 8;
        if (byteCount != expectedBytes || response.Length < 2 + byteCount)
        {
            throw new InvalidOperationException($"读位状态返回长度错误，功能码 0x{functionCode:X2}。");
        }

        var values = new bool[quantity];
        for (var index = 0; index < quantity; index++)
        {
            var dataByte = response[2 + index / 8];
            values[index] = (dataByte & (1 << (index % 8))) != 0;
        }

        return values;
    }

    private Task<byte[]> SendAsync(
        string host,
        int port,
        byte unitId,
        byte functionCode,
        byte[] payload,
        bool useRtuOverTcp,
        CancellationToken cancellationToken)
    {
        return useRtuOverTcp
            ? SendRtuOverTcpAsync(host, port, unitId, functionCode, payload, cancellationToken)
            : SendModbusTcpAsync(host, port, unitId, functionCode, payload, cancellationToken);
    }

    private async Task<byte[]> SendModbusTcpAsync(
        string host,
        int port,
        byte unitId,
        byte functionCode,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(host, port, timeout.Token);

        return await SendModbusTcpFrameAsync(
            client.GetStream(),
            host,
            port,
            unitId,
            functionCode,
            payload,
            timeout.Token);
    }

    private async Task<byte[]> SendModbusTcpFrameAsync(
        NetworkStream stream,
        string host,
        int port,
        byte unitId,
        byte functionCode,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var pduLength = 1 + payload.Length;
        var frame = new byte[7 + pduLength];
        var transactionId = unchecked(++_transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), (ushort)(1 + pduLength));
        frame[6] = unitId;
        frame[7] = functionCode;
        payload.CopyTo(frame.AsSpan(8));

        AddCommunicationLog("TX", "Modbus TCP", host, port, unitId, functionCode, payload, frame);
        await stream.WriteAsync(frame, cancellationToken);

        var header = new byte[7];
        await ReadExactlyAsync(stream, header, cancellationToken);
        var responseTransactionId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
        var protocolId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
        if (responseTransactionId != transactionId)
        {
            throw new InvalidOperationException("Modbus TCP 返回事务 ID 不匹配。");
        }

        if (protocolId != 0)
        {
            throw new InvalidOperationException("Modbus TCP 返回协议 ID 错误。");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        if (length < 2)
        {
            throw new InvalidOperationException("Modbus TCP 返回长度错误。");
        }

        var body = new byte[length - 1];
        await ReadExactlyAsync(stream, body, cancellationToken);
        AddCommunicationLog("RX", "Modbus TCP", host, port, unitId, functionCode, payload, Merge(header, body));
        if (header[6] != unitId)
        {
            throw new InvalidOperationException("Modbus TCP 返回站号不匹配。");
        }

        ValidateFunctionResponse(body[0], body.AsSpan(1), functionCode);
        return body;
    }

    private async Task<byte[]> SendRtuOverTcpAsync(
        string host,
        int port,
        byte unitId,
        byte functionCode,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(host, port, timeout.Token);

        return await SendRtuOverTcpFrameAsync(
            client.GetStream(),
            host,
            port,
            unitId,
            functionCode,
            payload,
            timeout.Token);
    }

    private async Task<byte[]> SendRtuOverTcpFrameAsync(
        NetworkStream stream,
        string host,
        int port,
        byte unitId,
        byte functionCode,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var frameWithoutCrc = new byte[2 + payload.Length];
        frameWithoutCrc[0] = unitId;
        frameWithoutCrc[1] = functionCode;
        payload.CopyTo(frameWithoutCrc.AsSpan(2));
        var frame = AppendCrc(frameWithoutCrc);

        AddCommunicationLog("TX", "RTU over TCP", host, port, unitId, functionCode, payload, frame);
        await stream.WriteAsync(frame, cancellationToken);

        var header = new byte[2];
        await ReadExactlyAsync(stream, header, cancellationToken);
        if (header[0] != unitId)
        {
            throw new InvalidOperationException("RTU over TCP 返回站号不匹配。");
        }

        if ((header[1] & 0x80) != 0)
        {
            var exceptionTail = new byte[3];
            await ReadExactlyAsync(stream, exceptionTail, cancellationToken);
            var exceptionFrame = Merge(header, exceptionTail);
            AddCommunicationLog("RX", "RTU over TCP", host, port, unitId, functionCode, payload, exceptionFrame);
            ValidateCrc(exceptionFrame);
            ValidateFunctionResponse(header[1], exceptionTail.AsSpan(0, 1), functionCode);
        }

        byte[] tail = functionCode switch
        {
            0x01 or 0x02 or 0x04 => await ReadCountedResponseTailAsync(stream, cancellationToken),
            0x05 => await ReadFixedResponseTailAsync(stream, 6, cancellationToken),
            _ => throw new NotSupportedException($"暂未实现功能码 0x{functionCode:X2} 的 RTU over TCP 响应解析。")
        };

        var responseFrame = Merge(header, tail);
        AddCommunicationLog("RX", "RTU over TCP", host, port, unitId, functionCode, payload, responseFrame);
        ValidateCrc(responseFrame);
        return responseFrame[1..^2];
    }

    private static async Task<byte[]> ReadCountedResponseTailAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var byteCountBuffer = new byte[1];
        await ReadExactlyAsync(stream, byteCountBuffer, cancellationToken);
        var dataAndCrc = new byte[byteCountBuffer[0] + 2];
        await ReadExactlyAsync(stream, dataAndCrc, cancellationToken);
        return Merge(byteCountBuffer, dataAndCrc);
    }

    private static async Task<byte[]> ReadFixedResponseTailAsync(
        NetworkStream stream,
        int length,
        CancellationToken cancellationToken)
    {
        var tail = new byte[length];
        await ReadExactlyAsync(stream, tail, cancellationToken);
        return tail;
    }

    private static void ValidateFunctionResponse(byte responseFunction, ReadOnlySpan<byte> data, byte requestFunction)
    {
        if ((responseFunction & 0x80) == 0)
        {
            return;
        }

        var exceptionCode = data.Length > 0 ? data[0] : (byte)0;
        throw new InvalidOperationException($"Modbus 异常响应：功能码 0x{requestFunction:X2}，异常码 0x{exceptionCode:X2}。");
    }

    private static byte[] AppendCrc(byte[] frameWithoutCrc)
    {
        var crc = CalculateCrc16(frameWithoutCrc);
        var frame = new byte[frameWithoutCrc.Length + 2];
        frameWithoutCrc.CopyTo(frame.AsSpan());
        frame[^2] = (byte)(crc & 0xFF);
        frame[^1] = (byte)(crc >> 8);
        return frame;
    }

    private static void ValidateCrc(byte[] frame)
    {
        if (frame.Length < 4)
        {
            throw new InvalidOperationException("RTU over TCP 返回帧长度不足，无法校验 CRC。");
        }

        var expected = CalculateCrc16(frame.AsSpan(0, frame.Length - 2));
        var actual = (ushort)(frame[^2] | (frame[^1] << 8));
        if (expected != actual)
        {
            throw new InvalidOperationException("RTU over TCP 返回 CRC16 校验失败。");
        }
    }

    private static ushort CalculateCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var current in data)
        {
            crc ^= current;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x0001) != 0
                    ? (ushort)((crc >> 1) ^ 0xA001)
                    : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new InvalidOperationException("Modbus TCP 连接已断开。");
            }

            offset += read;
        }
    }

    private static byte[] Merge(byte[] first, byte[] second)
    {
        var merged = new byte[first.Length + second.Length];
        first.CopyTo(merged.AsSpan());
        second.CopyTo(merged.AsSpan(first.Length));
        return merged;
    }

    private void AddCommunicationLog(
        string direction,
        string transport,
        string host,
        int port,
        byte unitId,
        byte functionCode,
        byte[] requestPayload,
        byte[] frame)
    {
        _communicationLog.Add(new ModbusCommunicationLogEntry(
            DateTimeOffset.Now,
            direction,
            transport,
            $"{host}:{port} / ID {unitId}",
            $"0x{functionCode:X2} {GetFunctionName(functionCode)}",
            GetAddressText(functionCode, requestPayload),
            GetDetail(functionCode, requestPayload),
            ToHex(frame)));
    }

    private static string GetFunctionName(byte functionCode)
    {
        return functionCode switch
        {
            0x01 => "读DO线圈",
            0x02 => "读DI输入",
            0x04 => "读AI输入寄存器",
            0x05 => "写DO线圈",
            _ => "未知功能"
        };
    }

    private static string GetAddressText(byte functionCode, byte[] payload)
    {
        if (payload.Length < 2)
        {
            return "--";
        }

        var address = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(0, 2));
        return functionCode switch
        {
            0x01 or 0x02 or 0x04 or 0x05 => $"{address:X4}H",
            _ => "--"
        };
    }

    private static string GetDetail(byte functionCode, byte[] payload)
    {
        if (payload.Length < 4)
        {
            return "--";
        }

        var address = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(0, 2));
        var value = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(2, 2));
        return functionCode switch
        {
            0x01 => $"DO 起始 {address:X4}H，数量 {value}",
            0x02 => $"DI 起始 {address:X4}H，数量 {value}",
            0x04 => $"AI 起始 {address:X4}H，数量 {value}",
            0x05 => $"DO 地址 {address:X4}H，写入 {(value == 0xFF00 ? "ON FF00" : "OFF 0000")}",
            _ => "--"
        };
    }

    private static string ToHex(byte[] frame)
    {
        return string.Join(" ", frame.Select(value => value.ToString("X2")));
    }
}
