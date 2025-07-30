using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Terminal.Gui;

namespace AliceMarkQuentinPaulPlayChess;

public static class ChessBoardDisplay
{
    private static Toplevel? _top;
    private static Window? _mainWindow;
    private static Label? _titleLabel, _moveHistoryLabel, _moveHistoryList, _statusLabel, _connectionLabel;
    private static ChessBoardView? _boardTextView;
    private static TextView? _negotiationLabel;
    private static Button? _playAgainButton, _startButton;
    private static bool _initialized, _isApplicationRunning, _gameEnded, _gameStarted, _isWhite = true, _useColor = true, _headless = false;
    private static string _lastFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", _lastMove = "", _status = "", _connection = "";
    private static List<(string move, DateTime time, bool isWhite, string capturedPiece)> _moveHistory = new();
    private static List<string> _negotiationLog = new();
    private static DateTime _gameStartTime = DateTime.Now, _whiteTime = DateTime.Now, _blackTime = DateTime.Now;
    private static Action? _playAgainCallback, _startGameCallback;

    public static void Initialize(bool isWhite)
    {
        (_initialized, _isWhite, _gameStartTime, _whiteTime, _blackTime, _gameEnded) = (true, isWhite, DateTime.Now, DateTime.Now, DateTime.Now, false);
        _moveHistory.Clear(); _negotiationLog.Clear();
        
        // Skip Terminal.Gui initialization in headless mode
        if (_headless) return;
        
        try
        {
            Application.Init(); 
            _top = Application.Top;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize Terminal.Gui: {ex.Message}");
            Console.WriteLine("This may be due to running in VS Code terminal or other incompatible environment.");
            Console.WriteLine("💡 Try running in Windows Terminal, Command Prompt, or use --headless flag");
            Console.WriteLine("🔄 Falling back to headless mode...");
            _headless = true;
            return;
        }

        var titleCs = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.White, Color.Blue) };
        var movesCs = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.White, Color.DarkGray) };
        var negotiationCs = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.Gray, Color.Black) };
        var statusCs = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black) };
        var connectionCs = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightMagenta, Color.Black) };

        _mainWindow = new Window("♟️ ChessAgent - Professional Chess Interface") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        var playerColor = _isWhite ? "White ⚪" : "Black ⚫";
        _titleLabel = new Label($"♟️ ChessAgent - Playing as {playerColor}") { X = Pos.Center(), Y = 0, ColorScheme = titleCs };
        _boardTextView = new ChessBoardView() { X = 1, Y = 2, Width = 30, Height = 15 };
        _moveHistoryLabel = new Label("") { X = 37, Y = 3, Width = 42, Height = 3, ColorScheme = movesCs };
        _moveHistoryList = new Label("") { X = 37, Y = 6, Width = 42, Height = 19, ColorScheme = movesCs };
        _negotiationLabel = new TextView() { X = 1, Y = 21, Width = 35, Height = 8, ColorScheme = negotiationCs, ReadOnly = true, WordWrap = false };
        _statusLabel = new Label("🔄 Initializing...") { X = 1, Y = 29, Width = 50, Height = 1, ColorScheme = statusCs };
        _connectionLabel = new Label("📡 Starting AMQP connection...") { X = 1, Y = 30, Width = 70, Height = 1, ColorScheme = connectionCs };
        _playAgainButton = new Button("🎮 Play Again") { X = Pos.Center(), Y = 31, IsDefault = true, Visible = false };
        _startButton = new Button("🚀 Start AMQP Connection") { X = Pos.Center(), Y = 10, IsDefault = true, Visible = true };
        _playAgainButton.Clicked += OnPlayAgainClicked; _startButton.Clicked += OnStartClicked;

        var movesTitle = new Label("♟️ Move History") { X = 37, Y = 2, ColorScheme = titleCs };
        var negotiationTitle = new Label("🔗 Connection Log") { X = 1, Y = 20, ColorScheme = titleCs };
        _mainWindow.Add(_titleLabel, _boardTextView, movesTitle, _moveHistoryLabel, _moveHistoryList, negotiationTitle, _negotiationLabel, _statusLabel, _connectionLabel, _playAgainButton, _startButton);
        _top.Add(_mainWindow);

        _mainWindow.KeyPress += (args) => { if (args.KeyEvent.Key == (Key.CtrlMask | Key.q) || args.KeyEvent.Key == (Key.CtrlMask | Key.c) || args.KeyEvent.Key == Key.Esc) { AddNegotiationEntry("Exit"); Application.RequestStop(); } };
        Application.Top.KeyPress += (args) => { if (args.KeyEvent.Key == (Key.CtrlMask | Key.c)) Application.RequestStop(); };
        _top.KeyPress += (args) => { if (args.KeyEvent.Key == (Key.CtrlMask | Key.c)) Application.RequestStop(); };

        AddNegotiationEntry($"Init {playerColor}"); AddNegotiationEntry($"@ {DateTime.Now:HH:mm:ss}");
        AddNegotiationEntry("Docker req:"); AddNegotiationEntry("1. Start Docker Desktop"); AddNegotiationEntry("2. Pull niklasf/stockfish");
        AddNegotiationEntry("3. Ensure container access"); AddNegotiationEntry("4. Click Start when ready");
        AddNegotiationEntry("Both players need AMQP"); AddNegotiationEntry("Ctrl+C/Q/ESC to exit");
        UpdateDisplay();
    }

    public static void SetIsWhite(bool isWhite) { _isWhite = isWhite; if (_initialized && !_headless) UpdateDisplay(); }
    
    public static void SetHeadlessMode(bool headless) { _headless = headless; }

    public static void UpdateBoard(string fen, string? lastMove = null, string status = "", string connection = "")
    {
        if (!string.IsNullOrEmpty(lastMove) && lastMove != _lastMove)
        {
            var now = DateTime.Now;
            var isWhiteMove = _moveHistory.Count % 2 == 0;
            var algebraicMove = ConvertToAlgebraicNotation(lastMove, _lastFen);
            string capturedPiece = " ";
            if (!string.IsNullOrEmpty(_lastFen) && algebraicMove.Contains('x'))
            {
                var destSquare = ExtractDestinationSquare(algebraicMove);
                if (!string.IsNullOrEmpty(destSquare))
                {
                    var pieceOnDestSquare = GetPieceAtSquare(_lastFen, destSquare);
                    if (pieceOnDestSquare != ' ') capturedPiece = PieceToSymbol(pieceOnDestSquare);
                }
            }
            _moveHistory.Add((algebraicMove, now, isWhiteMove, capturedPiece));
            if (isWhiteMove) _whiteTime = now; else _blackTime = now;
            
            // In headless mode, log move to console
            if (_headless)
            {
                Console.WriteLine($"🎯 Move {_moveHistory.Count}: {algebraicMove} ({(isWhiteMove ? "White" : "Black")})");
            }
        }
        (_lastFen, _lastMove, _status, _connection) = (fen, lastMove ?? _lastMove, status, connection);
        
        // In headless mode, log status updates to console
        if (_headless)
        {
            if (!string.IsNullOrEmpty(status)) Console.WriteLine($"📋 Status: {status}");
            if (!string.IsNullOrEmpty(connection)) Console.WriteLine($"🔗 Connection: {connection}");
        }
        else
        {
            UpdateDisplay();
        }
    }

    private static void UpdateDisplay()
    {
        if (!_initialized || _titleLabel == null || _boardTextView == null || _moveHistoryLabel == null || _moveHistoryList == null)
            return;

        // Update title
        var playerColor = _isWhite ? "White ⚪" : "Black ⚫";
        var titleText = $"♟️ ChessAgent - Playing as {playerColor}";
        
        // Update UI directly since we're back on the main thread
        _titleLabel.Text = titleText;
        
        // Update the chess board with current position
        ChessBoardView.UpdateBoard(_lastFen, _isWhite);
        _boardTextView.SetNeedsDisplay();
        
        UpdateMoveHistoryDisplay();
        UpdateNegotiationDisplay();
        
        if (_statusLabel != null)
        {
            _statusLabel.Text = string.IsNullOrEmpty(_status) ? "🔄 Ready" : $"🎯 {_status}";
        }
        
        if (_connectionLabel != null)
        {
            _connectionLabel.Text = string.IsNullOrEmpty(_connection) ? "📡 No connection" : $"📡 {_connection}";
        }
        
        // Only refresh if the application is actually running
        // Calling Application.Refresh() before Run() can cause blocking
        try { if (_isApplicationRunning) Application.Refresh(); } catch (Exception) { }
    }

    private static void AddNegotiationEntry(string message)
    {
        var seconds = (int)(DateTime.Now - _gameStartTime).TotalSeconds;
        var safeMessage = SafeString(message ?? "");
        var truncatedMessage = safeMessage.Length > 27 ? safeMessage.Substring(0, 25) + ".." : safeMessage;
        _negotiationLog.Add($"{seconds,3}s {truncatedMessage}");
        UpdateNegotiationDisplay();
    }

    private static void UpdateMoveHistoryDisplay()
    {
        if (_moveHistoryLabel == null || _moveHistoryList == null) return;

        _moveHistoryLabel.Text = SafeString("# | White    | Black    | Cap | Sec\n--|----------|----------|-----|----");
        var sb = new StringBuilder();
        
        try
        {
            if (_moveHistory.Count > 0)
            {
                var recentMoves = _moveHistory.TakeLast(17).ToList();
                for (int i = 0; i < recentMoves.Count; i += 2)
                {
                    var moveNum = (i / 2) + 1;
                    var whiteMove = i < recentMoves.Count ? recentMoves[i] : (string.Empty, DateTime.MinValue, true, " ");
                    var blackMove = (i + 1) < recentMoves.Count ? recentMoves[i + 1] : (string.Empty, DateTime.MinValue, false, " ");
                    
                    var whiteTimeStr = whiteMove.Item1 != string.Empty ? $"{(int)(whiteMove.Item2 - _gameStartTime).TotalSeconds,3}" : "   ";
                    var whiteMoveText = SafeTruncate(SafeString(whiteMove.Item1), 10);
                    var blackMoveText = SafeTruncate(SafeString(blackMove.Item1), 10);
                    var captureDisplay = SafeString($"{whiteMove.Item4}{blackMove.Item4}");
                    
                    if (!string.IsNullOrEmpty(whiteMoveText) || !string.IsNullOrEmpty(blackMoveText))
                        sb.AppendLine(SafeString($"{moveNum,2}|{whiteMoveText,-10}|{blackMoveText,-10}|{captureDisplay,-5}|{whiteTimeStr}"));
                }
                sb.AppendLine(SafeString($"Duration: {(DateTime.Now - _gameStartTime).TotalMinutes:F1}m")); 
                sb.AppendLine(SafeString($"Moves: {_moveHistory.Count}"));
            }
            else
            {
                sb.AppendLine("Waiting for first move...\n\nWhite: gray symbols\nBlack: black symbols\nLight squares: white bg\nDark squares: brown bg");
            }
            _moveHistoryList.Text = SafeString(sb.ToString());
        }
        catch (Exception ex) { _moveHistoryList.Text = SafeString($"Move history unavailable\nError: {ex.Message}"); }
    }

    // Helper method to ensure strings are safe for Terminal.Gui
    private static string SafeString(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var result = new StringBuilder();
        foreach (char c in input)
        {
            if ((c >= 32 && c <= 126) || c == '\n' || c == '\r' || c == ' ' || c == '\t' || (c >= 0x00A1 && c <= 0x00FF) || (c >= 0x2000 && c <= 0x206F) || (c >= 0x2600 && c <= 0x26FF) || (c >= 0x2700 && c <= 0x27BF)) result.Append(c);
        }
        return result.ToString();
    }

    private static string SafeTruncate(string input, int maxLength) => string.IsNullOrEmpty(input) ? "" : input.Length <= maxLength ? input : maxLength <= 2 ? input.Substring(0, Math.Max(1, maxLength)) : input.Substring(0, maxLength - 2) + "..";

    private static char GetPieceAtSquare(string fen, string square)
    {
        try
        {
            if (string.IsNullOrEmpty(square) || square.Length != 2) return ' ';
            var file = square[0] - 'a'; var rank = square[1] - '1';
            if (file < 0 || file > 7 || rank < 0 || rank > 7) return ' ';
            var fenRank = fen.Split(' ')[0].Split('/')[7 - rank];
            int currentFile = 0;
            foreach (char c in fenRank)
            {
                if (char.IsDigit(c)) { int emptySquares = c - '0'; if (currentFile + emptySquares > file) return ' '; currentFile += emptySquares; }
                else { if (currentFile == file) return c; currentFile++; }
            }
            return ' ';
        }
        catch (Exception) { return ' '; }
    }

    private static string ExtractDestinationSquare(string move)
    {
        try
        {
            if (string.IsNullOrEmpty(move)) return "";
            move = move.Replace("+", "").Replace("#", "").Replace("=Q", "").Replace("=R", "").Replace("=B", "").Replace("=N", "");
            if (move == "O-O" || move == "O-O-O") return "";
            if (move.Length >= 2)
            {
                var lastTwo = move.Substring(move.Length - 2);
                if (lastTwo.Length == 2 && lastTwo[0] >= 'a' && lastTwo[0] <= 'h' && lastTwo[1] >= '1' && lastTwo[1] <= '8') return lastTwo;
            }
            return "";
        }
        catch (Exception) { return ""; }
    }

    private static string PieceToSymbol(char piece)
    {
        var pieceSymbols = new Dictionary<char, char> { { 'Q', '♛' }, { 'q', '♛' }, { 'R', '♜' }, { 'r', '♜' }, { 'B', '♝' }, { 'b', '♝' }, { 'N', '♞' }, { 'n', '♞' }, { 'K', '♚' }, { 'k', '♚' }, { 'P', '♟' }, { 'p', '♟' } };
        return pieceSymbols.ContainsKey(piece) ? pieceSymbols[piece].ToString() : " ";
    }

    private static string ConvertToAlgebraicNotation(string coordinateMove, string currentFen)
    {
        try
        {
            if (string.IsNullOrEmpty(coordinateMove) || coordinateMove.Length < 4) return coordinateMove;
            var fromSquare = coordinateMove.Substring(0, 2); var toSquare = coordinateMove.Substring(2, 2);
            var promotion = coordinateMove.Length > 4 ? coordinateMove.Substring(4, 1) : "";
            var movingPiece = GetPieceAtSquare(currentFen, fromSquare); var capturedPiece = GetPieceAtSquare(currentFen, toSquare);
            
            if (char.ToLower(movingPiece) == 'k')
            {
                if (fromSquare == "e1" && toSquare == "g1") return "O-O"; if (fromSquare == "e1" && toSquare == "c1") return "O-O-O";
                if (fromSquare == "e8" && toSquare == "g8") return "O-O"; if (fromSquare == "e8" && toSquare == "c8") return "O-O-O";
            }

            var result = "";
            if (char.ToLower(movingPiece) != 'p') result += char.ToUpper(movingPiece);
            if (capturedPiece != ' ') { if (char.ToLower(movingPiece) == 'p') result += fromSquare[0]; result += "x"; }
            result += toSquare;
            if (!string.IsNullOrEmpty(promotion)) result += "=" + char.ToUpper(promotion[0]);
            return result;
        }
        catch (Exception) { return coordinateMove; }
    }

    private static string ExtractCapturedPiece(string move) => string.IsNullOrEmpty(move) || !move.Contains('x') ? " " : "×";

    private static void UpdateNegotiationDisplay()
    {
        if (_negotiationLabel == null) return;
        try
        {
            var sb = new StringBuilder();
            if (_negotiationLog.Count > 0)
            {
                foreach (var entry in _negotiationLog)
                {
                    var safeEntry = SafeString(entry ?? "");
                    var displayEntry = safeEntry.Length > 33 ? safeEntry.Substring(0, 30) + "..." : safeEntry;
                    sb.AppendLine(displayEntry);
                }
            }
            else sb.AppendLine("Connection log will appear here");
            _negotiationLabel.Text = SafeString(sb.ToString()); _negotiationLabel.MoveEnd();
        }
        catch (Exception) { _negotiationLabel.Text = "Connection log unavailable"; }
    }

    private static void OnPlayAgainClicked() => _playAgainCallback?.Invoke();

    private static void OnStartClicked()
    {
        if (_startButton != null) { _startButton.Visible = false; _gameStarted = true; AddNegotiationEntry("Start AMQP..."); AddNegotiationEntry("Init engine..."); }
        _startGameCallback?.Invoke();
    }

    public static void SetStartGameCallback(Action callback) => _startGameCallback = callback;
    public static void SetPlayAgainCallback(Action callback) => _playAgainCallback = callback;

    public static void ShowError(string message) { if (_headless || !_initialized || _top == null) Console.WriteLine($"❌ ERROR: {message}"); else MessageBox.ErrorQuery("Error", message, "OK"); }
    public static void ShowSuccess(string message) { if (_initialized && _top != null) MessageBox.Query("Success", message, "OK"); else System.Console.WriteLine($"SUCCESS: {message}"); }
    public static void ShowInfo(string message) { if (_initialized && _top != null) MessageBox.Query("Info", message, "OK"); else System.Console.WriteLine($"INFO: {message}"); }
    public static void ShowTitle() => UpdateDisplay();

    public static void ShowConnectionStatus(string status) { _connection = status; AddNegotiationEntry($"~ {status}"); if (_headless) Console.WriteLine($"🔗 {status}"); else if (_initialized) UpdateDisplay(); }

    public static void ShowGameResult(string result)
    {
        _gameEnded = true; AddNegotiationEntry($"Result: {result}");
        if (_headless)
        {
            Console.WriteLine($"🏆 GAME RESULT: {result}");
            Console.WriteLine("Game completed in headless mode.");
        }
        else if (_initialized && _top != null)
        {
            var playAgain = MessageBox.Query("🏆 Game Result", $"🏆 GAME RESULT: {result}\n\nWould you like to play again?", "Yes", "No");
            if (playAgain == 0 && _playAgainCallback != null) _playAgainCallback(); else Application.RequestStop();
        }
        else
        {
            System.Console.WriteLine($"GAME RESULT: {result}\nWould you like to play again? (y/n)");
            var key = System.Console.ReadKey();
            if (key.KeyChar == 'y' || key.KeyChar == 'Y') _playAgainCallback?.Invoke();
        }
    }

    public static void SetColorMode(bool useColor) => _useColor = useColor;

    public static void AddMoveToHistory(string move, bool isWhiteMove)
    {
        var now = DateTime.Now;
        var algebraicMove = ConvertToAlgebraicNotation(move, _lastFen);
        var capturedPiece = ExtractCapturedPiece(algebraicMove);
        _moveHistory.Add((algebraicMove, now, isWhiteMove, capturedPiece));
        if (isWhiteMove) _whiteTime = now; else _blackTime = now;
    }

    public static void LogNegotiation(string message) { if (_headless) Console.WriteLine($"📋 {message}"); else AddNegotiationEntry(message); }
    public static void LogAmqpStep(string step) { if (_headless) Console.WriteLine($"🔧 A: {step}"); else AddNegotiationEntry($"A: {step}"); }
    public static void LogAmqpSendMove(string move) { if (_headless) Console.WriteLine($"📤 AMQP Send: {move}"); else AddNegotiationEntry($"AMQP Snd: {move}"); }
    public static void LogAmqpReceiveMove(string move) { if (_headless) Console.WriteLine($"📥 AMQP Receive: {move}"); else AddNegotiationEntry($"AMQP Rcv: {move}"); }
    public static void LogAmqpConnection(string status) { if (_headless) Console.WriteLine($"🔗 Connection: {status}"); else AddNegotiationEntry($"C: {status}"); }
    public static void LogAmqpSession(string status) { if (_headless) Console.WriteLine($"📡 Session: {status}"); else AddNegotiationEntry($"S: {status}"); }
    public static void LogAmqpLink(string status) { if (_headless) Console.WriteLine($"🔗 Link: {status}"); else AddNegotiationEntry($"L: {status}"); }
    public static void LogContainerStart() { if (_headless) Console.WriteLine("🐳 Container: start"); else AddNegotiationEntry("+ start"); }
    public static void LogContainerStop() { if (_headless) Console.WriteLine("🛑 Container: stop"); else AddNegotiationEntry("- stop"); }
    public static void LogError(string error) { if (_headless) Console.WriteLine($"❌ Error: {error}"); else AddNegotiationEntry($"! {error}"); }

    public static void Reset()
    {
        _moveHistory.Clear(); _negotiationLog.Clear(); _gameEnded = false;
        _lastFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"; _lastMove = ""; _status = ""; _connection = ""; _gameStartTime = DateTime.Now;
        var playerColor = _isWhite ? "White ⚪" : "Black ⚫";
        AddNegotiationEntry($"Reset {playerColor}"); AddNegotiationEntry($"@ {DateTime.Now:HH:mm:ss}"); UpdateDisplay();
    }

    public static void HandleCtrlC() { AddNegotiationEntry("Ctrl+C - shutdown..."); RequestShutdown(); }
    public static void RequestShutdown() { AddNegotiationEntry("Exit..."); if (_initialized && _top != null) Application.RequestStop(); }
    public static void Cleanup() => Application.Shutdown();

    public static void Run() 
    { 
        if (_top != null) 
        { 
            _isApplicationRunning = true; 
            try 
            { 
                // Ensure console has minimum required size for Terminal.Gui (Windows only)
                try
                {
                    if (OperatingSystem.IsWindows() && (Console.WindowWidth < 80 || Console.WindowHeight < 25))
                    {
                        Console.SetWindowSize(Math.Max(80, Console.WindowWidth), Math.Max(25, Console.WindowHeight));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not adjust console size: {ex.Message}");
                }
                
                Application.Run(_top); 
            } 
            catch (System.IndexOutOfRangeException ex)
            {
                Console.WriteLine($"❌ Terminal.Gui display error: {ex.Message}");
                Console.WriteLine("🔍 This typically happens when running in VS Code's integrated terminal.");
                Console.WriteLine("💡 Solutions:");
                Console.WriteLine("   1. Run in Windows Terminal: wt -d . .\\ChessAgent.exe --white");
                Console.WriteLine("   2. Run in Command Prompt: .\\ChessAgent.exe --white");
                Console.WriteLine("   3. Use headless mode: .\\ChessAgent.exe --white --headless");
                Console.WriteLine("   4. In VS Code: Terminal → New Terminal → Select 'Command Prompt'");
                Console.WriteLine();
                Console.WriteLine("🔄 Switching to headless mode automatically...");
                
                // Switch to headless mode and continue
                _headless = true;
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Terminal.Gui error: {ex.Message}");
                Console.WriteLine("💡 Try running with --headless flag or in a different terminal");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
            }
            finally 
            { 
                _isApplicationRunning = false; 
            } 
        } 
    }
}

public class ChessBoardView : View
{
    private static string _currentFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private static bool _isWhite = true;
    private static readonly Dictionary<char, char> ChessPieces = new() { { 'k', '♚' }, { 'q', '♛' }, { 'r', '♜' }, { 'b', '♝' }, { 'n', '♞' }, { 'p', '♟' }, { 'K', '♚' }, { 'Q', '♛' }, { 'R', '♜' }, { 'B', '♝' }, { 'N', '♞' }, { 'P', '♟' } };

    public ChessBoardView() { Width = 30; Height = 15; CanFocus = false; }
    public static void UpdateBoard(string fen, bool isWhite) { _currentFen = fen; _isWhite = isWhite; }

    public override void Redraw(Rect bounds)
    {
        try
        {
            var board = ParseFen(_currentFen);
            var whiteSquareAttr = new Terminal.Gui.Attribute(Color.Black, Color.White);
            var brownSquareAttr = new Terminal.Gui.Attribute(Color.Black, Color.Brown);
            var whitePieceOnWhiteAttr = new Terminal.Gui.Attribute(Color.Gray, Color.White);
            var whitePieceOnBrownAttr = new Terminal.Gui.Attribute(Color.Gray, Color.Brown);
            var blackPieceOnWhiteAttr = new Terminal.Gui.Attribute(Color.Black, Color.White);
            var blackPieceOnBrownAttr = new Terminal.Gui.Attribute(Color.Black, Color.Brown);

            Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
            
            // Bounds check before attempting to draw
            if (bounds.Width < 30 || bounds.Height < 15)
            {
                // Console too small, draw a simple message
                Move(0, 0);
                Driver.AddStr("Console too small for chess board");
                return;
            }
            
            for (int y = 0; y < bounds.Height && y < 15; y++) 
            { 
                Move(0, y); 
                for (int x = 0; x < bounds.Width && x < 30; x++) 
                    Driver.AddRune(' '); 
            }

            int row = 1;
            Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black)); 
            Move(2, 0); 
            Driver.AddStr(" a  b  c  d  e  f  g  h");

            for (int rank = _isWhite ? 7 : 0; _isWhite ? rank >= 0 : rank <= 7; rank += _isWhite ? -1 : 1)
            {
                if (row >= bounds.Height) break; // Prevent going outside bounds
                
                Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black)); 
                Move(0, row); 
                Driver.AddStr($"{rank + 1}");
                
                for (int file = 0; file < 8; file++)
                {
                    if (2 + file * 3 + 2 >= bounds.Width) break; // Prevent going outside bounds
                    
                    char piece = board[rank, file]; bool isLightSquare = (rank + file) % 2 == 1;
                    Move(2 + file * 3, row);

                    if (piece == ' ')
                    {
                        Driver.SetAttribute(isLightSquare ? whiteSquareAttr : brownSquareAttr);
                        Driver.AddStr("   ");
                    }
                    else
                    {
                        bool isWhitePiece = char.IsUpper(piece);
                        char pieceSymbol = ChessPieces.ContainsKey(piece) ? ChessPieces[piece] : piece;
                        Terminal.Gui.Attribute attr = isLightSquare ? (isWhitePiece ? whitePieceOnWhiteAttr : blackPieceOnWhiteAttr) : (isWhitePiece ? whitePieceOnBrownAttr : blackPieceOnBrownAttr);
                        Driver.SetAttribute(attr); Driver.AddStr($" {pieceSymbol} ");
                    }
                }
                
                if (26 < bounds.Width && row < bounds.Height)
                {
                    Move(26, row); 
                    Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black)); 
                    Driver.AddStr($"{rank + 1}"); 
                }
                row++;
            }
            
            if (row < bounds.Height)
            {
                Move(2, row); 
                Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black)); 
                Driver.AddStr(" a  b  c  d  e  f  g  h"); 
                row++;
            }
            
            if (row < bounds.Height)
            {
                var toMove = _currentFen.Contains(" w ") ? "White" : "Black"; 
                Move(2, row); 
                Driver.AddStr($"{toMove} to move");
            }
        }
        catch (System.IndexOutOfRangeException)
        {
            // Handle bounds issues gracefully
            try
            {
                Move(0, 0);
                Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                Driver.AddStr("Display error - resize terminal");
            }
            catch
            {
                // If we can't even draw the error message, just fail silently
            }
        }
        catch (Exception)
        {
            // Handle any other drawing exceptions gracefully
            try
            {
                Move(0, 0);
                Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                Driver.AddStr("Chess board display error");
            }
            catch
            {
                // If we can't even draw the error message, just fail silently
            }
        }
    }

    private static char[,] ParseFen(string fen)
    {
        var board = new char[8, 8]; var ranks = fen.Split(' ')[0].Split('/');
        for (int rank = 0; rank < 8; rank++)
        {
            int file = 0;
            foreach (char c in ranks[7 - rank])
            {
                if (char.IsDigit(c)) { int emptySquares = c - '0'; for (int i = 0; i < emptySquares; i++) { board[rank, file] = ' '; file++; } }
                else { board[rank, file] = c; file++; }
            }
        }
        return board;
    }
}
