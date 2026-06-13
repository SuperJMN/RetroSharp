namespace RetroSharp.Parser;

using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Core.Targeting;

// Target-neutral readers for SDK call arguments shared by the portable operation
// collector across targets. These contain no target-specific knowledge: only
// arity, constant, identifier, HUD-mode, and member-access parsing.
public static class SdkCallReader
{
    public static void RequireArity(FunctionCall call, int expected)
    {
        var count = call.Parameters.Count();
        if (count != expected)
        {
            throw new InvalidOperationException($"{call.Name} expects {expected} arguments, got {count}.");
        }
    }

    public static string IdentifierArg(ExpressionSyntax expression, string context)
    {
        if (expression is not IdentifierSyntax identifier)
        {
            throw new InvalidOperationException($"{context} must be an identifier.");
        }

        return identifier.Identifier;
    }

    public static HudMode HudModeArg(ExpressionSyntax expression, string context)
    {
        var identifier = IdentifierArg(expression, context);
        return NormalizeMode(identifier) switch
        {
            "window" => HudMode.Window,
            "splitscroll" => HudMode.SplitScroll,
            "sprite" or "spritehud" => HudMode.Sprite,
            "none" => HudMode.None,
            _ => throw new InvalidOperationException($"{context} must be one of window, split_scroll, sprite_hud, or none."),
        };

        static string NormalizeMode(string value)
        {
            return value.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
        }
    }

    public static int ConstValue(ExpressionSyntax expression, string context)
    {
        if (expression is CastSyntax cast)
        {
            return ConstValue(cast.Expression, context);
        }

        if (expression is not ConstantSyntax constant)
        {
            throw new InvalidOperationException($"{context} must be a constant integer.");
        }

        var text = Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
        if (text == "true")
        {
            return 1;
        }

        if (text == "false")
        {
            return 0;
        }

        if (!IntegerLiteral.TryParse(text, out var value))
        {
            throw new InvalidOperationException($"{context} must be a constant integer.");
        }

        return value;
    }

    public static string MemberAccessName(MemberAccessSyntax memberAccess)
    {
        var parts = new Stack<string>();
        ExpressionSyntax current = memberAccess;
        while (current is MemberAccessSyntax member)
        {
            parts.Push(member.Member);
            current = member.Target;
        }

        if (current is not IdentifierSyntax identifier)
        {
            throw new InvalidOperationException("Member access currently requires an identifier base.");
        }

        parts.Push(identifier.Identifier);
        return string.Join(".", parts);
    }
}
