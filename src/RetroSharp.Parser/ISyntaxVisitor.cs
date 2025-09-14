namespace RetroSharp.Parser;

public interface ISyntaxVisitor
{
    void VisitBlock(BlockSyntax programSyntax);
    void VisitProgram(ProgramSyntax programSyntax);
    void VisitFunctionCall(FunctionCall functionCall);
    void VisitIdentifierLValue(IdentifierLValue identifierLValue);
    void VisitPointerDerefLValue(PointerDerefLValue pointerDerefLValue);
    void VisitIndexLValue(IndexLValue indexLValue);
    void VisitAssignment(AssignmentSyntax assignmentSyntax);
    void VisitExpressionStatement(ExpressionStatementSyntax expressionStatementSyntax);
    void VisitFunction(FunctionSyntax function);
    void VisitConstant(ConstantSyntax constantSyntax);
    void VisitDeclaration(DeclarationSyntax declarationSyntax);
    void VisitParameter(ParameterSyntax parameterSyntax);
    void VisitReturn(ReturnSyntax returnSyntax);
    void VisitIdentifier(IdentifierSyntax identifierSyntax);
    void VisitBinaryOperator(BinaryExpressionSyntax binaryExpressionSyntax);
    void VisitIfElse(IfElseSyntax ifElseSyntax);
}
