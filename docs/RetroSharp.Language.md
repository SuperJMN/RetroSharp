# RetroSharp Language Specification (Preview)

This document captures the current design decisions for RetroSharp, a C#-inspired language targeting 8-bit systems with zero hidden runtime. It focuses on explicit cost, portability, and predictable code generation.

Status: Language v1 preview. Type aliases, top-level and block-local numeric constants with optional type annotations, `sizeof(type)`, `offsetof(type, field)`, `countof(array)`, top-level enums, plain local structs with named and shorthand initializer lists, `.` member access, fixed-size local arrays of byte-backed values or byte-sized structs, initializer lists for byte-backed value arrays and byte-sized struct arrays, initializer-inferred lengths for byte-backed value arrays, constant or runtime index access, struct-array field access such as `actors[i].x`, explicit casts, arithmetic and bitwise compound assignment, statement-only `++`/`--`, `if`/`else if`/`else`, no-fallthrough `switch` with multi-value and half-open range cases, half-open range membership expressions, `while`, `do while`, `loop`, short-circuit logical conditions including unary `!`, byte-backed conditional value expressions with `condition ? whenTrue : whenFalse`, C-style `for` loops, half-open range `for` loops, `break`/`continue`, and inline helper functions with parameters, named arguments, default parameter values, single return expressions, or `=>` expression bodies are implemented for the front-end and the current Game Boy/NES cartridge targets; pointer-based member access and full ABI layout work remain planned.

---

## 1. Goals and non-goals

- Zero-cost abstractions: no GC, no heap allocation, no exceptions, no reflection.
- Deterministic, explicit semantics suitable for 8-bit CPUs (Z80, LR35902, 6502).
- Portable core + platform intrinsics. Clear ABI per backend.
- C#-like surface where it does not hide cost.

Non-goals:
- Managed object features (heap-backed class instances, RTTI, implicit object identity) or dynamic features (LINQ/dynamic/reflection).

---

## 2. Canonical primitive types

Canonical types in the core:
- i8, u8, i16, u16, bool
- ptr<T> (16-bit pointer)
- struct, enum (plain aggregates)
- `type Name = ExistingType;` aliases for source-level intent without layout changes
- Top-level and block-local `const` declarations for symbolic compile-time values

Rationale:
- Width and signedness are explicit; maps 1:1 to 8/16-bit registers.
- Simplifies ABI and codegen; avoids surprising promotions.
- Type aliases are normalized to the underlying type before semantic lowering and target codegen. They do not create new runtime types, storage, casts, or dispatch.
- `sizeof(type)` is compile-time only. It currently returns 1 for `i8`, `u8`, `bool`, and enum types; 2 for `i16`, `u16`, and `ptr<T>`; and the sum of field sizes for plain struct types.
- `offsetof(type, field)` is compile-time only. It returns the byte offset of a direct field in a plain struct using the same layout as `sizeof`; nested field paths remain planned.
- `countof(array)` is compile-time only. It returns the declared element count of a fixed-size local array visible at that point.

Frontend sugar (aliases), optional:
- byte → u8, sbyte → i8, ushort → u16, short → i16
- int/uint/long are not supported on 8-bit targets; emit a clear error suggesting i16/u16.

Literals:
- Current support: decimal (`42`), hexadecimal (`0x2A`), binary (`0b1010_0000`), `_` separators, and width suffixes (`255u8`, `-1i8`, `0x1234u16`, `0b1010_0000u8`) in integer literals. These are source-level forms for the same integer value and lower to the same constants or immediates as unsuffixed decimal.
- Without suffix, the default can be target-defined (e.g., u16).
- Minimize implicit promotions; require explicit casts when width/sign changes.
- Explicit casts use `(type)expr`. In the current cartridge targets they are validated against byte-backed local types and then lower as zero-cost expression markers: they do not add helper calls, temporaries, sign extension, or truncation code in this prototype.
- Casting a compile-time integer constant (or negated constant) to `u8`/`i8`/`u16`/`i16` is a semantic error when the value does not fit the target type's bit width (allowed `-128..255` for 8-bit, `-32768..65535` for 16-bit). Bit-pattern casts that fit the width, such as `(i8)200`, remain allowed; runtime-valued casts are unchecked.
- Initializing a declared integer local, `let`, or `const` with a compile-time integer constant uses the same bit-width check. An unannotated `let` or `const` initialized with a single width-suffixed integer literal (optionally signed), such as `let distance = 300u16;` or `const Big = 1000u16;`, infers its type from the suffix; the suffixed value is still range-checked against that width. If a declaration has an explicit integer type, a suffixed literal initializer must use the same type, so `u8 value = 5u16;` is rejected instead of silently erasing the suffix. Initializers whose type is already known from a symbol, member, indexed element, or cast must also match the declaration type, so `const Big = 300u16; u8 value = Big;` is rejected unless the author uses an explicit matching cast. Without a suffix, `let` and untyped `const` keep the zero-cost `u8` default, so `let value = 300;` is still rejected. Broader expression inference and signedness-aware diagnostics for compound non-constant expressions remain future language work.

---

## 3. Structs, enums, and memory layout

- struct: plain aggregates with explicit fields. No implicit padding unless requested.
- Current cartridge-target support: local structs with byte-backed fields lower to adjacent local storage slots. Named initializer lists such as `Vec2 position = { y: seed + 1, x: 2 };` zero-fill through the same declaration path, then emit direct field stores in declaration order; omitted fields remain zero. A field shorthand such as `{ x, y: seed + 1 }` is parsed as `{ x: x, y: seed + 1 }`. This is a zero-runtime abstraction over the same byte loads/stores used by separate locals.
- Current cartridge-target support: local fixed-size arrays of byte-backed element types use `u8 values[4];` declarations and may use initializer lists such as `u8 values[4] = [1, seed, seed + 1];`. Initializer declarations can omit the length as `u8 values[] = [1, seed, seed + 1];`; the parser infers the fixed length from the listed element count before semantic and target lowering. Constant element access lowers to direct adjacent local storage slots such as `values[0]`, `values[1]`; runtime byte-backed index access computes an address from the array base and the index. Local fixed-size arrays of structs are supported when every field is byte-sized (`u8`, `i8`, `bool`, or enum), for example `Actor actors[8]; actors[i].x += 1;`. They lower to fixed flattened field storage such as `actors[0].x`, `actors[0].y`, `actors[1].x`, and runtime field access computes `fieldBase + index * targetStructStride`. Per-element initializer lists such as `Actor actors[3] = [{ x: 1, active: 1 }, { y: seed + 1 }];` zero-fill the whole array, then emit direct field stores for listed fields; omitted fields and omitted trailing elements remain zero. True `i16`/`u16` fields in struct arrays are rejected until mixed-width pool layout exists. `countof(values)` folds to the declared or inferred element count before target lowering. There is no heap allocation, implicit bounds check, implicit index mask, hidden helper, or array object.
- Current cartridge-target support: top-level enums use qualified member access such as `Tile.Brick`. Explicit members use their literal value; implicit members start at `0` or continue from the previous member. Enum declarations do not reserve storage, and enum locals are byte-backed in the current cartridge target path.
- Current samples may use enums as zero-cost constant groups such as `World.Width` or `Player.ScreenX` when that improves readability. This is source-level grouping over numeric constants, not runtime object state. A dedicated `module` or `const group` syntax is the preferred post-v1 spelling for this pattern.
- Top-level and block-local `const` declarations name numeric or boolean literals, earlier constants visible at that point, `sizeof(type)`, `offsetof(type, field)`, `countof(array)`, simple integer constant expressions using arithmetic, shift, and bitwise operators, or conditional expressions whose condition selects a constant branch. Integer literals can use decimal, `0x` hexadecimal, `0b` binary, `_` digit separators, and `u8`, `i8`, `u16`, or `i16` suffixes. Their type annotation is optional because the value is folded into literal expressions before target lowering and does not reserve RAM or ROM storage by itself.
- Attributes for layout (planned): [packed], [align(n)], [section(".name")], [bank(n)], [zeropage] (6502), etc.
- const data → ROM. Non-const globals → static RAM. Locals → stack. Volatile for MMIO.

---

## 4. Member access with '.' and controlled auto-deref

You always use '.' to access and mutate fields.

Semantics:
- By value: if s: S, then s.x is an lvalue to the field x of s.
- Via pointer: if p: ptr<S>, then p.x is equivalent to (*p).x (single implicit deref) and is an lvalue.
- Arrays of structs: a: S[N] → a[i].x; p: ptr<S> → p[i].x.
- Address-of field: &s.x yields ptr<FieldType>.
- Error if the base is neither S nor ptr<S>.
- Only one implicit dereference is performed. For deeper levels, use explicit '*': (*pp).x when pp: ptr<ptr<S>>.

Codegen notes (Z80):
- Load p.x: HL ← p + offset(x); load from (HL)/(HL+1) depending on width.
- Store p.x = v: HL ← p + offset(x); store to (HL)/(HL+1).
- Offsets are computed at compile time.

Rationale:
- C/C# familiarity without introducing a separate operator (like '=>').
- Keeps cost explicit and predictable while staying ergonomic.

---

## 5. Control flow and operators (core)

- if/else if/else, while, do/while, loop, C-style for, half-open range for, break, continue, return.
- Operators: + - * / %  & | ^ ~ << >>  == != < > <= >=  && ||  += -= &= |= ^=
- `x += y`, `x -= y`, `x &= y`, `x |= y`, and `x ^= y` are assignment sugar for reading the target once as an expression, applying the operator, and storing the result back to the same lvalue. The current cartridge targets lower this to the same direct load/operation/store sequences as the expanded assignment.
- The current Game Boy/NES cartridge targets support byte-backed subtraction and comparison operators (`==`, `!=`, `<`, `>`, `<=`, `>=`) between constants, locals, fields, array elements, and nested byte-backed expressions. Variable-vs-variable combinations preserve operands through nested sub-expression evaluation with target CPU stack operations, not shared expression temporaries.
- Byte-backed `&`, `|`, and `^` lower to direct target bit operations for flag masks. Constant masks, including `~Mask`, fold before target lowering; byte-backed runtime masks use direct register or zero-page/WRAM operations where supported. There is no helper call, hidden local, or materialized boolean.
- `x++` and `x--` are statement-only mutation sugar for `x += 1` and `x -= 1`; they are also allowed in the increment slot of `for`. They are not value expressions, so they introduce no temporary, no unspecified evaluation order, and no hidden runtime helper.
- `else if` is source-level conditional nesting. The current cartridge targets lower it as an `else` branch containing another `if`, so it adds no dispatcher, state machine, or runtime helper beyond the direct branches the equivalent nested code would use.
- `switch (expr) { case Value { ... } default { ... } }` is structured branch sugar with no fallthrough. Each case owns a block; `break` is not required between cases. Multi-value cases use `case A, B { ... }` and lower to `expr == A || expr == B`. Half-open range cases use `case A..B { ... }` and lower to `expr >= A && expr < B`. The current cartridge targets lower switches to `if`/`else if` compare chains using direct comparisons and short-circuit branches, so they add no table, dispatcher, helper call, or hidden state.
- `value in start..end` is a half-open range membership expression. It lowers to `value >= start && value < end`, using the same short-circuit compare branches as the hand-written expression. It does not create a range object, helper call, hidden local, hidden bounds check, or implicit subject cache.
- In conditions, `&&` and `||` use short-circuit control flow. `a && b` emits a false branch after `a` before evaluating `b`; `a || b` emits a true branch after `a` and skips `b` when possible. Unary `!` inverts the branch direction. When these forms are used as byte-backed value expressions, the current cartridge targets materialize `1` or `0` with direct branches and no helper call or hidden storage.
- `condition ? whenTrue : whenFalse` is a byte-backed conditional value expression. The current cartridge targets lower it to the same direct branch machinery as `if`: evaluate the condition, emit only the selected branch expression on each path, and store the selected byte. There is no helper call, hidden local, or eager evaluation of both branches.
- `for (init; condition; increment) { ... }` is structured control-flow sugar. The current cartridge targets emit the initializer once, a branchable condition check, the body, the increment, and a jump back to the loop condition; there is no iterator object, hidden call, or runtime helper.
- `for (u8 i in start..end) { ... }` is half-open range sugar for `for (u8 i = start; i < end; i++) { ... }`. The range start initializes the loop local once; the end expression is the loop condition's exclusive upper bound and is evaluated wherever that condition would be evaluated. There is no iterator object, hidden temporary, hidden bounds check, helper call, or runtime range value.
- `loop { ... }` is explicit infinite-loop sugar for `while (true) { ... }`. The current cartridge targets emit the loop body and a direct jump back to its start; `continue` jumps to the start label, and `break` jumps to the end label. There is no condition expression, hidden state, helper call, or local storage.
- `do { ... } while (condition);` is a post-test loop. The current cartridge targets emit the body first, place the `continue` target at the final condition check, and branch directly back to the body while the condition is true. There is no hidden state or helper call.
- `break` and `continue` are direct control-flow jumps. In a `for` loop, `continue` jumps to the increment slot before returning to the condition; in a `while` loop, it jumps back to the loop condition.
- Casts are explicit. Overflow is unchecked by default; opt-in checked mode can be added.

---

## 6. Functions and ABI (sketch)

- Signature: RetType Name(params) { ... }
- Current Game Boy/NES cartridge targets inline user helper calls with parameter substitution. Statement helpers expand their block; value helpers are accepted when the body is exactly one `return expr;` and expand to that expression. Expression-bodied helpers use `Ret name(args) => expr;` and are normalized to the same single-return helper shape before target lowering. Call sites can use named arguments such as `step(amount: 5, value: 4)`, which are reordered against the helper signature before lowering. Parameters can provide defaults with `type name = expr`; omitted defaults are substituted before lowering and may reference constants or earlier parameters visible at that point. This provides reusable source-level helpers without call overhead, stack setup, hidden storage, or ABI cost.
- Return registers: u8 → A; u16 → HL (Z80) or A:X (6502).
- Parameters on stack by default; attributes may enable fastcall via registers.
- Attributes (planned): [inline], [naked], [fastcall], [regs(HL,DE)], [intrinsic], [romcall(addr)], [target(...)].

---

## 7. Intrinsics

Portable core (examples):
- halt, di, ei, nop
- memcpy(dst, src, n), memset(dst, v, n)
- wait_vblank, pad_read1, frame_present

Platform-specific (examples): Spectrum out/in/map_bank, GameBoy oam_dma/ppu_*, NES ppu_*/nmi_enable.

Implemented prototype: top-level extern functions can carry target-intrinsic metadata:

```c
[target("gb")]
[intrinsic("wait_frame")]
extern void gb_wait_frame();

inline void WaitFrame() {
    gb_wait_frame();
}
```

The current Game Boy and NES cartridge targets resolve extern intrinsics through
a per-target catalog. Both targets currently recognize `wait_frame`
(`wait_vblank` is accepted as an alias), `poll_input`, and `audio_update` on
declarations whose
`target(...)` matches the backend (`"gb"` or `"nes"`). Calling a source helper
over those externs emits the same bytes as the current `Sdk2DOperation.WaitFrame`,
`Sdk2DOperation.PollInput`, and `SdkAudioOperation.UpdateAudio` paths, with no call
ABI, stack setup, helper
thunk, or hidden storage. Extern intrinsic prototypes are not ordinary inline
helpers: if a target does not recognize the declared intrinsic, compilation
fails instead of emitting an empty function.

Target compilation now injects a small SDK source library before parsing. For
the first slice, that library defines `video.WaitVBlank()`, `input.Poll()`, and
`audio.Update()` as
inline wrappers over target-selected extern intrinsics. Functions can carry
`[target("gb")]` or `[target("nes")]`; the active cartridge compiler filters
non-matching variants before constant folding and function indexing, so portable
helper code can share one helper name while selecting the correct target extern.
Higher-level camera, sprite, and collision SDK calls still lower through
capability-checked SDK operations rather than direct intrinsics.

---

## 8. Grammar updates (EBNF excerpt)

Current parser support includes top-level plain enum and struct declarations, identifier-based member access as an expression and as an lvalue, member access through fixed-size array elements such as `actors[i].x`, and local fixed-size array declarations with constant or byte-backed runtime index reads/writes. Pointer/member forms remain planned.

```
ConstDecl     = "const" Type? Ident "=" ConstExpr ";" ;
TypeAlias     = "type" Ident "=" Type ";" ;
ConstExpr     = Literal | EarlierConstIdent | SizeOf | OffsetOf | CountOf
              | ConstExpr "?" ConstExpr ":" ConstExpr
              | ConstExpr ("+" | "-" | "*" | "/" | "%" | "<<" | ">>" | "&" | "^" | "|") ConstExpr
              | ("+" | "-" | "~") ConstExpr ;
SizeOf        = "sizeof" "(" Type ")" ;
OffsetOf      = "offsetof" "(" Type "," Ident ")" ;
CountOf       = "countof" "(" Ident ")" ;
EnumDecl      = "enum" Ident "{" EnumMember ("," EnumMember)* ","? "}" ;
EnumMember    = Ident ("=" Expr)? ;
StructDecl    = "struct" Ident "{" Field* "}" ;
Field         = Type Ident ";" ;
Statement     = ConstDecl | VarDecl | Assignment ";" | PostfixMutation ";" | If | Switch | While | DoWhile | Loop | ForLoop | RangeForLoop | Break | Continue | Return ;
Function      = Type Ident "(" Parameters? ")" (Block | "=>" Expr ";") ;
Parameter     = Type Ident ("=" Expr)? ;
FunctionCall  = Ident "(" Arguments? ")" ;
Arguments     = Argument ("," Argument)* ;
Argument      = Ident ":" Expr | Expr ;
VarDeclarator = Type Ident ("[" Expr? "]")? ("=" VariableInitializer)? ;
VariableInitializer = ArrayInitializer | StructInitializer | Expr ;
ArrayInitializer = "[" (Expr ("," Expr)* ","?)? "]" ;
StructInitializer = "{" (FieldInitializer ("," FieldInitializer)* ","?)? "}" ;
FieldInitializer = Ident (":" Expr)? ;
VarDecl       = VarDeclarator ";" ;
Assignment    = LValue ("=" | "+=" | "-=" | "&=" | "|=" | "^=") Expr ;
ConditionalExpr = OrExpr ("?" Expr ":" Expr)? ;
RangeMembershipExpr = ShiftExpr "in" ShiftExpr ".." ShiftExpr ;
PostfixMutation = LValue ("++" | "--") ;
ForLoop       = "for" "(" (VarDeclarator | Assignment)? ";" Expr? ";" (Assignment | PostfixMutation)? ")" Block ;
RangeForLoop  = "for" "(" Type Ident "in" Expr ".." Expr ")" Block ;
DoWhile       = "do" Block "while" "(" Expr ")" ";" ;
Loop          = "loop" Block ;
If            = "if" "(" Expr ")" Block ("else" (If | Block))? ;
Switch        = "switch" "(" Expr ")" "{" SwitchCase+ DefaultCase? "}" ;
SwitchCase    = "case" SwitchCasePattern ("," SwitchCasePattern)* Block ;
SwitchCasePattern = Expr (".." Expr)? ;
DefaultCase   = "default" Block ;
Break         = "break" ";" ;
Continue      = "continue" ";" ;
LValue        = MemberAccess | "*" Expr | Ident "[" Expr "]" | Ident ;
MemberAccess  = (Ident | IndexAccess) ("." Ident)+ ;
IndexAccess   = Ident "[" Expr "]" ;

Primary       = Number | Char | "true" | "false"
              | SizeOf | OffsetOf | CountOf | FunctionCall | MemberAccess | IndexAccess | Ident | "(" Expr ")" ;
```

The semantic analyzer resolves type aliases, declared constants, `sizeof(type)`, `offsetof(type, field)`, `countof(array)`, enum members, struct fields, struct initializer fields including shorthand fields, array element access, member access through array elements, array initializer elements, initializer-inferred fixed array lengths, statement-only postfix mutations, explicit `loop` blocks, half-open range `for` loops, no-fallthrough `switch` cases, half-open range membership expressions, conditional value expressions, and named helper-argument expressions through compile-time tables and structured scopes. Game Boy and NES normalize aliases to their underlying types, fold top-level and block-local constants with or without type annotations, `sizeof(type)`, `offsetof(type, field)`, `countof(array)`, and enum members into literals, lower named helper arguments and defaults to positional parameter substitutions, lower struct and byte-backed array initializer lists to the same direct stores as explicit assignments, lower `loop` blocks to `while (true)`, lower range `for` loops to counted `for` loops, lower `++`/`--` to compound assignments, lower `switch` statements to `if`/`else` compare chains, lower multi-value cases to short-circuit `||` comparisons, lower half-open range cases and `value in start..end` expressions to `>=` and `<` short-circuit bounds checks, lower logical value expressions and `condition ? whenTrue : whenFalse` to direct branch materialization, lower bitwise flag operations to direct AND/OR/XOR instructions, and lower current local struct fields and fixed array elements by flattening names such as `position.x`, `values[0]`, and `actors[0].x` into adjacent local byte storage. Runtime struct-array field access computes the field base plus `index * targetStructStride`, where current cartridge targets require byte-sized fields and reject mixed-width struct arrays.

---

## 9. Examples

Value access:

```c
const StartX = 72;
type ActorIndex = u8;

enum Tile { Empty, Brick, Ladder, Bonus }

struct Vec2 { i16 x; i16 y; }

void f() {
    Vec2 v;
    Tile tile = Tile.Brick;
    ActorIndex actor = 1;
    u8 history[4];
    const HistorySize = countof(history);
    switch (tile) {
        case Tile.Empty, Tile.Brick {
            actor = 0;
        }
        case Tile.Ladder..Tile.Bonus {
            actor = 1;
        }
        default {
            actor = 2;
        }
    }
    v.x = StartX;
    history[0] = v.x;
    history[0]++;
    history[1] = history[0];
    for (u8 i in 0..HistorySize) {
        if (i == 1) {
            continue;
        }
        history[i] += 1;
    }
    i16 t = v.y;
}
```

Planned pointer access:

```c
void g(ptr<Vec2> p) {
    p.x = 1;
    p.y = p.y + 4;
    ptr<i16> px = &p.x;
}
```

---

## 10. V1 closure and next steps

- Extend member access beyond identifier-based local structs and fixed struct-array elements to pointer targets and address-of fields.
- Extend array support from local byte-backed elements and byte-sized struct elements to pointer-backed arrays and wider element layouts.
- Extend enum support beyond byte-backed cartridge-target locals when the shared ABI and wider integer layout are implemented.
- Enforce canonical types and friendly errors for disallowed aliases (int/long).
- Add unit tests covering pointer field access, address-of, and backend-specific word-sized field layout.
- Document ABI details per backend (register conventions, preserved registers).

## 11. Post-v1 high-level surface

Iteration 12 adds source ergonomics only when the lowering remains static and predictable:

- `let name = expr;` declares an immutable local binding. The current cartridge targets infer the binding as byte-backed local storage and reject reassignment, compound assignment, and postfix mutation.
- `inline` marks a helper that must use source-level substitution in the current cartridge targets. Explicit inline value helpers fail clearly if the body is not a single return expression.
- `pure` marks a helper whose body must stay in the supported side-effect-free subset. It is validated before Game Boy/NES lowering and emits no runtime code by itself.
- `expr switch { Pattern => value, _ => fallback }` is an expression form of no-fallthrough switch lowering. The current lowering requires a default arm, compatible scalar/boolean branch shapes, and a simple subject so calls are not re-evaluated.
- `video.Init()`, `video.WaitVBlank()`, `input.Poll()`, `camera.SetPosition(x, y)`, and similar SDK dot-calls are compile-time module calls that lower to existing SDK functions and keep target capability checks.
- `actor.Move(dx, dy)` is a receiver method only when a static helper such as `void Move(this Actor actor, u8 dx)` exists. It lowers to a static helper call and does not add object identity, vtables, boxing, or dynamic dispatch.
- Lightweight object-oriented style can use restricted `class` declarations for real mutable state such as `PlayerState` or `EnemyState`. A class value lowers to the same fixed storage model as a plain `struct`; instance methods lower to receiver helpers. Plain `struct` plus receiver methods remains the explicit equivalent form.
- A future `module` or `const group` syntax should provide the clean spelling for static configuration groups, for example `World.Width` and `Player.ScreenX`, while lowering exactly like top-level constants.
- These forms are optional ergonomics. Authors can keep flat constants, flat locals, and direct helper calls when they want the most explicit source shape; the grouped or receiver-method spelling must lower to equivalent code.
- `value |> Clamp(0, 120) |> SnapToTile()` rewrites left-to-right to nested static/helper calls and does not create a pipe object, iterator, delegate, or hidden range value.

Iteration 13 adds restricted static class syntax. This is source organization over the existing value model, not managed objects:

- `class Actor { u8 x; u8 y; void Move(i8 dx, i8 dy) { ... } }` lowers before target emission to a plain `struct Actor` plus receiver helpers such as `Move(this Actor actor, dx, dy)`.
- Class fields use the same fixed-layout rules as structs. No object header, vtable pointer, runtime type id, monitor, allocator state, or hidden identity is inserted.
- Non-virtual instance methods lower to statically resolved helpers or inline substitutions. `this` is the receiver parameter, not a heap object reference.
- Static methods and constants lower like module helpers and compile-time constants.
- Class values currently use the same declaration and initializer forms as structs, for example `Actor actor;` or `Actor actor = { x: 10, y: 20 };`. Future constructor-like syntax, if accepted, is only shorthand for zero-fill plus direct field stores or an explicit inline initializer helper. It must not allocate memory.
- Inheritance, `virtual`, `override`, interfaces, `new` allocation, destructors, RTTI, `dynamic_cast`-style checks, and implicit lifetime management remain rejected unless a later roadmap defines an explicit opt-in cost model.

Traits, constraints, managed objects, closures, delegates, runtime polymorphism, and built-in `Option`/`Result` abstractions are outside these post-v1 iterations unless a later roadmap adds a concrete zero-cost or explicit-cost design.

---

## 12. Rationale summary

- Canonical fixed-width types make cost and ABI explicit and portable on 8-bit.
- '.'-based member access with single-level auto-deref provides familiarity and keeps codegen simple (base + offset).
- Frontend sugar (byte/short) maintains approachability without importing 32/64-bit assumptions.
