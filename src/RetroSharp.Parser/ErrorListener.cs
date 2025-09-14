// Template generated code from Antlr4BuildTasks.Template v 8.17

using System.Collections.ObjectModel;

namespace RetroSharp.Parser;

public class ErrorListener<T> : ConsoleErrorListener<T>
{
    private readonly List<Error<T>> errors = new();

    public bool HadErrors;

    public ReadOnlyCollection<Error<T>> Errors => errors.AsReadOnly();

    public override void SyntaxError(TextWriter output, IRecognizer recognizer, T offendingSymbol, int line,
        int col, string msg, RecognitionException e)
    {
        HadErrors = true;
        base.SyntaxError(output, recognizer, offendingSymbol, line, col, msg, e);
        errors.Add(new Error<T>(offendingSymbol, line, col, msg));
    }
}