using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Terminal.Gui;

namespace AliceMarkQuentinPaulPlayChess;

public static class ChessBoardDisplay
{
    // Terminal.Gui application and main window
    private static Toplevel? _top;
    private static Window? _mainWindow;
    private static Label? _titleLabel;
    private static ChessBoardView? _boardTextView;
    private static Label? _moveHistoryLabel;  // Moves display on the right - using Label for reliability
    private static Label? _moveHistoryList;  // Alternative to ListView to avoid Terminal.Gui issues
    private static TextView? _negotiationLabel;  // AMQP/Docker info under board - using TextView for scrolling
    private static Label? _statusLabel;
    private static Label? _connectionLabel;
    private static Button? _playAgainButton;
    private static Button? _startButton;
    private static bool _initialized = false;
    private static bool _isApplicationRunning = false;
    private static bool _gameEnded = false;
    private static bool _gameStarted = false;
    private static bool _isWhite = true;
    private static bool _useColor = true;
    private static string _lastFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private static string _lastMove = "";
    private static string _status = "";
    private static string _connection = "";
    private static List<(string move, DateTime time, bool isWhite, string capturedPiece)> _moveHistory = new();
    private static List<string> _negotiationLog = new();
    private static DateTime _gameStartTime = DateTime.Now;
    private static DateTime _whiteTime = DateTime.Now;
    private static DateTime _blackTime = DateTime.Now;
    private static Action? _playAgainCallback;
    private static Action? _startGameCallback;

    public static void Initialize(bool isWhite)
    {
        _initialized = true;
        _isWhite = isWhite;
        _gameStartTime = DateTime.Now;
        _whiteTime = DateTime.Now;
        _blackTime = DateTime.Now;
        _moveHistory.Clear();
        _negotiationLog.Clear();
        _gameEnded = false;

        // Initialize Terminal.Gui
        Application.Init();
        _top = Application.Top;

        // Define elegant, clean color schemes
        var titleColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.White, Color.Blue),
            Focus = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Blue)
        };

        var boardColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.Black, Color.White),
            Focus = new Terminal.Gui.Attribute(Color.Black, Color.White)
        };

        // Color schemes for chess pieces
        var whitePieceColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.Gray, Color.White), // Gray pieces on white squares
            Focus = new Terminal.Gui.Attribute(Color.Gray, Color.White)
        };

        var blackPieceColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.Black, Color.Brown), // Black pieces on brown squares
            Focus = new Terminal.Gui.Attribute(Color.Black, Color.Brown)
        };

        var movesColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.White, Color.DarkGray),
            Focus = new Terminal.Gui.Attribute(Color.BrightYellow, Color.DarkGray)
        };

        var negotiationColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.Gray, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black)
        };

        var statusColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black)
        };

        var connectionColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightMagenta, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.BrightMagenta, Color.Black)
        };

        // Create main window with clean design
        _mainWindow = new Window("♟️ ChessAgent - Professional Chess Interface")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = new ColorScheme
            {
                Normal = new Terminal.Gui.Attribute(Color.White, Color.Black)
            }
        };

        // Create title label
        var playerColor = _isWhite ? "White ⚪" : "Black ⚫";
        var titleText = $"♟️ ChessAgent - Playing as {playerColor}";
        _titleLabel = new Label(titleText)
        {
            X = Pos.Center(),
            Y = 0,
            ColorScheme = titleColorScheme
        };

        // Create custom chess board view with proper color support
        _boardTextView = new ChessBoardView()
        {
            X = 1,
            Y = 2,
            Width = 30,
            Height = 15
        };

        // Create move history table (positioned at right side, full height) - FIXED: using Label instead of ListView
        _moveHistoryLabel = new Label("")
        {
            X = 37,
            Y = 3,
            Width = 42,
            Height = 3,
            ColorScheme = movesColorScheme
        };

        // Create scrollable move history display using Label (more reliable than ListView)
        _moveHistoryList = new Label("")
        {
            X = 37,
            Y = 6,
            Width = 42,
            Height = 19,
            ColorScheme = movesColorScheme
        };

        // Create negotiation/AMQP log (positioned under the board) - using TextView for scrolling
        _negotiationLabel = new TextView()
        {
            X = 1,
            Y = 21,
            Width = 35,
            Height = 8,
            ColorScheme = negotiationColorScheme,
            ReadOnly = true,
            WordWrap = false
        };

        // Create status label
        _statusLabel = new Label("🔄 Initializing...")
        {
            X = 1,
            Y = 29,
            Width = 50,
            Height = 1,
            ColorScheme = statusColorScheme
        };

        // Create connection status label
        _connectionLabel = new Label("📡 Starting AMQP connection...")
        {
            X = 1,
            Y = 30,
            Width = 70,
            Height = 1,
            ColorScheme = connectionColorScheme
        };

        // Create play again button (initially hidden)
        _playAgainButton = new Button("🎮 Play Again")
        {
            X = Pos.Center(),
            Y = 31,
            IsDefault = true,
            Visible = false
        };
        _playAgainButton.Clicked += OnPlayAgainClicked;

        // Create start button (initially visible)
        _startButton = new Button("🚀 Start AMQP Connection")
        {
            X = Pos.Center(),
            Y = 10,
            IsDefault = true,
            Visible = true
        };
        _startButton.Clicked += OnStartClicked;

        // Add section titles
        var movesTitle = new Label("♟️ Move History")
        {
            X = 37,
            Y = 2,
            ColorScheme = titleColorScheme
        };

        var negotiationTitle = new Label("🔗 Connection Log")
        {
            X = 1,
            Y = 20,
            ColorScheme = titleColorScheme
        };

        _mainWindow.Add(_titleLabel);
        _mainWindow.Add(_boardTextView);
        _mainWindow.Add(movesTitle);
        _mainWindow.Add(_moveHistoryLabel);
        _mainWindow.Add(_moveHistoryList);
        _mainWindow.Add(negotiationTitle);
        _mainWindow.Add(_negotiationLabel);
        _mainWindow.Add(_statusLabel);
        _mainWindow.Add(_connectionLabel);
        _mainWindow.Add(_playAgainButton);
        _mainWindow.Add(_startButton);
        _top.Add(_mainWindow);

        // Add keyboard shortcuts
        _mainWindow.KeyPress += (args) =>
        {
            if (args.KeyEvent.Key == (Key.CtrlMask | Key.q))
            {
                AddNegotiationEntry("Ctrl+Q - exit");
                Application.RequestStop();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == (Key.CtrlMask | Key.c))
            {
                AddNegotiationEntry("Ctrl+C - exit");
                Application.RequestStop();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == Key.Esc)
            {
                AddNegotiationEntry("ESC - exit");
                Application.RequestStop();
                args.Handled = true;
            }
        };

        // Also add global application-level Ctrl+C handling
        Application.Top.KeyPress += (args) =>
        {
            if (args.KeyEvent.Key == (Key.CtrlMask | Key.c))
            {
                AddNegotiationEntry("Ctrl+C global");
                Application.RequestStop();
                args.Handled = true;
            }
        };

        // Add a global key handler that catches all key events
        _top.KeyPress += (args) =>
        {
            if (args.KeyEvent.Key == (Key.CtrlMask | Key.c))
            {
                AddNegotiationEntry("Ctrl+C intercept");
                Application.RequestStop();
                args.Handled = true;
            }
        };

        // Add negotiation log entry
        AddNegotiationEntry($"Init {playerColor}");
        AddNegotiationEntry($"@ {DateTime.Now:HH:mm:ss}");
        
        // Add Docker setup documentation
        AddNegotiationEntry("Docker req:");
        AddNegotiationEntry("1. Start Docker Desktop");
        AddNegotiationEntry("2. Pull niklasf/stockfish");
        AddNegotiationEntry("3. Ensure container access");
        AddNegotiationEntry("4. Click Start when ready");
        AddNegotiationEntry("Both players need AMQP");
        AddNegotiationEntry("Ctrl+C/Q/ESC to exit");

        // Draw initial board
        UpdateDisplay();
        
        // Don't start the event loop here - let the main application control it
    }

    public static void SetIsWhite(bool isWhite)
    {
        _isWhite = isWhite;
        if (_initialized)
        {
            UpdateDisplay();
        }
    }

    public static void UpdateBoard(string fen, string? lastMove = null, string status = "", string connection = "")
    {
        // Add move to history if this is a new move
        if (!string.IsNullOrEmpty(lastMove) && lastMove != _lastMove)
        {
            var now = DateTime.Now;
            // Determine if this is a white or black move based on current history count
            var isWhiteMove = _moveHistory.Count % 2 == 0;
            
            // Convert coordinate notation to algebraic notation
            var algebraicMove = ConvertToAlgebraicNotation(lastMove, _lastFen);
            
            // Detect captured piece by checking what was on the destination square in the previous position
            string capturedPiece = " ";
            
            if (!string.IsNullOrEmpty(_lastFen) && algebraicMove.Contains('x'))
            {
                var destSquare = ExtractDestinationSquare(algebraicMove);
                
                if (!string.IsNullOrEmpty(destSquare))
                {
                    var pieceOnDestSquare = GetPieceAtSquare(_lastFen, destSquare);
                    
                    if (pieceOnDestSquare != ' ')
                    {
                        capturedPiece = PieceToSymbol(pieceOnDestSquare);
                    }
                }
            }
            
            // Store the algebraic notation instead of the coordinate notation
            _moveHistory.Add((algebraicMove, now, isWhiteMove, capturedPiece));
            
            if (isWhiteMove)
                _whiteTime = now;
            else
                _blackTime = now;
                
            // Don't log moves to connection log - moves are shown in move history
        }
        
        _lastFen = fen;
        _lastMove = lastMove ?? _lastMove;
        _status = status;
        _connection = connection;

        UpdateDisplay();
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
        try 
        {
            if (_isApplicationRunning)
            {
                Application.Refresh();
            }
        }
        catch (Exception)
        {
            // Ignore refresh errors if Terminal.Gui isn't fully ready
        }
    }

    private static void AddNegotiationEntry(string message)
    {
        var seconds = (int)(DateTime.Now - _gameStartTime).TotalSeconds;
        var timePrefix = $"{seconds,3}s";
        
        // Ultra terse format: limit message to fit in available width (35 chars - 5 for time - 1 for space = 29 chars)
        var maxMessageLength = 29;
        var safeMessage = SafeString(message ?? "");
        var truncatedMessage = safeMessage.Length > maxMessageLength ? safeMessage.Substring(0, maxMessageLength-2) + ".." : safeMessage;
        
        _negotiationLog.Add($"{timePrefix} {truncatedMessage}");
        UpdateNegotiationDisplay();
    }

    private static void UpdateMoveHistoryDisplay()
    {
        if (_moveHistoryLabel == null || _moveHistoryList == null) return;

        // Update the header label
        _moveHistoryLabel.Text = SafeString("# | White    | Black    | Cap | Sec\n--|----------|----------|-----|----");

        // Create move display text with proper validation
        var sb = new StringBuilder();
        
        try
        {
            if (_moveHistory.Count > 0)
            {
                // Add moves in pairs (White, Black) - only show recent moves to fit in display
                var recentMoves = _moveHistory.TakeLast(17).ToList(); // Fit in 19 line height with some footer
                
                for (int i = 0; i < recentMoves.Count; i += 2)
                {
                    var moveNum = (i / 2) + 1;
                    var whiteMove = i < recentMoves.Count ? recentMoves[i] : (string.Empty, DateTime.MinValue, true, " ");
                    var blackMove = (i + 1) < recentMoves.Count ? recentMoves[i + 1] : (string.Empty, DateTime.MinValue, false, " ");
                    
                    var whiteTimeStr = whiteMove.Item1 != string.Empty ? 
                        $"{(int)(whiteMove.Item2 - _gameStartTime).TotalSeconds,3}" : "   ";
                    
                    var whiteMoveText = SafeString(whiteMove.Item1);
                    var blackMoveText = SafeString(blackMove.Item1);
                    
                    // Use the stored captured piece information instead of trying to extract it from move notation
                    var whiteCaptured = whiteMove.Item4; // capturedPiece
                    var blackCaptured = blackMove.Item4; // capturedPiece
                    var captureDisplay = SafeString($"{whiteCaptured}{blackCaptured}");
                    
                    if (!string.IsNullOrEmpty(whiteMoveText) || !string.IsNullOrEmpty(blackMoveText))
                    {
                        // Ensure strings fit in the column width (10 chars each) with safe truncation
                        whiteMoveText = SafeTruncate(whiteMoveText, 10);
                        blackMoveText = SafeTruncate(blackMoveText, 10);
                        
                        var moveEntry = SafeString($"{moveNum,2}|{whiteMoveText,-10}|{blackMoveText,-10}|{captureDisplay,-5}|{whiteTimeStr}");
                        sb.AppendLine(moveEntry);
                    }
                }
                
                // Add footer information with safe strings
                sb.AppendLine(SafeString($"Duration: {(DateTime.Now - _gameStartTime).TotalMinutes:F1}m"));
                sb.AppendLine(SafeString($"Moves: {_moveHistory.Count}"));
            }
            else
            {
                sb.AppendLine("Waiting for first move...");
                sb.AppendLine("");
                sb.AppendLine("White: gray symbols");
                sb.AppendLine("Black: black symbols");
                sb.AppendLine("Light squares: white bg");
                sb.AppendLine("Dark squares: brown bg");
            }
            
            // Update Label with the text
            _moveHistoryList.Text = SafeString(sb.ToString());
        }
        catch (Exception ex)
        {
            // Fallback to simple display if there are any issues
            _moveHistoryList.Text = SafeString($"Move history unavailable\nError: {ex.Message}");
        }
    }

    // Helper method to ensure strings are safe for Terminal.Gui
    private static string SafeString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "";
            
        // More permissive character filtering - allow most printable characters
        var result = new StringBuilder();
        foreach (char c in input)
        {
            if (c >= 32 && c <= 126) // Basic ASCII printable characters
                result.Append(c);
            else if (c == '\n' || c == '\r') // Allow line breaks
                result.Append(c);
            else if (c == ' ' || c == '\t') // Allow basic whitespace
                result.Append(c);
            else if (c >= 0x00A1 && c <= 0x00FF) // Allow Latin-1 supplement (includes accented chars)
                result.Append(c);
            else if (c >= 0x2000 && c <= 0x206F) // Allow general punctuation
                result.Append(c);
            else if (c >= 0x2600 && c <= 0x26FF) // Allow chess symbols and miscellaneous symbols
                result.Append(c);
            else if (c >= 0x2700 && c <= 0x27BF) // Allow dingbats
                result.Append(c);
            // Skip problematic characters silently instead of replacing with '?'
        }
        return result.ToString();
    }

    // Helper method to safely truncate strings
    private static string SafeTruncate(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input))
            return "";
            
        if (input.Length <= maxLength)
            return input;
            
        if (maxLength <= 2)
            return input.Substring(0, Math.Max(1, maxLength));
            
        return input.Substring(0, maxLength - 2) + "..";
    }

    // Helper method to parse FEN and get piece at specific square
    private static char GetPieceAtSquare(string fen, string square)
    {
        try
        {
            if (string.IsNullOrEmpty(square) || square.Length != 2)
                return ' ';

            var file = square[0] - 'a'; // 0-7
            var rank = square[1] - '1'; // 0-7

            if (file < 0 || file > 7 || rank < 0 || rank > 7)
                return ' ';

            var parts = fen.Split(' ');
            var ranks = parts[0].Split('/');
            var fenRank = ranks[7 - rank]; // FEN starts from rank 8

            int currentFile = 0;
            foreach (char c in fenRank)
            {
                if (char.IsDigit(c))
                {
                    int emptySquares = c - '0';
                    if (currentFile + emptySquares > file)
                        return ' '; // Empty square
                    currentFile += emptySquares;
                }
                else
                {
                    if (currentFile == file)
                        return c;
                    currentFile++;
                }
            }
            return ' ';
        }
        catch (Exception)
        {
            return ' ';
        }
    }

    // Helper method to extract destination square from move notation
    private static string ExtractDestinationSquare(string move)
    {
        try
        {
            if (string.IsNullOrEmpty(move))
                return "";

            // Remove check/checkmate indicators
            move = move.Replace("+", "").Replace("#", "").Replace("=Q", "").Replace("=R", "").Replace("=B", "").Replace("=N", "");

            // Handle castling
            if (move == "O-O" || move == "O-O-O")
                return "";

            // For normal moves, the destination square is typically the last 2 characters
            if (move.Length >= 2)
            {
                var lastTwo = move.Substring(move.Length - 2);
                if (lastTwo.Length == 2 && lastTwo[0] >= 'a' && lastTwo[0] <= 'h' && lastTwo[1] >= '1' && lastTwo[1] <= '8')
                    return lastTwo;
            }

            return "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    // Helper method to convert piece character to chess symbol
    private static string PieceToSymbol(char piece)
    {
        var pieceSymbols = new Dictionary<char, char>
        {
            { 'Q', '♛' }, { 'q', '♛' },
            { 'R', '♜' }, { 'r', '♜' },
            { 'B', '♝' }, { 'b', '♝' },
            { 'N', '♞' }, { 'n', '♞' },
            { 'K', '♚' }, { 'k', '♚' },
            { 'P', '♟' }, { 'p', '♟' }
        };

        return pieceSymbols.ContainsKey(piece) ? pieceSymbols[piece].ToString() : " ";
    }

    // Helper method to convert coordinate notation (e.g., "d2d4") to algebraic notation (e.g., "d4")
    private static string ConvertToAlgebraicNotation(string coordinateMove, string currentFen)
    {
        try
        {
            if (string.IsNullOrEmpty(coordinateMove) || coordinateMove.Length < 4)
                return coordinateMove; // Return as-is if not coordinate notation

            // Parse coordinate move (e.g., "d2d4", "e1g1")
            var fromSquare = coordinateMove.Substring(0, 2);
            var toSquare = coordinateMove.Substring(2, 2);
            
            // Handle promotion (e.g., "d7d8q")
            var promotion = coordinateMove.Length > 4 ? coordinateMove.Substring(4, 1) : "";

            // Get the piece that's moving
            var movingPiece = GetPieceAtSquare(currentFen, fromSquare);
            var capturedPiece = GetPieceAtSquare(currentFen, toSquare);
            
            // Handle castling
            if (char.ToLower(movingPiece) == 'k')
            {
                // King moves
                if (fromSquare == "e1" && toSquare == "g1") return "O-O";   // White kingside
                if (fromSquare == "e1" && toSquare == "c1") return "O-O-O"; // White queenside
                if (fromSquare == "e8" && toSquare == "g8") return "O-O";   // Black kingside
                if (fromSquare == "e8" && toSquare == "c8") return "O-O-O"; // Black queenside
            }

            var result = "";
            
            // Add piece symbol (except for pawns)
            if (char.ToLower(movingPiece) != 'p')
            {
                result += char.ToUpper(movingPiece);
            }
            
            // Add capture indicator
            if (capturedPiece != ' ')
            {
                // For pawn captures, add the file letter
                if (char.ToLower(movingPiece) == 'p')
                {
                    result += fromSquare[0]; // Add file (e.g., 'e' in "exd5")
                }
                result += "x";
            }
            
            // Add destination square
            result += toSquare;
            
            // Add promotion
            if (!string.IsNullOrEmpty(promotion))
            {
                result += "=" + char.ToUpper(promotion[0]);
            }
            
            // Note: We're not adding check (+) or checkmate (#) indicators here
            // as that would require analyzing the resulting position
            
            return result;
        }
        catch (Exception)
        {
            return coordinateMove; // Return original if conversion fails
        }
    }

    private static string ExtractCapturedPiece(string move)
    {
        if (string.IsNullOrEmpty(move) || !move.Contains('x'))
            return " ";
            
        try
        {
            // This is a fallback method when we don't have board state
            // It can't determine what piece was captured, so just indicate a capture occurred
            return "×"; // Multiplication sign to indicate capture
        }
        catch (Exception)
        {
            return " ";
        }
    }

    private static void UpdateNegotiationDisplay()
    {
        if (_negotiationLabel == null) return;

        try
        {
            // Create the full log text with proper line breaks
            var sb = new StringBuilder();
            
            if (_negotiationLog.Count > 0)
            {
                foreach (var entry in _negotiationLog)
                {
                    // Make sure entry is safe and truncate if needed to fit display width
                    var safeEntry = SafeString(entry ?? "");
                    var displayEntry = safeEntry.Length > 33 ? safeEntry.Substring(0, 30) + "..." : safeEntry;
                    sb.AppendLine(displayEntry);
                }
            }
            else
            {
                sb.AppendLine("Connection log will appear here");
            }
            
            // Update TextView with the full text
            _negotiationLabel.Text = SafeString(sb.ToString());
            
            // Auto-scroll to the bottom to show the latest entries
            _negotiationLabel.MoveEnd();
        }
        catch (Exception)
        {
            // Fallback display in case of any issues
            _negotiationLabel.Text = "Connection log unavailable";
        }
    }

    private static void OnPlayAgainClicked()
    {
        _playAgainCallback?.Invoke();
    }

    private static void OnStartClicked()
    {
        if (_startButton != null)
        {
            _startButton.Visible = false;
            _gameStarted = true;
            AddNegotiationEntry("Start AMQP...");
            AddNegotiationEntry("Init engine...");
        }
        _startGameCallback?.Invoke();
    }

    public static void SetStartGameCallback(Action callback)
    {
        _startGameCallback = callback;
    }

    public static void SetPlayAgainCallback(Action callback)
    {
        _playAgainCallback = callback;
    }

    public static void ShowError(string message)
    {
        if (_initialized && _top != null)
        {
            // Integration with Terminal.Gui - show a MessageBox
            MessageBox.ErrorQuery("Error", message, "OK");
        }
        else
        {
            // Fallback to console if Terminal.Gui not initialized
            System.Console.WriteLine($"ERROR: {message}");
        }
    }

    public static void ShowSuccess(string message)
    {
        if (_initialized && _top != null)
        {
            // Integration with Terminal.Gui - show a MessageBox
            MessageBox.Query("Success", message, "OK");
        }
        else
        {
            // Fallback to console if Terminal.Gui not initialized
            System.Console.WriteLine($"SUCCESS: {message}");
        }
    }

    public static void ShowInfo(string message)
    {
        if (_initialized && _top != null)
        {
            // Integration with Terminal.Gui - show a MessageBox  
            MessageBox.Query("Info", message, "OK");
        }
        else
        {
            // Fallback to console if Terminal.Gui not initialized
            System.Console.WriteLine($"INFO: {message}");
        }
    }

    public static void ShowTitle()
    {
        // Title is now integrated into the display
        UpdateDisplay();
    }

    public static void ShowConnectionStatus(string status)
    {
        _connection = status;
        AddNegotiationEntry($"~ {status}");
        // Update the display to show new connection status
        if (_initialized)
        {
            UpdateDisplay();
        }
    }

    public static void ShowGameResult(string result)
    {
        _gameEnded = true;
        AddNegotiationEntry($"Result: {result}");
        
        if (_initialized && _top != null)
        {
            // Show game result and ask if they want to play again
            var playAgain = MessageBox.Query("🏆 Game Result", $"🏆 GAME RESULT: {result}\n\nWould you like to play again?", "Yes", "No");
            if (playAgain == 0 && _playAgainCallback != null) // Yes was clicked
            {
                _playAgainCallback();
            }
            else
            {
                Application.RequestStop();
            }
        }
        else
        {
            // Fallback to console if Terminal.Gui not initialized
            System.Console.WriteLine($"GAME RESULT: {result}");
            System.Console.WriteLine("Would you like to play again? (y/n)");
            var key = System.Console.ReadKey();
            if (key.KeyChar == 'y' || key.KeyChar == 'Y')
            {
                _playAgainCallback?.Invoke();
            }
        }
    }

    public static void SetColorMode(bool useColor)
    {
        _useColor = useColor;
    }

    public static void AddMoveToHistory(string move, bool isWhiteMove)
    {
        var now = DateTime.Now;
        // Convert coordinate notation to algebraic notation
        var algebraicMove = ConvertToAlgebraicNotation(move, _lastFen);
        // For this method, we don't have access to board state to detect captures
        // So we'll use the old method as fallback
        var capturedPiece = ExtractCapturedPiece(algebraicMove);
        _moveHistory.Add((algebraicMove, now, isWhiteMove, capturedPiece));
        
        if (isWhiteMove)
            _whiteTime = now;
        else
            _blackTime = now;
            
        // Don't log moves to connection log - moves are shown in move history panel
    }

    public static void LogNegotiation(string message)
    {
        AddNegotiationEntry(message);
    }

    public static void LogAmqpStep(string step)
    {
        AddNegotiationEntry($"A: {step}");
    }

    public static void LogAmqpSendMove(string move)
    {
        AddNegotiationEntry($"AMQP Snd: {move}");
    }

    public static void LogAmqpReceiveMove(string move)
    {
        AddNegotiationEntry($"AMQP Rcv: {move}");
    }

    public static void LogAmqpConnection(string status)
    {
        AddNegotiationEntry($"C: {status}");
    }

    public static void LogAmqpSession(string status)
    {
        AddNegotiationEntry($"S: {status}");
    }

    public static void LogAmqpLink(string status)
    {
        AddNegotiationEntry($"L: {status}");
    }

    public static void LogContainerStart()
    {
        AddNegotiationEntry("+ start");
    }

    public static void LogContainerStop()
    {
        AddNegotiationEntry("- stop");
    }

    public static void LogError(string error)
    {
        AddNegotiationEntry($"! {error}");
    }

    public static void Reset()
    {
        _moveHistory.Clear();
        _negotiationLog.Clear();
        _gameEnded = false;
        _lastFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        _lastMove = "";
        _status = "";
        _connection = "";
        _gameStartTime = DateTime.Now;
        
        var playerColor = _isWhite ? "White ⚪" : "Black ⚫";
        AddNegotiationEntry($"Reset {playerColor}");
        AddNegotiationEntry($"@ {DateTime.Now:HH:mm:ss}");
        
        UpdateDisplay();
    }

    public static void HandleCtrlC()
    {
        AddNegotiationEntry("Ctrl+C - shutdown...");
        RequestShutdown();
    }

    public static void RequestShutdown()
    {
        AddNegotiationEntry("Exit...");
        if (_initialized && _top != null)
        {
            Application.RequestStop();
        }
    }

    public static void Cleanup()
    {
        Application.Shutdown();
    }

    public static void Run()
    {
        // Start the Terminal.Gui application loop on the main thread
        // This will block until the application is closed
        if (_top != null)
        {
            _isApplicationRunning = true;
            try 
            {
                Application.Run(_top);
            }
            finally
            {
                _isApplicationRunning = false;
            }
        }
    }
}

/// <summary>
/// Custom chess board view that draws the board with proper colors using Terminal.Gui drawing primitives
/// </summary>
public class ChessBoardView : View
{
    private static string _currentFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private static bool _isWhite = true;
    
    // Chess piece symbols - using solid black Unicode symbols for all pieces
    private static readonly Dictionary<char, char> ChessPieces = new()
    {
        // Black pieces (lowercase)
        { 'k', '♚' }, { 'q', '♛' }, { 'r', '♜' }, { 'b', '♝' }, { 'n', '♞' }, { 'p', '♟' },
        // White pieces (uppercase) - using same solid symbols, will be colored gray
        { 'K', '♚' }, { 'Q', '♛' }, { 'R', '♜' }, { 'B', '♝' }, { 'N', '♞' }, { 'P', '♟' }
    };

    public ChessBoardView()
    {
        Width = 30;  // Reduced width since no borders needed
        Height = 15; // Reduced height since no borders needed
        CanFocus = false;
    }

    public static void UpdateBoard(string fen, bool isWhite)
    {
        _currentFen = fen;
        _isWhite = isWhite;
    }

    public override void Redraw(Rect bounds)
    {
        // Parse the current board position
        var board = ParseFen(_currentFen);
        
        // Define colors for the chess board
        var whiteSquareAttr = new Terminal.Gui.Attribute(Color.Black, Color.White);      // Black text on white background
        var brownSquareAttr = new Terminal.Gui.Attribute(Color.Black, Color.Brown);      // Black text on brown background
        var whitePieceOnWhiteAttr = new Terminal.Gui.Attribute(Color.Gray, Color.White); // Gray piece on white square
        var whitePieceOnBrownAttr = new Terminal.Gui.Attribute(Color.Gray, Color.Brown); // Gray piece on brown square
        var blackPieceOnWhiteAttr = new Terminal.Gui.Attribute(Color.Black, Color.White); // Black piece on white square
        var blackPieceOnBrownAttr = new Terminal.Gui.Attribute(Color.Black, Color.Brown); // Black piece on brown square

        // Clear the view first
        Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
        for (int y = 0; y < bounds.Height; y++)
        {
            Move(0, y);
            for (int x = 0; x < bounds.Width; x++)
            {
                Driver.AddRune(' ');
            }
        }

        int row = 1;
        
        // Draw file labels (a-h)
        Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
        Move(2, 0);
        Driver.AddStr(" a  b  c  d  e  f  g  h");

        // Draw board rows (no borders needed with colored squares)
        for (int rank = _isWhite ? 7 : 0; _isWhite ? rank >= 0 : rank <= 7; rank += _isWhite ? -1 : 1)
        {
            // Draw rank number on left
            Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
            Move(0, row);
            Driver.AddStr($"{rank + 1}");

            // Draw squares for this rank (each square is 3 characters wide)
            for (int file = 0; file < 8; file++)
            {
                char piece = board[rank, file];
                bool isLightSquare = (rank + file) % 2 == 1; // a1 is dark square
                
                // Move to square position
                Move(2 + file * 3, row);

                if (piece == ' ')
                {
                    // Empty square - fill with background color
                    if (isLightSquare)
                    {
                        Driver.SetAttribute(whiteSquareAttr);
                        Driver.AddStr("   ");
                    }
                    else
                    {
                        Driver.SetAttribute(brownSquareAttr);
                        Driver.AddStr("   ");
                    }
                }
                else
                {
                    // Square with piece
                    bool isWhitePiece = char.IsUpper(piece);
                    char pieceSymbol = ChessPieces.ContainsKey(piece) ? ChessPieces[piece] : piece;

                    // Select appropriate color combination
                    Terminal.Gui.Attribute attr;
                    if (isLightSquare)
                    {
                        attr = isWhitePiece ? whitePieceOnWhiteAttr : blackPieceOnWhiteAttr;
                    }
                    else
                    {
                        attr = isWhitePiece ? whitePieceOnBrownAttr : blackPieceOnBrownAttr;
                    }

                    Driver.SetAttribute(attr);
                    Driver.AddStr($" {pieceSymbol} ");
                }
            }

            // Draw rank number on right
            Move(26, row);
            Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
            Driver.AddStr($"{rank + 1}");

            row++;
        }

        // Draw file labels again at bottom
        Move(2, row);
        Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
        Driver.AddStr(" a  b  c  d  e  f  g  h");
        row++;

        // Draw status information
        var toMove = _currentFen.Contains(" w ") ? "White" : "Black";
        Move(2, row);
        Driver.AddStr($"{toMove} to move");
        row++;

        
    }

    private static char[,] ParseFen(string fen)
    {
        var board = new char[8, 8];
        var parts = fen.Split(' ');
        var ranks = parts[0].Split('/');

        for (int rank = 0; rank < 8; rank++)
        {
            int file = 0;
            foreach (char c in ranks[7 - rank]) // FEN starts from rank 8
            {
                if (char.IsDigit(c))
                {
                    int emptySquares = c - '0';
                    for (int i = 0; i < emptySquares; i++)
                    {
                        board[rank, file] = ' ';
                        file++;
                    }
                }
                else
                {
                    board[rank, file] = c;
                    file++;
                }
            }
        }

        return board;
    }
}
