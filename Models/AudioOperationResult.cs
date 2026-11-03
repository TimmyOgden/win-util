namespace WinUtil.Models;

internal sealed record AudioOperationResult(string? ErrorMessage)
{
    internal bool Succeeded => ErrorMessage is null;
}
