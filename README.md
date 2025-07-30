# ♟️ AMQP Chess: Peer-to-Peer Communication Demo

*A practical demonstration of AMQP 1.0 for building decentralized agent applications*

![Chess Demo](https://img.shields.io/badge/Demo-AMQP%20P2P%20Chess-blue) ![.NET 9](https://img.shields.io/badge/.NET-9.0-purple) ![AMQP](https://img.shields.io/badge/Protocol-AMQP%201.0-green) ![Docker](https://img.shields.io/badge/Engine-Docker%20Stockfish-orange)

---

## 🎯 What This Demonstrates

This chess application showcases AMQP's capabilities for peer-to-peer communication. Two chess agents communicate directly with each other using AMQP messaging, with no central broker required.

### **Key Features**

```
    Alice (White) ⚪                    Mark (Black) ⚫
  ┌─────────────────┐                ┌─────────────────┐
  │ 🎯 Chess Engine │ ◄──── AMQP ────► │ 🎯 Chess Engine │
  │ 🔗 AMQP Listener│                │ 🔗 AMQP Listener│
  │ 📤 Smart Sender │                │ 📤 Smart Sender │
  └─────────────────┘                └─────────────────┘
        Independent chess agents
```

Each agent operates independently, making chess moves and communicating via AMQP messages.

---

## 🚀 Why AMQP is Perfect for Agentic Apps

### **Communication Patterns Comparison**

| Feature | **HTTP/REST & RPC** | **AMQP Brokered** | **AMQP Peer-to-Peer** |
|---------|---------------------|-------------------|------------------------|
| **Architecture** | Request-response only | Central message broker | Direct agent connections |
| **Latency** | Network + processing | Network + broker + processing | Network + processing only |
| **Scalability** | Load balancer required | Broker handles routing | Distributed by design |
| **Persistence** | Stateless (external storage) | Queue persistence | Application-managed |
| **Reliability** | Manual retry logic | Built-in delivery guarantees | Built-in delivery guarantees |
| **Real-time** | Polling or WebSockets | Push notifications | Push notifications |
| **Infrastructure** | Web servers + databases | Message broker cluster | Minimal (just agents) |
| **Failure Handling** | Circuit breakers, timeouts | Automatic redelivery | Automatic reconnection |

### **Why This Demo Uses AMQP Peer-to-Peer**

**AMQP is most commonly deployed with message brokers** like RabbitMQ, Apache Qpid, or Azure Service Bus. In brokered scenarios, agents send messages to queues managed by the central broker - a proven, enterprise-grade pattern used extensively in production systems.

**This chess demo showcases AMQP's lesser-known peer-to-peer capabilities** where agents communicate directly without any central infrastructure. Both approaches offer significant advantages over traditional HTTP/REST APIs:

### **AMQP Benefits (Both Brokered & Peer-to-Peer):**

1. **True Bidirectional Communication**: Unlike HTTP's request-response model, agents can initiate conversations naturally in either direction OR communicate 
over a shared socket. 
2. **Built-in Reliability**: Automatic acknowledgments, delivery guarantees, and connection recovery without custom retry logic
3. **Low-Latency Binary Protocol**: Optimized for high-frequency, low-overhead messaging between systems
4. **Platform & Language Agnostic**: Works seamlessly across .NET, Java, Python, Go, Rust, and any AMQP-compliant library
5. **Enterprise-Grade Features**: Flow control, message routing, and Quality of Service (QoS) guarantees built into the protocol

### **When to Choose Each Approach:**

**AMQP Brokered (Traditional):**
- ✅ Centralized message management and monitoring
- ✅ Complex routing patterns and fan-out scenarios  
- ✅ Message persistence and guaranteed delivery across system restarts
- ✅ Well-established operational patterns and tooling

**AMQP Peer-to-Peer (This Demo):**
- ✅ **Edge computing** where central brokers may not be available
- ✅ **Autonomous agent networks** that must operate independently
- ✅ **Reduced infrastructure complexity** by eliminating broker dependencies  
- ✅ **Ultra-low latency** through direct connections between communicating parties
- ✅ **Decentralized architectures** where no single point of failure is acceptable

---

## Application Interface

### **Terminal.Gui Interface**

The chess application features a modern Terminal User Interface with:

- **Real-time Chess Board**: Live 8x8 board display with algebraic notation
- **Extended Move History**: Shows up to 20 complete exchanges (40 half-moves) 
- **Game Status**: Current player turn, connection status, and game state
- **Interactive Controls**: Quit and Help buttons for user interaction
- **Automatic Fallback**: Gracefully switches to headless mode if Terminal.Gui encounters issues

### **Sample Interface Display**
```
┌──────── Chess Board ────────┬──────── Move History ────────┐
│   a b c d e f g h           │ 1. e4 e5                      │
│ 8 r n b q k b n r           │ 2. Nf3 Nc6                    │
│ 7 p p p p . p p p           │ 3. Bb5 a6                     │
│ 6 . . . . . . . .           │ 4. Ba4 Nf6                    │
│ 5 . . . . p . . .           │ 5. O-O Be7                    │
│ 4 . . . . P . . .           │ 6. Re1 b5                     │
│ 3 . . . . . N . .           │ 7. Bb3 d6                     │
│ 2 P P P P . P P P           │ 8. c3 O-O                     │
│ 1 R N B Q K B . R           │ 9. h3 Nb8                     │
│                             │ 10. d4 Nbd7                   │
│ [Current: White to move]    │                               │
│ [Status: Connected]         │                               │
│                             │                               │
│ [Quit] [Help]               │                               │
└─────────────────────────────┴───────────────────────────────┘
```

---

## Quick Start

### **Prerequisites**
- **Windows** with PowerShell 5.1+
- **Docker Desktop** (for the chess engine)
- **.NET 9.0 SDK**
- **Windows Terminal** or **Command Prompt** (VS Code's integrated terminal may have display issues)

### **1. Clone & Build**
```powershell
git clone <repository-url>
cd AliceMarkQuentinPaulPlayChess
dotnet build
```

### **2. Launch the Chess Application**
```powershell
.\play-chess.ps1
```

That's it! Windows Terminal will split into two panes:
- **Left**: Alice (White) - The initiator
- **Right**: Mark (Black) - The responder

### **3. Observe AMQP Communication**

You'll see real-time logging showing:
- AMQP connections being established
- Messages with reply-to addresses being sent
- Cached sender links being reused for efficiency
- Chess moves in algebraic notation

---

## Technical Implementation

### **Bidirectional Reply-To Pattern**
```csharp
// Every message includes sender's address for responses
message.Properties = new Properties
{
    ReplyTo = "amqp://localhost:5672"  // Alice's address
};

// Receiver creates cached connection back to sender
var senderLink = await GetOrCreateSenderLinkAsync(replyToUri);
senderLink.Send(responseMessage);
```

### **Smart Caching System**
```csharp
// Efficient connection reuse
private readonly Dictionary<Uri, SenderLink> _senderLinkCache = new();

// No wasteful connection creation
if (_senderLinkCache.TryGetValue(replyToUri, out var existingLink))
{
    return existingLink; // Reuse existing connection
}
```

### **Docker-Powered Chess Intelligence** 🐳
The chess engines run in Docker containers, showcasing how AMQP seamlessly integrates with containerized microservices:

```bash
# Engine spins up automatically
docker run -d --name stockfish-engine stockfish/engine
```

---

## Application Interface

### **Modern Terminal Interface**
The application features a professional Terminal.Gui interface with:
- **Black background** for reduced eye strain
- **Real-time chess board** with Unicode pieces (♔♕♖♗♘♙)
- **Move history display** showing up to 20 recent exchanges
- **Live AMQP connection status** and message logging
- **Automatic error handling** with graceful fallback to headless mode

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                    ♟️ ChessAgent - Playing as White ⚪                        │
│                                                                              │ 
│    a  b  c  d  e  f  g  h         ♟️ Move History                           │
│ 8  ♜  ♞  ♝  ♛  ♚  ♝  ♞  ♜ 8       # │ White    │ Black    │ Cap │ Sec      │
│ 7  ♟  ♟  ♟  ♟  ♟  ♟  ♟  ♟ 7       ──┼──────────┼──────────┼─────┼────      │
│ 6                         6       15│ Nf3      │ Nc6      │     │ 42       │
│ 5                         5       16│ Bb5      │ a6       │     │ 44       │
│ 4                         4       17│ Bxc6+    │ dxc6     │ ♞♟  │ 46       │
│ 3                         3       18│ d3       │ Nf6      │     │ 48       │
│ 2  ♙  ♙  ♙  ♙  ♙  ♙  ♙  ♙ 2       19│ Bg5      │ Be7      │     │ 50       │
│ 1  ♖  ♘  ♗  ♕  ♔  ♗  ♘  ♖ 1       20│ Nbd2     │ O-O      │     │ 52       │
│    a  b  c  d  e  f  g  h                                                    │
│   White to move                     Duration: 2.1m                          │
│                                     Moves: 39                               │
│                                                                              │
│   🔗 Connection Log                                                          │
│   42s AMQP Snd: Nxc6                                                       │
│   43s AMQP Rcv: dxc6                                                       │
│   44s A: Connected                                                          │
│   45s + start                                                               │
│                                                                              │
│ 🎯 Move 21: Calculating...                                                  │
│ 📡 Connected - Single-session mode                                          │
└──────────────────────────────────────────────────────────────────────────────┘
```

### **Enhanced Move History**
- **Proper move numbering**: Shows actual chess exchange numbers (not just 1-9)
- **Extended display**: Up to 20 move exchanges visible at once
- **Capture notation**: Visual indicators for captured pieces
- **Time tracking**: Shows when each move was made
- **Game statistics**: Duration and total move count

### **Real-time AMQP Logging**
```
📋 A: Connected to Black player
📤 AMQP Snd: e4
📥 AMQP Rcv: e5
🔗 C: Single-session mode active
📡 S: Session established
🔗 L: Reply link cached
🎯 Move 15: Nf3 (White)
🎯 Move 16: Nc6 (Black)
```

### **VS Code Integration**
The project includes optimized launch configurations:
- **External Terminal Mode**: Launches in Windows Terminal/Command Prompt for full Terminal.Gui support
- **Headless Mode**: Runs in VS Code's integrated terminal with console-only output
- **Automatic fallback**: Detects VS Code terminal and switches to headless mode when needed

---

## Advanced Usage

### **Single-Session Mode**

For advanced AMQP patterns, this project includes a **single-session mode** where only one player establishes connections:

```powershell
# Launch in single-session mode
.\play-chess.ps1 -SingleSession

# Or use the dedicated demo script  
.\demo-single-session.ps1
```

**How Single-Session Mode Works:**

| **Normal Mode** | **Single-Session Mode** |
|----------------|------------------------|
| Both players connect to each other | Only White connects to Black |
| Each player creates 1 outbound link | White creates 2 links (send + receive) |
| Symmetric P2P architecture | Asymmetric client-server-like pattern |

```
Normal Mode:           Single-Session Mode:
Alice ←──────────→ Mark    Alice ────→ Mark
      AMQP Links              ├─ Send Link
                              └─ Reply Link
```

**Key Benefits:**
- **Simplified network topology** - Only one endpoint needs to know the other's address
- **Connection management** - Centralized link creation reduces complexity  
- **Firewall friendly** - Only one player needs inbound port access
- **Enterprise patterns** - Mimics client-server while maintaining AMQP benefits

**Technical Implementation:**
```csharp
// White creates both sender and receiver links
var senderLink = new SenderLink(session, "chess-sender", "chess");
var receiverLink = new ReceiverLink(session, "chess-receiver", "chess-reply");

// Black only accepts incoming connections - no outbound links
// Black uses the reply-to mechanism to send moves back to White
```

### **Headless Mode**

For automated testing, CI/CD pipelines, or server deployments, use the **headless mode**:

```powershell
# Launch headless chess match (no UI, auto-starts, full logging)
.\demo-headless.ps1

# Or manually:
# Black player (server)
dotnet run -- --bind amqp://localhost:5673 --color black --single-session --headless

# White player (client) 
dotnet run -- --bind amqp://localhost:5672 --connect amqp://localhost:5673 --color white --single-session --headless
```

**Headless Mode Features:**
- **🤖 No UI** - Runs without Terminal.Gui interface
- **🚀 Auto-start** - Begins playing immediately (no "Start Game" button)
- **📋 Full logging** - Automatically enables verbose output to console
- **🔧 AMQP tracing** - Enables AMQP .NET Lite debug tracing for protocol analysis
- **⚡ Perfect for CI/CD** - Ideal for automated testing and server deployments

**Output Example:**
```
🤖 Starting ChessAgent in HEADLESS mode as WHITE player
🔗 Bind: amqp://localhost:5672
🔗 Connect: amqp://localhost:5673
🔧 AMQP .NET Lite tracing enabled (DEBUG level)

[AMQP Frame] OPEN: container-id=...
[AMQP Verbose] session.begin: channel=0
📤 AMQP Send: e4
🎯 Move 1: e4 (White)
📥 AMQP Receive: e5
🎯 Move 2: e5 (Black)
```

### **VS Code Development**

The project includes optimized launch configurations in `.vscode/launch.json`:

**GUI Mode (External Terminal):**
```json
{
    "name": "Debug Chess - White Player (Initiator)", 
    "console": "externalTerminal",  // Launches in Windows Terminal
    "args": ["--bind", "amqp://localhost:5673", "--connect", "amqp://localhost:5672", "--color", "white", "--verbose", "--single-session"]
}
```

**Headless Mode (Integrated Terminal):**
```json
{
    "name": "Debug Chess - White Player (Headless)",
    "console": "integratedTerminal",  // Runs in VS Code terminal
    "args": ["--bind", "amqp://localhost:5673", "--connect", "amqp://localhost:5672", "--color", "white", "--verbose", "--headless"]
}
```

**Compound Configurations:**
- **"Launch Both Chess Players (Terminal.Gui)"**: Opens both players in external terminals
- **"Launch Both Chess Players (Headless)"**: Runs both players in integrated terminals

### **Network Deployment**
The agents can run on separate machines:
```powershell
# Machine 1 (Alice)
dotnet run -- --bind amqp://0.0.0.0:5672 --connect amqp://machine2:5673 --color white

# Machine 2 (Mark)  
dotnet run -- --bind amqp://0.0.0.0:5673 --connect amqp://machine1:5672 --color black
```

---

## Use Cases for AMQP Agent Communication

This pattern demonstrates capabilities useful for:

- **AI Agent Networks** - Agents that collaborate and compete
- **IoT Device Communication** - Smart devices communicating directly  
- **Real-time Applications** - Low-latency, reliable messaging
- **Financial Systems** - High-frequency, mission-critical communication
- **Microservice Architecture** - Direct service-to-service communication

---

## Project Structure

```
AliceMarkQuentinPaulPlayChess/
├── AliceMarkQuentinPaulPlayChess/     # Main application
│   ├── ChessApp.cs                    # AMQP agent logic
│   ├── ChessEngine.cs                 # Docker engine interface
│   └── ChessBoardDisplay.cs           # Terminal UI
├── play-chess.ps1                     # Launch script
└── README.md                          # Documentation
```

---

## Technical Notes

- **Automatic reconnection**: Agents reconnect if connections drop
- **Memory efficient**: Sender links are cached and reused
- **Cross-platform**: Runs on Windows, Linux, macOS with .NET 9
- **Container-ready**: Suitable for containerized deployment
- **Language agnostic**: AMQP works with Java, Python, Go, Rust, etc.

---

## 🎯 The Bottom Line

**AMQP isn't just a messaging protocol—it's the foundation for the next generation of intelligent, autonomous applications.**

This chess demo proves that when agents can communicate naturally and efficiently, they become more than the sum of their parts. They become a **distributed intelligence network**.

---

*Ready to build the future of agentic applications? Start with AMQP! ⚡*

**[🎮 Try the Demo Now →](#-quick-start-watch-the-magic-happen)**
