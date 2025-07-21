using Amqp;
using Amqp.Framing;
using Amqp.Listener;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;
using System.Text.Json;
using Ceres.Chess;
using Ceres.Chess.MoveGen;
using Ceres.Chess.MoveGen.Converters;

namespace AliceMarkQuentinPaulPlayChess;

public class ChessMove
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Promotion { get; set; } = string.Empty;
    public string San { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
}

public class AmqpChessPlayer
{
    private readonly string _bindHost;
    private readonly int _bindPort;
    private readonly string _remoteHost;
    private readonly int _remotePort;
    private Position _position;
    private readonly bool _isWhite;
    private readonly bool _isInitiator;
    private ContainerHost? _host;
    private Connection? _connection;
    private Session? _session;
    private SenderLink? _sender;
    private ReceiverLink? _receiver;
    private readonly JsonEventFormatter _eventFormatter;
    private readonly Random _random;

    public AmqpChessPlayer(string bindHost, int bindPort, string remoteHost, int remotePort, string color)
    {
        _bindHost = bindHost;
        _bindPort = bindPort;
        _remoteHost = remoteHost;
        _remotePort = remotePort;
        _position = Position.StartPosition;
        _isWhite = color.ToLower() == "white";
        _isInitiator = _isWhite; // White initiates, Black listens first
        _eventFormatter = new JsonEventFormatter();
        _random = new Random();
        
        Console.WriteLine($"Chess Player initialized as {color} ({(_isInitiator ? "Initiator" : "Listener")})");
        Console.WriteLine($"Binding to: {_bindHost}:{_bindPort}");
        Console.WriteLine($"Remote peer: {_remoteHost}:{_remotePort}");
        Console.WriteLine($"Initial board state: {_position.FEN}");
    }

    public async Task StartAsync()
    {
        try
        {
            // Always set up listener first
            await SetupListenerAsync();
            
            if (_isInitiator)
            {
                // White starts - wait a bit then connect and make first move
                Console.WriteLine("Waiting for black player to be ready...");
                await Task.Delay(2000);
                await InitiateConnectionAsync();
                await Task.Delay(1000);
                await MakeBestMoveAsync();
            }
            else
            {
                Console.WriteLine("Waiting for white player to connect...");
            }

            // Keep the application running
            Console.WriteLine("\nPress 'q' to quit, 'm' to show current board...");
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                    break;
                if (key.KeyChar == 'm' || key.KeyChar == 'M')
                {
                    Console.WriteLine($"\nCurrent board state: {_board.ToFen()}");
                    Console.WriteLine($"Move count: {_board.ExecutedMoves.Count}");
                }
                await Task.Delay(100);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in StartAsync: {ex.Message}");
            throw;
        }
    }

    private async Task SetupListenerAsync()
    {
        try
        {
            var address = new Address(_bindHost, _bindPort, null, null);
            _host = new ContainerHost(address);
            
            _host.RegisterLinkProcessor(new ChessLinkProcessor(this));
            
            await Task.Run(() => _host.Open());
            Console.WriteLine($"AMQP listener started on {_bindHost}:{_bindPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting up listener: {ex.Message}");
            throw;
        }
    }

    private async Task InitiateConnectionAsync()
    {
        try
        {
            var factory = new ConnectionFactory();
            var address = new Address(_remoteHost, _remotePort, null, null);
            
            _connection = await factory.CreateAsync(address);
            _session = _connection.CreateSession();
            
            _sender = _session.CreateSender("chess-moves");
            _receiver = _session.CreateReceiver("chess-moves");
            
            _receiver.Start(50, OnMessageReceived);
            
            Console.WriteLine($"Connected to remote peer at {_remoteHost}:{_remotePort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to remote peer: {ex.Message}");
            throw;
        }
    }

    private async Task MakeBestMoveAsync()
    {
        try
        {
            var legalMoves = _board.Moves();
            if (!legalMoves.Any())
            {
                Console.WriteLine("No legal moves available. Game over.");
                return;
            }

            // Simple chess strategy: prefer center control, then captures, then development
            var preferredMove = SelectBestMove(legalMoves);

            var result = _board.Move(preferredMove);
            
            var moveData = new ChessMove
            {
                From = preferredMove.OriginalPosition.ToString(),
                To = preferredMove.NewPosition.ToString(),
                Promotion = preferredMove.Promotion?.ToString() ?? "",
                San = preferredMove.SAN,
                Fen = _board.ToFen()
            };

            await SendMoveAsync(moveData);
            
            Console.WriteLine($"✓ Made move: {preferredMove.SAN} ({preferredMove.OriginalPosition} -> {preferredMove.NewPosition})");
            
            CheckGameState();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error making move: {ex.Message}");
        }
    }

    private Move SelectBestMove(IEnumerable<Move> legalMoves)
    {
        var moves = legalMoves.ToList();
        
        // Prefer captures
        var captures = moves.Where(m => m.CapturedPiece != null).ToList();
        if (captures.Any())
        {
            return captures[_random.Next(captures.Count)];
        }
        
        // Prefer center control (d4, d5, e4, e5)
        var centerMoves = moves.Where(m => 
            (m.NewPosition.File == File.D || m.NewPosition.File == File.E) &&
            (m.NewPosition.Rank == Rank.Four || m.NewPosition.Rank == Rank.Five)).ToList();
        if (centerMoves.Any())
        {
            return centerMoves[_random.Next(centerMoves.Count)];
        }
        
        // Prefer development moves (not pawn moves)
        var developmentMoves = moves.Where(m => m.Piece.GetPieceType() != PieceType.Pawn).ToList();
        if (developmentMoves.Any())
        {
            return developmentMoves[_random.Next(developmentMoves.Count)];
        }
        
        // Random move
        return moves[_random.Next(moves.Count)];
    }

    private void CheckGameState()
    {
        if (_board.IsCheckmate)
        {
            Console.WriteLine("🏆 Checkmate! I win!");
        }
        else if (_board.IsStaleMate)
        {
            Console.WriteLine("🤝 Stalemate! Game is a draw.");
        }
        else if (_board.IsCheck)
        {
            Console.WriteLine("⚠️  Check given!");
        }
    }

    private async Task SendMoveAsync(ChessMove move)
    {
        if (_sender == null)
        {
            Console.WriteLine("❌ Sender not initialized");
            return;
        }

        try
        {
            var cloudEvent = new CloudEvent
            {
                Type = "chess.move",
                Source = new Uri($"amqp://{_bindHost}:{_bindPort}"),
                Id = Guid.NewGuid().ToString(),
                Time = DateTimeOffset.UtcNow,
                Data = move,
                DataContentType = "application/json"
            };

            var jsonBytes = _eventFormatter.EncodeStructuredModeMessage(cloudEvent, out var contentType);
            var message = new Message(jsonBytes.ToArray())
            {
                Properties = new Properties
                {
                    ContentType = contentType,
                    MessageId = cloudEvent.Id
                }
            };

            await _sender.SendAsync(message);
            Console.WriteLine($"📤 Sent CloudEvent: chess.move -> {move.San}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error sending move: {ex.Message}");
        }
    }

    private void OnMessageReceived(IReceiver receiver, Message message)
    {
        try
        {
            var bodyBytes = message.GetBody<byte[]>();
            var cloudEvent = _eventFormatter.DecodeStructuredModeMessage(bodyBytes, null, null);
            
            Console.WriteLine($"📥 Received CloudEvent: {cloudEvent.Type} (ID: {cloudEvent.Id})");
            
            if (cloudEvent.Type == "chess.move" && cloudEvent.Data != null)
            {
                var moveJson = JsonSerializer.Serialize(cloudEvent.Data);
                var move = JsonSerializer.Deserialize<ChessMove>(moveJson);
                
                if (move != null)
                {
                    ProcessOpponentMove(move);
                }
            }
            
            receiver.Accept(message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing received message: {ex.Message}");
            receiver.Reject(message);
        }
    }

    private async void ProcessOpponentMove(ChessMove move)
    {
        try
        {
            Console.WriteLine($"🔄 Processing opponent move: {move.San} ({move.From} -> {move.To})");
            
            // Find the matching move in our legal moves
            var legalMoves = _board.Moves();
            var matchingMove = legalMoves.FirstOrDefault(m => 
                m.OriginalPosition.ToString().Equals(move.From, StringComparison.OrdinalIgnoreCase) &&
                m.NewPosition.ToString().Equals(move.To, StringComparison.OrdinalIgnoreCase));
            
            if (matchingMove != null)
            {
                _board.Move(matchingMove);
                Console.WriteLine($"✓ Applied opponent move: {matchingMove.SAN}");
                
                if (_board.IsCheckmate)
                {
                    Console.WriteLine("😞 Checkmate! Opponent wins!");
                    return;
                }
                else if (_board.IsStaleMate)
                {
                    Console.WriteLine("🤝 Stalemate! Game is a draw.");
                    return;
                }
                else if (_board.IsCheck)
                {
                    Console.WriteLine("⚠️  I'm in check!");
                }
                
                // Make our response move after a brief delay
                await Task.Delay(1500); // Thinking time
                await MakeBestMoveAsync();
            }
            else
            {
                Console.WriteLine($"❌ Invalid move received: {move.From} -> {move.To}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing opponent move: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        try
        {
            Console.WriteLine("🔌 Shutting down AMQP connections...");
            _receiver?.Close();
            _sender?.Close();
            _session?.Close();
            _connection?.Close();
            _host?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error during cleanup: {ex.Message}");
        }
    }

    // Internal method to handle incoming connections from the listener
    internal async Task HandleIncomingConnection(IConnection connection, ISession session, ISender sender, IReceiver receiver)
    {
        _connection = connection;
        _session = session;
        _sender = sender;
        _receiver = receiver;
        
        _receiver.Start(50, OnMessageReceived);
        Console.WriteLine("✅ Accepted incoming AMQP connection from remote peer");
    }
}

public class ChessLinkProcessor : ILinkProcessor
{
    private readonly AmqpChessPlayer _chessPlayer;

    public ChessLinkProcessor(AmqpChessPlayer chessPlayer)
    {
        _chessPlayer = chessPlayer;
    }

    public void Process(AttachContext attachContext)
    {
        if (attachContext.Attach.Role)
        {
            // This is a receiver attach (sender link from remote)
            var receiver = attachContext.Link as IReceiver;
            if (receiver != null)
            {
                var session = attachContext.Link.Session;
                var connection = session.Connection;
                var sender = session.CreateSender("chess-moves");
                
                Task.Run(async () => await _chessPlayer.HandleIncomingConnection(connection, session, sender, receiver));
            }
        }
    }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("🏰 AMQP Peer-to-Peer Chess Application");
        Console.WriteLine("=====================================");
        
        if (args.Length != 5)
        {
            Console.WriteLine("Usage: AliceMarkQuentinPaulPlayChess <bind-host> <bind-port> <remote-host> <remote-port> <color>");
            Console.WriteLine("Example: AliceMarkQuentinPaulPlayChess localhost 5672 localhost 5673 white");
            Console.WriteLine("Color should be 'white' or 'black'");
            Console.WriteLine("\nThis application demonstrates AMQP's power for building agentic bi-directional communication!");
            return;
        }

        var bindHost = args[0];
        if (!int.TryParse(args[1], out var bindPort))
        {
            Console.WriteLine("❌ Invalid bind port");
            return;
        }

        var remoteHost = args[2];
        if (!int.TryParse(args[3], out var remotePort))
        {
            Console.WriteLine("❌ Invalid remote port");
            return;
        }

        var color = args[4];
        if (!color.Equals("white", StringComparison.OrdinalIgnoreCase) && 
            !color.Equals("black", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("❌ Color must be 'white' or 'black'");
            return;
        }

        var player = new AmqpChessPlayer(bindHost, bindPort, remoteHost, remotePort, color);
        
        try
        {
            await player.StartAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Application error: {ex.Message}");
        }
        finally
        {
            await player.StopAsync();
        }
        
        Console.WriteLine("👋 Application terminated. Thanks for playing!");
    }
}
