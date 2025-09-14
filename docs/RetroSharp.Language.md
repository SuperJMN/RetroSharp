# RetroSharp Language Specification (Preview)

This document captures the current design decisions for RetroSharp, a C#-inspired language targeting 8-bit systems with zero hidden runtime. It focuses on explicit cost, portability, and predictable code generation.

Status: Draft for contributors. Parser/semantic updates will follow.

---

## 1. Goals and non-goals

- Zero-cost abstractions: no GC, no heap allocation, no exceptions, no reflection.
- Deterministic, explicit semantics suitable for 8-bit CPUs (Z80, LR35902, 6502).
- Portable core + platform intrinsics. Clear ABI per backend.
- C#-like surface where it does not hide cost.

Non-goals:
- Managed features (classes/heap/RTTI) or dynamic features (LINQ/dynamic/reflection).

---

## 2. Canonical primitive types

Canonical types in the core:
- i8, u8, i16, u16, bool
- ptr<T> (16-bit pointer)
- struct, enum (plain aggregates)

Rationale:
- Width and signedness are explicit; maps 1:1 to 8/16-bit registers.
- Simplifies ABI and codegen; avoids surprising promotions.

Frontend sugar (aliases), optional:
- byte → u8, sbyte → i8, ushort → u16, short → i16
- int/uint/long are not supported on 8-bit targets; emit a clear error suggesting i16/u16.

Literals:
- Allow numeric suffixes: 255u8, 0x1234u16. Without suffix, the default can be target-defined (e.g., u16).
- Minimize implicit promotions; require explicit casts when width/sign changes.

---

## 3. Structs, enums, and memory layout

- struct: plain aggregates with explicit fields. No implicit padding unless requested.
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

- if, while, for, return.
- Operators: + - * / %  & | ^ ~ << >>  == != < > <= >=  && ||
- Casts are explicit. Overflow is unchecked by default; opt-in checked mode can be added.

---

## 6. Functions and ABI (sketch)

- Signature: RetType Name(params) { ... }
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

---

## 8. Grammar updates (EBNF excerpt)

Adds member access as an expression and as an lvalue. One-level implicit deref in semantics.

```
LValue        = MemberAccess | "*" Expr | Ident "[" Expr "]" | Ident ;
MemberAccess  = Base "." Ident ;
Base          = Ident | "(" Expr ")" | Ident "[" Expr "]" | "*" Expr ;

Primary       = Number | Char | "true" | "false"
              | MemberAccess | Ident | Ident "[" Expr "]" | "(" Expr ")" ;
```

Note: The parser will be updated to reflect this. Until then, code using '.' may not parse.

---

## 9. Examples

Value and pointer access:

```c
struct Vec2 { i16 x; i16 y; }

void f() {
    Vec2 v;
    v.x = 10;
    i16 t = v.y;
}

void g(ptr<Vec2> p) {
    p.x = 1;
    p.y = p.y + 4;
    ptr<i16> px = &p.x;
}
```

---

## 10. Status and next steps

- Implement parser changes for member access and extend lvalue recognition.
- Enforce canonical types and friendly errors for disallowed aliases (int/long).
- Add unit tests covering struct field access (value/pointer/array), address-of, and codegen offsets.
- Document ABI details per backend (register conventions, preserved registers).

---

## 11. Rationale summary

- Canonical fixed-width types make cost and ABI explicit and portable on 8-bit.
- '.'-based member access with single-level auto-deref provides familiarity and keeps codegen simple (base + offset).
- Frontend sugar (byte/short) maintains approachability without importing 32/64-bit assumptions.
