namespace T2G_Cookie.Shared.Ast;

public abstract record class Value;
public abstract record SimpleValue : Value;

public record class Literal(long value) : SimpleValue;
public record class LiteralBool(bool value) : SimpleValue;
public record class Variable(string variable) : SimpleValue;

public record class BinOp(SimpleValue left, SimpleValue right, BinOp.Operation operation) : Value
{
    public enum Operation
    {
        Add,
        Sub,
        Mul,
        Eq,
        Neq,
        Lt,
        Leq,
        Gt,
        Geq,
        And,
        Or
    }
}
public record class Not(SimpleValue Value) : Value;
public record IsThere(SimpleValue Arg) : Value;