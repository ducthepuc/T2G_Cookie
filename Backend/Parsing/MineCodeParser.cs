using Antlr4.Runtime;
using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Backend.Parsing;

public static class MineCode
{
    public record ParseError(int Line, int Column, string Message)
    {
        public override string ToString() => $"line {Line}:{Column} {Message}";
    }

    public class MineCodeParseException : Exception
    {
        public IReadOnlyList<ParseError> Errors { get; }

        public MineCodeParseException(List<ParseError> errors)
            : base(string.Join("\n", errors))
        {
            Errors = errors;
        }
    }

    class ErrorCollector : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
    {
        public List<ParseError> Errors { get; } = new();

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int col, string msg, RecognitionException e)
            => Errors.Add(new ParseError(line, col, msg));

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int col, string msg, RecognitionException e)
            => Errors.Add(new ParseError(line, col, msg));
    }   

    public static Body ParseCode(string code)
    {
        var errors = new ErrorCollector();

        AntlrInputStream stream = new AntlrInputStream(code);

        MineCodeLexer lexer = new MineCodeLexer(stream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errors);

        CommonTokenStream tokenStream = new CommonTokenStream(lexer);

        MineCodeParser parser = new MineCodeParser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errors);

        var root = parser.program();

        if (errors.Errors.Count > 0)
            throw new MineCodeParseException(errors.Errors);

        MineCodeVisitor visitor = new MineCodeVisitor();

        return visitor.VisitProgram(root);
    }

    public static string[] Tokenize(string code)
    {
        AntlrInputStream stream = new(code);

        MineCodeLexer lexer = new(stream);

        CommonTokenStream tokenStream = new(lexer);

        return lexer.GetAllTokens().Select(t => t.Text).ToArray();
    }
}
