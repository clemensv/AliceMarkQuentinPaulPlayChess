#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AliceMarkQuentinPaulPlayChess;

/// <summary>UCI engine wrapper; starts a Dockfish container.</summary>
public sealed class UciEngine : IAsyncDisposable
{
    private readonly int  _port;
    private readonly string? _containerId;
    private readonly ClientWebSocket? _ws;
    private readonly SemaphoreSlim _wsLock = new(1, 1);
    private readonly bool _verbose;
    private readonly CancellationTokenSource _cts = new();
    private static readonly List<string> _activeContainers = new();
    private static readonly object _containerListLock = new();

    private void Log(string message) { if (_verbose) Console.WriteLine(message); }

    private bool IsDockerRunning()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "version --format '{{.Server.Version}}'") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (process == null) return false;
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool IsContainerRunning(string containerId)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", $"ps --filter id={containerId} --format \"{{{{.Status}}}}\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            if (process == null) return false;
            process.WaitForExit(3000);
            return process.ExitCode == 0 && process.StandardOutput.ReadToEnd().Trim().StartsWith("Up");
        }
        catch { return false; }
    }

    public UciEngine(bool verbose = false)
    {
        _verbose = verbose;
        if (!IsDockerRunning()) throw new InvalidOperationException("Docker not running. Start Docker Desktop and try again.");
        
        _port = new Random().Next(9000, 9999);
        _containerId = $"dockfish-{Guid.NewGuid():N}".Substring(0, 16);
        
        using var p = Process.Start(new ProcessStartInfo("docker", $"run -d --name {_containerId} -e PORT={_port} -p {_port}:{_port} ivangabriele/dockfish:15") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
        if (p == null) throw new InvalidOperationException("Failed to start Docker container");
        p.WaitForExit();
        
        if (p.ExitCode != 0) throw new InvalidOperationException($"Docker container failed (exit {p.ExitCode}): {p.StandardError.ReadToEnd()}");
        
        var containerId = p.StandardOutput.ReadToEnd().Trim();
        if (string.IsNullOrEmpty(containerId)) throw new InvalidOperationException("No container ID returned");
        
        lock (_containerListLock) { _activeContainers.Add(_containerId); }
        Thread.Sleep(8000); // Wait for container startup
        
        if (!IsContainerRunning(containerId)) throw new InvalidOperationException($"Container not running: {_containerId}");
        
        _ws = new ClientWebSocket();
        try
        {
            _ws.ConnectAsync(new Uri($"ws://localhost:{_port}/stockfish"), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            try { RunCmd("docker", $"rm -f {_containerId}"); } catch { }
            throw new InvalidOperationException($"WebSocket connection failed: {ex.Message}", ex);
        }
    }

    public async ValueTask SetPositionAsync(string fen, CancellationToken ct)
    {
        if (_ws?.State == WebSocketState.Open)
        {
            await _wsLock.WaitAsync(ct);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "uci:command", payload = $"position fen {fen}" }));
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                await Task.Delay(100, ct);
            }
            finally { _wsLock.Release(); }
        }
    }

    public async ValueTask<string> GetBestMoveAsync(int thinkTimeMs, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) throw new InvalidOperationException("WebSocket not connected");
        
        await _wsLock.WaitAsync(ct);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "uci:command", payload = $"go movetime {thinkTimeMs}" }));
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            
            var buffer = new ArraySegment<byte>(new byte[4096]);
            while (!ct.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Text && buffer.Array != null)
                {
                    var response = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                    try
                    {
                        using var doc = JsonDocument.Parse(response);
                        if (doc.RootElement.TryGetProperty("payload", out var payload))
                        {
                            var payloadStr = payload.GetString() ?? "";
                            if (payloadStr.StartsWith("bestmove ")) return payloadStr.Substring(9).Split(' ')[0];
                        }
                    }
                    catch (JsonException) { }
                }
            }
            throw new InvalidOperationException("Timeout waiting for bestmove");
        }
        finally { _wsLock.Release(); }
    }

    public async ValueTask<string> GetPositionAfterMoveAsync(string fen, string move, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) throw new InvalidOperationException("WebSocket not connected");
        
        await _wsLock.WaitAsync(ct);
        try
        {
            // Set position and make move
            var posBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "uci:command", payload = $"position fen {fen} moves {move}" }));
            await _ws.SendAsync(posBytes, WebSocketMessageType.Text, true, ct);
            await Task.Delay(200, ct);
            
            // Request board display
            var displayBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "uci:command", payload = "d" }));
            await _ws.SendAsync(displayBytes, WebSocketMessageType.Text, true, ct);
            
            var buffer = new ArraySegment<byte>(new byte[4096]);
            while (!ct.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Text && buffer.Array != null)
                {
                    var response = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                    try
                    {
                        using var doc = JsonDocument.Parse(response);
                        if (doc.RootElement.TryGetProperty("payload", out var payload))
                        {
                            var payloadStr = payload.GetString() ?? "";
                            var fenIndex = payloadStr.IndexOf("Fen: ");
                            if (fenIndex >= 0)
                            {
                                var fenStart = fenIndex + 5;
                                var lines = payloadStr.Substring(fenStart).Split('\n');
                                if (lines.Length > 0) return lines[0].Trim();
                            }
                        }
                    }
                    catch (JsonException) { }
                }
            }
            throw new OperationCanceledException("Operation cancelled while waiting for position");
        }
        finally { _wsLock.Release(); }
    }

    private static void RunCmd(string file, string args) => Process.Start(new ProcessStartInfo(file, args) { RedirectStandardError = true, UseShellExecute = false })?.WaitForExit();

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_containerId != null)
        {
            RunCmd("docker", $"rm -f {_containerId}");
            lock (_containerListLock) { _activeContainers.Remove(_containerId); }
        }
        _ws?.Dispose();
        _cts.Dispose();
        _wsLock.Dispose();
    }
    
    public static void CleanupAllContainers()
    {
        lock (_containerListLock)
        {
            foreach (var containerId in _activeContainers.ToList())
            {
                try
                {
                    Process.Start(new ProcessStartInfo("docker", $"rm -f {containerId}") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(3000);
                }
                catch (Exception ex) { Console.WriteLine($"⚠️ Failed to cleanup container {containerId}: {ex.Message}"); }
            }
            _activeContainers.Clear();
        }
    }
}
