lexer grammar MineCodeLexer;

options
{
    superClass = MineCodeLexerSuperClass;
}

tokens { INDENT, DEDENT }

@header {
namespace T2G_Cookie.Backend.Parsing;
}

@members {
    protected override int NewlineType => NEWLINE;
    protected override int IndentType  => INDENT;
    protected override int DedentType  => DEDENT;
}


// Instructions
STEP          : 'set forward';
TURN          : 'turn by';
MINE          : 'mine here';
PRINT         : 'print';
SET           : 'set';
TO            : 'to';

// Flow control
IF            : 'if';
ELSE          : 'else';
WHILE         : 'while';

// Expressions
NOT           : 'not';
ISTHERE       : 'isThere';
AND           : 'and';
OR            : 'or';

ADD           : '+';
MINUS         : '-';
STAR          : '*';
EQ            : '=';
NEQ           : '<>';
LT            : '<';
GT            : '>';
LEQ           : '<=';
GEQ           : '>=';

NEWLINE       : ('\r'? '\n' ' '*)+ { HandleNewline(); } ;

// Misc
IDENTIFIER    : [a-zA-Z][a-zA-Z0-9]*;
NUMBER        : [0-9]+;

TRUE          : 'true';
FALSE         : 'false';

SPACE         : ' ' -> skip;