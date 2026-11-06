parser grammar MineCodeParser;

options { 
    tokenVocab = MineCodeLexer; 
}

program
    : NEWLINE? instructions? EOF
    ;


body
    : INDENT instructions DEDENT
    ;

instructions
    : instruction+
    ;

instruction
    : stepInstruction NEWLINE
    | turnInstruction NEWLINE
    | mineInstruction NEWLINE
    | printInstruction NEWLINE
    | setInstruction NEWLINE
    | ifInstruction
    | whileInstruction
    ;

stepInstruction
    : STEP expression
    ;

turnInstruction
    : TURN expression
    ;

mineInstruction
    : MINE
    ;

printInstruction
    : PRINT expression
    ;

setInstruction
    : SET IDENTIFIER TO expression
    ;

ifInstruction
    : IF expression NEWLINE body (ELSE NEWLINE body)?
    ;

whileInstruction
    : WHILE expression body
    ;

expression
    : atom
    | functionExpression
    | binExpression
    | unaryExpression
    ;

functionExpression
    : ISTHERE atom
    ;

unaryExpression
    : NOT atom
    ;

binExpression
    : atom (EQ | NEQ | LT | GT | LEQ | GEQ | AND | OR | ADD | MINUS | STAR) atom
    ;

atom
    : TRUE
    | FALSE
    | NUMBER
    | IDENTIFIER
    ;