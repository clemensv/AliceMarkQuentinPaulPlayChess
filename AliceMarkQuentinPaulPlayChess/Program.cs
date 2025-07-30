using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Amqp;

namespace AliceMarkQuentinPaulPlayChess;

public static class Program
{
    private static ChessAgent? _currentAgent = null;
    
    public static async Task<int> Main(string[] args)
    {
        // Parse arguments first
        var parsedArgs = ChessAgentArgs.Parse(args);
        
        // Enable AMQP tracing in headless mode
        if (parsedArgs.Headless)
        {
            Amqp.Trace.TraceLevel = Amqp.TraceLevel.Frame | Amqp.TraceLevel.Verbose;
            Amqp.Trace.TraceListener = (l, f, a) => Console.WriteLine($"[AMQP {l}] {string.Format(f, a)}");
            Console.WriteLine("🔧 AMQP .NET Lite tracing enabled (DEBUG level)");
        }
        
        if (parsedArgs.Headless)
        {
            return await RunHeadlessMode(parsedArgs);
        }
        else
        {
            return await RunInteractiveMode(parsedArgs);
        }
    }

    private static async Task<int> RunHeadlessMode(ChessAgentArgs parsedArgs)
    {
        // Enable headless mode in ChessBoardDisplay
        ChessBoardDisplay.SetHeadlessMode(true);
        ChessBoardDisplay.Initialize(parsedArgs.White);
        
        Console.WriteLine($"🤖 Starting ChessAgent in HEADLESS mode as {(parsedArgs.White ? "WHITE" : "BLACK")} player");
        Console.WriteLine($"🔗 Bind: {parsedArgs.Bind}");
        if (!string.IsNullOrEmpty(parsedArgs.Connect))
            Console.WriteLine($"🔗 Connect: {parsedArgs.Connect}");
        Console.WriteLine($"🔧 Single-session: {parsedArgs.SingleSession}");
        Console.WriteLine();

        // Set up cancellation token for Ctrl+C handling
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        
        // Set up exit handlers
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n🛑 Shutdown requested - Cleaning up...");
            cts.Cancel();
        };
        
        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            Console.WriteLine("🛑 Process exit detected - Cleaning up Docker containers...");
            CleanupDockerContainers();
        };

        try
        {
            // Create and start the chess agent
            Console.WriteLine("🔧 Creating chess agent...");
            var agent = new ChessAgent(parsedArgs.Bind, parsedArgs.Connect, parsedArgs.White, parsedArgs.Verbose, parsedArgs.SingleSession);
            _currentAgent = agent;
            
            Console.WriteLine("🚀 Starting AMQP agent...");
            await agent.StartAsync(cancellationToken);
            
            // Keep running until cancelled
            Console.WriteLine("✅ Chess agent started successfully. Press Ctrl+C to exit.");
            
            // Wait indefinitely until cancellation
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => tcs.SetResult(true));
            await tcs.Task;
            
            Console.WriteLine("🛑 Shutting down...");
            await agent.DisposeAsync();
            _currentAgent = null;
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Fatal error: {ex.Message}");
            if (parsedArgs.Verbose)
            {
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            await EnsureProperDisposal();
            _currentAgent = null;
            
            return 1;
        }
        finally
        {
            CleanupDockerContainers();
        }
    }

    private static async Task<int> RunInteractiveMode(ChessAgentArgs parsedArgs)
    {
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
                        agent = new ChessAgent(parsedArgs.Bind, parsedArgs.Connect, parsedArgs.White, parsedArgs.Verbose, parsedArgs.SingleSession);
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
