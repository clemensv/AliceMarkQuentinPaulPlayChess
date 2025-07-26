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

### **Traditional Approaches vs AMQP** 

| **HTTP/REST APIs** | **AMQP Peer-to-Peer** |
|----------------------|---------------------------|
| Request-response only | Bidirectional messaging |
| Requires central server | Direct peer communication |
| Polling for updates | Real-time push notifications |
| Stateless interactions | Stateful conversations |
| Complex load balancing | Built-in reliability |

### **AMQP Benefits for Agent Applications:**

1. **Bidirectional Conversations**: Agents can initiate and respond naturally
2. **Low Latency**: Binary protocol with minimal overhead
3. **Built-in Reliability**: Automatic acknowledgments and delivery guarantees
4. **Platform Agnostic**: Works across any programming language or platform
5. **Smart Routing**: Messages find their destination automatically
6. **Enterprise Grade**: Proven in financial and IoT systems

---

## Quick Start

### **Prerequisites**
- **Windows** with PowerShell 5.1+
- **Docker Desktop** (for the chess engine)
- **.NET 9.0 SDK**
- **Windows Terminal** (recommended for best experience)

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

### **Terminal Interface**
```
┌──────────────────────┐ │ Move History           │
│ 8 │ ♜ ♞ ♝ ♛ ♚ ♝ ♞ ♜ │ │ ──────────────────     │
│ 7 │ ♟ ♟ ♟ ♟ ♟ ♟ ♟ ♟ │ │ 1. e4    e5           │
│ 6 │                 │ │ 2. Nf3   Nc6          │
│ 5 │                 │ │ 3. Bb5   a6           │
│ 4 │                 │ │ 4. Bxc6+ dxc6         │
│ 3 │                 │ │                       │
│ 2 │ ♙ ♙ ♙ ♙ ♙ ♙ ♙ ♙ │ │ AMQP Communication    │
│ 1 │ ♖ ♘ ♗ ♕ ♔ ♗ ♘ ♖ │ │ ──────────────────    │
└──────────────────────┘ │ Connected to peer     │
```

### **Real-time AMQP Logging**
```
AMQP: Setting ReplyTo address: amqp://localhost:5672
AMQP: Message contains reply-to address: amqp://localhost:5673
AMQP: Using cached sender link for amqp://localhost:5672
AMQP: Successfully sent move via reply-to link: Nf3
```

---

## Advanced Usage

### **Custom Configuration**
```powershell
# Verbose logging for development
.\play-chess.ps1 -Verbose

# Custom ports for network testing
.\play-chess.ps1 -WhitePort 8080 -BlackPort 8081

# Test without executing
.\play-chess.ps1 -DryRun
```

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
