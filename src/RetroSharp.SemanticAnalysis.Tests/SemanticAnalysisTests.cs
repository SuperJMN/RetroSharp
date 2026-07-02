using RetroSharp.Parser;

namespace RetroSharp.SemanticAnalysis.Tests;

public class SemanticAnalysisTests
{
    [Fact]
    public void Empty_program()
    {
        var input = "";
        var result = Analyze(input);
        result.Should().BeEquivalentToIgnoringWhitespace(input);
    }

    [Fact]
    public void Empty_main()
    {
        var input = "void Main(){}";
        var result = Analyze(input);
        result.Should().BeEquivalentToIgnoringWhitespace(input);
    }

    [Fact]
    public void Declaration()
    {
        var input = "void Main(){ i16 a; }";
        var result = Analyze(input);

        result.Should().BeEquivalentToIgnoringWhitespace(input);
    }

    [Fact]
    public void Assignment()
    {
        var input = "void Main(){ i16 a; a = 1; }";
        var result = Analyze(input);

        result.Should().BeEquivalentToIgnoringWhitespace(input);
    }

    [Fact]
    public void Using_undeclared_variable_fails()
    {
        var input = "void Main(){ a = 1; }";
        Errors(input).Should().ContainMatch("*undeclared*");
    }

    [Fact]
    public void Addition()
    {
        var input = "void Main(){ i16 a; i16 b; i16 c; a = 1; b = 2; c = a + b; }";
        var result = Analyze(input);

        result.Should().BeEquivalentToIgnoringWhitespace(input);
    }

    [Fact]
    public void Addition_with_undeclared_vars()
    {
        var input = "void Main(){ a = 1; b = 2; c = a + b; }";
        var enumerable = Errors(input);
        enumerable.Should().ContainMatch("*undeclared*");
    }

    [Fact]
    public void Expression_bodied_function_resolves_parameters_and_body()
    {
        var input = "u8 choose_speed(u8 moving, u8 fast) => moving != 0 ? fast : 0; void Main(){ u8 moving = 1; u8 speed = choose_speed(moving, 2); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Inline_and_pure_helper_modifiers_are_compile_time_contracts()
    {
        var input = "inline pure u8 step(u8 value) => value + 1; void Main(){ u8 next = step(4); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Pure_helper_rejects_side_effects()
    {
        Errors("pure void draw(){ video_init(); }")
            .Should().ContainMatch("*pure*side-effect*");
        Errors("pure u8 step(u8 value){ value += 1; return value; }")
            .Should().ContainMatch("*pure*return expression*");
    }

    [Fact]
    public void Function_parameter_default_value_resolves_parameter_and_default()
    {
        var input = "u8 step(u8 value, u8 amount = value + 1) => value + amount; void Main(){ u8 next = step(4); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Function_parameter_default_value_reports_undeclared_symbols()
    {
        var input = "u8 step(u8 value, u8 amount = missing + 1) => value + amount; void Main(){ u8 next = step(4); }";
        Errors(input).Should().ContainMatch("*undeclared*missing*");
    }

    [Fact]
    public void Struct_member_access_resolves_declared_fields()
    {
        var input = "struct Vec2 { i16 x; i16 y; } void Main(){ Vec2 position; position.x = 12; position.y = position.x + 4; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Struct_initializer_resolves_named_fields_and_values()
    {
        var input = "struct Vec2 { u8 x; u8 y; } void Main(){ u8 seed = 4; Vec2 position = { y: seed + 1, x: 2 }; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Struct_initializer_shorthand_resolves_matching_variables()
    {
        var input = "struct Vec2 { u8 x; u8 y; } void Main(){ u8 x = 2; u8 y = 4; Vec2 position = { x, y: y + 1 }; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Struct_initializer_reports_unknown_fields_and_values()
    {
        var input = "struct Vec2 { u8 x; u8 y; } void Main(){ Vec2 position = { z: missing }; }";
        Errors(input).Should().ContainMatch("*field*z*")
            .And.ContainMatch("*undeclared*missing*");
    }

    [Fact]
    public void Type_alias_resolves_to_underlying_type()
    {
        var input = "type ActorIndex = u8; struct Vec2 { u8 x; u8 y; } type Position = Vec2; void Main(){ ActorIndex actor = 1; Position position; position.x = actor; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Const_identifier_resolves_without_storage()
    {
        var input = "const u8 StartX = 40; void Main(){ i16 x; x = StartX; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Hex_binary_and_separated_integer_literals_resolve_as_constants()
    {
        var input = "const Mask = 0b1010_0000u8; const Tile = 0x2Au8; void Main(){ u8 flags = Mask | 0x0Fu8; u8 tile = Tile; u16 distance = 1_28u16; i8 delta = -1i8; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Width_suffix_widens_unannotated_let_and_const_inference()
    {
        // A u16 suffix makes the declaration u16, so a value above the u8 range is accepted.
        var input = "const Big = 1000u16; void Main(){ let distance = 300u16; let near = 5u8; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Unsuffixed_let_keeps_zero_cost_u8_default_and_rejects_overflow()
    {
        Errors("void Main(){ let value = 300; }").Should().ContainMatch("*does not fit*u8*");
    }

    [Fact]
    public void Width_suffix_still_enforces_its_own_range()
    {
        Errors("void Main(){ let value = 256u8; }").Should().ContainMatch("*does not fit*u8*");
    }

    [Fact]
    public void Declared_initializer_suffix_must_match_explicit_type()
    {
        Errors("void Main(){ u8 value = 5u16; }")
            .Should().ContainMatch("*suffix type 'u16'*declared type 'u8'*");
    }

    [Fact]
    public void Const_initializer_suffix_must_match_explicit_type()
    {
        Errors("const u8 Value = 5u16; void Main(){ }")
            .Should().ContainMatch("*suffix type 'u16'*declared type 'u8'*");
    }

    [Fact]
    public void Declared_initializer_symbol_type_must_match_explicit_type()
    {
        Errors("const Big = 300u16; void Main(){ u8 value = Big; }")
            .Should().ContainMatch("*Initializer type 'u16'*declared type 'u8'*");
    }

    [Fact]
    public void Let_initializer_symbol_type_must_match_default_type()
    {
        Errors("const Big = 300u16; void Main(){ let value = Big; }")
            .Should().ContainMatch("*Initializer type 'u16'*declared type 'u8'*");
    }

    [Fact]
    public void Local_const_identifier_resolves_in_block_scope()
    {
        var input = "void Main(){ const u8 StartX = 40; const u8 Copy = StartX; u8 x; x = Copy; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Sizeof_type_expression_resolves_to_constant()
    {
        var input = "struct Vec2 { u8 x; u16 y; } void Main(){ const u8 Size = sizeof(Vec2); const u8 PtrSize = sizeof(ptr<u8>); u8 x; x = Size + PtrSize; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Offsetof_field_expression_resolves_to_constant()
    {
        var input = "struct Actor { u8 x; u16 y; bool active; } void Main(){ const u8 YOffset = offsetof(Actor, y); u8 x; x = YOffset; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Enum_member_access_resolves_declared_enum()
    {
        var input = "enum Direction { Left = 1, Right, Up = 8 } void Main(){ Direction direction; direction = Direction.Right; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Fixed_size_array_constant_index_access_resolves_declared_array()
    {
        var input = "void Main(){ u8 values[4]; values[0] = 40; values[1] = values[0]; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Fixed_size_array_hex_constant_index_access_resolves_declared_array()
    {
        var input = "void Main(){ u8 values[4]; values[0x1] = 40; values[0b10] = values[0x1]; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Countof_fixed_size_array_resolves_to_constant()
    {
        var input = "void Main(){ u8 values[4]; u8 size; size = countof(values); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Countof_scalar_reports_semantic_error()
    {
        var input = "void Main(){ u8 value; u8 size; size = countof(value); }";
        Errors(input).Should().ContainMatch("*not an array*");
    }

    [Fact]
    public void Countof_scalar_shadow_reports_semantic_error()
    {
        var input = "void Main(){ u8 values[4]; if (true){ u8 values; u8 size; size = countof(values); } }";
        Errors(input).Should().ContainMatch("*not an array*");
    }

    [Fact]
    public void Fixed_size_array_runtime_index_access_resolves_element_type()
    {
        var input = "void Main(){ u8 values[4]; u8 i = 1; values[i] += 1; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Fixed_size_struct_array_field_access_resolves_field_type()
    {
        var input = "struct Actor { u8 x; u8 y; bool active; } void Main(){ Actor actors[3]; u8 i = 1; actors[0].x = 4; actors[i].y += 1; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Fixed_size_array_initializer_resolves_element_expressions()
    {
        var input = "void Main(){ u8 seed = 1; u8 values[3] = [seed, seed + 1, 3]; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Fixed_size_struct_array_initializer_resolves_element_fields_and_values()
    {
        var input = "struct Actor { u8 x; u8 y; bool active; } void Main(){ u8 seed = 1; Actor actors[2] = [{ x: 1, active: 1 }, { y: seed + 1 }]; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Fixed_size_array_initializer_infers_length_for_countof()
    {
        var input = "void Main(){ u8 seed = 1; u8 values[] = [seed, seed + 1, 3]; u8 size = countof(values); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Fixed_size_array_initializer_reports_undeclared_element_symbols()
    {
        var input = "void Main(){ u8 values[2] = [1, missing]; }";
        Errors(input).Should().ContainMatch("*undeclared*missing*");
    }

    [Fact]
    public void Immutable_let_binding_resolves_like_a_local()
    {
        var input = "void Main(){ let speed = 2; u8 next = speed + 1; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Immutable_let_binding_rejects_mutation()
    {
        Errors("void Main(){ let speed = 2; speed = 3; }")
            .Should().ContainMatch("*immutable*speed*");
        Errors("void Main(){ let speed = 2; speed += 1; }")
            .Should().ContainMatch("*immutable*speed*");
        Errors("void Main(){ let speed = 2; speed++; }")
            .Should().ContainMatch("*immutable*speed*");
    }

    [Fact]
    public void Compound_assignment_resolves_declared_lvalue()
    {
        var input = "void Main(){ u8 x = 1; x += 2; x -= 1; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Bitwise_expressions_and_compound_assignment_resolve_declared_lvalue()
    {
        var input = "const Solid = 1; const Hazard = 2; const Toggle = 4; void Main(){ u8 flags = 0; flags |= Solid; flags &= ~Hazard; flags ^= Toggle; u8 visible = flags & Solid; u8 toggled = flags ^ Toggle; u8 combined = flags | Toggle; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Value_returning_helper_call_expression_resolves_arguments()
    {
        var input = "u8 set_flag(u8 flags, u8 mask){ return flags | mask; } void Main(){ u8 flags = 0; flags = set_flag(flags, 1); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Value_returning_helper_call_expression_resolves_named_arguments()
    {
        var input = "u8 step(u8 value, u8 amount = value + 1) => value + amount; void Main(){ u8 next = step(amount: 5, value: 4); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Sdk_namespaced_dot_calls_resolve_arguments()
    {
        var input = "void Main(){ Video.Init(); Camera.Init(4, 0, 1); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Receiver_method_call_resolves_receiver_and_arguments()
    {
        var input = "struct Actor { u8 x; } inline void Move(this Actor actor, u8 dx){ actor.x += dx; } void Main(){ Actor actor; actor.Move(2); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Static_class_methods_resolve_like_struct_receiver_helpers()
    {
        var input = "class Actor { u8 x; inline void Move(u8 dx){ x += dx; } } void Main(){ Actor actor; actor.Move(2); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Static_class_constants_and_methods_resolve_as_compile_time_members()
    {
        var input = "class Tuning { static const Step = 2; static u8 Apply(u8 value){ return value + Tuning.Step; } } void Main(){ u8 speed = Tuning.Apply(Tuning.Step); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Static_class_self_calls_resolve_like_receiver_helper_calls()
    {
        var input = "class Actor { u8 x; inline void Nudge(){ x += 1; } inline void Move(){ Nudge(); } } void Main(){ Actor actor; actor.Move(); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Receiver_method_call_resolves_inside_nested_blocks()
    {
        var input = "struct Actor { u8 x; } inline void Move(this Actor actor, u8 dx){ actor.x += dx; } void Main(){ Actor actor; if (1 != 0){ actor.Move(2); } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Receiver_method_call_rejects_type_mismatch()
    {
        var input = "struct Actor { u8 x; } inline void Move(this Actor actor, u8 dx){ actor.x += dx; } void Main(){ u8 actor = 0; actor.Move(2); }";
        Errors(input).Should().ContainMatch("*receiver*Actor*");
    }

    [Fact]
    public void Receiver_method_call_allows_variable_that_shadows_sdk_module_name()
    {
        var input = "struct Actor { u8 x; } inline void Move(this Actor actor, u8 dx){ actor.x += dx; } void Main(){ Actor video; video.Move(2); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Pipeline_expression_resolves_nested_helper_arguments()
    {
        var input = "u8 Clamp(u8 value, u8 min, u8 max) => value < min ? min : value > max ? max : value; u8 SnapToTile(u8 value) => value & 0xF8; void Main(){ u8 value = 130; u8 snapped = value |> Clamp(0, 120) |> SnapToTile(); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Pipeline_expression_resolves_receiver_method_value()
    {
        var input = "struct Actor { u8 x; } u8 X(this Actor actor) => actor.x; u8 Clamp(u8 value, u8 min = 0, u8 max = 120) => value < min ? min : value > max ? max : value; void Main(){ Actor actor = { x: 130 }; u8 clamped = actor.X() |> Clamp(max: 120); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Explicit_cast_expression_resolves_operand_and_target_type()
    {
        var input = "void Main(){ u16 wide = 1; u8 narrowed = (u8)(wide | 2); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Explicit_cast_of_constant_exceeding_target_bit_width_is_rejected()
    {
        Errors("void Main(){ u8 narrowed = (u8)300; }")
            .Should().ContainMatch("*does not fit target type 'u8'*");
    }

    [Fact]
    public void Explicit_cast_of_negative_constant_below_target_bit_width_is_rejected()
    {
        Errors("void Main(){ u8 narrowed = (u8)-200; }")
            .Should().ContainMatch("*does not fit target type 'u8'*");
    }

    [Fact]
    public void Explicit_cast_of_bit_pattern_constant_within_target_width_is_allowed()
    {
        Errors("void Main(){ i8 value = (i8)200; }").Should().BeEmpty();
    }

    [Fact]
    public void Declared_u8_initializer_constant_outside_bit_width_is_rejected()
    {
        Errors("void Main(){ u8 value = 300; }")
            .Should().ContainMatch("*does not fit target type 'u8'*");
    }

    [Fact]
    public void Let_initializer_constant_outside_default_bit_width_is_rejected()
    {
        Errors("void Main(){ let value = 300; }")
            .Should().ContainMatch("*does not fit target type 'u8'*");
    }

    [Fact]
    public void Const_initializer_constant_outside_declared_bit_width_is_rejected()
    {
        Errors("const u8 Value = 300; void Main(){ }")
            .Should().ContainMatch("*does not fit target type 'u8'*");
    }

    [Fact]
    public void Increment_and_decrement_resolve_declared_lvalue()
    {
        var input = "void Main(){ u8 x = 1; x++; x--; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void For_loop_resolves_initializer_condition_body_and_increment()
    {
        var input = "void Main(){ u8 x = 0; for (u8 i = 0; i < 3; i += 1){ x += i; } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void For_loop_resolves_postfix_increment()
    {
        var input = "void Main(){ u8 x = 0; for (u8 i = 0; i < 3; i++){ x += i; } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Range_for_loop_resolves_as_counted_loop()
    {
        var input = "void Main(){ u8 x = 0; for (u8 i in 0..3){ x += i; } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Bare_loop_resolves_break_and_continue()
    {
        var input = "void Main(){ u8 x = 0; loop { x++; if (x == 1){ continue; } if (x == 3){ break; } } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Break_and_continue_resolve_inside_for_loop()
    {
        var input = "void Main(){ u8 x = 0; for (u8 i = 0; i < 4; i += 1){ if (i == 1){ continue; } if (i == 3){ break; } x += 1; } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Do_while_resolves_condition_body_break_and_continue()
    {
        var input = "void Main(){ u8 x = 0; do { x++; if (x == 1){ continue; } if (x == 3){ break; } x += 2; } while (x < 4); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Logical_conditions_resolve_both_operands()
    {
        var input = "void Main(){ u8 x = 0; u8 y = 1; if (x != 0 && y != 0){ x += 1; } if (x != 0 || y != 0){ y += 1; } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Range_membership_condition_resolves_subject_and_bounds()
    {
        var input = "void Main(){ u8 tile = 2; u8 first = 1; u8 limit = 4; if (tile in first..limit){ tile += 1; } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Logical_value_expressions_resolve_both_operands()
    {
        var input = "void Main(){ u8 x = 0; u8 y = 1; u8 both = x != 0 && y != 0; u8 either = x != 0 || y != 0; u8 notX = !(x != 0); }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Conditional_value_expression_resolves_condition_and_branches()
    {
        var input = "void Main(){ u8 moving = 1; u8 speed = moving != 0 ? 2 : 0; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Switch_expression_resolves_subject_patterns_and_results()
    {
        var input = "void Main(){ u8 state = 2; u8 speed = state switch { 0 => 0, 1, 2 => 3, 3..6 => 5, _ => 1 }; }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Switch_expression_requires_default_arm()
    {
        var input = "void Main(){ u8 state = 2; u8 speed = state switch { 0 => 0 }; }";
        Errors(input).Should().ContainMatch("*switch expression*default*");
    }

    [Fact]
    public void Switch_expression_rejects_non_simple_subject()
    {
        var input = "u8 next(u8 value) => value + 1; void Main(){ u8 speed = next(1) switch { 0 => 0, _ => 1 }; }";
        Errors(input).Should().ContainMatch("*switch expression*subject*simple*");
    }

    [Fact]
    public void Switch_expression_rejects_incompatible_branch_result_shapes()
    {
        var input = "void Main(){ u8 state = 2; u8 speed = state switch { 0 => true, _ => 1 }; }";
        Errors(input).Should().ContainMatch("*switch expression*branch*compatible*");
    }

    [Fact]
    public void Unary_not_condition_resolves_operand()
    {
        var input = "void Main(){ u8 x = 0; if (!(x != 0)){ x += 1; } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Else_if_chain_resolves_nested_conditions()
    {
        var input = "void Main(){ u8 x = 0; if (x == 0){ x += 1; } else if (x == 1){ x += 2; } else { x += 3; } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Switch_resolves_subject_cases_and_default_blocks()
    {
        var input = "enum State { Idle, Run } void Main(){ State state = State.Run; u8 speed; switch (state){ case State.Idle { speed = 0; } case State.Run { speed = 2; } default { speed = 1; } } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Switch_resolves_multiple_case_values()
    {
        var input = "enum State { Idle, Walk, Run } void Main(){ State state = State.Run; u8 speed; switch (state){ case State.Idle, State.Walk { speed = 1; } case State.Run { speed = 3; } } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Switch_resolves_half_open_case_ranges()
    {
        var input = "const u8 LastGround = 4; enum Tile { Empty, Brick, Coin, Spike, Water } void Main(){ Tile tile = Tile.Coin; u8 solid; switch (tile){ case Tile.Brick..LastGround { solid = 1; } default { solid = 0; } } }";
        Errors(input).Should().BeEmpty();
    }

    [Fact]
    public void Untyped_constants_are_resolved_in_global_and_block_scopes()
    {
        var input = "const Start = 2; void Main(){ const End = Start + 3; u8 value = End; }";
        Errors(input).Should().BeEmpty();
    }

    private IEnumerable<string> Errors(string input)
    {
        // Create a new instance of the SomeParser class.
        var parser = new SomeParser();
        // Parse the input string.
        var parseResult = parser.Parse(input);
        // Check if the parsing was successful.
        if (parseResult.IsFailure)
        {
            // If the parsing failed, return the error messages.
            return ["Can't analyze"];
        }

        var analyzer = new SemanticAnalyzer();
        var analyzeResult = analyzer.Analyze(parseResult.Value);
        return analyzeResult.Node.GetAllErrors();
    }

    private static string Analyze(string input)
    {
        // Create a new instance of the SomeParser class.
        var parser = new SomeParser();
        // Parse the input string.
        var parseResult = parser.Parse(input);
        // Check if the parsing was successful.
        if (parseResult.IsFailure)
        {
            // If the parsing failed, return the error messages.
            return string.Join("\n", parseResult.Error);
        }
        // Create a new instance of the SemanticAnalyzer class.
        var analyzer = new SemanticAnalyzer();

        // Analyze the parsed program.
        var analyzeResult = analyzer.Analyze(parseResult.Value);
        var printNodeVisitor = new PrintNodeVisitor();
        analyzeResult.Node.Accept(printNodeVisitor);
        return printNodeVisitor.ToString();
    }
}
