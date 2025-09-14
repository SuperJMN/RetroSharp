namespace RetroSharp.Parser;

public class ProgramSyntax : Syntax
{
    public IList<FunctionSyntax> Functions { get; }

    public ProgramSyntax(IList<FunctionSyntax> functions)
    {
        Functions = functions;
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitProgram(this);
    }
}