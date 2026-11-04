/// <summary>Shared event classification; BREAK remains an internal failure reason.</summary>
public static class JudgeRank
{
    public static bool IsFailure(string rank) => rank == "MISS" || rank == "BREAK";

    public static bool IsSuccess(string rank) => rank == "PERFECT" || rank == "GOOD" ||
        rank == "CLEAR" || rank == "PASS" || rank == "YES";
}
