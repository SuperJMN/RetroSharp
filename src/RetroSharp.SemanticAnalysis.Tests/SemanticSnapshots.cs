using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

namespace RetroSharp.SemanticAnalysis.Tests;

public class SemanticSnapshots
{
    [Fact]
    public async Task Basic_declaration_and_assignment()
    {
        var src = "void main(){ i16 a; a = 1; }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Basic_declaration_and_assignment");
    }

    [Fact]
    public async Task Undeclared_symbol_diagnostic()
    {
        var src = "void main(){ a = 1; }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Undeclared_symbol_diagnostic");
    }

    [Fact]
    public async Task Addition_and_multiplication_precedence()
    {
        var src = "void main(){ i16 a; i16 b; i16 c; i16 d; a = b + c * d; }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Addition_and_multiplication_precedence");
    }

    [Fact]
    public async Task Parentheses_enforced_on_lower_precedence()
    {
        var src = "void main(){ i16 a; i16 b; i16 c; i16 d; a = (b + c) * d; }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Parentheses_enforced_on_lower_precedence");
    }

    [Fact]
    public async Task Symbol_usage_statement_visibility()
    {
        var src = "void main(){ i16 a; a; }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Symbol_usage_statement_visibility");
    }

    [Fact]
    public async Task Unknowns_in_binary_report_two_diagnostics()
    {
        var src = "void main(){ i16 c; c = a + b; }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Unknowns_in_binary_report_two_diagnostics");
    }

    [Fact]
    public async Task Redeclaration_reports_error()
    {
        var src = "void main(){ i16 a; i16 a; }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Redeclaration_reports_error");
    }

    [Fact]
    public async Task Multiple_functions_empty()
    {
        var src = "void main(){} void other(){}";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Multiple_functions_empty");
    }

    [Fact]
    public async Task Constant_expression_statement()
    {
        var src = "void main(){ 1; }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Constant_expression_statement");
    }

    // Not yet supported in SemanticAnalyzer: add skipped tests
    [Fact(Skip = "Return statements are not yet supported by SemanticAnalyzer")]
    public async Task Return_statement_supported()
    {
        var src = "void main(){ return; }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Return_statement_supported");
    }

    [Fact(Skip = "If/else statements are not yet supported by SemanticAnalyzer")]
    public async Task If_else_supported()
    {
        var src = "void main(){ if (1) { } else { } }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("If_else_supported");
    }

    [Fact(Skip = "Function calls are not yet supported by SemanticAnalyzer")]
    public async Task Function_call_supported()
    {
        var src = "void main(){ Foo(1); }";
        var analyzed = SemanticTestDriver.Analyze(src);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);
        await Verifier.Verify(text).UseMethodName("Function_call_supported");
    }
}
