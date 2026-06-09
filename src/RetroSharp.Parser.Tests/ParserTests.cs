namespace RetroSharp.Parser.Tests;

public class ParserTests
{
    [Fact]
    public void Empty_main()
    {
        var source = @"i16 main() { }";
        AssertParse(source);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(1)]
    [InlineData(2304)]
    public void Return_integer_constant(int constant)
    {
        var source = $@"i16 main() {{ return {constant}; }}";
        AssertParse(source);
    }

    [Fact]
    public void Assignment()
    {
        var source = @"i16 main() { a = 12; }";
        AssertParse(source);
    }

    [Fact]
    public void Declaration()
    {
        var source = """
                     i16 main() 
                     { 
                        i16 a = 1;
                        i16 b = 2;                         
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Multiple_lines()
    {
        var source = @"i16 main() { i16 b = 13; i16 a = 1; }";
        AssertParse(source);
    }

    [Fact]
    public void More_than_one_function()
    {
        var source = @"i16 main() { } void another() { }";
        AssertParse(source);
    }

    [Fact]
    public void Function_with_arguments()
    {
        var source = @"i16 main(i16 a, i16 b) { }";
        AssertParse(source);
    }

    [Fact]
    public void Arithmetic_addition()
    {
        var source = @"i16 main() { a = b + c; }";
        AssertParse(source);
    }

    [Fact]
    public void Arithmetic_mult()
    {
        var source = @"i16 main() { a = b * c; }";
        AssertParse(source);
    }

    [Fact]
    public void Equality()
    {
        var source = @"i16 main() { a = b == c; }";
        AssertParse(source);
    }

    [Fact]
    public void Inequality()
    {
        var source = @"i16 main() { a = b != c; }";
        AssertParse(source);
    }

    [Fact]
    public void Greater_than()
    {
        var source = @"i16 main() { a = b > c; }";
        AssertParse(source);
    }

    [Fact]
    public void Less_than()
    {
        var source = @"i16 main() { a = b < c; }";
        AssertParse(source);
    }

    [Fact]
    public void Less_than_or_equal()
    {
        var source = @"i16 main() { a = b <= c; }";
        AssertParse(source);
    }

    [Fact]
    public void Greater_than_or_equal()
    {
        var source = @"i16 main() { a = b >= c; }";
        AssertParse(source);
    }

    [Fact]
    public void True()
    {
        var source = @"i16 main() { a = true; }";
        AssertParse(source);
    }

    [Fact]
    public void False()
    {
        var source = @"i16 main() { a = false; }";
        AssertParse(source);
    }

    [Fact]
    public void Empty_return()
    {
        var source = @"i16 main() { return; }";
        AssertParse(source);
    }

    [Fact]
    public void If_statement_without_else()
    {
        var source = @"i16 main() { if (a > b) { return a; }}";
        AssertParse(source);
    }

    [Fact]
    public void If_statement_with_else()
    {
        var source = @"i16 main() { if (a > b) { return a; } else { return b; } }";
        AssertParse(source);
    }

    [Fact]
    public void While_loop()
    {
        var source = @"i16 main() { while (true) { a = a + 1; } }";
        AssertParse(source);
    }

    [Fact]
    public void Do_while_loop()
    {
        var source = """
                     void main()
                     {
                        u8 x = 0;
                        do
                        {
                           x++;
                           if (x == 1)
                           {
                              continue;
                           }
                           x += 2;
                        } while (x < 3);
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Parenthesis_are_OK()
    {
        var source = @"i16 main() { a = 2*(3+2); }";
        AssertParse(source);
    }

    [Fact]
    public void Call()
    {
        var source = @"i16 main() { Func(13); }";
        AssertParse(source);
    }

    [Fact]
    public void Expression_bodied_function()
    {
        var source = """
                     u8 choose_speed(u8 moving, u8 fast) => moving != 0 ? fast : 0;

                     void main()
                     {
                        u8 moving = 1;
                        u8 fast = 2;
                        u8 speed = choose_speed(moving, fast);
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Inline_and_pure_function_modifiers()
    {
        var source = """
                     inline pure u8 step(u8 value) => value + 1;

                     void main()
                     {
                        u8 next = step(4);
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Function_modifiers_are_preserved_in_the_ast()
    {
        var result = new SomeParser().Parse("inline pure u8 step(u8 value) => value + 1;");

        result.Should().Succeed();
        var function = Assert.Single(result.Value.Functions);
        function.IsInline.Should().BeTrue();
        function.IsPure.Should().BeTrue();
    }

    [Fact]
    public void Function_parameter_default_value()
    {
        var source = """
                     u8 step(u8 value, u8 amount = value + 1) => value + amount;

                     void main()
                     {
                        u8 next = step(4);
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Switch_expression()
    {
        var source = """
                     void main()
                     {
                        u8 state = 2;
                        u8 speed = state switch { 0 => 0, 1, 2 => 3, 3..6 => 5, _ => 1 };
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Switch_expression_is_preserved_in_the_ast()
    {
        var result = new SomeParser().Parse("void main(){ u8 state = 2; u8 speed = state switch { 0 => 0, _ => 1 }; }");

        result.Should().Succeed();
        var function = Assert.Single(result.Value.Functions);
        var declaration = Assert.IsType<DeclarationSyntax>(function.Block.Statements[1]);
        Assert.IsType<SwitchExpressionSyntax>(declaration.Initialization.Value);
    }

    [Fact]
    public void Function_call_named_arguments()
    {
        var source = """
                     u8 step(u8 value, u8 amount = value + 1) => value + amount;

                     void main()
                     {
                        u8 next = step(amount: 5, value: 4);
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Sdk_namespaced_dot_calls()
    {
        var source = """
                     void main()
                     {
                        video.Init();
                        input.Poll();
                        camera.SetPosition(4, 0);
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Sdk_namespaced_dot_call_is_preserved_in_the_ast()
    {
        var result = new SomeParser().Parse("void main(){ video.Init(); }");

        result.Should().Succeed();
        var function = Assert.Single(result.Value.Functions);
        var statement = Assert.IsType<ExpressionStatementSyntax>(Assert.Single(function.Block.Statements));
        var call = Assert.IsType<SdkDotCallSyntax>(statement.Expression);
        call.Module.Should().Be("video");
        call.Method.Should().Be("Init");
    }

    [Fact]
    public void Receiver_method_declaration_and_call()
    {
        var source = """
                     struct Actor
                     {
                        u8 x;
                     }

                     inline void Move(this Actor actor, u8 dx)
                     {
                        actor.x += dx;
                     }

                     void main()
                     {
                        Actor actor;
                        actor.Move(2);
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Static_class_declaration_lowers_to_struct_and_receiver_helper_ast()
    {
        const string source = """
                              class Actor
                              {
                                 u8 x;

                                 inline void Move(u8 dx)
                                 {
                                    x += dx;
                                 }
                              }

                              void main()
                              {
                                 Actor actor;
                                 actor.Move(2);
                              }
                              """;

        const string expected = """
                                struct Actor
                                {
                                 u8 x;
                                }
                                inline void Move(this Actor actor, u8 dx)
                                {
                                 actor.x += dx;
                                }
                                void main()
                                {
                                 Actor actor;
                                 actor.Move(2);
                                }
                                """;

        AssertParse(source, expected);
    }

    [Fact]
    public void Static_class_self_calls_lower_to_receiver_helper_calls()
    {
        const string source = """
                              class Actor
                              {
                                 u8 x;

                                 inline void Nudge()
                                 {
                                    x += 1;
                                 }

                                 inline void Move()
                                 {
                                    Nudge();
                                 }
                              }
                              """;

        const string expected = """
                                struct Actor
                                {
                                 u8 x;
                                }
                                inline void Nudge(this Actor actor)
                                {
                                 actor.x += 1;
                                }
                                inline void Move(this Actor actor)
                                {
                                 Nudge(actor);
                                }
                                """;

        AssertParse(source, expected);
    }

    [Fact]
    public void Static_class_rejects_runtime_object_features()
    {
        new SomeParser().Parse("class Actor { virtual void Update() { } }")
            .Should().Fail();
    }

    [Fact]
    public void Receiver_parameter_is_preserved_in_the_ast()
    {
        var result = new SomeParser().Parse("struct Actor { u8 x; } void Move(this Actor actor, u8 dx){}");

        result.Should().Succeed();
        var function = Assert.Single(result.Value.Functions);
        function.Parameters[0].IsReceiver.Should().BeTrue();
    }

    [Fact]
    public void Pipeline_expression()
    {
        var source = """
                     u8 Clamp(u8 value, u8 min, u8 max) => value < min ? min : value > max ? max : value;
                     u8 SnapToTile(u8 value) => value & 0xF8;

                     void main()
                     {
                        u8 value = 130;
                        u8 snapped = value |> Clamp(0, 120) |> SnapToTile();
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Pipeline_expression_is_preserved_in_the_ast()
    {
        var result = new SomeParser().Parse("void main(){ u8 snapped = value |> Clamp(0, 120) |> SnapToTile(); }");

        result.Should().Succeed();
        var function = Assert.Single(result.Value.Functions);
        var declaration = Assert.IsType<DeclarationSyntax>(Assert.Single(function.Block.Statements));
        Assert.IsType<PipelineExpressionSyntax>(declaration.Initialization.Value);
    }

    [Fact]
    public void Struct_declaration_and_member_access()
    {
        var source = """
                     struct Vec2
                     {
                        i16 x;
                        i16 y;
                     }

                     void main()
                     {
                        Vec2 position;
                        position.x = 12;
                        position.y = position.x + 4;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Struct_initializer()
    {
        var source = """
                     struct Vec2
                     {
                        u8 x;
                        u8 y;
                     }

                     void main()
                     {
                        Vec2 position = { x: 12, y: 7 };
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Struct_initializer_field_shorthand()
    {
        var source = """
                     struct Vec2
                     {
                        u8 x;
                        u8 y;
                     }

                     void main()
                     {
                        u8 x = 12;
                        u8 y = 7;
                        Vec2 position = { x, y: y + 1 };
                     }
                     """;
        var expected = """
                       struct Vec2
                       {
                          u8 x;
                          u8 y;
                       }

                       void main()
                       {
                          u8 x = 12;
                          u8 y = 7;
                          Vec2 position = { x: x, y: y + 1 };
                       }
                       """;
        AssertParse(source, expected);
    }

    [Fact]
    public void Type_alias_declaration()
    {
        var source = """
                     type ActorIndex = u8;
                     type Position = Vec2;

                     struct Vec2
                     {
                        u8 x;
                        u8 y;
                     }

                     void main()
                     {
                        ActorIndex actor = 1;
                        Position position;
                        position.x = actor;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Const_declaration()
    {
        var source = """
                     const u8 StartX = 40;

                     void main()
                     {
                        i16 x = StartX;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Local_const_declaration()
    {
        var source = """
                     void main()
                     {
                        const u8 StartX = 40;
                        const u8 StartY = StartX;
                        u8 x = StartY;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Sizeof_type_expression()
    {
        var source = """
                     struct Vec2
                     {
                        u8 x;
                        u16 y;
                     }

                     void main()
                     {
                        const u8 Vec2Size = sizeof(Vec2);
                        const u8 PointerSize = sizeof(ptr<u8>);
                        u8 bytes[sizeof(Vec2)];
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Offsetof_field_expression()
    {
        var source = """
                     struct Actor
                     {
                        u8 x;
                        u16 y;
                        bool active;
                     }

                     void main()
                     {
                        const u8 YOffset = offsetof(Actor, y);
                        u8 bytes[offsetof(Actor, active)];
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Enum_declaration_and_member_access()
    {
        var source = """
                     enum Direction
                     {
                        Left = 1,
                        Right,
                        Up = 8
                     }

                     void main()
                     {
                        Direction direction = Direction.Right;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Fixed_size_array_declaration_and_constant_index_access()
    {
        var source = """
                     void main()
                     {
                        u8 values[4];
                        values[0] = 40;
                        values[1] = values[0];
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Fixed_size_array_initializer()
    {
        var source = """
                     void main()
                     {
                        u8 values[3] = [1, 2, 3];
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Fixed_size_array_initializer_can_infer_length()
    {
        var source = """
                     void main()
                     {
                        u8 values[] = [1, 2, 3];
                     }
                     """;
        var expected = """
                       void main()
                       {
                          u8 values[3] = [1, 2, 3];
                       }
                       """;
        AssertParse(source, expected);
    }

    [Fact]
    public void Countof_fixed_size_array_expression()
    {
        var source = """
                     void main()
                     {
                        u8 values[4];
                        const u8 Count = countof(values);
                        u8 copy[countof(values)];
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Countof_does_not_cross_scalar_shadow()
    {
        var source = """
                     void main()
                     {
                        u8 values[4];
                        if (true)
                        {
                           u8 values;
                           const u8 Count = countof(values);
                        }
                     }
                     """;

        var parseResult = new SomeParser().Parse(source);
        parseResult.Should().Succeed();
        var action = () => ConstantFolder.Fold(parseResult.Value);
        action.Should().Throw<InvalidOperationException>().WithMessage("*countof(values)*");
    }

    [Fact]
    public void Compound_assignment()
    {
        var source = """
                     void main()
                     {
                        u8 x = 1;
                        x += 2;
                        x -= 1;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Bitwise_expressions_and_compound_assignment()
    {
        var source = """
                     const Solid=1;
                     const Hazard=2;
                     const Toggle=4;
                     void main()
                     {
                        u8 flags = 0;
                        flags |= Solid;
                        flags &= ~Hazard;
                        flags ^= Toggle;
                        u8 visible = flags & Solid;
                        u8 toggled = flags ^ Toggle;
                        u8 combined = flags | Toggle;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Value_returning_helper_call_expression()
    {
        var source = """
                     u8 set_flag(u8 flags, u8 mask)
                     {
                        return flags | mask;
                     }

                     void main()
                     {
                        u8 flags = 0;
                        flags = set_flag(flags, 1);
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Explicit_cast_expression()
    {
        var source = """
                     void main()
                     {
                        u16 wide = 1;
                        u8 narrowed = (u8)(wide | 2);
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Increment_and_decrement_statements()
    {
        var source = """
                     void main()
                     {
                        u8 x = 1;
                        x++;
                        x--;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void For_loop_with_local_initializer_and_compound_increment()
    {
        var source = """
                     void main()
                     {
                        u8 x = 0;
                        for (u8 i = 0; i < 3; i += 1)
                        {
                           x += i;
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void For_loop_with_postfix_increment()
    {
        var source = """
                     void main()
                     {
                        u8 x = 0;
                        for (u8 i = 0; i < 3; i++)
                        {
                           x += i;
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Range_for_loop()
    {
        var source = """
                     void main()
                     {
                        u8 x = 0;
                        for (u8 i in 0..3)
                        {
                           x += i;
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Bare_loop_statement()
    {
        var source = """
                     void main()
                     {
                        u8 x = 0;
                        loop
                        {
                           x++;
                           if (x == 1)
                           {
                              continue;
                           }
                           if (x == 3)
                           {
                              break;
                           }
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Fixed_size_array_runtime_index_access()
    {
        var source = """
                     void main()
                     {
                        u8 values[4];
                        u8 i = 1;
                        values[i] += 1;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void For_loop_with_break_and_continue()
    {
        var source = """
                     void main()
                     {
                        u8 x = 0;
                        for (u8 i = 0; i < 4; i += 1)
                        {
                           if (i == 1)
                           {
                              continue;
                           }
                           if (i == 3)
                           {
                              break;
                           }
                           x += 1;
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Logical_conditions_preserve_both_operands()
    {
        var source = """
                     void main()
                     {
                        u8 x = 0;
                        u8 y = 1;
                        if (x != 0 && y != 0)
                        {
                           x += 1;
                        }
                        if (x != 0 || y != 0)
                        {
                           y += 1;
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Range_membership_condition()
    {
        var source = """
                     void main()
                     {
                        u8 tile = 2;
                        if (tile in 1..4)
                        {
                           tile += 1;
                        }
                     }
                     """;
        var expected = """
                       void main()
                       {
                          u8 tile = 2;
                          if (tile >= 1 && tile < 4)
                          {
                             tile += 1;
                          }
                       }
                       """;
        AssertParse(source, expected);
    }

    [Fact]
    public void Logical_value_expressions()
    {
        var source = """
                     void main()
                     {
                        u8 x = 0;
                        u8 y = 1;
                        u8 both = x != 0 && y != 0;
                        u8 either = x != 0 || y != 0;
                        u8 notX = !(x != 0);
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Conditional_value_expression()
    {
        var source = """
                     void main()
                     {
                        u8 moving = 1;
                        u8 speed = moving != 0 ? 2 : 0;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Conditional_value_expression_as_binary_operand_keeps_parentheses()
    {
        var source = """
                     void main()
                     {
                        u8 moving = 1;
                        u8 speed = (moving != 0 ? 2 : 0) + 1;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Unary_not_condition()
    {
        var source = """
                     void main()
                     {
                        u8 x = 0;
                        if (!(x != 0))
                        {
                           x += 1;
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Else_if_chain()
    {
        var source = """
                     void main()
                     {
                        u8 x = 0;
                        if (x == 0)
                        {
                           x += 1;
                        }
                        else if (x == 1)
                        {
                           x += 2;
                        }
                        else
                        {
                           x += 3;
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Switch_statement_without_fallthrough()
    {
        var source = """
                     void main()
                     {
                        u8 state = 1;
                        u8 value;
                        switch (state)
                        {
                           case 0
                           {
                              value = 10;
                           }
                           case 1
                           {
                              value = 20;
                           }
                           default
                           {
                              value = 30;
                           }
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Switch_case_with_multiple_values()
    {
        var source = """
                     void main()
                     {
                        u8 state = 1;
                        u8 value;
                        switch (state)
                        {
                           case 0, 1
                           {
                              value = 10;
                           }
                           default
                           {
                              value = 30;
                           }
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Switch_case_with_half_open_range()
    {
        var source = """
                     void main()
                     {
                        u8 state = 2;
                        u8 value;
                        switch (state)
                        {
                           case 1..4
                           {
                              value = 10;
                           }
                           default
                           {
                              value = 30;
                           }
                        }
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Const_declaration_can_omit_type_annotation()
    {
        var source = """
                     const StartX=7;
                     void main()
                     {
                        u8 x=StartX;
                        const NextX=StartX+1;
                        x=NextX;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Hex_binary_and_separated_integer_literals()
    {
        var source = """
                     const Mask = 0b1010_0000;
                     const Tile = 0x2A;
                     void main()
                     {
                        u8 flags = Mask | 0x0F;
                        u8 tile = Tile;
                        u16 distance = 1_024u16;
                        i8 delta = -1i8;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Immutable_let_local_binding()
    {
        var source = """
                     void main()
                     {
                        let speed = 2;
                        u8 next = speed + 1;
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Immutable_let_local_binding_is_not_parsed_as_a_user_type()
    {
        var result = new SomeParser().Parse("void main(){ let speed = 2; }");

        result.Should().Succeed();
        var function = Assert.Single(result.Value.Functions);
        var declaration = Assert.IsType<DeclarationSyntax>(Assert.Single(function.Block.Statements));
        declaration.Type.Should().Be("u8");
        declaration.IsImmutable.Should().BeTrue();
    }

    [Fact]
    public void Top_level_let_binding_is_rejected()
    {
        new SomeParser().Parse("let speed = 2; void main(){}")
            .Should().Fail();
    }

    private static void AssertParse(string source)
    {
        AssertParse(source, source);
    }

    private static void AssertParse(string source, string expected)
    {
        var sut = new SomeParser();
        var result = sut.Parse(source);

        var visitor = new PrintNodeVisitor();
        result.Should().Succeed()
            .And.Subject.Value.ToSyntaxString().Should().BeEquivalentToIgnoringWhitespace(expected);
    }
}
