using CSharpFunctionalExtensions;
using RetroSharp.Core;
using RetroSharp.Parser;

namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    // When the program configures no camera the camera is fixed at the origin, so shared
    // projections read a literal 0 instead of the target camera runtime state. This keeps
    // camera-less pooled draws deterministic across targets and emulators (see ConfiguresCamera).
    private static ExpressionSyntax ConfiguredCameraComponent(bool configuresCamera, string cameraFunction)
            => configuresCamera ? new FunctionCall(cameraFunction, []) : Constant(0);

    private static BlockSyntax StableSpriteDrawBlock(
            string arrayName,
            string indexName,
            string variablePrefix,
            string kindName,
            string sprite,
            ActorScreenProjection projection,
            int hiddenY,
            ActorFrameworkState state)
    {
        var drawX = $"{variablePrefix}_x_{kindName}";
        var drawY = $"{variablePrefix}_y_{kindName}";

        return new BlockSyntax([
            new DeclarationSyntax(
                    "u8",
                    drawX,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(Constant(0))),
                new DeclarationSyntax(
                    "u8",
                    drawY,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(Constant(hiddenY))),
                new IfElseSyntax(
                    And(
                        new BinaryExpressionSyntax(PoolField(arrayName, indexName, "active"), Constant(0), Operator.NotEqual),
                        projection.Visible),
                    new BlockSyntax([
                        new ExpressionStatementSyntax(new AssignmentSyntax(
                            new IdentifierLValue(drawX),
                            "=",
                            projection.ScreenX)),
                        new ExpressionStatementSyntax(new AssignmentSyntax(
                            new IdentifierLValue(drawY),
                            "=",
                            projection.ScreenY)),
                    ]),
                    Maybe<BlockSyntax>.None),
                new ExpressionStatementSyntax(IntrinsicCall(
                    state,
                    SpriteDrawIntrinsic,
                    [
                        new IdentifierSyntax(sprite),
                        new IdentifierSyntax(drawX),
                        new IdentifierSyntax(drawY),
                        Constant(0),
                        new IdentifierSyntax("false"),
                        Constant(0),
                    ])),
            ]);
    }

    private static IfElseSyntax PooledKindDispatch(string poolName, string indexName, IReadOnlyList<KindBranch> branches, string emptyBranchesMessage)
    {
        if (branches.Count == 0)
        {
            throw new InvalidOperationException(emptyBranchesMessage);
        }

        var first = branches[0];
        var elseBlock = branches.Count == 1
            ? Maybe<BlockSyntax>.None
            : Maybe.From(new BlockSyntax([PooledKindDispatch(poolName, indexName, branches.Skip(1).ToList(), emptyBranchesMessage)]));

        return new IfElseSyntax(
            new BinaryExpressionSyntax(PoolField(poolName, indexName, "kind"), new IdentifierSyntax(first.Kind), Operator.Equal),
            first.Block,
            elseBlock);
    }

    private static IfElseSyntax SetByteFlagIf(string flagName, ExpressionSyntax condition)
    {
        return new IfElseSyntax(
            condition,
            new BlockSyntax([Assign(new IdentifierLValue(flagName), Constant(1))]),
            Maybe<BlockSyntax>.None);
    }

    private static IReadOnlyList<StatementSyntax> PooledCameraDeclarations(string poolName, string phase, bool configuresCamera)
    {
        var cameraXLow = $"__{poolName}_{phase}_camera_x_lo";
        var cameraXHigh = $"__{poolName}_{phase}_camera_x_hi";
        var cameraYLow = $"__{poolName}_{phase}_camera_y_lo";
        var cameraYHigh = $"__{poolName}_{phase}_camera_y_hi";

        return
        [
            new DeclarationSyntax(
                    "u8",
                    cameraXLow,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From(ConfiguredCameraComponent(configuresCamera, ActorCameraXLowFunction))),
                new DeclarationSyntax(
                    "u8",
                    cameraXHigh,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From(ConfiguredCameraComponent(configuresCamera, ActorCameraXHighFunction))),
                new DeclarationSyntax(
                    "u8",
                    cameraYLow,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From(ConfiguredCameraComponent(configuresCamera, ActorCameraYLowFunction))),
                new DeclarationSyntax(
                    "u8",
                    cameraYHigh,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From(ConfiguredCameraComponent(configuresCamera, ActorCameraYHighFunction))),
            ];
    }

    private static ActorScreenProjection BuildPooledScreenProjection(
            string arrayName,
            string indexName,
            string poolName,
            string phase,
            int screenWidth,
            int screenHeight,
            int margin = 0,
            string? cameraPhase = null)
    {
        var cameraVariablePhase = cameraPhase ?? phase;
        var cameraXLow = $"__{poolName}_{cameraVariablePhase}_camera_x_lo";
        var cameraXHigh = $"__{poolName}_{cameraVariablePhase}_camera_x_hi";
        var cameraYLow = $"__{poolName}_{cameraVariablePhase}_camera_y_lo";
        var cameraYHigh = $"__{poolName}_{cameraVariablePhase}_camera_y_hi";
        var screenX = $"__{poolName}_{phase}_screen_x";
        var screenY = $"__{poolName}_{phase}_screen_y";
        var visibleXName = $"__{poolName}_{phase}_visible_x";
        var visibleYName = $"__{poolName}_{phase}_visible_y";

        var declarations = new List<StatementSyntax>
            {
                new DeclarationSyntax(
                    "u8",
                    screenX,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(
                        PoolField(arrayName, indexName, "x"),
                        new IdentifierSyntax(cameraXLow),
                        Operator.Get("-")))),
                new DeclarationSyntax(
                    "u8",
                    screenY,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(
                        PoolField(arrayName, indexName, "y"),
                        new IdentifierSyntax(cameraYLow),
                        Operator.Get("-")))),
                new DeclarationSyntax("u8", visibleXName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(Constant(0))),
                new DeclarationSyntax("u8", visibleYName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(Constant(0))),
            };

        var sameCameraXPage = And(
            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "xHi"), new IdentifierSyntax(cameraXHigh), Operator.Equal),
            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "x"), new IdentifierSyntax(cameraXLow), Operator.Get(">=")));
        var nextCameraXPage = And(
            new BinaryExpressionSyntax(
                PoolField(arrayName, indexName, "xHi"),
                new BinaryExpressionSyntax(new IdentifierSyntax(cameraXHigh), Constant(1), Operator.Get("+")),
                Operator.Equal),
            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "x"), new IdentifierSyntax(cameraXLow), Operator.LessThan));
        var xMargin = screenWidth + margin < 256 ? margin : 0;
        ExpressionSyntax forwardXExpression = Or(sameCameraXPage, nextCameraXPage);
        var rightLimit = screenWidth + xMargin;
        if (rightLimit < 256)
        {
            forwardXExpression = And(
                forwardXExpression,
                new BinaryExpressionSyntax(new IdentifierSyntax(screenX), Constant(rightLimit), Operator.LessThan));
        }

        declarations.Add(SetByteFlagIf(visibleXName, forwardXExpression));

        if (xMargin > 0)
        {
            var leftOfCameraX = And(
                new BinaryExpressionSyntax(new IdentifierSyntax(screenX), Constant(256 - xMargin), Operator.GreaterThanOrEqual),
                Or(
                    And(
                        new BinaryExpressionSyntax(PoolField(arrayName, indexName, "xHi"), new IdentifierSyntax(cameraXHigh), Operator.Equal),
                        new BinaryExpressionSyntax(PoolField(arrayName, indexName, "x"), new IdentifierSyntax(cameraXLow), Operator.LessThan)),
                    And(
                        new BinaryExpressionSyntax(
                            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "xHi"), Constant(1), Operator.Get("+")),
                            new IdentifierSyntax(cameraXHigh),
                            Operator.Equal),
                        new BinaryExpressionSyntax(PoolField(arrayName, indexName, "x"), new IdentifierSyntax(cameraXLow), Operator.GreaterThanOrEqual))));
            declarations.Add(SetByteFlagIf(visibleXName, leftOfCameraX));

            if (rightLimit >= 256)
            {
                var rightOfCameraX = And(
                    new BinaryExpressionSyntax(new IdentifierSyntax(screenX), Constant(xMargin), Operator.LessThan),
                    Or(
                        And(
                            new BinaryExpressionSyntax(
                                PoolField(arrayName, indexName, "xHi"),
                                new BinaryExpressionSyntax(new IdentifierSyntax(cameraXHigh), Constant(1), Operator.Get("+")),
                                Operator.Equal),
                            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "x"), new IdentifierSyntax(cameraXLow), Operator.GreaterThanOrEqual)),
                        And(
                            new BinaryExpressionSyntax(
                                PoolField(arrayName, indexName, "xHi"),
                                new BinaryExpressionSyntax(new IdentifierSyntax(cameraXHigh), Constant(2), Operator.Get("+")),
                                Operator.Equal),
                            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "x"), new IdentifierSyntax(cameraXLow), Operator.LessThan))));
                declarations.Add(SetByteFlagIf(visibleXName, rightOfCameraX));
            }
        }

        var sameCameraYPage = And(
            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "yHi"), new IdentifierSyntax(cameraYHigh), Operator.Equal),
            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "y"), new IdentifierSyntax(cameraYLow), Operator.Get(">=")));
        var nextCameraYPage = And(
            new BinaryExpressionSyntax(
                PoolField(arrayName, indexName, "yHi"),
                new BinaryExpressionSyntax(new IdentifierSyntax(cameraYHigh), Constant(1), Operator.Get("+")),
                Operator.Equal),
            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "y"), new IdentifierSyntax(cameraYLow), Operator.LessThan));
        var yMargin = screenHeight + margin < 256 ? margin : 0;
        ExpressionSyntax forwardYExpression = Or(sameCameraYPage, nextCameraYPage);
        var bottomLimit = screenHeight + yMargin;
        if (bottomLimit < 256)
        {
            forwardYExpression = And(
                forwardYExpression,
                new BinaryExpressionSyntax(new IdentifierSyntax(screenY), Constant(bottomLimit), Operator.LessThan));
        }

        declarations.Add(SetByteFlagIf(visibleYName, forwardYExpression));

        if (yMargin > 0)
        {
            var aboveCameraY = And(
                new BinaryExpressionSyntax(new IdentifierSyntax(screenY), Constant(256 - yMargin), Operator.GreaterThanOrEqual),
                Or(
                    And(
                        new BinaryExpressionSyntax(PoolField(arrayName, indexName, "yHi"), new IdentifierSyntax(cameraYHigh), Operator.Equal),
                        new BinaryExpressionSyntax(PoolField(arrayName, indexName, "y"), new IdentifierSyntax(cameraYLow), Operator.LessThan)),
                    And(
                        new BinaryExpressionSyntax(
                            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "yHi"), Constant(1), Operator.Get("+")),
                            new IdentifierSyntax(cameraYHigh),
                            Operator.Equal),
                        new BinaryExpressionSyntax(PoolField(arrayName, indexName, "y"), new IdentifierSyntax(cameraYLow), Operator.GreaterThanOrEqual))));
            declarations.Add(SetByteFlagIf(visibleYName, aboveCameraY));

            if (bottomLimit >= 256)
            {
                var belowCameraY = And(
                    new BinaryExpressionSyntax(new IdentifierSyntax(screenY), Constant(yMargin), Operator.LessThan),
                    Or(
                        And(
                            new BinaryExpressionSyntax(
                                PoolField(arrayName, indexName, "yHi"),
                                new BinaryExpressionSyntax(new IdentifierSyntax(cameraYHigh), Constant(1), Operator.Get("+")),
                                Operator.Equal),
                            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "y"), new IdentifierSyntax(cameraYLow), Operator.GreaterThanOrEqual)),
                        And(
                            new BinaryExpressionSyntax(
                                PoolField(arrayName, indexName, "yHi"),
                                new BinaryExpressionSyntax(new IdentifierSyntax(cameraYHigh), Constant(2), Operator.Get("+")),
                                Operator.Equal),
                            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "y"), new IdentifierSyntax(cameraYLow), Operator.LessThan))));
                declarations.Add(SetByteFlagIf(visibleYName, belowCameraY));
            }
        }

        return new ActorScreenProjection(
            declarations,
            new IdentifierSyntax(screenX),
            new IdentifierSyntax(screenY),
            And(
                new BinaryExpressionSyntax(new IdentifierSyntax(visibleXName), Constant(0), Operator.NotEqual),
                new BinaryExpressionSyntax(new IdentifierSyntax(visibleYName), Constant(0), Operator.NotEqual)));
    }

    private sealed record ActorScreenProjection(IReadOnlyList<StatementSyntax> Declarations, IdentifierSyntax ScreenX, IdentifierSyntax ScreenY, ExpressionSyntax Visible);

    private sealed record KindBranch(string Kind, BlockSyntax Block);

}
