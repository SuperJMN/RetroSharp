grammar RetroSharp;

// We define the main rule that starts parsing the file
program: (importDeclaration | usingDeclaration)* (typeAliasDeclaration | constDeclaration | enumDeclaration | structDeclaration | classDeclaration | variableDeclaration | externFunction | function)* EOF;

importDeclaration: 'import' qualifiedIdentifier ';';
usingDeclaration: 'using' qualifiedIdentifier ';';
qualifiedIdentifier: IDENTIFIER ('.' IDENTIFIER)*;

enumDeclaration: 'enum' IDENTIFIER '{' enumMember (',' enumMember)* ','? '}';
enumMember: IDENTIFIER ('=' expression)?;

typeAliasDeclaration: 'type' IDENTIFIER '=' type ';';

structDeclaration: 'struct' IDENTIFIER '{' structField* '}';
structField: type IDENTIFIER ';';

classDeclaration: 'static'? 'class' IDENTIFIER '{' classMember* '}';
classMember: structField | classConstDeclaration | classStaticFunction | classFunction;
classConstDeclaration: 'static'? constDeclaration;
classFunction: functionModifier* attrs type IDENTIFIER '(' parameters? ')' (block | '=>' expression ';');
classStaticFunction: 'static' functionModifier* attrs type IDENTIFIER '(' parameters? ')' (block | '=>' expression ';');

constDeclaration: 'const' (type IDENTIFIER | IDENTIFIER) '=' expression ';';
letDeclaration: 'let' IDENTIFIER '=' expression ';';

// Variable declaration with optional fixed-size array rank and inline initialization
variableDeclaration: variableDeclarator ';';
variableDeclarator: type IDENTIFIER arraySize? ('=' variableInitializer)?;
arraySize: '[' expression? ']';
variableInitializer: arrayInitializer | structInitializer | expression;
arrayInitializer: '[' (variableInitializer (',' variableInitializer)* ','?)? ']';
structInitializer: '{' (fieldInitializer (',' fieldInitializer)* ','?)? '}';
fieldInitializer: IDENTIFIER (':' expression)?;

// Data types
// Canonical core types only. Aliases may be handled in a higher layer if desired.
type: 'void'
    | 'u8' | 'i8' | 'u16' | 'i16' | 'bool'
    | 'ptr' '<' type '>'
    | qualifiedIdentifier
    ;

// Attributes (zero-cost hints; currently ignored by semantics)
attrs: ('[' IDENTIFIER ('(' arguments? ')')? ']')* ;
functionModifier: 'inline' | 'pure';

// Extern function declaration (prototype only)
externFunction: attrs 'extern' type IDENTIFIER '(' parameters? ')' ';' ;

// Function definition (now supports optional attributes before the signature)
function: functionModifier* attrs type IDENTIFIER '(' parameters? ')' (block | '=>' expression ';');

// Function parameters, separated by commas
parameters: parameter (',' parameter)*;
parameter: receiverParameter | type IDENTIFIER ('=' expression)?;
receiverParameter: 'this' type IDENTIFIER;

// Code blocks, for functions, conditionals, and loops
block: '{' statement* '}';

// Statements that can appear within functions and blocks
statement: letDeclaration
          | variableDeclaration
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
expression: assignment | switchExpression | pipelineExpression | conditionalExpression ;

assignment: lvalue assignmentOperator expression ;
assignmentOperator: '=' | '+=' | '-=' | '&=' | '|=' | '^=' ;
postfixMutation: lvalue postfixOperator ;
postfixOperator: '++' | '--' ;

switchExpression: conditionalExpression 'switch' '{' switchExpressionArm (',' switchExpressionArm)* ','? '}';
switchExpressionArm: switchExpressionCaseArm | switchExpressionDefaultArm;
switchExpressionCaseArm: switchCasePattern (',' switchCasePattern)* '=>' expression;
switchExpressionDefaultArm: UNDERSCORE '=>' expression;
pipelineExpression: conditionalExpression pipelineStep+;
pipelineStep: '|>' IDENTIFIER '(' arguments? ')';

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
       | qualifiedCall
       | functionCall
       | memberAccess
       | indexExpression
       | IDENTIFIER
       | LITERAL ;

sizeofExpression: 'sizeof' '(' type ')';
offsetofExpression: 'offsetof' '(' type ',' IDENTIFIER ')';
countofExpression: 'countof' '(' IDENTIFIER ')';

qualifiedCall: IDENTIFIER ('.' IDENTIFIER)+ '(' arguments? ')';
memberAccess: (IDENTIFIER | indexExpression) ('.' IDENTIFIER)+;
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
CLASS: 'class';
STATIC: 'static';
CONST: 'const';
LET: 'let';
INLINE: 'inline';
PURE: 'pure';
UNDERSCORE: '_';
THIS: 'this';
USING: 'using';

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

// Comments (ignored). Line comments run to end of line; block comments are non-nesting.
LINE_COMMENT: '//' ~[\r\n]* -> skip;
BLOCK_COMMENT: '/*' .*? '*/' -> skip;
