namespace RetroSharp.Parser;

public abstract class Syntax : ISyntax
{
    public abstract void Accept(ISyntaxVisitor visitor);
}