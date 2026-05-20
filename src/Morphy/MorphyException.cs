namespace Morphy;

public sealed class MorphyException : Exception
{
    public MorphyException(string message) : base(message) { }
    public MorphyException(string message, Exception inner) : base(message, inner) { }
}
