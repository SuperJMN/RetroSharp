grammar RetroSharp;

// We define the main rule that starts parsing the file
program: (typeAliasDeclaration | constDeclaration | enumDeclaration | structDeclaration | variableDeclaration | externFunction | function | statement)* EOF;

enumDeclaration: 'enum' IDENTIFIER '{' enumMember (',' enumMember)* ','? '}';
enumMember: IDENTIFIER ('=' expression)?;

typeAliasDeclaration: 'type' IDENTIFIER '=' type ';';

structDeclaration: 'struct' IDENTIFIER '{' structField* '}';
structField: type IDENTIFIER ';';

constDeclaration: 'const' (type IDENTIFIER | IDENTIFIER) '=' expression ';';

// Variable declaration with optional fixed-size array rank and inline initialization
variableDeclaration: variableDeclarator ';';
variableDeclarator: type IDENTIFIER arraySize? ('=' variableInitializer)?;
arraySize: '[' expression? ']';
variableInitializer: arrayInitializer | structInitializer | expression;
arrayInitializer: '[' (expression (',' expression)* ','?)? ']';
structInitializer: '{' (fieldInitializer (',' fieldInitializer)* ','?)? '}';
fieldInitializer: IDENTIFIER (':' expression)?;

// Data types
// Canonical core types only. Aliases may be handled in a higher layer if desired.
type: 'void'
    | 'u8' | 'i8' | 'u16' | 'i16' | 'bool'
    | 'ptr' '<' type '>'
    | IDENTIFIER
    ;

// Attributes (zero-cost hints; currently ignored by semantics)
attrs: ('[' IDENTIFIER ('(' arguments? ')')? ']')* ;

// Extern function declaration (prototype only)
externFunction: attrs 'extern' type IDENTIFIER '(' parameters? ')' ';' ;

// Function definition (now supports optional attributes before the signature)
function: attrs type IDENTIFIER '(' parameters? ')' (block | '=>' expression ';');

// Function parameters, separated by commas
parameters: parameter (',' parameter)*;
parameter: type IDENTIFIER ('=' expression)?;

// Code blocks, for functions, conditionals, and loops
block: '{' statement* '}';

// Statements that can appear within functions and blocks
statement: variableDeclaration
          | constDeclaration
          | breakStatement
          | continueStatement
          | postfixMutation ';'
          | conditional
          | whileLoop
          | doWhileLoop
          | loopStatement
          | rangeForLoop
          | forLoop
          | switchStatement
          | expression ';'
          | block // Allows nested blocks
          | returnStatement
          ;

// Expressions
expression: assignment | conditionalExpression ;

assignment: lvalue assignmentOperator expression ;
assignmentOperator: '=' | '+=' | '-=' | '&=' | '|=' | '^=' ;
postfixMutation: lvalue postfixOperator ;
postfixOperator: '++' | '--' ;

conditionalExpression: conditionalOrExpression ('?' expression ':' expression)? ;

conditionalOrExpression: conditionalOrExpression '||' conditionalAndExpression
                       | conditionalAndExpression ;

conditionalAndExpression: conditionalAndExpression '&&' bitwiseOrExpression
                        | bitwiseOrExpression ;

bitwiseOrExpression: bitwiseOrExpression '|' bitwiseXorExpression
                   | bitwiseXorExpression ;

bitwiseXorExpression: bitwiseXorExpression '^' bitwiseAndExpression
                    | bitwiseAndExpression ;

bitwiseAndExpression: bitwiseAndExpression '&' equalityExpression
                    | equalityExpression ;

equalityExpression: equalityExpression ('==' | '!=') rangeMembershipExpression
                  | rangeMembershipExpression ;

rangeMembershipExpression: shiftExpression 'in' shiftExpression '..' shiftExpression
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

unaryExpression: castExpression
               | ('+' | '-' | '!' | '~') unaryExpression
               | primary ;

castExpression: '(' type ')' unaryExpression;

primary: '(' expression ')'
       | sizeofExpression
       | offsetofExpression
       | countofExpression
       | functionCall
       | memberAccess
       | indexExpression
       | IDENTIFIER
       | LITERAL ;

sizeofExpression: 'sizeof' '(' type ')';
offsetofExpression: 'offsetof' '(' type ',' IDENTIFIER ')';
countofExpression: 'countof' '(' IDENTIFIER ')';

memberAccess: IDENTIFIER ('.' IDENTIFIER)+;
indexExpression: IDENTIFIER '[' expression ']';

// LValue forms for assignments (parser-only for now)
lvalue: memberAccess
      | IDENTIFIER '[' expression ']'
      | IDENTIFIER
      | '*' expression
      ;

// Function call
functionCall: IDENTIFIER '(' arguments? ')';

// Arguments in function calls, separated by commas
arguments: argument (',' argument)*;
argument: IDENTIFIER ':' expression | expression;

// Control structures
conditional: 'if' '(' expression ')' block ('else' (conditional | block))?;
whileLoop: 'while' '(' expression ')' block;
doWhileLoop: 'do' block 'while' '(' expression ')' ';';
loopStatement: 'loop' block;
rangeForLoop: 'for' '(' type IDENTIFIER 'in' expression '..' expression ')' block;
forLoop: 'for' '(' forInitializer? ';' expression? ';' forIncrement? ')' block;
forInitializer: variableDeclarator | assignment;
forIncrement: assignment | postfixMutation;
breakStatement: 'break' ';';
continueStatement: 'continue' ';';
switchStatement: 'switch' '(' expression ')' '{' switchCase+ switchDefault? '}';
switchCase: 'case' switchCasePattern (',' switchCasePattern)* block;
switchCasePattern: expression ('..' expression)?;
switchDefault: 'default' block;

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
DO: 'do';
LOOP: 'loop';
FOR: 'for';
IN: 'in';
RETURN: 'return';
BREAK: 'break';
CONTINUE: 'continue';
SWITCH: 'switch';
CASE: 'case';
DEFAULT: 'default';
EXTERN: 'extern';
ENUM: 'enum';
STRUCT: 'struct';
CONST: 'const';

IDENTIFIER: [a-zA-Z_][a-zA-Z_0-9]*;
LITERAL: LITERAL_INT | LITERAL_CHAR | LITERAL_STRING  | 'true' | 'false';
LITERAL_INT: '0' [xX] [0-9a-fA-F] [0-9a-fA-F_]* INTEGER_SUFFIX?
           | '0' [bB] [01] [01_]* INTEGER_SUFFIX?
           | [0-9] [0-9_]* INTEGER_SUFFIX?
           ;
fragment INTEGER_SUFFIX: 'u8' | 'i8' | 'u16' | 'i16';
LITERAL_CHAR: '\'' . '\'';
LITERAL_STRING: '"' ('\\"' | .)*? '"';

// Spaces and line breaks (ignored)
WS: [ \t\r\n]+ -> skip;
