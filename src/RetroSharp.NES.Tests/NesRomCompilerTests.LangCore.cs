namespace RetroSharp.NES.Tests;

using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.NES.Tests.NesTestAssets;

public partial class NesRomCompilerTests
{
    [Fact]
    public void Compiles_video_api_calls_to_an_ines_rom()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Set(0, 15);
                                  Palette.Set(1, 39);
                                  Palette.Set(2, 22);
                                  Palette.Set(3, 48);
                                  Tilemap.Set(14, 12, 1);
                                  Tilemap.Set(15, 12, 2);
                                  Tilemap.Set(16, 12, 1);
                                  Tilemap.Set(15, 13, 3);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        Assert.Equal(16 + (32 * 1_024) + (8 * 1_024), rom.Length);
        Assert.Equal((byte)'N', rom[0]);
        Assert.Equal((byte)'E', rom[1]);
        Assert.Equal((byte)'S', rom[2]);
        Assert.Equal(0x1A, rom[3]);
        Assert.Equal(2, rom[4]);
        Assert.Equal(1, rom[5]);

        var prg = rom.Skip(16).Take(32 * 1024).ToArray();
        var chr = rom.Skip(16 + 32 * 1024).Take(8 * 1024).ToArray();

        Assert.Equal(0x00, prg[^4]);
        Assert.Equal(0x80, prg[^3]);
        Assert.Contains(chr, b => b != 0);
    }

    [Fact]
    public void Compiles_while_with_large_body_using_far_condition_trampoline()
    {
        var repeatedBody = string.Join(Environment.NewLine, Enumerable.Repeat("x += 1;", 120));
        var source = $$"""
                       void Main() {
                           Video.Init();
                           u8 x = 0;
                           while (x != 1) {
                               {{repeatedBody}}
                           }
                       }
                       """;

        _ = NesRomCompiler.CompileSource(source);
    }

    [Fact]
    public void Compiles_disabled_hud_mode_as_no_op_for_nes()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Hud.SetTile(none, 0, 0, 1);
                                  return;
                              }
                              """;

        _ = NesRomCompiler.CompileSource(source);
    }



    [Fact]
    public void Compiles_struct_member_access_as_adjacent_zero_page_fields()
    {
        const string source = """
                              struct Vec2 {
                                  i16 x;
                                  i16 y;
                              }

                              void Main() {
                                  Video.Init();
                                  Vec2 position;
                                  position.x = 40;
                                  position.y = position.x;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x28, 0x85, 0x00, 0xA9, 0x00, 0x85, 0x01]), "ROM should store position.x as a two-byte zero-page field.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x02, 0xA5, 0x01, 0x85, 0x03]), "ROM should copy position.x to the next two-byte position.y field with direct zero-page access.");
    }

    [Fact]
    public void Compiles_struct_initializer_like_explicit_member_assignments()
    {
        const string explicitSource = """
                                      struct Vec2 {
                                          u8 x;
                                          u8 y;
                                      }

                                      void Main() {
                                          Video.Init();
                                          u8 seed = 4;
                                          Vec2 position;
                                          position.x = 2;
                                          position.y = seed + 1;
                                          return;
                                      }
                                      """;

        const string initializerSource = """
                                         struct Vec2 {
                                             u8 x;
                                             u8 y;
                                         }

                                         void Main() {
                                             Video.Init();
                                             u8 seed = 4;
                                             Vec2 position = { y: seed + 1, x: 2 };
                                             return;
                                         }
                                         """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitSource), NesRomCompiler.CompileSource(initializerSource));
    }

    [Fact]
    public void Compiles_struct_initializer_shorthand_like_explicit_member_assignments()
    {
        const string explicitSource = """
                                      struct Vec2 {
                                          u8 x;
                                          u8 y;
                                      }

                                      void Main() {
                                          Video.Init();
                                          u8 x = 2;
                                          u8 y = 4;
                                          Vec2 position;
                                          position.x = x;
                                          position.y = y + 1;
                                          return;
                                      }
                                      """;

        const string initializerSource = """
                                         struct Vec2 {
                                             u8 x;
                                             u8 y;
                                         }

                                         void Main() {
                                             Video.Init();
                                             u8 x = 2;
                                             u8 y = 4;
                                             Vec2 position = { x, y: y + 1 };
                                             return;
                                         }
                                         """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitSource), NesRomCompiler.CompileSource(initializerSource));
    }

    [Fact]
    public void Compiles_type_aliases_as_underlying_types_without_runtime_cost()
    {
        const string source = """
                              type ActorIndex = u8;

                              struct Vec2 {
                                  u8 x;
                                  u8 y;
                              }

                              type Position = Vec2;

                              void Main() {
                                  Video.Init();
                                  ActorIndex actor = 7;
                                  Position position;
                                  position.x = actor;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x07, 0x85, 0x00]), "Alias ActorIndex should compile as the first byte-backed local.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x01]), "Alias Position should compile as a struct field at the next local address.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x02]), "Alias Position should preserve all struct fields with no alias storage.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "Alias assignments should lower to direct local load/store.");
    }

    [Fact]
    public void Compiles_const_identifiers_as_immediates_without_local_storage()
    {
        const string source = """
                              const u8 StartX = 40;
                              const u8 Copy = StartX - 39;

                              void Main() {
                                  Video.Init();
                                  i16 x = StartX;
                                  i16 y = Copy;
                                  y = x;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x28, 0x85, 0x00, 0xA9, 0x00, 0x85, 0x01]), "Const StartX should compile as an immediate store to the first two-byte zero-page local.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x02, 0xA9, 0x00, 0x85, 0x03]), "Const Copy should compile as an immediate store to the second two-byte zero-page local, with no const storage slot.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x02, 0xA5, 0x01, 0x85, 0x03]), "Const declarations should not shift runtime local addresses.");
    }

    [Fact]
    public void Compiles_local_const_identifiers_as_immediates_without_local_storage()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  const u8 StartX = 40;
                                  const u8 Copy = StartX + 1;
                                  i16 x = Copy;
                                  i16 y = 1;
                                  y = x;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x29, 0x85, 0x00, 0xA9, 0x00, 0x85, 0x01]), "Local const Copy should compile its derived value as an immediate store to the first two-byte zero-page local.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x02, 0xA9, 0x00, 0x85, 0x03]), "Local const declarations should not reserve zero-page storage before the second local.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x02, 0xA5, 0x01, 0x85, 0x03]), "Local const declarations should not shift runtime local addresses.");
    }

    [Fact]
    public void Compiles_hex_binary_and_separated_literals_like_decimal_immediates()
    {
        const string decimalSource = """
                                     const Mask = 160;
                                     const Tile = 42;

                                     void Main() {
                                         Video.Init();
                                         u8 flags = Mask | 15;
                                         u8 tile = Tile;
                                         u16 distance = 128;
                                         return;
                                     }
                                     """;

        const string literalSource = """
                                     const Mask = 0b1010_0000;
                                     const Tile = 0x2A;

                                     void Main() {
                                         Video.Init();
                                         u8 flags = Mask | 0x0F;
                                         u8 tile = Tile;
                                         u16 distance = 1_28u16;
                                         return;
                                     }
                                     """;

        Assert.Equal(NesRomCompiler.CompileSource(decimalSource), NesRomCompiler.CompileSource(literalSource));
    }

    [Fact]
    public void Compiles_sizeof_type_as_immediate_without_runtime_code()
    {
        const string source = """
                              struct Actor {
                                  u8 x;
                                  u16 y;
                                  bool active;
                              }

                              void Main() {
                                  Video.Init();
                                  u8 actorSize = sizeof(Actor);
                                  u8 pointerSize = sizeof(ptr<u8>);
                                  pointerSize = actorSize;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x04, 0x85, 0x00]), "sizeof(Actor) should compile as the struct byte size immediate.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x85, 0x01]), "sizeof(ptr<u8>) should compile as the pointer byte size immediate.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "sizeof expressions should not reserve storage or emit helper code.");
    }

    [Fact]
    public void Compiles_offsetof_field_as_immediate_without_runtime_code()
    {
        const string source = """
                              struct Actor {
                                  u8 x;
                                  u16 y;
                                  bool active;
                              }

                              void Main() {
                                  Video.Init();
                                  u8 yOffset = offsetof(Actor, y);
                                  u8 activeOffset = offsetof(Actor, active);
                                  activeOffset = yOffset;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x00]), "offsetof(Actor, y) should compile as the field byte offset immediate.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x03, 0x85, 0x01]), "offsetof(Actor, active) should compile as the field byte offset immediate.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "offsetof expressions should not reserve storage or emit helper code.");
    }

    [Fact]
    public void Compiles_u16_i16_locals_and_struct_fields_as_adjacent_zero_page_words()
    {
        const string source = """
                              struct Position {
                                  u8 tag;
                                  u16 x;
                                  i16 y;
                              }

                              void Main() {
                                  Position position = { tag: 1, x: 300u16, y: -2i16 };
                                  u16 total = position.x + 20u16;
                                  total -= 5u16;
                                  if (total == 315u16) {
                                      position.y += 3i16;
                                  }
                                  if (position.y > 0i16) {
                                      position.x = total;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(
            ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x00, 0xA9, 0x2C, 0x85, 0x01, 0xA9, 0x01, 0x85, 0x02, 0xA9, 0xFE, 0x85, 0x03, 0xA9, 0xFF, 0x85, 0x04]),
            "mixed-width struct fields should reserve adjacent zero-page bytes: tag, x low/high, then y low/high.");
    }

    [Fact]
    public void Compiles_fixed_size_array_constant_indices_as_adjacent_zero_page_bytes()
    {
        const string source = """
                              const u8 Count = 4;
                              const u8 First = 0;
                              const u8 Second = 1;

                              void Main() {
                                  u8 values[Count];
                                  values[First] = 40;
                                  values[Second] = values[First];
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x28, 0x85, 0x00]), "Array index 0 should store to the first zero-page byte.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "Array index 1 should load from the adjacent zero-page byte with direct addressing.");
    }

    [Fact]
    public void Compiles_fixed_size_array_initializer_like_explicit_assignments()
    {
        const string explicitSource = """
                                      void Main() {
                                          Video.Init();
                                          u8 seed = 3;
                                          u8 values[4];
                                          values[0] = 1;
                                          values[1] = seed;
                                          values[2] = seed + 1;
                                          u8 copy = values[3];
                                          return;
                                      }
                                      """;

        const string initializerSource = """
                                         void Main() {
                                             Video.Init();
                                             u8 seed = 3;
                                             u8 values[4] = [1, seed, seed + 1];
                                             u8 copy = values[3];
                                             return;
                                         }
                                         """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitSource), NesRomCompiler.CompileSource(initializerSource));
    }

    [Fact]
    public void Compiles_struct_array_initializer_like_explicit_field_assignments()
    {
        const string explicitSource = """
                                      struct Actor {
                                          u8 x;
                                          u8 y;
                                          bool active;
                                      }

                                      void Main() {
                                          Video.Init();
                                          u8 seed = 3;
                                          Actor actors[3];
                                          actors[0].x = 1;
                                          actors[0].active = 1;
                                          actors[1].y = seed + 1;
                                          u8 copy = actors[2].active;
                                          return;
                                      }
                                      """;

        const string initializerSource = """
                                         struct Actor {
                                             u8 x;
                                             u8 y;
                                             bool active;
                                         }

                                         void Main() {
                                             Video.Init();
                                             u8 seed = 3;
                                             Actor actors[3] = [{ x: 1, active: 1 }, { y: seed + 1 }];
                                             u8 copy = actors[2].active;
                                             return;
                                         }
                                         """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitSource), NesRomCompiler.CompileSource(initializerSource));
    }

    [Fact]
    public void Compiles_array_initializer_with_inferred_length_like_explicit_length()
    {
        const string explicitLengthSource = """
                                            void Main() {
                                                Video.Init();
                                                u8 seed = 3;
                                                u8 values[3] = [1, seed, seed + 1];
                                                u8 size = countof(values);
                                                return;
                                            }
                                            """;

        const string inferredLengthSource = """
                                            void Main() {
                                                Video.Init();
                                                u8 seed = 3;
                                                u8 values[] = [1, seed, seed + 1];
                                                u8 size = countof(values);
                                                return;
                                            }
                                            """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitLengthSource), NesRomCompiler.CompileSource(inferredLengthSource));
    }

    [Fact]
    public void Compiles_countof_fixed_size_array_as_immediate_without_runtime_code()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  u8 values[4];
                                  u8 size = countof(values);
                                  values[0] = size;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x04, 0x85, 0x04]), "countof(values) should compile as an immediate store after the four array bytes.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x04, 0x85, 0x00]), "countof should not emit a helper; subsequent array assignment should remain direct.");
    }

    [Fact]
    public void Compiles_enum_members_as_immediates_without_local_storage()
    {
        const string source = """
                              enum Tile {
                                  Empty,
                                  Brick = 40,
                                  Bonus
                              }

                              void Main() {
                                  Tile tile = Tile.Brick;
                                  Tile bonus = Tile.Bonus;
                                  bonus = tile;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x28, 0x85, 0x00]), "Enum Brick should compile as an immediate store to the first zero-page local.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x29, 0x85, 0x01]), "Implicit enum Bonus should compile as the next immediate value with no enum storage slot.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "Enum declarations should not shift runtime local addresses.");
    }

    [Fact]
    public void Compiles_compound_assignment_as_direct_lvalue_arithmetic()
    {
        const string source = """
                              void Main() {
                                  u8 x = 1;
                                  u8 y = 2;
                                  x += y;
                                  x -= 1;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x65, 0x00, 0x85, 0x00]), "x += y should lower to direct zero-page addition and store.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE9, 0x01, 0x85, 0x00]), "x -= 1 should lower to direct zero-page subtract/store without a helper call.");
    }

    [Fact]
    public void Compiles_increment_decrement_and_for_postfix_increment_as_direct_arithmetic()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  x++;
                                  x--;
                                  for (u8 i = 0; i < 3; i++) {
                                      x += i;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]), "x++ should lower to direct x += 1 zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE9, 0x01, 0x85, 0x00]), "x-- should lower to direct x -= 1 zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x69, 0x01, 0x85, 0x01]), "for i++ should lower to direct i += 1 zero-page arithmetic.");
    }

    [Fact]
    public void Compiles_range_for_loop_as_direct_counted_loop()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  for (u8 i in 0..3) {
                                      x += i;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x01]), "range-for should initialize the loop local once from the range start.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0xC9, 0x03, 0xB0]), "range-for should compare i with the exclusive upper bound and branch out when i >= end.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x65, 0x00, 0x85, 0x00]), "range-for body should use direct x += i zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x69, 0x01, 0x85, 0x01]), "range-for should increment i with direct i++ zero-page arithmetic.");
    }

    [Fact]
    public void Compiles_struct_array_field_access_with_runtime_index_stride()
    {
        const string source = """
                              struct Actor {
                                  u8 x;
                                  u8 y;
                                  bool active;
                              }

                              void Main() {
                                  Video.Init();
                                  Actor actors[3];
                                  u8 i = 2;
                                  actors[1].active = 7;
                                  u8 copy = actors[1].active;
                                  actors[i].y += 1;
                                  u8 runtimeCopy = actors[i].x;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x07, 0x85, 0x05]), "actors[1].active should store at base + sizeof(Actor) + offsetof(active).");
        Assert.True(ContainsSequence(prg, [0xA5, 0x05, 0x85, 0x0A]), "constant indexed field reads should use the flattened field address directly.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x09, 0x85, 0xE8, 0x0A, 0x18, 0x65, 0xE8, 0xAA, 0xB5, 0x01, 0x18, 0x69, 0x01, 0x95, 0x01]), "actors[i].y should compute X from i * sizeof(Actor) with logarithmic shift/add work and use the y-field base address.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x09, 0x85, 0xE8, 0x0A, 0x18, 0x65, 0xE8, 0xAA, 0xB5, 0x00, 0x85, 0x0B]), "runtime indexed field reads should compute X from i * sizeof(Actor) with logarithmic shift/add work and use the field base address.");
    }

    [Fact]
    public void Compiles_hand_written_actor_pool_update_loop_as_fixed_storage_and_branches()
    {
        const string source = """
                              struct Actor {
                                  u8 x;
                                  u8 y;
                                  u8 active;
                              }

                              void Main() {
                                  Video.Init();
                                  Actor actors[3] = [{ active: 1, x: 4 }, { x: 9 }, { active: 1, x: 12 }];
                                  for (u8 i = 0; i < countof(actors); i += 1) {
                                      if (actors[i].active != 0) {
                                          actors[i].x += 1;
                                      }
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x09, 0xC9, 0x03]), "pool loop should compare the byte index with countof(actors), not use an iterator object.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x09, 0x85, 0xE8, 0x0A, 0x18, 0x65, 0xE8, 0xAA, 0xB5, 0x02, 0xC9, 0x00]), "active checks should read actors[i].active through the fixed field base plus logarithmically materialized i * stride.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x09, 0x85, 0xE8, 0x0A, 0x18, 0x65, 0xE8, 0xAA, 0xB5, 0x00, 0x18, 0x69, 0x01, 0x95, 0x00]), "updates should mutate actors[i].x through fixed storage with no actor object or dispatch table.");
    }

    [Fact]
    public void Compiles_struct_array_fields_with_mixed_width_stride()
    {
        const string source = """
                              struct Actor {
                                  u16 worldX;
                                  u8 y;
                              }

                              void Main() {
                                  Actor actors[2];
                                  actors[1].worldX = 300u16;
                                  actors[1].y = 7;
                                  u8 i = 1;
                                  actors[i].y += 1;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x2C, 0x85, 0x03, 0xA9, 0x01, 0x85, 0x04]), "actors[1].worldX should store low/high at base + sizeof(Actor).");
        Assert.True(ContainsSequence(prg, [0xA9, 0x07, 0x85, 0x05]), "actors[1].y should be placed after the two-byte worldX field.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x06, 0x85, 0xE8, 0x0A, 0x18, 0x65, 0xE8, 0xAA, 0xB5, 0x02, 0x18, 0x69, 0x01, 0x95, 0x02]), "actors[i].y should use the mixed-width struct stride with logarithmic shift/add work.");
    }

    [Fact]
    public void Compiles_bare_loop_break_and_continue_as_direct_jumps()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  while (true) {
                                      x++;
                                      if (x == 1) {
                                          continue;
                                      }
                                      if (x == 3) {
                                          break;
                                      }
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var loopStart = IndexOfSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]);
        var continueCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x01, 0xD0]);
        var breakCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x03, 0xD0]);

        Assert.True(loopStart >= 0, "loop body should start with direct x++ zero-page arithmetic.");
        Assert.True(continueCompare >= 0, "continue guard should compare x with 1.");
        Assert.True(breakCompare >= 0, "break guard should compare x with 3.");
        Assert.True(ContainsAbsoluteJumpTo(prg, 0x8000 + loopStart, continueCompare, breakCompare), "continue should jump back to the loop start.");
        Assert.True(ContainsAbsoluteJumpAfter(prg, breakCompare, prg.Length, 0x8000 + breakCompare), "break should jump beyond the loop body.");
    }

    [Fact]
    public void Compiles_break_and_continue_as_direct_for_loop_jumps()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  for (u8 i = 0; i < 4; i += 1) {
                                      if (i == 1) {
                                          continue;
                                      }
                                      if (i == 3) {
                                          break;
                                      }
                                      x += 1;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var continueCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x01, 0xD0]);
        var breakCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x03, 0xD0]);
        var increment = IndexOfSequence(prg, [0xA5, 0x01, 0x18, 0x69, 0x01, 0x85, 0x01]);

        Assert.True(continueCompare >= 0, "continue guard should compare i with 1.");
        Assert.True(breakCompare >= 0, "break guard should compare i with 3.");
        Assert.True(increment >= 0, "for increment should still be emitted as direct i += 1 arithmetic.");

        Assert.True(ContainsAbsoluteJumpTo(prg, 0x8000 + increment, continueCompare, breakCompare), "continue should jump to the for increment.");
        Assert.True(ContainsAbsoluteJumpAfter(prg, breakCompare, prg.Length, 0x8000 + increment + 7), "break should jump beyond the increment and final loop jump.");
    }

    [Fact]
    public void Compiles_runtime_while_condition_as_direct_branching()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  while (x < 3) {
                                      x += 1;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var conditionCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x03, 0xB0]);
        var bodyIncrement = IndexOfSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]);

        Assert.True(conditionCompare >= 0, "while condition should compare runtime x with 3.");
        Assert.True(bodyIncrement > conditionCompare, "while body should emit after the runtime condition check.");
        Assert.True(ContainsAbsoluteJumpTo(prg, 0x8000 + conditionCompare, bodyIncrement, prg.Length), "while should jump back to the condition after the body.");
        Assert.True(ReadRelativeTarget(prg, conditionCompare + 4) > 0x8000 + bodyIncrement, "false condition should branch beyond the loop body.");
    }

    [Fact]
    public void Compiles_short_for_loop_condition_as_direct_relative_branch_without_trampoline()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  for (u8 i = 0; i < 3; i += 1) {
                                      x += 1;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var conditionCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x03, 0xB0]);
        var bodyIncrement = IndexOfSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]);

        Assert.True(conditionCompare >= 0, "for condition should compare i with the literal upper bound.");
        Assert.True(bodyIncrement > conditionCompare, "for body should follow the condition.");
        Assert.NotEqual(0x4C, prg[conditionCompare + 6]);
        Assert.True(ReadRelativeTarget(prg, conditionCompare + 4) > 0x8000 + bodyIncrement, "direct false branch should skip the body and increment without landing on a trampoline.");
    }

    [Fact]
    public void Compiles_long_for_loop_condition_with_branch_trampoline()
    {
        var body = string.Join(Environment.NewLine, Enumerable.Repeat("x += 1;", 40));
        var source = $$"""
                       void Main() {
                           u8 x = 0;
                           for (u8 i = 0; i < 1; i += 1) {
                       {{body}}
                           }
                           return;
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var conditionCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x01, 0xB0]);
        var trampolineAddress = ReadRelativeTarget(prg, conditionCompare + 4);
        var trampolineOffset = trampolineAddress - 0x8000;

        Assert.True(conditionCompare >= 0, "for condition should compare i with the literal upper bound.");
        Assert.Equal(0x4C, prg[conditionCompare + 6]);
        Assert.Equal(0x4C, prg[trampolineOffset]);
        Assert.True(ReadLittleEndian16(prg, trampolineOffset + 1) > trampolineAddress, "long false path should jump forward to the for end label.");
    }

    [Fact]
    public void Compiles_do_while_continue_to_condition_check()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  do {
                                      x++;
                                      if (x == 1) {
                                          continue;
                                      }
                                      x += 2;
                                  } while (x < 3);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var continueCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x01, 0xD0]);
        var conditionCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x03, 0xB0]);

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]), "do body should emit x++ as direct zero-page arithmetic before the first condition check.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x02, 0x85, 0x00]), "do body should emit direct zero-page arithmetic after the continue guard.");
        Assert.True(continueCompare >= 0, "continue guard should compare x with 1.");
        Assert.True(conditionCompare >= 0, "do-while condition should compare x with 3 at the bottom of the loop.");

        Assert.True(ContainsAbsoluteJumpTo(prg, 0x8000 + conditionCompare, continueCompare, conditionCompare), "continue should jump to the do-while condition check.");
    }

    [Fact]
    public void Compiles_logical_conditions_with_short_circuit_branches()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  u8 y = 1;
                                  u8 z = 0;
                                  if (x != 0 && y != 0) {
                                      z += 1;
                                  }
                                  if (x != 0 || y != 0) {
                                      z += 2;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var andLeftCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xF0]);
        var andRightCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x00, 0xF0]);
        var orLeftCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xD0]);
        var orBody = IndexOfSequence(prg, [0xA5, 0x02, 0x18, 0x69, 0x02, 0x85, 0x02]);

        Assert.True(andLeftCompare >= 0, "&& should test the left condition and branch false before touching the right side.");
        Assert.True(andRightCompare > andLeftCompare, "&& should evaluate the right condition only after the left condition succeeds.");
        Assert.True(orLeftCompare >= 0, "|| should test the left condition with a direct true branch.");
        Assert.True(orBody > orLeftCompare, "|| body should be emitted after the left condition branch.");
        var orLeftTarget = ReadRelativeTarget(prg, orLeftCompare + 4);
        Assert.True(orLeftTarget > 0x8000 + orLeftCompare && orLeftTarget <= 0x8000 + orBody, "|| should short-circuit toward the body when the left condition succeeds.");
    }

    [Fact]
    public void Compiles_range_membership_condition_like_explicit_bounds_checks()
    {
        const string explicitSource = """
                                      void Main() {
                                          u8 tile = 2;
                                          u8 hit = 0;
                                          if (tile >= 1 && tile < 4) {
                                              hit = 1;
                                          }
                                          return;
                                      }
                                      """;

        const string membershipSource = """
                                        void Main() {
                                            u8 tile = 2;
                                            u8 hit = 0;
                                            if (tile in 1..4) {
                                                hit = 1;
                                            }
                                            return;
                                        }
                                        """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitSource), NesRomCompiler.CompileSource(membershipSource));
    }

    [Fact]
    public void Compiles_unary_not_condition_by_inverting_the_branch()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  if (!(x != 0)) {
                                      x += 1;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xD0]), "! should invert x != 0 into a false branch when the inner condition is true.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]), "then body should remain direct x += 1 zero-page arithmetic.");
    }

    [Fact]
    public void Compiles_else_if_chain_as_nested_direct_branches()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  if (x == 0) {
                                      x += 1;
                                  } else if (x == 1) {
                                      x += 2;
                                  } else {
                                      x += 3;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xD0]), "first if should compare x with 0 and branch to else when false.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x01, 0xD0]), "else-if should compile as a nested if compare.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]), "first body should remain direct x += 1 zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x02, 0x85, 0x00]), "else-if body should remain direct x += 2 zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x03, 0x85, 0x00]), "else body should remain direct x += 3 zero-page arithmetic.");
    }

    [Fact]
    public void Compiles_untyped_constants_as_folded_literals()
    {
        const string source = """
                              const BaseValue = 4;
                              void Main() {
                                  u8 value = BaseValue;
                                  const NextValue = BaseValue + 1;
                                  value = NextValue;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x04, 0x85, 0x00]), "Untyped top-level const should fold to the direct literal initializer.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x05, 0x85, 0x00]), "Untyped block-local const should fold to the direct literal assignment.");
    }

    [Fact]
    public void Compiles_bitwise_compound_assignment_as_direct_mask_operations()
    {
        const string source = """
                              const Solid = 1;
                              const Hazard = 2;
                              const Toggle = 4;
                              void Main() {
                                  u8 flags = 0;
                                  flags |= Solid;
                                  flags &= ~Hazard;
                                  flags ^= Toggle;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x09, 0x01, 0x85, 0x00]), "flags |= Solid should lower to LDA/ORA immediate/STA with no helper.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x29, 0xFD, 0x85, 0x00]), "flags &= ~Hazard should lower to LDA/AND immediate/STA with no helper.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x49, 0x04, 0x85, 0x00]), "flags ^= Toggle should lower to LDA/EOR immediate/STA with no helper.");
    }

}
