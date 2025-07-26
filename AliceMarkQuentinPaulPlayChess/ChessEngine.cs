// encoding: utf-8
// -----------------------------------------------------------------------------
//  Copyright (c) 2025 Clemens
//  All rights reserved.
//
//  **Chess Engine Integration (Docker Stockfish)**
//
//  – Stockfish‑in‑Docker engine: `ivangabriele/dockfish:15` started on a
//    random free port and talked to over WebSocket `/stockfish` endpoint.
//  – Handles all UCI protocol communication and move calculations.
//  – Manages Docker container lifecycle automatically.
// -----------------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AliceMarkQuentinPaulPlayChess;

/// <summary>UCI engine wrapper; starts a Dockfish container when <c>docker==true</c>.</summary>
public sealed class UciEngine : IAsyncDisposable
{
    private readonly int  _port;
    private readonly string? _containerId;
    private readonly ClientWebSocket? _ws;
    private readonly SemaphoreSlim _wsLock = new(1, 1);
    private readonly bool _verbose;
    private Task? _receiveTask;
    private readonly CancellationTokenSource _cts = new();
    private static readonly List<string> _activeContainers = new();
    private static readonly object _containerListLock = new();

    private void Log(string message)
    {
        if (_verbose)
        {
            Console.WriteLine(message);
        }
    }

    private bool IsDockerRunning()
    {
        try
        {
            Log("🔍 Checking if Docker is running...");
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "version --format '{{.Server.Version}}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Log("❌ Failed to start docker command");
                return false;
            }
            
            process.WaitForExit(5000); // 5 second timeout
            
            if (process.ExitCode == 0)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                Log($"✅ Docker is running (Server version: {output})");
                return true;
            }
            else
            {
                var error = process.StandardError.ReadToEnd();
                Log($"❌ Docker command failed (exit code {process.ExitCode}): {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Error checking Docker status: {ex.Message}");
            return false;
        }
    }

    private static bool IsContainerRunning(string containerId)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"ps --filter id={containerId} --format \"{{{{.Status}}}}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null) return false;
            
            process.WaitForExit(3000);
            
            if (process.ExitCode == 0)
            {
                var status = process.StandardOutput.ReadToEnd().Trim();
                Console.WriteLine($"🔍 Container status: '{status}'");
                return status.StartsWith("Up");
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error checking container status: {ex.Message}");
            return false;
        }
    }

    public UciEngine(bool verbose = false)
    {
        _verbose = verbose;
        
        // Check if Docker is running before attempting to start container
        if (!IsDockerRunning())
        {
            throw new InvalidOperationException(
                "❌ Docker is not running or not installed. Please start Docker Desktop and try again.\n" +
                "   You can verify Docker is running by executing: docker version");
        }

        _port = new Random().Next(9000, 9999);
        _containerId = $"dockfish-{Guid.NewGuid():N}".Substring(0, 16);

        Log($"🐳 Starting Docker engine on port {_port}...");
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"run -d --name {_containerId} -e PORT={_port} -p {_port}:{_port} ivangabriele/dockfish:15",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var p = Process.Start(startInfo);
        if (p == null)
        {
            throw new InvalidOperationException("❌ Failed to start Docker container process");
        }
        
        p.WaitForExit();
        
        if (p.ExitCode != 0)
        {
            var error = p.StandardError.ReadToEnd();
            var output = p.StandardOutput.ReadToEnd();
            throw new InvalidOperationException(
                $"❌ Docker container failed to start (exit code {p.ExitCode}):\n" +
                $"   Error: {error}\n" +
                $"   Output: {output}\n" +
                $"   Possible causes:\n" +
                $"   • Docker image 'ivangabriele/dockfish:15' not found (try: docker pull ivangabriele/dockfish:15)\n" +
                $"   • Port {_port} already in use\n" +
                $"   • Container name '{_containerId}' already exists");
        }
        
        var containerId = p.StandardOutput.ReadToEnd().Trim();
        if (string.IsNullOrEmpty(containerId))
        {
            throw new InvalidOperationException("❌ Docker container started but no container ID returned");
        }
        
        Log($"✅ Docker container started with ID: {containerId[..12]}...");
        
        // Track this container for cleanup
        lock (_containerListLock)
        {
            _activeContainers.Add(_containerId);
        }
        
        // Wait longer for container to be ready (proven timing from test)
        Log("⏳ Waiting 8 seconds for container to start...");
        Thread.Sleep(8000);

        // Verify container is still running
        if (!IsContainerRunning(containerId))
        {
            throw new InvalidOperationException(
                $"❌ Docker container {containerId[..12]}... is not running\n" +
                $"   Check container logs: docker logs {_containerId}");
        }
        
        Log($"✅ Container {containerId[..12]}... is running");

        _ws = new ClientWebSocket();
        try
        {
            Log($"🔌 Connecting to WebSocket at ws://localhost:{_port}/stockfish...");
            _ws.ConnectAsync(new Uri($"ws://localhost:{_port}/stockfish"), CancellationToken.None).GetAwaiter().GetResult();
            Log($"✅ Connected to WebSocket at localhost:{_port}");
            
            // TEMPORARILY DISABLE ReceiveLoop to avoid race condition with direct WebSocket calls
            // Start receive loop
            // _receiveTask = Task.Run(ReceiveLoop);
            Log($"🔧 ReceiveLoop DISABLED to avoid race conditions");
        }
        catch (Exception ex)
        {
            Log($"❌ Failed to connect to WebSocket at ws://localhost:{_port}/stockfish");
            Log($"   Error: {ex.Message}");
            Log($"   Possible causes:");
            Log($"   • Container not fully started yet (may need longer wait time)");
            Log($"   • Port {_port} not accessible");
            Log($"   • Stockfish service not running in container");
            Log($"   Try checking container status: docker logs {_containerId}");
            
            // Clean up the failed container
            try
            {
                RunCmd("docker", $"rm -f {_containerId}");
            }
            catch { }
            
            throw new InvalidOperationException($"Failed to connect to Docker Stockfish engine: {ex.Message}", ex);
        }
    }

    private async Task ReceiveLoop()
    {
        Log($"🔧 ReceiveLoop STARTING");
        var buffer = new ArraySegment<byte>(new byte[4096]);
        while (!_cts.Token.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                Log($"🔧 ReceiveLoop: About to call ReceiveAsync at {DateTime.Now:HH:mm:ss.fff}");
                var result = await _ws.ReceiveAsync(buffer, _cts.Token);
                Log($"🔧 ReceiveLoop: ReceiveAsync returned - Type: {result.MessageType}, Count: {result.Count}");
                
                if (result.MessageType == WebSocketMessageType.Text && buffer.Array != null)
                {
                    var json = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                    Log($"📨 Engine response (ReceiveLoop): {json}");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ WebSocket receive error in ReceiveLoop: {ex.Message}");
                break;
            }
        }
        Log($"🔧 ReceiveLoop EXITING - Cancelled: {_cts.Token.IsCancellationRequested}, WebSocket State: {_ws?.State}");
    }

    public async ValueTask SetPositionAsync(string fen, CancellationToken ct)
    {
        if (_ws is { State: WebSocketState.Open })
        {
            await _wsLock.WaitAsync(ct);
            try
            {
                var msg = new
                {
                    type = "uci:command",
                    payload = $"position fen {fen}"
                };
                var json = JsonSerializer.Serialize(msg);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                Log($"📤 Set position: {fen}");
                
                // Give engine time to process
                await Task.Delay(100, ct);
            }
            finally
            {
                _wsLock.Release();
            }
        }
    }

    public async ValueTask<string> GetBestMoveAsync(int thinkTimeMs, CancellationToken ct)
    {
        Log($"🔧 GetBestMoveAsync ENTRY: thinkTimeMs={thinkTimeMs}");
        
        if (_ws?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket not connected to engine");
        }

        Log($"🔧 GetBestMoveAsync: Waiting for lock...");
        await _wsLock.WaitAsync(ct);
        Log($"🔧 GetBestMoveAsync: Lock acquired");
        
        try
        {
            // Send go command
            Log($"🔧 GetBestMoveAsync: Sending go command");
            var goMsg = new
            {
                type = "uci:command",
                payload = $"go movetime {thinkTimeMs}"
            };
            var json = JsonSerializer.Serialize(goMsg);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            Log($"📤 Requested best move with {thinkTimeMs}ms think time");
            
            // Wait for response - proven parsing logic from test
            var buffer = new ArraySegment<byte>(new byte[4096]);
            var startTime = DateTime.UtcNow;
            
            Log($"🔧 GetBestMoveAsync: Starting to wait for response at {startTime:HH:mm:ss.fff}");
            
            while (!ct.IsCancellationRequested)
            {
                Log($"🔧 GetBestMoveAsync: Calling ReceiveAsync at {DateTime.UtcNow:HH:mm:ss.fff}");
                var result = await _ws.ReceiveAsync(buffer, ct);
                Log($"🔧 GetBestMoveAsync: ReceiveAsync returned - Type: {result.MessageType}, Count: {result.Count}");
                
                if (result.MessageType == WebSocketMessageType.Text && buffer.Array != null)
                {
                    var response = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                    Log($"📨 Engine response (GetBestMoveAsync): {response}");
                    
                    try
                    {
                        using var doc = JsonDocument.Parse(response);
                        if (doc.RootElement.TryGetProperty("payload", out var payload))
                        {
                            var payloadStr = payload.GetString() ?? "";
                            
                            if (payloadStr.StartsWith("bestmove "))
                            {
                                var move = payloadStr.Substring(9).Split(' ')[0];
                                Log($"🎯 Best move found: {move}");
                                Log($"🔧 GetBestMoveAsync SUCCESS EXIT");
                                return move;
                            }
                            else
                            {
                                Log($"🔧 GetBestMoveAsync: Not a bestmove, continuing...");
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Log($"⚠️ JSON parse error in GetBestMoveAsync: {ex.Message}");
                    }
                }
                
                var elapsed = DateTime.UtcNow - startTime;
                Log($"🔧 GetBestMoveAsync: Still waiting... Elapsed: {elapsed.TotalSeconds:F1}s");
            }
            
            throw new InvalidOperationException("Timeout waiting for bestmove from engine");
        }
        catch (Exception ex)
        {
            Log($"❌ Error getting move: {ex.Message}");
            throw;
        }
        finally
        {
            Log($"🔧 GetBestMoveAsync: Releasing lock");
            _wsLock.Release();
        }
    }

    public async ValueTask<string> GetPositionAfterMoveAsync(string fen, string move, CancellationToken ct)
    {
        Log($"🔧 GetPositionAfterMoveAsync ENTRY: fen={fen}, move={move}");
        
        if (_ws?.State != WebSocketState.Open)
        {
            Log($"❌ WebSocket not open. State: {_ws?.State}");
            throw new InvalidOperationException("WebSocket not connected to engine");
        }

        Log($"🔧 WebSocket is open, waiting for lock...");
        await _wsLock.WaitAsync(ct);
        Log($"🔧 Lock acquired, proceeding with position command...");
        
        try
        {
            // Set position and make the move
            Log($"🔧 Sending position command: position fen {fen} moves {move}");
            var posMsg = new
            {
                type = "uci:command",
                payload = $"position fen {fen} moves {move}"
            };
            var posJson = JsonSerializer.Serialize(posMsg);
            await _ws.SendAsync(Encoding.UTF8.GetBytes(posJson), WebSocketMessageType.Text, true, ct);
            
            Log($"🔧 Position command sent, waiting 200ms...");
            await Task.Delay(200, ct); // Proven timing from test
            
            // Request board display to get current FEN
            Log($"🔧 Sending display command: d");
            var displayMsg = new
            {
                type = "uci:command", 
                payload = "d"
            };
            var displayJson = JsonSerializer.Serialize(displayMsg);
            await _ws.SendAsync(Encoding.UTF8.GetBytes(displayJson), WebSocketMessageType.Text, true, ct);
            Log($"🔧 Display command sent, waiting for response...");
            
            // Wait for response - NO TIMEOUT, will hang here if there's an issue
            var buffer = new ArraySegment<byte>(new byte[4096]);
            var startTime = DateTime.UtcNow;
            
            Log($"🔧 Starting to wait for WebSocket response at {startTime:HH:mm:ss.fff}");
            
            while (!ct.IsCancellationRequested)
            {
                Log($"🔧 Calling _ws.ReceiveAsync... Time: {DateTime.UtcNow:HH:mm:ss.fff}");
                var result = await _ws.ReceiveAsync(buffer, ct);
                Log($"🔧 ReceiveAsync returned. MessageType: {result.MessageType}, Count: {result.Count}");
                
                if (result.MessageType == WebSocketMessageType.Text && buffer.Array != null)
                {
                    var response = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                    Log($"🔧 Received response: {response}");
                    
                    try
                    {
                        using var doc = JsonDocument.Parse(response);
                        if (doc.RootElement.TryGetProperty("payload", out var payload))
                        {
                            var payloadStr = payload.GetString() ?? "";
                            Log($"🔧 Payload: {payloadStr}");
                            
                            // Extract FEN from response (format: "Fen: rnbqkbnr/...")
                            var fenIndex = payloadStr.IndexOf("Fen: ");
                            if (fenIndex >= 0)
                            {
                                Log($"🔧 Found 'Fen: ' at index {fenIndex}");
                                var fenStart = fenIndex + 5;
                                var lines = payloadStr.Substring(fenStart).Split('\n');
                                if (lines.Length > 0)
                                {
                                    var extractedFen = lines[0].Trim();
                                    Log($"📋 Extracted FEN: {extractedFen}");
                                    Log($"🔧 GetPositionAfterMoveAsync SUCCESS EXIT");
                                    return extractedFen;
                                }
                            }
                            else
                            {
                                Log($"🔧 No 'Fen: ' found in payload, continuing to wait...");
                            }
                        }
                        else
                        {
                            Log($"🔧 No 'payload' property found, continuing to wait...");
                        }
                    }
                    catch (JsonException ex)
                    {
                        Log($"⚠️ JSON parse error: {ex.Message}, continuing to wait...");
                    }
                }
                else
                {
                    Log($"🔧 Received non-text message or null buffer, continuing to wait...");
                }
                
                var elapsed = DateTime.UtcNow - startTime;
                Log($"🔧 Still waiting for FEN response... Elapsed: {elapsed.TotalSeconds:F1}s");
            }
            
            Log($"🔧 CancellationToken was cancelled");
            throw new OperationCanceledException("Operation was cancelled while waiting for position");
        }
        catch (Exception ex)
        {
            Log($"❌ Error in GetPositionAfterMoveAsync: {ex.Message}");
            Log($"❌ Exception type: {ex.GetType().Name}");
            Log($"❌ Stack trace: {ex.StackTrace}");
            throw;
        }
        finally
        {
            Log($"🔧 Releasing lock in GetPositionAfterMoveAsync");
            _wsLock.Release();
        }
    }

    private static void RunCmd(string file, string args)
    {
        var p = Process.Start(new ProcessStartInfo(file, args) 
        { 
            RedirectStandardError = true,
            UseShellExecute = false
        });
        p?.WaitForExit();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        
        if (_receiveTask != null)
        {
            try { await _receiveTask; } catch { }
        }
        
        if (_containerId != null)
        {
            Log($"🧹 Stopping container {_containerId}");
            RunCmd("docker", $"rm -f {_containerId}");
            
            // Remove from active containers list
            lock (_containerListLock)
            {
                _activeContainers.Remove(_containerId);
            }
        }
        
        _ws?.Dispose();
        _cts.Dispose();
        _wsLock.Dispose();
    }
    
    /// <summary>
    /// Static method to cleanup all active Docker containers created by this class.
    /// Used as a safety net in exit handlers.
    /// </summary>
    public static void CleanupAllContainers()
    {
        lock (_containerListLock)
        {
            foreach (var containerId in _activeContainers.ToList())
            {
                try
                {
                    Console.WriteLine($"🧹 Emergency cleanup of container: {containerId}");
                    var process = Process.Start(new ProcessStartInfo("docker", $"rm -f {containerId}")
                    {
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    process?.WaitForExit(3000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to cleanup container {containerId}: {ex.Message}");
                }
            }
            _activeContainers.Clear();
        }
    }
}
