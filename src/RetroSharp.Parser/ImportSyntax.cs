namespace RetroSharp.Parser;

public sealed class ImportSyntax : Syntax
{
    public ImportSyntax(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitImport(this);
    }
}
