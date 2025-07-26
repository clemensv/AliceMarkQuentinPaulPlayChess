// encoding: utf-8
// -----------------------------------------------------------------------------
//  Copyright (c) 2025 Clemens
//  All rights reserved.
//
//  **ChessAgent - Peer‑to‑peer AMQP 1.0 chess demonstration (AmqpNetLite 2.6)**
//
//  🎯 **AMQP Features Demonstrated:**
//  – Bidirectional peer‑to‑peer messaging without a             Log($"🔍 Getting move {_moveCount} from position: {_fen}");
//  – Each peer creates **one** outbound `SenderLink` and accepts incoming connections via `ILinkProcessor`
//  – Proper AMQP credit flow control: credit issued **only** by receiver; sender never calls `SetCredit`
//  – Async message handling with no blocking calls
//  – Connection recovery and retry logic for robust peer communication
//  – Link state management and proper cleanup
//
//  🏗️ **Architecture:**
//  – ChessAgent: Core AMQP communication logic with visual chess board
//  – ChessLinkProcessor: Handles incoming AMQP connections from remote peers
//  – ChessLinkEndpoint: Manages message flow and credit for each link
//  – ChessMove: Serializable message payload exchanged between peers
//  – ChessBoardDisplay: Beautiful ASCII art chess board visualization
//
// -----------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amqp;
using Amqp.Framing;
using Amqp.Listener;

namespace AliceMarkQuentinPaulPlayChess;

/// <summary>Chess move data exchanged between peers via AMQP</summary>
public sealed record ChessMove(
    string From,
    string To, 
    string San,
    string Fen,
    bool GameOver = false,
    string Result = ""
);

/// <summary>Command line arguments for ChessAgent with modern options and legacy support</summary>
public sealed class ChessAgentArgs
{
    public string Bind { get; init; } = "";
    public string Connect { get; init; } = "";
    public bool White { get; init; }
    public bool Verbose { get; init; }
    public bool UseColor { get; init; } = true;

    public static ChessAgentArgs Parse(string[] args)
    {
        // Modern argument format: --verbose --bind amqp://localhost:5672 --connect amqp://localhost:5673 --color white
        if (args.Length > 0 && args[0].StartsWith("--"))
        {
            var bind = "";
            var connect = "";
            var white = true;
            var verbose = false;
            var useColor = true;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--verbose":
                        verbose = true;
                        break;
                    case "--bind":
                        if (i + 1 < args.Length) bind = args[++i];
                        break;
                    case "--connect":
                        if (i + 1 < args.Length) connect = args[++i];
                        break;
                    case "--color":
                        if (i + 1 < args.Length) 
                        {
                            white = string.Equals(args[++i], "white", StringComparison.OrdinalIgnoreCase);
                        }
                        break;
                    case "--no-color":
                        useColor = false;
                        break;
                }
            }

            if (string.IsNullOrEmpty(bind) || string.IsNullOrEmpty(connect))
            {
                ShowUsage();
                Environment.Exit(1);
            }

            return new ChessAgentArgs 
            { 
                Bind = bind, 
                Connect = connect, 
                White = white, 
                Verbose = verbose,
                UseColor = useColor
            };
        }

        // Legacy format: <bindHost> <bindPort> <connectHost> <connectPort> <white|black>
        if (args.Length != 5)
        {
            ShowUsage();
            Environment.Exit(1);
        }

        var legacyBind = $"amqp://{args[0]}:{args[1]}";
        var legacyConnect = $"amqp://{args[2]}:{args[3]}";
        var legacyWhite = string.Equals(args[4], "white", StringComparison.OrdinalIgnoreCase);

        return new ChessAgentArgs 
        { 
            Bind = legacyBind, 
            Connect = legacyConnect, 
            White = legacyWhite, 
            Verbose = false,
            UseColor = true
        };
    }

    private static void ShowUsage()
    {
        ChessBoardDisplay.ShowError("Usage:");
        Console.WriteLine("Modern format:");
        Console.WriteLine("  ChessAgent --bind amqp://localhost:5672 --connect amqp://localhost:5673 --color white [--verbose] [--no-color]");
        Console.WriteLine();
        Console.WriteLine("Legacy format:");
        Console.WriteLine("  ChessAgent <bindHost> <bindPort> <connectHost> <connectPort> <white|black>");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  Player 1 (White): ChessAgent --bind amqp://localhost:5672 --connect amqp://localhost:5673 --color white");
        Console.WriteLine("  Player 2 (Black): ChessAgent --bind amqp://localhost:5673 --connect amqp://localhost:5672 --color black --verbose");
    }
}

/// <summary>AMQP 1.0 chess agent with beautiful visual display</summary>
public sealed class ChessAgent : IAsyncDisposable
{
    private readonly string _bindAddress;
    private readonly string _connectAddress;
    private readonly bool _white;
    private readonly bool _verbose;
    private readonly UciEngine _engine; 
    private readonly ContainerHost _host;
    
    private SenderLink? _senderLink;
    internal ChessLinkEndpoint? _linkEndpoint;
    
    // Dictionary to cache sender links for reply-to addresses
    private readonly Dictionary<Uri, SenderLink> _senderLinkCache = new();
    private readonly object _senderLinkCacheLock = new();
    
    private string _fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private int _moveCount = 0;
    private bool _gameOver = false;

    public ChessAgent(string bindAddress, string connectAddress, bool white, bool verbose = false)
    {
        _bindAddress = bindAddress;
        _connectAddress = connectAddress;
        _white = white;
        _verbose = verbose;
        _engine = new UciEngine(verbose);
        
        // Don't initialize display here anymore since it's done early in Main
        ChessBoardDisplay.SetPlayAgainCallback(() => RestartGame());
        
        // Use newer Address-based ContainerHost constructor
        var addresses = new List<Address> { new Address(_bindAddress) };
        _host = new ContainerHost(addresses, null);
        _host.RegisterLinkProcessor(new ChessLinkProcessor(this));
        
        Log($"🎯 ChessAgent initialized as {(white ? "WHITE" : "BLACK")} player");
        ChessBoardDisplay.LogNegotiation($"🔧 ChessAgent created as {(white ? "WHITE ⚪" : "BLACK ⚫")} player");
    }

    private void RestartGame()
    {
        // Reset game state
        _fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        _moveCount = 0;
        _gameOver = false;
        
        // Reset display
        ChessBoardDisplay.Reset();
        
        // Start a new game asynchronously
        _ = Task.Run(async () => 
        {
            try
            {
                await StartAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                ChessBoardDisplay.LogError($"Failed to restart game: {ex.Message}");
            }
        });
    }

    internal void Log(string message)
    {
        if (_verbose)
        {
            Console.WriteLine(message);
            ChessBoardDisplay.LogNegotiation(message);
        }
    }

    // Helper method to get or create a sender link for a specific reply-to URI
    private async Task<SenderLink?> GetOrCreateSenderLinkAsync(Uri replyToUri, CancellationToken ct)
    {
        lock (_senderLinkCacheLock)
        {
            // Check if we already have a cached sender link for this URI
            if (_senderLinkCache.TryGetValue(replyToUri, out var existingLink))
            {
                // Verify the link is still in good state
                if (existingLink.LinkState == LinkState.Attached)
                {
                    Log($"🔄 AMQP: Using cached sender link for {replyToUri}");
                    return existingLink;
                }
                else
                {
                    // Remove stale link from cache
                    Log($"🧹 AMQP: Removing stale sender link for {replyToUri} (state: {existingLink.LinkState})");
                    _senderLinkCache.Remove(replyToUri);
                    existingLink.Close();
                }
            }
        }

        try
        {
            Log($"🔗 AMQP: Creating new sender link for {replyToUri}");
            
            // Create new connection and sender link
            var connectionFactory = new ConnectionFactory();
            var connection = await connectionFactory.CreateAsync(new Address(replyToUri.ToString()));
            var session = new Session(connection);
            var senderLink = new SenderLink(session, $"chess-reply-sender-{DateTime.UtcNow.Ticks}", "chess");
            
            // Wait for link to be attached
            var timeout = TimeSpan.FromSeconds(10);
            var start = DateTime.UtcNow;
            while (senderLink.LinkState != LinkState.Attached && DateTime.UtcNow - start < timeout)
            {
                await Task.Delay(100, ct);
            }
            
            if (senderLink.LinkState != LinkState.Attached)
            {
                Log($"❌ AMQP: Failed to attach sender link to {replyToUri} within timeout");
                senderLink.Close();
                return null;
            }
            
            // Cache the new sender link
            lock (_senderLinkCacheLock)
            {
                _senderLinkCache[replyToUri] = senderLink;
            }
            
            Log($"✅ AMQP: Created and cached new sender link for {replyToUri}");
            return senderLink;
        }
        catch (Exception ex)
        {
            Log($"❌ AMQP: Failed to create sender link for {replyToUri}: {ex.Message}");
            return null;
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            // 🔧 Start AMQP host for incoming connections
            _host.Open();
            Log($"🔧 AMQP: Host started, listening on {_bindAddress}");
            ChessBoardDisplay.LogAmqpStep($"Host started, listening on {_bindAddress}");
            
            ChessBoardDisplay.ShowConnectionStatus($"Listening on {_bindAddress}");
            ChessBoardDisplay.UpdateBoard(_fen, status: "Waiting for game to start");
            
            // ⏳ Brief delay to ensure host is ready
            await Task.Delay(2000, ct);
            
            // Both players need to connect to each other
            if (_bindAddress != _connectAddress)
            {
                Log($"🔗 Connecting to remote peer at {_connectAddress}");
                ChessBoardDisplay.LogAmqpStep($"Connecting to remote peer at {_connectAddress}");
                await ConnectToRemotePeer(ct);
            }
            
            // If we're white, we start the game by making the first move
            if (_white)
            {
                Log($"⚪ WHITE starts: Making first move...");
                await SendMoveAsync(ct);
            }
            else
            {
                Log($"⚫ BLACK waits: Listening for first move from WHITE...");
                ChessBoardDisplay.UpdateBoard(_fen, status: "Waiting for White's first move");
            }
        }
        catch (Exception ex)
        {
            ChessBoardDisplay.ShowError($"Failed to start: {ex.Message}");
            ChessBoardDisplay.LogError($"Failed to start: {ex.Message}");
            throw;
        }
    }

    private async Task ConnectToRemotePeer(CancellationToken ct)
    {
        const int maxRetries = 10;
        const int retryDelayMs = 3000;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Log($"🔄 AMQP: Connection attempt {attempt}/{maxRetries} to {_connectAddress}");
                ChessBoardDisplay.ShowConnectionStatus($"Connecting to peer... (attempt {attempt}/{maxRetries})");
                ChessBoardDisplay.LogAmqpStep($"Connection attempt {attempt}/{maxRetries} to {_connectAddress}");
                
                // Create connection with explicit AMQP 1.0 protocol version
                var address = new Address(_connectAddress);
                var connectionFactory = new ConnectionFactory();
                var connection = await connectionFactory.CreateAsync(address);
                var session = new Session(connection);
                
                ChessBoardDisplay.LogAmqpStep("AMQP connection and session created");
                
                var senderLink = new SenderLink(session, "chess-sender", "chess");
                
                // Wait for link to be attached before considering it ready
                var timeout = TimeSpan.FromSeconds(10);
                var start = DateTime.UtcNow;
                while (senderLink.LinkState != LinkState.Attached && DateTime.UtcNow - start < timeout)
                {
                    await Task.Delay(100, ct);
                }
                
                if (senderLink.LinkState != LinkState.Attached)
                {
                    throw new InvalidOperationException($"Sender link failed to attach within 10s timeout (state: {senderLink.LinkState})");
                }
                
                _senderLink = senderLink;
                
                Log($"✅ AMQP: Connected successfully to {_connectAddress}");
                ChessBoardDisplay.ShowConnectionStatus("Connected to peer");
                ChessBoardDisplay.LogAmqpStep($"✅ Successfully connected to {_connectAddress}");
                return;
            }
            catch (Exception ex)
            {
                Log($"❌ AMQP: Connection attempt {attempt} failed: {ex.Message}");
                ChessBoardDisplay.LogError($"Connection attempt {attempt} failed: {ex.Message}");
                
                if (attempt < maxRetries)
                {
                    Log($"⏳ AMQP: Waiting {retryDelayMs}ms before retry...");
                    ChessBoardDisplay.LogAmqpStep($"⏳ Waiting {retryDelayMs}ms before retry...");
                    await Task.Delay(retryDelayMs, ct);
                }
            }
        }
        
        var errorMsg = $"Failed to connect to remote peer after {maxRetries} attempts";
        ChessBoardDisplay.ShowError(errorMsg);
        ChessBoardDisplay.LogError(errorMsg);
    }

    private async Task SendMoveAsync(CancellationToken ct)
    {
        try
        {
            if (_gameOver)
            {
                Log("🛑 Game is over, not sending move");
                return;
            }
            
            if (_senderLink == null)
            {
                throw new InvalidOperationException("❌ AMQP: No sender link available to send move - cannot continue");
            }
            
            // 🔍 AMQP Link State Validation - don't wait, just throw if not ready
            if (_senderLink.LinkState != LinkState.Attached)
            {
                throw new InvalidOperationException($"❌ AMQP: Sender link not attached. State: {_senderLink.LinkState} - cannot continue");
            }
            
            _moveCount++;
            Log($"🔍 Getting move {_moveCount} from position: {_fen}");
            await _engine.SetPositionAsync(_fen, ct);
            var m = await _engine.GetBestMoveAsync(2000, ct);
            
            // Check if the engine returned a special move indicating game over
            if (m == "(none)" || m == "0000")
            {
                // Default to draw since we can't easily determine checkmate vs stalemate
                var gameResult = "1/2-1/2"; // Stalemate/Draw
                    
                var gameOverMove = new ChessMove("", "", "(none)", _fen, true, gameResult);
                var gameOverMessage = new Message(JsonSerializer.SerializeToUtf8Bytes(gameOverMove));
                
                try
                {
                    _senderLink.Send(gameOverMessage);
                    ChessBoardDisplay.LogAmqpSendMove($"game over: {gameResult}");
                    ChessBoardDisplay.ShowGameResult(gameResult);
                }
                catch (Exception sendEx)
                {
                    Log($"❌ Failed to send game over message: {sendEx.Message}");
                }
                
                _gameOver = true;
                return;
            }
            
            // Update FEN with the move
            Log($"🔄 Getting new position for move {m}");
            
            var newFen = await GetPositionAfterMove(_fen, m, ct);
            Log($"✅ New position: {newFen}");
            
            var move = new ChessMove(m[..2], m[2..4], m, newFen);
            
            // Check for game termination conditions
            if (_moveCount >= 100) // 50-move rule (simplified)
            {
                move = move with { GameOver = true, Result = "1/2-1/2" };
                _gameOver = true;
            }
            
            Log($"📤 SEND move {_moveCount}: {move.San} (from {move.From} to {move.To})");
            Log($"📋 Sending position: {newFen}");
            
            // Update our position and display the move
            _fen = newFen;
            ChessBoardDisplay.UpdateBoard(_fen, move.San, $"Move {_moveCount}");
            
            // 📤 AMQP Message Transmission
            try
            {
                var message = new Message(JsonSerializer.SerializeToUtf8Bytes(move));
                
                // Set the reply-to address to our own bind address
                message.Properties = new Properties
                {
                    ReplyTo = _bindAddress
                };
                
                Log($"🔍 AMQP: Sender link state before send: {_senderLink.LinkState}");
                Log($"📤 AMQP: Setting ReplyTo address: {_bindAddress}");
                
                if (_senderLink.LinkState == LinkState.Attached)
                {
                    _senderLink.Send(message);
                    ChessBoardDisplay.LogAmqpSendMove(move.San);
                    Log($"✅ AMQP: Successfully sent move via sender link: {move.San}");
                }
                else
                {
                    Log($"❌ AMQP: Cannot send move - sender link state: {_senderLink.LinkState}");
                }
            }
            catch (Exception sendEx)
            {
                Log($"❌ AMQP: Failed to send move via AMQP: {sendEx.Message}");
                Log($"❌ Exception type: {sendEx.GetType().Name}");
                Log($"❌ AMQP: Sender link state after error: {_senderLink?.LinkState}");
                
                // 🔄 Try to recreate the connection if it failed
                if (_senderLink?.LinkState != LinkState.Attached)
                {
                    Log($"🔄 AMQP: Sender link is no longer attached, will need reconnection");
                    _senderLink = null;
                    // The connection will be retried on the next move
                }
            }
            
            if (_gameOver)
            {
                ChessBoardDisplay.ShowGameResult(move.Result);
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Failed to send move: {ex.Message}");
            Log($"❌ Stack trace: {ex.StackTrace}");
        }
    }

    private async ValueTask<string> GetPositionAfterMove(string fen, string move, CancellationToken ct)
    {
        return await _engine.GetPositionAfterMoveAsync(fen, move, ct);
    }

    internal async Task OnReceiveAsync(ChessMove mv, string? replyToAddress, CancellationToken ct)
    {
        Log($"📥 AMQP: RECV move {_moveCount + 1}: {mv.San} (from {mv.From} to {mv.To})");
        Log($"📋 AMQP: Received position: {mv.Fen}");
        Log($"📬 AMQP: Reply-to address: {replyToAddress ?? "none"}");
        
        if (mv.GameOver)
        {
            ChessBoardDisplay.ShowGameResult(mv.Result);
            _gameOver = true;
            return;
        }
        
        if (mv.San == "(none)")
        {
            ChessBoardDisplay.ShowGameResult("Game Over - Opponent cannot move");
            _gameOver = true;
            return;
        }
        
        // Update our position with the received move and display it
        _fen = mv.Fen;
        ChessBoardDisplay.UpdateBoard(_fen, mv.San, $"Opponent's Move {_moveCount + 1}");
        
        if (!_gameOver && _moveCount < 100)
        {
            Log($"⏳ Waiting 500ms before responding...");
            await Task.Delay(500, ct);
            
            // Use reply-to address if provided, otherwise fall back to original sender link
            if (!string.IsNullOrEmpty(replyToAddress))
            {
                await SendMoveViaReplyToAsync(replyToAddress, ct);
            }
            else
            {
                // Fallback to original behavior
                if (_senderLink != null)
                {
                    await SendMoveAsync(ct);
                }
                else
                {
                    Log($"❌ AMQP: Cannot respond - no reply-to address and no sender link available");
                }
            }
        }
        else if (_gameOver)
        {
            Log($"🏁 Game is already over");
        }
        else if (_moveCount >= 100)
        {
            ChessBoardDisplay.ShowGameResult("1/2-1/2 - Draw by 50-move rule");
            _gameOver = true;
        }
    }

    // Send a move via the reply-to address using cached sender links
    private async Task SendMoveViaReplyToAsync(string replyToAddress, CancellationToken ct)
    {
        try
        {
            Log($"🔄 AMQP: Preparing to send response via reply-to: {replyToAddress}");
            
            if (!Uri.TryCreate(replyToAddress, UriKind.Absolute, out var replyToUri))
            {
                Log($"❌ AMQP: Invalid reply-to URI: {replyToAddress}");
                return;
            }
            
            var senderLink = await GetOrCreateSenderLinkAsync(replyToUri, ct);
            if (senderLink == null)
            {
                Log($"❌ AMQP: Failed to get sender link for reply-to address: {replyToAddress}");
                return;
            }
            
            // Generate the move response
            if (_gameOver)
            {
                Log("🛑 Game is over, not sending move");
                return;
            }
            
            _moveCount++;
            Log($"🔍 Getting move {_moveCount} from position: {_fen}");
            
            await _engine.SetPositionAsync(_fen, ct);
            var m = await _engine.GetBestMoveAsync(2000, ct);
            
            // Check if the engine returned a special move indicating game over
            if (m == "(none)" || m == "0000")
            {
                // Default to draw since we can't easily determine checkmate vs stalemate
                var gameResult = "1/2-1/2"; // Stalemate/Draw
                
                var gameOverMove = new ChessMove("", "", "(none)", _fen, true, gameResult);
                var gameOverMessage = new Message(JsonSerializer.SerializeToUtf8Bytes(gameOverMove));
                
                // Set reply-to for game over message as well
                gameOverMessage.Properties = new Properties
                {
                    ReplyTo = _bindAddress
                };
                
                try
                {
                    senderLink.Send(gameOverMessage);
                    ChessBoardDisplay.LogAmqpSendMove($"game over: {gameResult}");
                    ChessBoardDisplay.ShowGameResult(gameResult);
                }
                catch (Exception sendEx)
                {
                    Log($"❌ Failed to send game over message via reply-to: {sendEx.Message}");
                }
                
                _gameOver = true;
                return;
            }
            
            // Get the new position after the move
            var newFen = await GetPositionAfterMove(_fen, m, ct);
            var move = new ChessMove(m[..2], m[2..4], m, newFen);
            
            // Check for game termination conditions
            if (_moveCount >= 100) // 50-move rule (simplified)
            {
                move = move with { GameOver = true, Result = "1/2-1/2" };
                _gameOver = true;
            }
            
            Log($"📤 SEND move {_moveCount} via reply-to: {move.San} (from {move.From} to {move.To})");
            
            // Update our position and display the move
            _fen = newFen;
            ChessBoardDisplay.UpdateBoard(_fen, move.San, $"Move {_moveCount}");
            
            // Send the move via reply-to
            var message = new Message(JsonSerializer.SerializeToUtf8Bytes(move));
            message.Properties = new Properties
            {
                ReplyTo = _bindAddress
            };
            
            senderLink.Send(message);
            ChessBoardDisplay.LogAmqpSendMove(move.San);
            Log($"✅ AMQP: Successfully sent move via reply-to link: {move.San}");
            
            if (_gameOver)
            {
                ChessBoardDisplay.ShowGameResult(move.Result);
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Failed to send move via reply-to: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Log("🧹 Starting ChessAgent disposal...");
        
        try
        {
            // 🧹 AMQP Cleanup
            _senderLink?.Close();
            
            // Clean up cached sender links
            lock (_senderLinkCacheLock)
            {
                foreach (var kvp in _senderLinkCache)
                {
                    try
                    {
                        kvp.Value.Close();
                        Log($"🧹 Closed cached sender link for {kvp.Key}");
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ Error closing cached sender link for {kvp.Key}: {ex.Message}");
                    }
                }
                _senderLinkCache.Clear();
            }
            
            _host.Close();
            Log("🧹 AMQP resources disposed");
            
            // Engine cleanup (handled by ChessEngine.cs)
            await _engine.DisposeAsync();
            Log("🧹 Chess engine disposed");
        }
        catch (Exception ex)
        {
            Log($"⚠️ Error during normal disposal: {ex.Message}");
        }
        finally
        {
            // Safety net: ensure Docker containers are cleaned up
            try
            {
                UciEngine.CleanupAllContainers();
                Log("🧹 Docker containers cleanup completed");
            }
            catch (Exception ex)
            {
                Log($"⚠️ Error during Docker cleanup: {ex.Message}");
            }
        }
    }

    // 📡 AMQP Link Processor - handles incoming connections from remote peers
    internal sealed class ChessLinkProcessor : ILinkProcessor
    {
        private readonly ChessAgent _agent;
        
        public ChessLinkProcessor(ChessAgent agent) => _agent = agent;
        
        public void Process(AttachContext attachContext)
        {
            _agent.Log($"🔧 AMQP: Link processor called - Role: {attachContext.Attach.Role}, LinkName: {attachContext.Attach.LinkName}");
            
            if (attachContext.Attach.Role)
            {
                _agent.Log($"❌ AMQP: Rejecting receiver link");
                attachContext.Complete(new Error(ErrorCode.NotAllowed) { Description = "Only sender link is allowed." });
                return;
            }

            // 🏷️ AMQP: Check address routing
            string sourceAddress = attachContext.Attach.Source != null ? ((Source)attachContext.Attach.Source).Address : "";
            string targetAddress = attachContext.Attach.Target != null ? ((Target)attachContext.Attach.Target).Address : "";
            
            _agent.Log($"🔧 AMQP: Processing sender link - Source: '{sourceAddress}', Target: '{targetAddress}'");
            
            // Accept connection if target address is "chess" (this is what the sender specified)
            if (!string.Equals("chess", targetAddress, StringComparison.OrdinalIgnoreCase))
            {
                _agent.Log($"❌ AMQP: Address mismatch: expected target 'chess', got '{targetAddress}'");
                attachContext.Complete(new Error(ErrorCode.NotFound) { Description = "Cannot find address " + targetAddress });
                return;
            }

            ChessBoardDisplay.ShowConnectionStatus($"Client connected: {attachContext.Attach.LinkName}");
            var endpoint = new ChessLinkEndpoint(attachContext.Link as ListenerLink, _agent);
            attachContext.Complete(endpoint, 100);
            _agent.Log($"✅ AMQP: Link endpoint created and attached");
        }
    }

    // 📨 AMQP Link Endpoint - manages message flow and credit for each connection
    internal sealed class ChessLinkEndpoint : LinkEndpoint
    {
        private readonly ListenerLink _link;
        private readonly ChessAgent _agent;
        private int _credit = 100;
        
        public ChessLinkEndpoint(ListenerLink? link, ChessAgent agent)
        {
            _link = link ?? throw new ArgumentNullException(nameof(link));
            _agent = agent;
            _link.Closed += OnLinkClosed;
            
            _agent.Log($"🔧 AMQP: ChessLinkEndpoint constructor called");
            
            // 🔗 Register this endpoint with the agent for message sending
            _agent._linkEndpoint = this;
            
            _agent.Log($"🔧 AMQP: Link endpoint assigned to agent");
            
            // Note: StartAsync method handles game initialization to prevent race conditions
        }

        // 💳 AMQP Credit Flow Control
        public override void OnFlow(FlowContext flowContext)
        {
            Interlocked.Add(ref _credit, flowContext.Messages);
            _agent.Log($"💳 AMQP: Credit received: {flowContext.Messages}, Total: {_credit}");
        }

        public override void OnDisposition(DispositionContext dispositionContext)
        {
            // Message acknowledgment handling
        }

        // 📥 AMQP Message Reception and Processing
        public override void OnMessage(MessageContext messageContext)
        {
            // 📨 Receive and process incoming chess moves via AMQP
            var message = messageContext.Message;
            messageContext.Complete();
            
            _agent.Log($"📨 AMQP: Received message via link endpoint");
            
            // Extract reply-to address from message properties
            string? replyToAddress = null;
            if (message.Properties?.ReplyTo != null)
            {
                replyToAddress = message.Properties.ReplyTo.ToString();
                _agent.Log($"📬 AMQP: Message contains reply-to address: {replyToAddress}");
            }
            else
            {
                _agent.Log($"📬 AMQP: Message has no reply-to address");
            }
            
            if (message.Body is byte[] bytes)
            {
                try
                {
                    var move = JsonSerializer.Deserialize<ChessMove>(bytes);
                    if (move != null)
                    {
                        ChessBoardDisplay.LogAmqpReceiveMove(move.San);
                        _agent.Log($"📨 AMQP: Deserialized chess move: {move.San}");
                        _ = Task.Run(() => _agent.OnReceiveAsync(move, replyToAddress, CancellationToken.None));
                    }
                }
                catch (Exception ex)
                {
                    _agent.Log($"❌ AMQP: Failed to deserialize chess move: {ex.Message}");
                }
            }
        }

        private void OnLinkClosed(IAmqpObject sender, Error error)
        {
            ChessBoardDisplay.ShowConnectionStatus("Remote peer disconnected");
        }
    }
}

public static class Program
{
    private static ChessAgent? _currentAgent = null;
    
    public static async Task<int> Main(string[] args)
    {
        // Parse arguments first
        var parsedArgs = ChessAgentArgs.Parse(args);
        
        // Initialize Terminal.Gui early in the lifecycle
        ChessBoardDisplay.Initialize(parsedArgs.White);
        if (!parsedArgs.UseColor)
        {
            ChessBoardDisplay.SetColorMode(false);
        }
        
        ChessBoardDisplay.ShowTitle();
        ChessBoardDisplay.ShowConnectionStatus($"Ready to start as {(parsedArgs.White ? "WHITE" : "BLACK")} player");
        
        // Set up cancellation token for Ctrl+C handling
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        
        // Set up comprehensive exit handlers to ensure Docker cleanup
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent immediate termination
            cts.Cancel(); // Signal cancellation
            
            // Use the ChessBoardDisplay method to handle shutdown
            ChessBoardDisplay.HandleCtrlC();
        };
        
        // Handle application domain unload (covers most exit scenarios)
        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            ChessBoardDisplay.LogNegotiation("🛑 Process exit detected - Cleaning up Docker containers...");
            CleanupDockerContainers();
        };
        
        // Handle unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            ChessBoardDisplay.LogNegotiation("🛑 Unhandled exception - Cleaning up Docker containers...");
            CleanupDockerContainers();
        };

        try
        {
            ChessAgent? agent = null;
            Task? agentTask = null;
            
            // Set up the start game callback to initialize AMQP when the user clicks Start
            ChessBoardDisplay.SetStartGameCallback(() =>
            {
                // Use Task.Run to handle the async operation without blocking the UI
                _ = Task.Run(async () =>
                {
                    try
                    {
                        ChessBoardDisplay.LogNegotiation("🔧 Creating chess agent...");
                        agent = new ChessAgent(parsedArgs.Bind, parsedArgs.Connect, parsedArgs.White, parsedArgs.Verbose);
                        _currentAgent = agent; // Store reference for cleanup handlers
                        
                        // Add keyboard shortcut info to display
                        ChessBoardDisplay.LogNegotiation("💡 Press Ctrl+C to exit gracefully");
                        ChessBoardDisplay.LogNegotiation("💡 Press Ctrl+Q to quit (in Terminal.Gui)");
                        
                        // Start the agent in the background
                        ChessBoardDisplay.LogNegotiation("🚀 Starting AMQP agent...");
                        agentTask = agent.StartAsync(cancellationToken);
                        await agentTask;
                    }
                    catch (Exception ex)
                    {
                        ChessBoardDisplay.LogError($"Failed to start agent: {ex.Message}");
                    }
                });
            });
            
            // Start the Terminal.Gui application loop - this will block until the application is closed
            ChessBoardDisplay.Run();
            
            // Clean up
            ChessBoardDisplay.Cleanup();
            
            // Wait for agent to finish cleanup if it was started
            if (agentTask != null)
            {
                try
                {
                    await agentTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelled
                    ChessBoardDisplay.LogNegotiation("✅ Application cancelled successfully");
                }
            }
            
            // Ensure proper disposal
            await EnsureProperDisposal();
            _currentAgent = null;
            try
            {
                if (agentTask != null)
                {
                    await agentTask;
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
                ChessBoardDisplay.LogNegotiation("✅ Application cancelled successfully");
            }
            
            // Ensure proper disposal
            await EnsureProperDisposal();
            _currentAgent = null;
            
            return 0;
        }
        catch (Exception ex)
        {
            ChessBoardDisplay.ShowError($"Fatal error: {ex.Message}");
            ChessBoardDisplay.Cleanup();
            
            // Ensure Docker cleanup even on error
            await EnsureProperDisposal();
            _currentAgent = null;
            
            return 1;
        }
        finally
        {
            // Final safety net for Docker cleanup
            CleanupDockerContainers();
        }
    }
    
    private static async Task EnsureProperDisposal()
    {
        if (_currentAgent != null)
        {
            try
            {
                ChessBoardDisplay.LogNegotiation("🧹 Ensuring proper agent disposal...");
                await _currentAgent.DisposeAsync();
                ChessBoardDisplay.LogNegotiation("✅ Agent disposed successfully");
            }
            catch (Exception ex)
            {
                ChessBoardDisplay.LogNegotiation($"⚠️ Error during agent disposal: {ex.Message}");
            }
        }
    }
    
    private static void CleanupDockerContainers()
    {
        try
        {
            // First, use the UciEngine's built-in cleanup
            UciEngine.CleanupAllContainers();
            
            // Then do a broader cleanup of any dockfish containers
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "ps -a --filter name=dockfish --format \"{{.Names}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            process.WaitForExit(5000);
            
            if (process.ExitCode == 0)
            {
                var containerNames = process.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(containerNames))
                {
                    var containers = containerNames.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var container in containers)
                    {
                        var cleanupProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "docker",
                                Arguments = $"rm -f {container.Trim()}",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        
                        cleanupProcess.Start();
                        cleanupProcess.WaitForExit(3000);
                        
                        if (cleanupProcess.ExitCode == 0)
                        {
                            Console.WriteLine($"🧹 Cleaned up Docker container: {container.Trim()}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error during Docker cleanup: {ex.Message}");
        }
    }
}
