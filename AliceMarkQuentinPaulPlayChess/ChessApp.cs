#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amqp;
using Amqp.Framing;
using Amqp.Listener;

namespace AliceMarkQuentinPaulPlayChess;

public sealed class ChessAgent : LinkEndpoint, ILinkProcessor, IAsyncDisposable
{
    private readonly string _bindAddress, _connectAddress;
    internal readonly bool _white, _verbose, _singleSession;
    private readonly UciEngine _engine;
    private readonly ContainerHost _host;
    private Connection? _connection;
    private Session? _session;
    private SenderLink? _senderLink;
    private ReceiverLink? _receiverLink;
    internal ListenerLink? _singleSessionReplyLink;
    private ListenerLink? _currentLink;
    private MessageContext? _currentMessageContext; // Store context for single-session replies
    private long _credit;
    private readonly Dictionary<Uri, SenderLink> _senderLinkCache = new();
    private readonly object _senderLinkCacheLock = new();
    private string _fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private int _moveCount;
    private bool _gameOver;

    public ChessAgent(string bindAddress, string connectAddress, bool white, bool verbose = false, bool singleSession = false)
    {
        (_bindAddress, _connectAddress, _white, _verbose, _singleSession) = (bindAddress, connectAddress, white, verbose, singleSession);
        _engine = new UciEngine(verbose);
        ChessBoardDisplay.SetPlayAgainCallback(() => RestartGame());
        _host = new ContainerHost(new List<Address> { new Address(_bindAddress) }, null);
        _host.RegisterLinkProcessor(this);
        Log($"🎯 ChessAgent initialized as {(white ? "WHITE" : "BLACK")} player");
    }

    private void RestartGame()
    {
        (_fen, _moveCount, _gameOver) = ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 0, false);
        ChessBoardDisplay.Reset();
        _ = Task.Run(async () => { try { await StartAsync(CancellationToken.None); } catch (Exception ex) { ChessBoardDisplay.LogError($"Failed to restart: {ex.Message}"); } });
    }

    internal void Log(string message) { if (_verbose) { Console.WriteLine(message); ChessBoardDisplay.LogNegotiation(message); } }

    private async Task<SenderLink?> GetOrCreateSenderLinkAsync(Uri replyToUri, CancellationToken ct)
    {
        lock (_senderLinkCacheLock)
        {
            if (_senderLinkCache.TryGetValue(replyToUri, out var existingLink) && existingLink.LinkState == LinkState.Attached) return existingLink;
            if (existingLink != null) { _senderLinkCache.Remove(replyToUri); existingLink.Close(); }
        }

        try
        {
            var connection = await new ConnectionFactory().CreateAsync(new Address(replyToUri.ToString()));
            var senderLink = new SenderLink(new Session(connection), $"chess-reply-{DateTime.UtcNow.Ticks}", "chess");

            var start = DateTime.UtcNow;
            while (senderLink.LinkState != LinkState.Attached && DateTime.UtcNow - start < TimeSpan.FromSeconds(10)) await Task.Delay(100, ct);

            if (senderLink.LinkState != LinkState.Attached) { senderLink.Close(); return null; }

            lock (_senderLinkCacheLock) { _senderLinkCache[replyToUri] = senderLink; }
            return senderLink;
        }
        catch (Exception ex) { Log($"❌ Failed to create sender link: {ex.Message}"); return null; }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            if (_singleSession)
            {
                if (_white)
                {
                    ChessBoardDisplay.ShowConnectionStatus($"Connecting to Black player");
                    ChessBoardDisplay.UpdateBoard(_fen, status: "Connecting to game");
                    await ConnectToRemotePeer(ct, true);
                    _ = Task.Run(() => StartReceivingMessages(ct));
                    await SendMove(ct);
                }
                else
                {
                    _host.Open();
                    ChessBoardDisplay.ShowConnectionStatus($"Listening on {_bindAddress}");
                    ChessBoardDisplay.UpdateBoard(_fen, status: "Waiting for White to connect in single-session mode");
                }
            }
            else
            {
                _host.Open();
                ChessBoardDisplay.ShowConnectionStatus($"Listening on {_bindAddress}");
                ChessBoardDisplay.UpdateBoard(_fen, status: "Waiting for game to start");
                await Task.Delay(2000, ct);
                if (_bindAddress != _connectAddress) await ConnectToRemotePeer(ct, false);
                if (_white) await SendMove(ct); else ChessBoardDisplay.UpdateBoard(_fen, status: "Waiting for White's first move");
            }
        }
        catch (Exception ex) { ChessBoardDisplay.ShowError($"Failed to start: {ex.Message}"); throw; }
    }

    // Unified connection logic for all modes
    private async Task ConnectToRemotePeer(CancellationToken ct, bool isSingleSession)
    {
        const int maxRetries = 10;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                ChessBoardDisplay.ShowConnectionStatus($"Connecting... ({attempt}/{maxRetries})");

                _connection = await new ConnectionFactory().CreateAsync(new Address(_connectAddress));
                _session = new Session(_connection);

                var senderLink = new SenderLink(_session, "chess-sender", "chess");
                ReceiverLink? receiverLink = isSingleSession ? new ReceiverLink(_session, "chess-receiver", "chess-reply") : null;

                var start = DateTime.UtcNow;
                while (DateTime.UtcNow - start < TimeSpan.FromSeconds(10))
                {
                    bool senderReady = senderLink.LinkState == LinkState.Attached;
                    bool receiverReady = !isSingleSession || receiverLink?.LinkState == LinkState.Attached;
                    if (senderReady && receiverReady) break;
                    await Task.Delay(100, ct);
                }

                if (senderLink.LinkState != LinkState.Attached || (isSingleSession && receiverLink?.LinkState != LinkState.Attached))
                    throw new InvalidOperationException($"Links failed to attach");

                _senderLink = senderLink;
                if (isSingleSession) _receiverLink = receiverLink;

                ChessBoardDisplay.ShowConnectionStatus(isSingleSession ? "Single-session: Connected" : "Connected");
                return;
            }
            catch (Exception ex)
            {
                CleanupFailedConnection();
                if (attempt < maxRetries) await Task.Delay(3000, ct);
            }
        }

        var errorMsg = $"Failed to connect after {maxRetries} attempts";
        ChessBoardDisplay.ShowError(errorMsg);
        if (isSingleSession) throw new InvalidOperationException(errorMsg);
    }

    private void CleanupFailedConnection() { _senderLink?.Close(); _senderLink = null; _receiverLink?.Close(); _receiverLink = null; _session?.Close(); _session = null; _connection?.Close(); _connection = null; }

    private async Task StartReceivingMessages(CancellationToken ct)
    {
        if (_receiverLink == null) return;
        try
        {
            while (!ct.IsCancellationRequested && !_gameOver)
            {
                _receiverLink.SetCredit(10);
                var message = await _receiverLink.ReceiveAsync(TimeSpan.FromSeconds(30));
                if (message != null)
                {

                    try
                    {
                        await ProcessMessage(message, ct, "SINGLE-SESSION");
                        _receiverLink.Accept(message);
                    }
                    catch (Exception ex)
                    {
                        Log($"❌ SINGLE-SESSION: Error processing: {ex.Message}");
                        _receiverLink.Reject(message, new Error(ErrorCode.NotAllowed) { Description = "Invalid message format" });
                    }
                }
            }
        }
        catch (Exception ex) { Log($"❌ SINGLE-SESSION: Error receiving: {ex.Message}"); }
    }

    internal async Task<bool> ProcessMessage(Message message, CancellationToken ct = default, string context = "AMQP")
    {
        try
        {
            string? replyToAddress = message.Properties?.ReplyTo?.ToString();
            var move = message.Body is byte[] bytes ? JsonSerializer.Deserialize<ChessMove>(bytes) : JsonSerializer.Deserialize<ChessMove>(message.Body.ToString());

            if (move != null) { ChessBoardDisplay.LogAmqpReceiveMove(move.San); await HandleIncomingMove(move, replyToAddress, ct); return true; }
            return false;
        }
        catch (Exception ex) { Log($"❌ {context}: Error processing: {ex.Message}"); return false; }
    }

    private async Task HandleIncomingMove(ChessMove mv, string? replyToAddress, CancellationToken ct)
    {
        if (mv.GameOver || mv.San == "(none)") { ChessBoardDisplay.ShowGameResult(mv.GameOver ? mv.Result : "Game Over - Opponent cannot move"); _gameOver = true; return; }

        _fen = mv.Fen;
        ChessBoardDisplay.UpdateBoard(_fen, mv.San, $"Opponent's Move {_moveCount + 1}");

        if (!_gameOver && _moveCount < 100)
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            SendMove(ct, replyToAddress).ContinueWith(t =>
            {
                if (t.IsFaulted) ChessBoardDisplay.LogError($"Failed to send move: {t.Exception?.Message}");
            }, TaskScheduler.Default);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        } // Faster testing
        else if (_moveCount >= 100) { ChessBoardDisplay.ShowGameResult("1/2-1/2 - Draw by 50-move rule"); _gameOver = true; }
    }

    // Unified move generation and sending
    private async Task SendMove(CancellationToken ct, string? replyToAddress = null)
    {
        try
        {
            if (_gameOver) return;

            _moveCount++;
            await _engine.SetPositionAsync(_fen, ct);
            var m = await _engine.GetBestMoveAsync(500, ct); // Reduced thinking time for faster testing

            ChessMove move = (m == "(none)" || m == "0000") ? new ChessMove("", "", "(none)", _fen, true, "1/2-1/2") : new ChessMove(m[..2], m[2..4], m, await _engine.GetPositionAfterMoveAsync(_fen, m, ct));

            if (move.GameOver || _moveCount >= 100) { move = move with { GameOver = true, Result = "1/2-1/2" }; _gameOver = true; }

            _fen = move.Fen;
            ChessBoardDisplay.UpdateBoard(_fen, move.San, $"Move {_moveCount}");

            var message = new Message(JsonSerializer.SerializeToUtf8Bytes(move));
            // Normal mode and single-session White: Use existing connection with async send
            message.Properties = new Properties { ReplyTo = _bindAddress };

            if (this._singleSessionReplyLink != null)
            {
                try
                {
                    // Use async send with shorter timeout to prevent blocking
                    _singleSessionReplyLink.SendMessage(message);
                    ChessBoardDisplay.LogAmqpSendMove(move.GameOver ? $"game over: {move.Result}" : move.San);
                }
                catch (TimeoutException)
                {
                    Log($"❌ Send timeout");
                }
                return;
            }
            else if (_senderLink?.LinkState == LinkState.Attached)
            {
                try
                {
                    // Use async send with shorter timeout to prevent blocking
                    _senderLink.SendAsync(message, TimeSpan.FromSeconds(60)).ContinueWith(t =>
                    {
                        if (t.IsFaulted) ChessBoardDisplay.LogError($"Failed to send move: {t.Exception?.Message}");
                    }, TaskScheduler.Default);
                    ChessBoardDisplay.LogAmqpSendMove(move.GameOver ? $"game over: {move.Result}" : move.San);
                }
                catch (TimeoutException)
                {
                    Log($"❌ Send timeout");
                }
                return;
            }
            else if (!string.IsNullOrEmpty(replyToAddress) && Uri.TryCreate(replyToAddress, UriKind.Absolute, out var replyToUri))
            {
                var senderLink = await GetOrCreateSenderLinkAsync(replyToUri, ct);
                if (senderLink != null)
                {
                    message.Properties = new Properties { ReplyTo = _bindAddress };
                    senderLink.SendAsync(message, TimeSpan.FromSeconds(60)).ContinueWith(t =>
                    {
                        if (t.IsFaulted) ChessBoardDisplay.LogError($"Failed to send move: {t.Exception?.Message}");
                    }, TaskScheduler.Default);
                }
            }
            else return;

            ChessBoardDisplay.LogAmqpSendMove(move.GameOver ? $"game over: {move.Result}" : move.San);
            if (move.GameOver) ChessBoardDisplay.ShowGameResult(move.Result);
        }
        catch (Exception ex) { Log($"❌ Failed to send move: {ex.Message}"); }
    }

    // 💳 AMQP Credit Flow Control (LinkEndpoint implementation)
    public override void OnFlow(FlowContext flowContext) { Interlocked.Add(ref _credit, flowContext.Messages); Log($"💳 Credit: {flowContext.Messages}, Total: {_credit}"); }

    public override void OnDisposition(DispositionContext dispositionContext) { }

    public override void OnMessage(MessageContext messageContext)
    {
        try
        {
            if (_singleSession && !_white) _currentMessageContext = messageContext; // Store for reply
            var success = ProcessMessage(messageContext.Message, CancellationToken.None, "AMQP").Result;
            if (success)
            {
                messageContext.Complete();
                // In single-session mode, Black should NOT clear the context here - keep it for reply
                if (!(_singleSession && !_white)) _currentMessageContext = null;
            }
            else
            {
                messageContext.Complete(new Error(ErrorCode.NotAllowed) { Description = "Invalid message format" });
                if (!(_singleSession && !_white)) _currentMessageContext = null; // Clear context only if not in single-session Black mode
            }
        }
        catch (Exception ex) { Log($"❌ OnMessage error: {ex.Message}"); }
    }

    private void OnLinkClosed(IAmqpObject sender, Error error) => ChessBoardDisplay.ShowConnectionStatus("Remote peer disconnected");

    internal void InitializeLinkEndpoint(ListenerLink? link) { _currentLink = link ?? throw new ArgumentNullException(nameof(link)); _currentLink.Closed += OnLinkClosed; }

    // Link Processor implementation
    public void Process(AttachContext attachContext)
    {
        if (_singleSession && !_white) { ProcessSingleSessionBlackLink(attachContext); return; }

        if (attachContext.Attach.Role) { attachContext.Complete(new Error(ErrorCode.NotAllowed) { Description = "Only sender link is allowed in normal mode." }); return; }

        string targetAddress = attachContext.Attach.Target != null ? ((Target)attachContext.Attach.Target).Address : "";

        if (!string.Equals("chess", targetAddress, StringComparison.OrdinalIgnoreCase)) { attachContext.Complete(new Error(ErrorCode.NotFound) { Description = "Cannot find address " + targetAddress }); return; }

        ChessBoardDisplay.ShowConnectionStatus($"Client connected: {attachContext.Attach.LinkName}");
        InitializeLinkEndpoint(attachContext.Link as ListenerLink);
        attachContext.Complete(this, 100);
    }
    private void ProcessSingleSessionBlackLink(AttachContext attachContext)
    {
        string targetAddress = attachContext.Attach.Target != null ? ((Target)attachContext.Attach.Target).Address : "";

        // White sends to "chess" address (White role=False/sender, Black role=True/receiver)
        if (!attachContext.Attach.Role)
        {
            if (!string.Equals("chess", targetAddress, StringComparison.OrdinalIgnoreCase)) { attachContext.Complete(new Error(ErrorCode.NotFound) { Description = "Cannot find address " + targetAddress }); return; }

            ChessBoardDisplay.ShowConnectionStatus($"Single-session: Receiver connected");
            InitializeLinkEndpoint(attachContext.Link as ListenerLink);
            attachContext.Complete(this, 100);
        }
        // White receives from "chess-reply" address (White role=True/receiver, Black role=False/sender)
        else
        {
            string sourceAddress = attachContext.Attach.Source != null ? ((Source)attachContext.Attach.Source).Address : "";
            if (!string.Equals("chess-reply", sourceAddress, StringComparison.OrdinalIgnoreCase)) { attachContext.Complete(new Error(ErrorCode.NotFound) { Description = "Cannot find address " + sourceAddress }); return; }

            ChessBoardDisplay.ShowConnectionStatus($"Single-session: Reply connected");
            _singleSessionReplyLink = attachContext.Link as ListenerLink;
            attachContext.Complete(null, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _senderLink?.Close();
            lock (_senderLinkCacheLock)
            {
                foreach (var kvp in _senderLinkCache) { try { kvp.Value.Close(); } catch (Exception ex) { Log($"⚠️ Error closing link: {ex.Message}"); } }
                _senderLinkCache.Clear();
            }
            _host.Close();
            await _engine.DisposeAsync();
        }
        catch (Exception ex) { Log($"⚠️ Disposal error: {ex.Message}"); }
        finally { try { UciEngine.CleanupAllContainers(); } catch (Exception ex) { Log($"⚠️ Docker cleanup error: {ex.Message}"); } }
    }
}
