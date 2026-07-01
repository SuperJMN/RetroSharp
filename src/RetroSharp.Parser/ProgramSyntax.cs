namespace RetroSharp.Parser;

public class ProgramSyntax : Syntax
{
    public IList<ImportSyntax> Imports { get; }
    public IList<TypeAliasSyntax> TypeAliases { get; }
    public IList<ConstDeclarationSyntax> Constants { get; }
    public IList<EnumSyntax> Enums { get; }
    public IList<StructSyntax> Structs { get; }
    public IList<FunctionSyntax> Functions { get; }

    public ProgramSyntax(IList<FunctionSyntax> functions)
        : this([], [], [], [], functions)
    {
    }

    public ProgramSyntax(IList<StructSyntax> structs, IList<FunctionSyntax> functions)
        : this([], [], [], structs, functions)
    {
    }

    public ProgramSyntax(IList<ConstDeclarationSyntax> constants, IList<StructSyntax> structs, IList<FunctionSyntax> functions)
        : this([], constants, [], structs, functions)
    {
    }

    public ProgramSyntax(IList<ConstDeclarationSyntax> constants, IList<EnumSyntax> enums, IList<StructSyntax> structs, IList<FunctionSyntax> functions)
        : this([], constants, enums, structs, functions)
    {
    }

    public ProgramSyntax(IList<TypeAliasSyntax> typeAliases, IList<ConstDeclarationSyntax> constants, IList<EnumSyntax> enums, IList<StructSyntax> structs, IList<FunctionSyntax> functions)
        : this([], typeAliases, constants, enums, structs, functions)
    {
    }

    public ProgramSyntax(IList<ImportSyntax> imports, IList<TypeAliasSyntax> typeAliases, IList<ConstDeclarationSyntax> constants, IList<EnumSyntax> enums, IList<StructSyntax> structs, IList<FunctionSyntax> functions)
    {
        Imports = imports;
        TypeAliases = typeAliases;
        Constants = constants;
        Enums = enums;
        Structs = structs;
        Functions = functions;
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitProgram(this);
    }
}
