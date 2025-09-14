grammar RetroSharp;

// We define the main rule that starts parsing the file
program: (variableDeclaration | externFunction | function | statement)* EOF;

// Variable declaration with optional inline initialization
variableDeclaration: type IDENTIFIER ('=' expression)? ';';

// Data types
// Canonical core types only. Aliases may be handled in a higher layer if desired.
type: 'void'
    | 'u8' | 'i8' | 'u16' | 'i16' | 'bool'
    | 'ptr' '<' type '>'
    ;

// Attributes (zero-cost hints; currently ignored by semantics)
attrs: ('[' IDENTIFIER ('(' arguments? ')')? ']')* ;

// Extern function declaration (prototype only)
externFunction: attrs? 'extern' type IDENTIFIER '(' parameters? ')' ';' ;

// Function definition (now supports optional attributes before the signature)
function: attrs? type IDENTIFIER '(' parameters? ')' block;

// Function parameters, separated by commas
parameters: parameter (',' parameter)*;
parameter: type IDENTIFIER;

// Code blocks, for functions, conditionals, and loops
block: '{' statement* '}';

// Statements that can appear within functions and blocks
statement: variableDeclaration
          | expression ';'
          | conditional
          | whileLoop
          | forLoop
          | block // Allows nested blocks
          | returnStatement
          ;

// Expressions
expression: assignment | conditionalOrExpression ;

assignment: lvalue '=' expression ;

conditionalOrExpression: conditionalOrExpression '||' conditionalAndExpression
                       | conditionalAndExpression ;

conditionalAndExpression: conditionalAndExpression '&&' equalityExpression
                        | equalityExpression ;

equalityExpression: equalityExpression ('==' | '!=') relationalExpression
                  | relationalExpression ;

relationalExpression: relationalExpression ('<' | '<=' | '>' | '>=') shiftExpression
                    | shiftExpression ;

// Shift operations (new level between relational and additive)
shiftExpression: shiftExpression ('<<' | '>>') addExpression
               | addExpression ;

addExpression: addExpression ('+' | '-') mulExpression
             | mulExpression ;

mulExpression: mulExpression ('*' | '/' | '%') unaryExpression
             | unaryExpression ;

unaryExpression: ('+' | '-' | '!' | '~') unaryExpression
               | primary ;

primary: '(' expression ')'
       | IDENTIFIER
       | functionCall
       | LITERAL ;

// LValue forms for assignments (parser-only for now)
lvalue: IDENTIFIER
      | '*' expression
      | IDENTIFIER '[' expression ']' ;

// Function call
functionCall: IDENTIFIER '(' arguments? ')';

// Arguments in function calls, separated by commas
arguments: expression (',' expression)*;

// Control structures
conditional: 'if' '(' expression ')' block ('else' block)?;
whileLoop: 'while' '(' expression ')' block;
forLoop: 'for' '(' (variableDeclaration | assignment)? ';' expression? ';' assignment? ')' block;

// Return statement
returnStatement: 'return' expression? ';';

// Tokens
// Keyword tokens (must appear before IDENTIFIER to avoid being lexed as identifiers)
VOID: 'void';
I16: 'i16';
U16: 'u16';
I8: 'i8';
U8: 'u8';
BOOL: 'bool';
PTR: 'ptr';
IF: 'if';
ELSE: 'else';
WHILE: 'while';
FOR: 'for';
RETURN: 'return';
EXTERN: 'extern';

IDENTIFIER: [a-zA-Z_][a-zA-Z_0-9]*;
LITERAL: LITERAL_INT | LITERAL_CHAR | LITERAL_STRING  | 'true' | 'false';
LITERAL_INT: [0-9]+;
LITERAL_CHAR: '\'' . '\'';
LITERAL_STRING: '"' ('\\"' | .)*? '"';

// Spaces and line breaks (ignored)
WS: [ \t\r\n]+ -> skip;
