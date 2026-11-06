using Antlr4;
using Antlr4.Runtime;

namespace T2G_Cookie.Backend.Parsing;
public abstract class MineCodeLexerSuperClass : Lexer
{
    readonly Stack<int> indents = new Stack<int>();
    readonly Queue<IToken> pending = new Queue<IToken>();
    int lastType;
    bool eofHandled;

    protected abstract int NewlineType { get; }
    protected abstract int IndentType { get; }
    protected abstract int DedentType { get; }

    protected MineCodeLexerSuperClass(ICharStream input, TextWriter output, TextWriter errorOutput)
        : base(input, output, errorOutput)
    {
        indents.Push(0);
    }

    public override IToken NextToken()
    {
        IToken t;

        if (pending.Count > 0)
        {
            t = pending.Dequeue();
        }
        else
        {
            t = base.NextToken();

            if (t.Type == TokenConstants.EOF && !eofHandled)
            {
                eofHandled = true;

                if (lastType != 0 && lastType != NewlineType)
                    pending.Enqueue(new CommonToken(NewlineType, ""));

                while (indents.Count > 1)
                {
                    indents.Pop();
                    pending.Enqueue(new CommonToken(DedentType, ""));
                }

                pending.Enqueue(t);
                t = pending.Dequeue();
            }
        }

        if (t.Type != IndentType && t.Type != DedentType)
            lastType = t.Type;

        return t;
    }

    protected void HandleNewline()
    {
        int spaces = Text.Length - Text.TrimEnd(' ').Length;

        if (spaces > indents.Peek())
        {
            indents.Push(spaces);
            pending.Enqueue(new CommonToken(IndentType, ""));
        }
        else
        {
            while (spaces < indents.Peek())
            {
                indents.Pop();
                pending.Enqueue(new CommonToken(DedentType, ""));
            }
        }
    }
}