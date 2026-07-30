namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoyTestSupport;

public partial class GameBoyRomCompilerTests
{
    [Fact]
    public void Compiles_video_api_calls_to_a_game_boy_rom()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Set(0, 0);
                                  Palette.Set(1, 1);
                                  Palette.Set(2, 2);
                                  Palette.Set(3, 3);
                                  Tilemap.Set(8, 7, 1);
                                  Tilemap.Set(9, 7, 2);
                                  Tilemap.Set(10, 7, 1);
                                  Tilemap.Set(9, 8, 3);
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32 * 1_024, rom.Length);
        Assert.Equal(0x00, rom[0x0100]);
        Assert.Equal(0xC3, rom[0x0101]);
        Assert.Equal(0x50, rom[0x0102]);
        Assert.Equal(0x01, rom[0x0103]);
        Assert.Equal("RETROSHARPGB", System.Text.Encoding.ASCII.GetString(rom, 0x0134, 12));
        Assert.Equal(0x00, rom[0x0147]);
        Assert.Equal(0x00, rom[0x0148]);
        Assert.Equal(0x00, rom[0x0149]);
        Assert.Equal(HeaderChecksum(rom), rom[0x014D]);
        Assert.Contains(rom.Skip(0x0150), b => b != 0);
    }

    [Fact]
    public void GameBoy_hud_sample_compiles_with_window_hud()
    {
        var sourcePath = RepositoryFile("samples/window-hud/hud.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("Hud.SetTile(window", source);

        _ = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
    }

    [Fact]
    public void Compiles_long_runtime_loop_with_column_streaming()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  i16 camera = 0;
                                  i16 fine = 0;
                                  i16 streamColumn = 20;
                                  i16 marker = 0;
                                  i16 frame = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      scroll_set(camera, 0);
                                      sprite_set(0, 72, 80, 6 + frame, 0);
                                      sprite_set(1, 80, 80, 8 + frame, 0);
                                      camera = camera + 1;
                                      fine = fine + 1;
                                      frame = frame + 4;
                                      if (fine == 8) {
                                          fine = 0;
                                          tilemap_fill_column(streamColumn, 11, 2, 0);
                                          tilemap_fill_column(streamColumn, 13, 1, 4);
                                          tilemap_fill_column(streamColumn, 14, 1, 5);
                                          marker = marker + 1;
                                          if (marker == 4) {
                                              marker = 0;
                                              tilemap_fill_column(streamColumn, 12, 1, 5);
                                          }
                                          streamColumn = streamColumn + 1;
                                          if (streamColumn == 32) {
                                              streamColumn = 0;
                                          }
                                      }
                                      if (frame == 8) {
                                          frame = 0;
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Contains(rom.Skip(0x0150), b => b == 0xC3);
    }

    [Fact]
    public void Compiles_relational_condition_against_a_constant()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  u8 y = 80;
                                  u8 grounded = 0;
                                  if (y >= 78) {
                                      grounded = 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x4E, 0xDA]), "ROM should compare y with 78 and jump over the branch when y is below the threshold.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x01, 0xC0]), "ROM should execute the branch body when the relation is true.");
    }

    [Fact]
    public void Compiles_addition_between_runtime_locals()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  i16 y = 40;
                                  i16 velocity = 3;
                                  y = y + velocity;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x02, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "ROM should add the low bytes of two word-backed locals and store the result.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x03, 0xC0, 0x47, 0xFA, 0x01, 0xC0, 0x88, 0xEA, 0x01, 0xC0]), "ROM should propagate carry through the high bytes of two word-backed locals.");
    }

    [Fact]
    public void Compiles_struct_member_access_as_adjacent_wram_fields()
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
                                  position.y = 3;
                                  position.x = position.x + position.y;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x28, 0xEA, 0x00, 0xC0, 0x3E, 0x00, 0xEA, 0x01, 0xC0]), "ROM should store position.x as a two-byte field.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0xEA, 0x02, 0xC0, 0x3E, 0x00, 0xEA, 0x03, 0xC0]), "ROM should store position.y at the next two-byte field address.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x02, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "ROM should use direct low-byte member arithmetic with no helper call.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x03, 0xC0, 0x47, 0xFA, 0x01, 0xC0, 0x88, 0xEA, 0x01, 0xC0]), "ROM should use direct high-byte member arithmetic with no helper call.");
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
                                         }
                                         """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(initializerSource));
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
                                         }
                                         """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(initializerSource));
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x07, 0xEA, 0x00, 0xC0]), "Alias ActorIndex should compile as the first byte-backed local.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x01, 0xC0]), "Alias Position should compile as a struct field at the next local address.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x02, 0xC0]), "Alias Position should preserve all struct fields with no alias storage.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "Alias assignments should lower to direct local load/store.");
    }

    [Fact]
    public void Compiles_const_identifiers_as_immediates_without_local_storage()
    {
        const string source = """
                              const u8 StartX = 40;
                              const u8 Velocity = StartX - 37;

                              void Main() {
                                  Video.Init();
                                  i16 x = StartX;
                                  i16 velocity = Velocity;
                                  x = x + velocity;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x28, 0xEA, 0x00, 0xC0, 0x3E, 0x00, 0xEA, 0x01, 0xC0]), "Const StartX should compile as an immediate store to the first two-byte local.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0xEA, 0x02, 0xC0, 0x3E, 0x00, 0xEA, 0x03, 0xC0]), "Const Velocity should compile as an immediate store to the second two-byte local, with no const storage slot.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x02, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "Const declarations should not shift runtime local addresses.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x03, 0xC0, 0x47, 0xFA, 0x01, 0xC0, 0x88, 0xEA, 0x01, 0xC0]), "Const declarations should preserve high-byte arithmetic.");
    }

    [Fact]
    public void Compiles_local_const_identifiers_as_immediates_without_local_storage()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  const u8 StartX = 40;
                                  const u8 Velocity = StartX + 1;
                                  i16 x = Velocity;
                                  i16 y = 1;
                                  y = x;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x29, 0xEA, 0x00, 0xC0, 0x3E, 0x00, 0xEA, 0x01, 0xC0]), "Local const Velocity should compile its derived value as an immediate store to the first two-byte local.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x02, 0xC0, 0x3E, 0x00, 0xEA, 0x03, 0xC0]), "Local const declarations should not reserve WRAM before the second local.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x02, 0xC0, 0xFA, 0x01, 0xC0, 0xEA, 0x03, 0xC0]), "Local const declarations should not shift runtime local addresses.");
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
                                     }
                                     """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(decimalSource), GameBoyRomCompiler.CompileSource(literalSource));
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x04, 0xEA, 0x00, 0xC0]), "sizeof(Actor) should compile as the struct byte size immediate.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x02, 0xEA, 0x01, 0xC0]), "sizeof(ptr<u8>) should compile as the pointer byte size immediate.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "sizeof expressions should not reserve storage or emit helper code.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x00, 0xC0]), "offsetof(Actor, y) should compile as the field byte offset immediate.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0xEA, 0x01, 0xC0]), "offsetof(Actor, active) should compile as the field byte offset immediate.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "offsetof expressions should not reserve storage or emit helper code.");
    }

    [Fact]
    public void Compiles_fixed_size_array_constant_indices_as_adjacent_wram_bytes()
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

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x28, 0xEA, 0x00, 0xC0]), "Array index 0 should store to the first WRAM byte.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "Array index 1 should load from the adjacent WRAM byte with direct addressing.");
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
                                      }
                                      """;

        const string initializerSource = """
                                         void Main() {
                                             Video.Init();
                                             u8 seed = 3;
                                             u8 values[4] = [1, seed, seed + 1];
                                             u8 copy = values[3];
                                         }
                                         """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(initializerSource));
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
                                         }
                                         """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(initializerSource));
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
                                            }
                                            """;

        const string inferredLengthSource = """
                                            void Main() {
                                                Video.Init();
                                                u8 seed = 3;
                                                u8 values[] = [1, seed, seed + 1];
                                                u8 size = countof(values);
                                            }
                                            """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitLengthSource), GameBoyRomCompiler.CompileSource(inferredLengthSource));
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x04, 0xEA, 0x04, 0xC0]), "countof(values) should compile as an immediate store after the four array bytes.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x04, 0xC0, 0xEA, 0x00, 0xC0]), "countof should not emit a helper; subsequent array assignment should remain direct.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x28, 0xEA, 0x00, 0xC0]), "Enum Brick should compile as an immediate store to the first local.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x29, 0xEA, 0x01, 0xC0]), "Implicit enum Bonus should compile as the next immediate value with no enum storage slot.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "Enum declarations should not shift runtime local addresses.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "x += y should lower to the same direct load/add/store sequence as x = x + y.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xD6, 0x01, 0xEA, 0x00, 0xC0]), "x -= 1 should lower to direct subtract/store without a helper call.");
    }

    [Fact]
    public void Compiles_u16_i16_locals_and_struct_fields_as_adjacent_wram_words()
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(
            ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x00, 0xC0, 0x3E, 0x2C, 0xEA, 0x01, 0xC0, 0x3E, 0x01, 0xEA, 0x02, 0xC0, 0x3E, 0xFE, 0xEA, 0x03, 0xC0, 0x3E, 0xFF, 0xEA, 0x04, 0xC0]),
            "mixed-width struct fields should reserve adjacent WRAM bytes: tag, x low/high, then y low/high.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x01, 0xEA, 0x00, 0xC0]), "x++ should lower to direct x += 1 arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xD6, 0x01, 0xEA, 0x00, 0xC0]), "x-- should lower to direct x -= 1 arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xC6, 0x01, 0xEA, 0x01, 0xC0]), "for i++ should lower to direct i += 1 arithmetic.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x01, 0xC0]), "range-for should initialize the loop local once from the range start.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x03, 0xD2]), "range-for should compare i with the exclusive upper bound and branch out when i >= end.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "range-for body should use direct x += i arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xC6, 0x01, 0xEA, 0x01, 0xC0]), "range-for should increment i with direct i++ arithmetic.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x07, 0xEA, 0x05, 0xC0]), "actors[1].active should store at base + sizeof(Actor) + offsetof(active).");
        Assert.True(ContainsSequence(rom, [0xFA, 0x05, 0xC0, 0xEA, 0x0A, 0xC0]), "constant indexed field reads should use the flattened field address directly.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x09, 0xC0, 0x47, 0x87, 0x80, 0x21, 0x01, 0xC0, 0x5F, 0x16, 0x00, 0x19, 0x7E, 0xC6, 0x01, 0x77]), "actors[i].y should compute HL from the y-field base plus i * sizeof(Actor).");
        Assert.True(ContainsSequence(rom, [0xFA, 0x09, 0xC0, 0x47, 0x87, 0x80, 0x21, 0x00, 0xC0, 0x5F, 0x16, 0x00, 0x19, 0x7E, 0xEA, 0x0B, 0xC0]), "runtime indexed field reads should compute HL from the field base plus i * sizeof(Actor).");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x09, 0xC0, 0xFE, 0x03]), "pool loop should compare the byte index with countof(actors), not use an iterator object.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x09, 0xC0, 0x47, 0x87, 0x80, 0x21, 0x02, 0xC0, 0x5F, 0x16, 0x00, 0x19, 0x7E, 0xFE, 0x00]), "active checks should read actors[i].active through the fixed field base plus i * stride.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x09, 0xC0, 0x47, 0x87, 0x80, 0x21, 0x00, 0xC0, 0x5F, 0x16, 0x00, 0x19, 0x7E, 0xC6, 0x01, 0x77]), "updates should mutate actors[i].x through fixed storage with no actor object or dispatch table.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x2C, 0xEA, 0x03, 0xC0, 0x3E, 0x01, 0xEA, 0x04, 0xC0]), "actors[1].worldX should store low/high at base + sizeof(Actor).");
        Assert.True(ContainsSequence(rom, [0x3E, 0x07, 0xEA, 0x05, 0xC0]), "actors[1].y should be placed after the two-byte worldX field.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x06, 0xC0, 0x47, 0x87, 0x80, 0x21, 0x02, 0xC0, 0x5F, 0x16, 0x00, 0x19, 0x7E, 0xC6, 0x01, 0x77]), "actors[i].y should use the mixed-width struct stride.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        var loopStart = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x01, 0xEA, 0x00, 0xC0]);
        var continueCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01, 0xC2]);
        var breakCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x03, 0xC2]);

        Assert.True(loopStart >= 0, "loop body should start with direct x++ arithmetic.");
        Assert.True(continueCompare >= 0, "continue guard should compare x with 1.");
        Assert.True(breakCompare >= 0, "break guard should compare x with 3.");
        Assert.Equal(loopStart, ReadLittleEndian16(rom, continueCompare + 9));
        Assert.True(ReadLittleEndian16(rom, breakCompare + 9) > breakCompare, "break should jump beyond the loop body.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        var continueCompare = IndexOfSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x01, 0xC2]);
        var breakCompare = IndexOfSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x03, 0xC2]);
        var increment = IndexOfSequence(rom, [0xFA, 0x01, 0xC0, 0xC6, 0x01, 0xEA, 0x01, 0xC0]);

        Assert.True(continueCompare >= 0, "continue guard should compare i with 1.");
        Assert.True(breakCompare >= 0, "break guard should compare i with 3.");
        Assert.True(increment >= 0, "for increment should still be emitted as direct i += 1 arithmetic.");

        var continueJumpTarget = ReadLittleEndian16(rom, continueCompare + 9);
        Assert.Equal(increment, continueJumpTarget);

        var breakJumpTarget = ReadLittleEndian16(rom, breakCompare + 9);
        Assert.True(breakJumpTarget > increment + 8, "break should jump beyond the increment and final loop jump.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        var continueCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01, 0xC2]);
        var conditionCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x03, 0xD2]);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x01, 0xEA, 0x00, 0xC0]), "do body should emit x++ as direct arithmetic before the first condition check.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x02, 0xEA, 0x00, 0xC0]), "do body should emit direct arithmetic after the continue guard.");
        Assert.True(continueCompare >= 0, "continue guard should compare x with 1.");
        Assert.True(conditionCompare >= 0, "do-while condition should compare x with 3 at the bottom of the loop.");

        var continueJumpTarget = ReadLittleEndian16(rom, continueCompare + 9);
        Assert.Equal(conditionCompare, continueJumpTarget);
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        var andLeftCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xCA]);
        var andRightCompare = IndexOfSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x00, 0xCA]);
        var orLeftCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xC2]);
        var orBody = IndexOfSequence(rom, [0xFA, 0x02, 0xC0, 0xC6, 0x02, 0xEA, 0x02, 0xC0]);

        Assert.True(andLeftCompare >= 0, "&& should test the left condition and jump false before touching the right side.");
        Assert.True(andRightCompare > andLeftCompare, "&& should evaluate the right condition only after the left condition succeeds.");
        Assert.True(orLeftCompare >= 0, "|| should test the left condition with a direct true branch.");
        Assert.True(orBody > orLeftCompare, "|| body should be emitted after the left condition branch.");
        Assert.Equal(orBody, ReadLittleEndian16(rom, orLeftCompare + 6));
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
                                      }
                                      """;

        const string membershipSource = """
                                        void Main() {
                                            u8 tile = 2;
                                            u8 hit = 0;
                                            if (tile in 1..4) {
                                                hit = 1;
                                            }
                                        }
                                        """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(membershipSource));
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xC2]), "! should invert x != 0 into a false jump when the inner condition is true.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x01, 0xEA, 0x00, 0xC0]), "then body should remain direct x += 1 arithmetic.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xC2]), "first if should compare x with 0 and jump to the else branch when false.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01, 0xC2]), "else-if should compile as a nested if compare.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x01, 0xEA, 0x00, 0xC0]), "first body should remain direct x += 1 arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x02, 0xEA, 0x00, 0xC0]), "else-if body should remain direct x += 2 arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x03, 0xEA, 0x00, 0xC0]), "else body should remain direct x += 3 arithmetic.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x04, 0xEA, 0x00, 0xC0]), "Untyped top-level const should fold to the direct literal initializer.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x05, 0xEA, 0x00, 0xC0]), "Untyped block-local const should fold to the direct literal assignment.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xF6, 0x01, 0xEA, 0x00, 0xC0]), "flags |= Solid should lower to LD/OR immediate/store with no helper.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xE6, 0xFD, 0xEA, 0x00, 0xC0]), "flags &= ~Hazard should lower to LD/AND immediate/store with no helper.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEE, 0x04, 0xEA, 0x00, 0xC0]), "flags ^= Toggle should lower to LD/XOR immediate/store with no helper.");
    }

    [Fact]
    public void Compiles_constant_groups_like_flat_constants()
    {
        const string flatSource = """
                                  const WorldWidth = 16;
                                  const WorldStreamY = 9;
                                  const WorldHeight = 5;
                                  const PlayerScreenX = 72;

                                  void Main() {
                                      Video.Init();
                                      World.Column(0, 1, 2, 3, 4, 5);
                                      World.Flags(0, 0, 0, 1, 1, 1);
                                      World.Map(WorldWidth, WorldStreamY, WorldHeight);
                                      Camera.Init(WorldWidth, WorldStreamY, WorldHeight);
                                      i16 cameraX = 0;
                                      i16 playerWorldX = cameraX + PlayerScreenX;
                                      Camera.SetPosition(cameraX, 0);
                                  }
                                  """;

        const string groupedSource = """
                                     enum World { Width = 16, StreamY = 9, Height = 5 }
                                     enum Player { ScreenX = 72 }

                                     void Main() {
                                         Video.Init();
                                         World.Column(0, 1, 2, 3, 4, 5);
                                         World.Flags(0, 0, 0, 1, 1, 1);
                                         World.Map(World.Width, World.StreamY, World.Height);
                                         Camera.Init(World.Width, World.StreamY, World.Height);
                                         i16 cameraX = 0;
                                         i16 playerWorldX = cameraX + Player.ScreenX;
                                         Camera.SetPosition(cameraX, 0);
                                     }
                                     """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(flatSource), GameBoyRomCompiler.CompileSource(groupedSource));
    }

}
