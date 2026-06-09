namespace RetroSharp.Parser;

public static class LoopLowerer
{
    public static WhileSyntax Lower(LoopSyntax loopSyntax)
    {
        return new WhileSyntax(new ConstantSyntax("true"), loopSyntax.Body);
    }
}
