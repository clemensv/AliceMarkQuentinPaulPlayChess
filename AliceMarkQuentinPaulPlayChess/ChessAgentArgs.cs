using System;

namespace AliceMarkQuentinPaulPlayChess;

/// <summary>Command line arguments for ChessAgent with modern options and legacy support</summary>
public sealed class ChessAgentArgs
{
    public string Bind { get; init; } = "";
    public string Connect { get; init; } = "";
    public bool White { get; init; }
    public bool Verbose { get; init; }
    public bool UseColor { get; init; } = true;
    public bool SingleSession { get; init; }
    public bool Headless { get; init; }

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
            var singleSession = false;
            var headless = false;

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
                    case "--single-session":
                        singleSession = true;
                        break;
                    case "--headless":
                        headless = true;
                        verbose = true; // Headless implies verbose logging
                        break;
                }
            }

            // Auto-configure addresses if not provided (especially for headless mode)
            if (string.IsNullOrEmpty(bind))
            {
                bind = white ? "amqp://localhost:5672" : "amqp://localhost:5673";
            }
            if (string.IsNullOrEmpty(connect) && !singleSession)
            {
                connect = white ? "amqp://localhost:5673" : "amqp://localhost:5672";
            }
            
            // Single-session mode validation and auto-configuration
            if (singleSession)
            {
                if (white && string.IsNullOrEmpty(connect))
                {
                    connect = "amqp://localhost:5673"; // White connects to black's port
                }
                if (!white && !string.IsNullOrEmpty(connect))
                {
                    ChessBoardDisplay.ShowError("Single-session mode: Black player should not have --connect address");
                    ShowUsage();
                    Environment.Exit(1);
                }
            }

            return new ChessAgentArgs 
            { 
                Bind = bind, 
                Connect = connect, 
                White = white, 
                Verbose = verbose,
                UseColor = useColor,
                SingleSession = singleSession,
                Headless = headless
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
            UseColor = true,
            SingleSession = false,
            Headless = false
        };
    }

    private static void ShowUsage()
    {
        Console.WriteLine();
        Console.WriteLine("🎮 ChessAgent - AMQP 1.0 Chess Game");
        Console.WriteLine("====================================");
        Console.WriteLine();
        Console.WriteLine("Usage: ChessAgent [options]");
        Console.WriteLine();
        Console.WriteLine("Required Options:");
        Console.WriteLine("  --bind <uri>        Local AMQP endpoint to listen on");
        Console.WriteLine("  --connect <uri>     Remote AMQP endpoint to connect to");
        Console.WriteLine("  --color <color>     Playing color: 'white' or 'black'");
        Console.WriteLine();
        Console.WriteLine("Optional Options:");
        Console.WriteLine("  --verbose           Enable detailed logging");
        Console.WriteLine("  --no-color          Disable colored output");
        Console.WriteLine("  --single-session    Use single-session mode");
        Console.WriteLine("  --headless          Run without UI (implies --verbose, auto-starts)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine();
        Console.WriteLine("  Normal Mode (both players connect to each other):");
        Console.WriteLine("    White: ChessAgent --bind amqp://localhost:5672 --connect amqp://localhost:5673 --color white");
        Console.WriteLine("    Black: ChessAgent --bind amqp://localhost:5673 --connect amqp://localhost:5672 --color black");
        Console.WriteLine();
        Console.WriteLine("  Single-Session Mode (White manages both links):");
        Console.WriteLine("    White: ChessAgent --bind amqp://localhost:5672 --connect amqp://localhost:5673 --color white --single-session");
        Console.WriteLine("    Black: ChessAgent --bind amqp://localhost:5673 --color black --single-session");
        Console.WriteLine();
        Console.WriteLine("Note: In single-session mode, Black does NOT specify --connect");
        Console.WriteLine();
    }
}
