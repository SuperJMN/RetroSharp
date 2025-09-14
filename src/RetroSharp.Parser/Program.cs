// Template generated code from Antlr4BuildTasks.Template v 8.17

namespace RetroSharp.Parser;

public class Program
{
    static void Main(string[] args)
    {
        Try("void main(){ int a; a = 12; }");
    }

    static void Try(string input)
    {
        var str = new AntlrInputStream(input);
        System.Console.WriteLine(input);
        var lexer = new RetroSharpLexer(str);
        var tokens = new CommonTokenStream(lexer);
        var parser = new RetroSharpParser(tokens);
        var listener_lexer = new ErrorListener<int>();
        var listener_parser = new ErrorListener<IToken>();
        lexer.AddErrorListener(listener_lexer);
        parser.AddErrorListener(listener_parser);
        var tree = parser.program();
        if (listener_lexer.HadErrors || listener_parser.HadErrors)
            System.Console.WriteLine("error in parse.");
        else
            System.Console.WriteLine("parse completed.");
    }

    static string ReadAllInput(string fn)
    {
        var input = System.IO.File.ReadAllText(fn);
        return input;
    }
}