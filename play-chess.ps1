# PowerShell script to launch ChessAgent with Windows Terminal split view
# Creates two panes: White player (left) and Black player (right)

param(
    [switch]$Verbose,
    [switch]$NoColor,
    [switch]$DryRun,
    [switch]$Help,
    [switch]$Debug,
    [switch]$SingleSession,
    [string]$WhitePort = "5672",
    [string]$BlackPort = "5673"
)

if ($Help) {
    Write-Host "🎮 ChessAgent Windows Terminal Launcher" -ForegroundColor Cyan
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage: .\play-chess.ps1 [options]" -ForegroundColor White
    Write-Host ""
    Write-Host "Options:" -ForegroundColor Yellow
    Write-Host "  -Verbose        Enable verbose logging for debugging" -ForegroundColor Gray
    Write-Host "  -NoColor        Disable color output" -ForegroundColor Gray
    Write-Host "  -DryRun         Show commands without executing" -ForegroundColor Gray
    Write-Host "  -Debug          Show Windows Terminal command for debugging" -ForegroundColor Gray
    Write-Host "  -Help           Show this help message" -ForegroundColor Gray
    Write-Host "  -SingleSession  Use single-session mode (White creates both links, Black only accepts)" -ForegroundColor Gray
    Write-Host "  -WhitePort      Port for white player (default: 5672)" -ForegroundColor Gray
    Write-Host "  -BlackPort      Port for black player (default: 5673)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor Yellow
    Write-Host "  .\play-chess.ps1                    # Normal game" -ForegroundColor White
    Write-Host "  .\play-chess.ps1 -Verbose           # With debug output" -ForegroundColor White
    Write-Host "  .\play-chess.ps1 -DryRun            # Test without running" -ForegroundColor White
    Write-Host "  .\play-chess.ps1 -WhitePort 8080    # Custom ports" -ForegroundColor White
    Write-Host "  .\play-chess.ps1 -SingleSession     # Single-session mode" -ForegroundColor White
    Write-Host ""
    Write-Host "Requirements:" -ForegroundColor Yellow
    Write-Host "  • Windows Terminal installed" -ForegroundColor Gray
    Write-Host "  • ChessAgent.exe built (run 'dotnet build' first)" -ForegroundColor Gray
    Write-Host ""
    exit 0
}

$ErrorActionPreference = "Stop"

# Get absolute paths
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$ExePath = Join-Path $ScriptPath "AliceMarkQuentinPaulPlayChess\bin\Debug\net9.0\ChessAgent.exe"

# Verify ChessAgent exists
if (-not (Test-Path $ExePath)) {
    Write-Host "❌ ChessAgent.exe not found at: $ExePath" -ForegroundColor Red
    Write-Host "   Please build the project first: dotnet build" -ForegroundColor Yellow
    
    # Try alternative path
    $AltExePath = Join-Path $ScriptPath "bin\Debug\net9.0\ChessAgent.exe"
    if (Test-Path $AltExePath) {
        $ExePath = $AltExePath
        Write-Host "✅ Found ChessAgent at alternative path: $ExePath" -ForegroundColor Green
    } else {
        Write-Host "   Also checked: $AltExePath" -ForegroundColor Yellow
        exit 1
    }
}

# Build command line arguments
if ($SingleSession) {
    # Single-session mode: White creates both send and receive links, Black only accepts connections
    $WhiteArgs = @(
        "--bind", "amqp://localhost:$WhitePort"
        "--connect", "amqp://localhost:$BlackPort"
        "--color", "white"
        "--single-session"
    )

    # Black in single-session mode does NOT connect - it only accepts
    $BlackArgs = @(
        "--bind", "amqp://localhost:$BlackPort"
        "--color", "black"
        "--single-session"
    )
} else {
    # Normal mode: Both players connect to each other
    $WhiteArgs = @(
        "--bind", "amqp://localhost:$WhitePort"
        "--connect", "amqp://localhost:$BlackPort"
        "--color", "white"
    )

    $BlackArgs = @(
        "--bind", "amqp://localhost:$BlackPort" 
        "--connect", "amqp://localhost:$WhitePort"
        "--color", "black"
    )
}

if ($Verbose) {
    $WhiteArgs += "--verbose"
    $BlackArgs += "--verbose"
}

if ($NoColor) {
    $WhiteArgs += "--no-color"
    $BlackArgs += "--no-color"
}

# Convert arguments to strings for Windows Terminal
$WhiteArgsStr = $WhiteArgs -join " "
$BlackArgsStr = $BlackArgs -join " "

$ModeText = if ($SingleSession) { "SINGLE-SESSION" } else { "NORMAL" }

Write-Host "🎯 Starting ChessAgent Match in $ModeText mode..." -ForegroundColor Cyan
Write-Host "⚪ White Player: amqp://localhost:$WhitePort" -ForegroundColor White
Write-Host "⚫ Black Player: amqp://localhost:$BlackPort" -ForegroundColor Gray
Write-Host "🔧 Executable: $ExePath" -ForegroundColor Green

if ($SingleSession) {
    Write-Host "🔗 Single-session mode: White creates both send and receive links" -ForegroundColor Yellow
    Write-Host "🔗 Single-session mode: Black only accepts incoming connections" -ForegroundColor Yellow
}

if ($Verbose) {
    Write-Host "📋 Verbose mode enabled" -ForegroundColor Yellow
}

if ($NoColor) {
    Write-Host "🎨 Color mode disabled" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "🎮 Launching Windows Terminal with split view..." -ForegroundColor Cyan
Write-Host "   White player will start the game automatically" -ForegroundColor Gray
Write-Host "   Press Ctrl+C in either pane to stop the game" -ForegroundColor Gray
Write-Host ""

if ($DryRun) {
    Write-Host "🧪 DRY RUN - Commands that would be executed:" -ForegroundColor Magenta
    Write-Host "   White: `"$ExePath`" $($WhiteArgs -join ' ')" -ForegroundColor White
    Write-Host "   Black: `"$ExePath`" $($BlackArgs -join ' ')" -ForegroundColor Gray
    exit 0
}

try {
    # Build simple command strings
    $WhiteCmd = "`"$ExePath`" " + ($WhiteArgs -join " ")
    $BlackCmd = "`"$ExePath`" " + ($BlackArgs -join " ")
    
    # Build the complete Windows Terminal command as a single string
    $WtCommand = "wt.exe --size 200,50 " +
                 "new-tab -p `"Windows PowerShell`" --title `"White Player`" " +
                 "powershell.exe -NoExit -Command `"$WhiteCmd`" " +
                 "; " +
                 "split-pane -p `"Windows PowerShell`" --title `"Black Player`" " +
                 "powershell.exe -NoExit -Command `"$BlackCmd`""
    
    if ($Debug) {
        Write-Host "🔧 Debug: Windows Terminal command:" -ForegroundColor Magenta
        Write-Host "   $WtCommand" -ForegroundColor Gray
        Write-Host "   White: $WhiteCmd" -ForegroundColor White
        Write-Host "   Black: $BlackCmd" -ForegroundColor Gray
        Write-Host ""
    }
    
    # Execute the command using cmd.exe to avoid PowerShell parsing issues
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c", $WtCommand
    
    Write-Host "✅ Windows Terminal launched successfully!" -ForegroundColor Green
    Write-Host "   Both chess players should be running in split panes" -ForegroundColor Gray
}
catch {
    Write-Host "❌ Failed to launch Windows Terminal: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Make sure Windows Terminal is installed and in PATH" -ForegroundColor Yellow
    Write-Host "   Fallback: Run the players manually in separate terminals:" -ForegroundColor Yellow
    Write-Host "   White: `"$ExePath`" $($WhiteArgs -join ' ')" -ForegroundColor White
    Write-Host "   Black: `"$ExePath`" $($BlackArgs -join ' ')" -ForegroundColor Gray
    exit 1
}
