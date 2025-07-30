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
