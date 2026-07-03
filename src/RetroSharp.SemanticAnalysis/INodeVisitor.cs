namespace RetroSharp.SemanticAnalysis;

public interface INodeVisitor
{
    void VisitDeclarationNode(DeclarationNode node);
    void VisitConstDeclarationNode(ConstDeclarationNode node);
    void VisitBlockNode(BlockNode node);
    void VisitFunctionNode(FunctionNode node);
    void VisitProgramNode(ProgramNode node);
    void VisitAssignment(AssignmentNode assignmentNode);
    void VisitConstant(ConstantNode constantNode);
    void VisitExpressionStatement(ExpressionStatementNode expressionStatementNode);
    void VisitKnownSymbol(KnownSymbolNode knownSymbolNode);
    void VisitUnknownSymbol(UnknownSymbol unknownSymbol);
    void VisitSymbolExpression(SymbolExpressionNode symbolExpressionNode);
    void VisitBinaryExpression(BinaryExpressionNode binaryExpressionNode);
    void VisitUnaryExpression(UnaryExpressionNode unaryExpressionNode);
    void VisitCastExpression(CastExpressionNode castExpressionNode);
    void VisitConditionalExpression(ConditionalExpressionNode conditionalExpressionNode);
    void VisitReturn(ReturnNode returnNode);
    void VisitIfElse(IfElseNode ifElseNode);
    void VisitWhile(WhileNode whileNode);
    void VisitDoWhile(DoWhileNode doWhileNode);
    void VisitFor(ForNode forNode);
    void VisitBreak(BreakNode breakNode);
    void VisitContinue(ContinueNode continueNode);
    void VisitFunctionCall(FunctionCallExpressionNode functionCall);
}
