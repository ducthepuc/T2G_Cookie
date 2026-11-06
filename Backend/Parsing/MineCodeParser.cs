using Antlr4.Runtime;
using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Backend.Parsing;

public static class MineCode
{
    public static Body ParseCode(string code)
    {
        AntlrInputStream stream = new AntlrInputStream(code);

        MineCodeLexer lexer = new MineCodeLexer(stream);

        CommonTokenStream tokenStream = new CommonTokenStream(lexer);

        MineCodeParser parser = new MineCodeParser(tokenStream);

        var root = parser.program();

        System.Console.WriteLine(root.ToStringTree(parser));

        MineCodeVisitor visitor = new MineCodeVisitor();

        return visitor.VisitProgram(root);
    }
}
